#nullable enable annotations
using System;
using System.Globalization;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization.Settings;

namespace Game.App
{
    // 원본 튜토리얼, 기지, 제로존의 캠페인 월드를 씬 수명 동안 조립하고, 입력과 월드 사건을 세션 명령으로 잇는다.
    // 원본 LevelManager의 레벨 초기화와 종료 알림은 기반 클래스가 받고, 플레이어 준비(모터, 지도, 게임 카메라)와 프레임 순서는 이 클래스가 정한다.
    // 전투, 입력, 인벤토리 화면, 상호작용, 적 파사드, 생존 처리, 퀵슬롯 표시, 문, 시체와 유실물은 협력 객체가 맡는다.
    // 저장할 상태는 GameSessionManager와 GameManager의 상태 소유자가 맡는다.
    [DisallowMultipleComponent]
    public sealed class OriginalGameplayController : SceneLevelController, ISceneCancelHandler
    {
        // 플레이 카메라와 이동 애니메이션은 제작 도구가 직렬화한다. 카메라 암 값은 공간 Volume의 OriginalCameraArmControl이 정한다.
        // 테스트가 리플렉션으로 _camera를 읽으니 이름을 지킨다.
        [SerializeField] Camera _camera = null!;
        [SerializeField] RuntimeAnimatorController _locomotion = null!;
        // 원본 MainVirtualCamera 전용 초점 Volume이다. 게임 카메라가 런타임 복제본에서 초점을 갱신하고 대사 블렌드 때 가중치를 교차한다.
        [SerializeField] UnityEngine.Rendering.Volume? _focusVolume;
        // 원본 LevelManager의 TimeOfDayController처럼 게임 시계로 조명 프로필을 바꾸는 시간대 볼륨이다. 시간대가 없는 씬은 비워 둔다.
        [SerializeField] OriginalTimeOfDay? _timeOfDay;
        // 원작 기지 주 씬(Base.unity)의 SetTimeOnEnterBaseFirstTime.setTimeTo(8)다. 그 컴포넌트가 있는 씬만 제작 도구가 0~23을 넣고,
        // 다른 씬은 -1로 두어 첫 방문 시각 맞춤을 하지 않는다.
        [SerializeField, Range(-1, 23)] int _firstVisitHour = -1;
        // 레벨 초기화가 처음 확보하고 레벨 정리(ReleaseLevel)가 마지막에 되돌리는 플레이어 준비다. 테스트가 리플렉션으로 읽으니 이름을 지킨다.
        OriginalPlayerRig? _rig;
        // AttachPresentation이 연결을 시작했는지다. 연결 도중 실패했거나 HUD가 먼저 파괴돼 컨텍스트의 PresentationAttached가
        // false여도, 정적 로캘 구독과 대사 상자 연결은 되돌려야 해서 따로 둔다. DetachPresentation은 이 값을 먼저 내려 두 번 되돌리지 않는다.
        bool _presentationAttached;
        // AttachPresentation이 건 공용 HUD 명령, 표시음, 로캘 구독의 해제 동작이다. DetachPresentation이 비운다.
        readonly UiEventSubscriptions _presentationSubscriptions = new UiEventSubscriptions();
        // 제작 도구가 저작한 지점이며 실행 중 다시 대입하지 않는다. 시체와 유실물 같은 런타임 지점은 여기에 넣지 않고 World에서 빌린다.
        [SerializeField] OriginalGameplayPoint[] _points = Array.Empty<OriginalGameplayPoint>();
        // 제작 도구가 저작한 F 상호작용 대상과 문이다. 실행 중 다시 대입하지 않는다.
        [SerializeField] InteractableBase[] _interactables = Array.Empty<InteractableBase>();
        [SerializeField] Door[] _doors = Array.Empty<Door>();
        [SerializeField] OriginalEnemyController[] _enemies = Array.Empty<OriginalEnemyController>();
        // 제작 도구가 기록한 이 월드의 논리 씬 ID 문자열이다. PrepareWorld가 GameScene으로 변환한다.
        [SerializeField] string _sceneId = "";
        // PrepareWorld가 _sceneId에서 한 번 변환한 논리 씬이다. 준비 전에는 null이라 GameScene 값과 비교하면 항상 다르다.
        GameScene? _scene;
        // 내비게이션 조회와 실제 NavMeshAgent가 함께 쓰는 전용 베이크 에이전트 타입이다.
        // 이 값과 _navigationData는 Editor의 ComponentAuthoring.ConfigureNavigation, ConfigureNavigationData가 쓴다.
        [SerializeField] int _agentTypeId;
        // 각 Surface는 이 씬 전용 NavMeshData 복제본(_navigationInstance)을 쓰고, 레벨 정리가 등록을 빼고 파괴한다.
        // Surface가 없는 월드만 비워 둔다. Surface가 있으면 OriginalGameplaySceneDataTests가 연결을 확인하므로 PrepareWorld는 검사하지 않는다.
        [SerializeField] UnityEngine.AI.NavMeshData? _navigationData;
        UnityEngine.AI.NavMeshData? _navigationInstance;
        // 원본이 교체 로드하는 공간의 루트들이다. 하나만 켜서 그 공간의 조명, 충돌, 렌더러를 함께 활성화한다.
        // 이 배열과 _subsceneIds는 Editor의 ComponentAuthoring.ConfigureSubscenes가 쓴다.
        [SerializeField] GameObject[] _subscenes=Array.Empty<GameObject>();
        // _subscenes와 같은 순서의 원본 하위 씬 이름이다. 예: Level_Guide_1. 사망 기록과 유실물 소속을 저장할 때 ID로 쓴다.
        [SerializeField] string[] _subsceneIds=Array.Empty<string>();
        // _subscenes와 같은 순서의 원작 하위 씬 앰비언스 키(SubSceneEntry.overrideAmbientSound)다. 예: base, sewer.
        // Editor 제작 도구가 원본 주 씬의 하위 씬 목록에서 읽어 쓴다.
        [SerializeField] string[] _subsceneAmbience = Array.Empty<string>();
        // 현재 활성 하위 씬 인덱스다. 하위 씬이 없거나 아직 활성화하지 않았으면 -1이다.
        int _activeSubscene=-1;
        // 하위 씬 하나만 켜고 활성 인덱스를 기록한다. 인덱스는 첫 월드 초기화의 0이나 순간이동 지점에 저작한 범위 안의 값이다.
        internal void SwitchSubscene(int index)
        {
            for(int i=0;i<_subscenes.Length;i++)_subscenes[i].SetActive(i==index);
            _activeSubscene=index;
            // 떠난 하위 씬의 적을 다음 물리 틱까지 두지 않고 바로 끈다. 튜토리얼은 아래에서 하위 씬 0 밖의 적을 직접 끈다.
            if (_enemyWorld != null && _scene != GameScene.Tutorial) _enemyWorld.TickDistance();
            // 원본 지도가 MultiSceneCore.ActiveSubSceneID를 읽듯, 같은 루트의 지도에 활성 하위 씬을 알려 지도 구역의 출처를 이 값 하나로 둔다.
            // 하위 씬 루트와 이름은 같은 순서로 짝지어 저작되므로 같은 인덱스의 이름을 넘긴다. 지도가 없는 월드면 건너뛴다.
            if(TryGetComponent(out SceneMapContext map))map.SetActiveSubscene(_subsceneIds[index]);
            // 튜토리얼은 원본처럼 첫 공간인 하위 씬 0에만 적을 둔다. 첫 초기화가 적을 준비하기 전에는 모두 켜고, 그 뒤에는 준비된 적만 켠다.
            // 스폰 그룹에 뽑히지 않아 꺼진 적의 Collider가 탄을 막지 않게 하기 위해서다.
            if(_scene==GameScene.Tutorial)foreach(var enemy in _enemies)enemy.gameObject.SetActive(index==0&&(!_enemiesPrepared||enemy.Prepared));
            Physics.SyncTransforms();
            // 원작 OnSubSceneLoaded 구독자처럼, 켠 하위 씬의 바닥과 위치가 살아난 뒤 미룬 상자 현금을 내고 과제 위치를 다시 맞춘다.
            // 준비 전에는 협력 객체가 없고, 레벨 정리 뒤에는 바닥 아이템과 과제 연결이 이미 풀려 있어 건너뛴다.
            if (!_prepared) return;
            Interactions.OnSubsceneSwitched();
            if (_questTasks != null) _questTasks.OnSubsceneSwitched();
            // 원작 AudioManager.OnSubSceneLoaded처럼 켠 하위 씬의 앰비언스로 바꾼다. 첫 초기화가 켜는 첫 하위 씬도 이 경로로 시작한다.
            _ambience!.Play(_subsceneAmbience[index]);
        }
        // 사망 기록의 소속으로 쓰는 현재 하위 씬의 원본 이름이다. 하위 씬이 없거나 첫 초기화 전이면 던지지 않고 null이다.
        internal string? ActiveSubsceneId => _activeSubscene >= 0 && _activeSubscene < _subsceneIds.Length ? _subsceneIds[_activeSubscene] : null;
        // 전투 시뮬레이션이다. 씬이 생성과 종료를 조정하며 PrepareWorld가 만든다.
        // 이 접근자와 아래 협력 객체 접근자는 PrepareWorld 뒤에만 읽으므로 null을 다시 확인하지 않는다.
        CombatSimulation? _combat;
        CombatSimulation Combat => _combat!;
        // PrepareWorld와 첫 월드 초기화가 건 입력, 옵션, 세션, 문 구독의 해제 동작이다. 레벨 정리에서 한 번에 비운다.
        // 공용 HUD와 로캘 구독은 켜져 있는 동안만 걸리므로 _presentationSubscriptions가 따로 맡는다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // PrepareWorld가 앱의 입력 실행 사본에서 찾은 게임플레이 입력, 휠 상태, UI 액션 임대, 상호작용 키캡이다.
        // 원작 InputManager 몫을 맡는 협력 객체라, 레벨 정리는 그 임대만 돌려준다.
        GameplayInputBindings? _inputs;
        GameplayInputBindings Inputs => _inputs!;
        // 인벤토리 화면 상태를 전투, 상호작용과 떼어 맡는 협력 객체다. PrepareWorld가 만든다.
        InventoryScreen? _inventoryScreen;
        InventoryScreen Inventory => _inventoryScreen!;
        // 쪽지를 읽으면 여는 쪽지 창 소유자다. 준비 때 만들고 표시 해제가 닫는다.
        NoteScreen? _notes;
        // 퀘스트 창구, 상점, 보관함, 퀘스트 일지 화면 묶음이다. 준비 때 만들고, ESC와 표시 해제가 닫는다.
        GameplayScreens? _screens;
        // 공용 HUD를 붙이는 동안 단추 의도를 구독한 탭 단추 줄(원작 Tabs/ViewButtons)이다. 매 프레임 지금 탭을 넘기고 HUD를 뗄 때 비운다.
        // 단추 줄은 별도 제작 도구가 연결하므로 연결 전 HUD에서는 비어 있다.
        OriginalViewButtonsView? _viewButtons;
        // 이 레벨 씬의 퀘스트 과제 생성 도우미다. 준비 끝에 루트 아래에서 찾아 Bind하고 레벨 정리가 Unbind한다. 없는 씬은 비어 있다.
        QuestTaskSpawner? _questTasks;
        // 실행 중에 지은 건물의 상호작용 대상을 이 월드에 등록하도록 연결한 건설 영역이다. 정리 때 끊는다.
        BuildingArea[] _buildingAreas = Array.Empty<BuildingArea>();
        // _prepared는 PrepareWorld가 끝까지 마쳤을 때만 선다. 루트가 로드 때부터 켜져 있어 Update, OnEnable, 알림 처리가
        // 레벨 초기화 전이나 초기화 실패 뒤에 올 수 있으므로, 모두 이 값으로 걸러 반쯤 준비된 월드를 매 프레임 돌리지 않는다.
        bool _prepared, _initialized, _transitioning;
        // 첫 월드 초기화가 적 준비 반복문을 마쳤는지다. 튜토리얼 하위 씬 전환이 이 값으로 준비 전에는 모든 적을, 뒤에는 준비된 적만 켠다.
        bool _enemiesPrepared;
        // 명령 게이트를 통과해 한 번뿐인 월드 초기화를 시작했는지다. 세션 그래프와 생존 수치 플레이어 초기화도 이 안에서 한다.
        // 예기치 못한 예외를 포함해 실패하면 실패한 쪽이 한 번 기록하고, 다시 시도하지 않으며 시뮬레이션도 열지 않는다.
        bool _initializationStarted;
        // PrepareWorld가 만든 월드 공용 컨텍스트이며, 이 컨트롤러도 카탈로그, HUD, 모터 같은 씬 참조를 여기서만 읽는다.
        // 정의나 그래프 결함도 협력 객체와 같은 기록 집합으로 키마다 한 번만 남긴다. PlayMode 테스트가 리플렉션으로 읽으므로 이름을 바꾸지 않는다.
        GameplayWorldContext? _shared;
        GameplayWorldContext Shared => _shared!;
        // 적을 움직이기 시작할 프레임이다. 다시 들어올 때 Surface 등록과 남은 carving 제거가 물리 프레임에 반영되기를 기다린다.
        // 첫 월드 초기화는 하위 씬 활성화와 문 복원이 NavMesh에 반영되도록 _navigationSettleFrames 뒤부터 연다.
        const int _navigationSettleFrames = 2;
        int _navigationReadyFrame;
        // 원작 CharacterSpawnerRoot들이다. 첫 월드 초기화가 찾아 후보를 끄고, 그 뒤 매 프레임 TickSpawns가 판정한다.
        // _levelTime은 원작 LevelManager.LevelTime 자리로, 시뮬레이션이 열린 동안 쌓은 초다.
        OriginalEnemySpawnRoot[] _spawnRoots = Array.Empty<OriginalEnemySpawnRoot>();
        float _levelTime;
        // 사망, 탈출, 튜토리얼 완료로 이 월드를 끝내는 처리를 한 번만 시작한다.
        bool _worldEnding;
        // 사망으로 끝날 때 플레이어 위치에서 내는 원본 오리 사망 음성이다.
        const string _playerDeathVoice = "event:/Char/Voice/vo_duck_death";
        // 종료 결과 화면을 이 월드에서 한 번만 띄운다. '계속'이 거부되면 다시 띄울 수 있게 이 값을 내린다.
        bool _closurePresented;
        // 화면의 분 단위 시계는 원본 배속으로 진행하고 실제 시간 _clockFlushRealSeconds마다 시계 소유자(GameClock)에 기록한다.
        const double _clockFlushRealSeconds = .25;
        double _clockStep, _clockRealStep;
        // 프롤로그와 같은 생존 틱, 갈증과 기아 보정이다. PrepareWorld가 만들며 정리할 것이 없다.
        PlayerSurvival? _survival;
        PlayerSurvival Survival => _survival!;
        // 프롤로그와 같은 생존 HUD 최대치 연결과 매 프레임 갱신이다. 생존 처리와 함께 PrepareWorld가 만들며 정리할 것이 없다.
        PlayerStatusHud? _statusHud;
        PlayerStatusHud StatusHud => _statusHud!;
        // 원작 CostTakerHUD처럼 건설 지점의 이름과 필요 아이템을 보이는 표시기다. 월드 초기화가 HUD 연결 뒤에 만든다.
        CostTakerHud? _costTakerHud;
        ItemOwnershipState? _displayItems;
        ItemShortcutState? _displayShortcuts;
        uint? _displayDeathToken;
        // 퀵슬롯과 장착 무기 칸 표시, 숫자키가 쓸 칸 아이템 조회를 맡는다. PrepareWorld가 만들며 정리할 것이 없다.
        QuickSlotHud? _quickSlots;
        QuickSlotHud QuickSlots => _quickSlots!;
        // 저작 문의 복원, 사건 동기화, 문 소리를 맡는다. PrepareWorld가 만들고 레벨 정리가 문 소리 구독을 푼다.
        // 직렬화한 문 배열 _doors와 이름이 겹치지 않게 _doorSync로 둔다.
        DoorSync? _doorSync;
        DoorSync Doors => _doorSync!;
        // 대사, 상호작용, 사건 제출을 맡는 월드 협력 객체다. PrepareWorld가 만들며, 테스트가 리플렉션으로 이 필드를 읽는다.
        WorldInteractions? _interactions;
        WorldInteractions Interactions => _interactions!;
        // 대사 지점의 대사 진행자다. PrepareWorld가 만들고, 레벨 정리가 진행 중인 대사의 상자, HUD, 억제 토큰을 정리한다.
        DialogueSequenceRunner? _dialogue;
        // 유실물 묘비와 적 시체 지점을 World에서 빌리고 상호작용 객체에 그 목록을 준다. PrepareWorld가 만들고 레벨 정리가 놓는다.
        DeadBodySpawner? _bodies;
        DeadBodySpawner Bodies => _bodies!;
        // 소지 칸이 줄어 넘친 아이템을 바닥 줍기 지점으로 World에서 빌린다. 상호작용 객체는 그 지점의 줍기를 이 객체에 맡긴다.
        // PrepareWorld가 만들고 레벨 정리가 놓는다.
        DroppedItemSpawner? _drops;
        DroppedItemSpawner Drops => _drops!;
        // 소지 칸과 펫 가방 칸 수를 스탯에 맞추고 넘친 아이템을 떨군다. PrepareWorld가 바닥 아이템 생성 객체 바로 다음에 만든다.
        InventoryCapacitySync? _capacity;
        // 몸에 입힌 장비 외형을 확정 그래프에 맞춘다. PrepareWorld가 만들며 정리할 것이 없다.
        EquipmentVisualSync? _equipmentVisuals;
        EquipmentVisualSync EquipmentVisuals => _equipmentVisuals!;
        // 적과 스포너가 월드에 묻고 맡기는 일을 받는 적 파사드다. PrepareWorld가 상호작용 객체 바로 다음에 만들며 정리할 것이 없다.
        OriginalEnemyWorld? _enemyWorld;
        OriginalEnemyWorld EnemyWorld => _enemyWorld!;
        // PrepareWorld가 묶은 이 씬의 펫이다. 레벨 정리가 플레이어 준비보다 먼저 푼다. 펫을 저작하지 않은 씬은 비어 있다.
        PetCharacter? _pet;
        // 하위 씬 앰비언스다. PrepareWorld가 _prepared보다 먼저 만들므로 하위 씬 전환 알림 블록에서는 늘 있다.
        SubsceneAmbience? _ambience;

        // 준비 뒤 확정 세션이 있는 동안에만 부르는 접근자라 세션을 다시 검사하지 않는다.
        // 그동안은 앱도 살아 있으므로 세션을 GameManager에서 바로 읽는다.
        CampaignState Campaign => GameManager.SessionManager!.Snapshot().Campaign;
        // 체력과 생존 수치는 생존 수치 소유자(PlayerVitals)가 가진다. 세션 스냅샷의 플레이어 사본은 갱신되지 않으므로 읽지 않는다.
        // 첫 월드 초기화가 세션 그래프보다 플레이어를 먼저 만들고, 그 뒤에만 부르므로 null을 다시 검사하지 않는다.
        PlayerState Player => Shared.Runtime.Vitals.Current!;
        // 원본 Health.IsDead처럼 체력이 0 이하인지다. 플레이어 GAS 시스템이 체력이 0 이하인 동안 State.Dead를 쥔다.
        // 적 피해와 기아 피해가 모두 생존 수치 소유자에 확정되므로 사망도 그 시스템으로 판정한다.
        bool IsDead => Shared.Runtime.Vitals.Abilities.Tags.HasTag(CharacterTags.Dead);
        // 대사 진행자, 표시음, 월드 종료와 테스트는 준비 뒤에만 읽는다. 플레이어가 파괴됐으면 컨텍스트가 원점을 돌려준다.
        public Vector3 PlayerPosition => Shared.PlayerPosition;
        // 초기화, 내비게이션 준비, 씬 전환, 대사 같은 씬 조건과 Runtime의 게임플레이 활성 판정을 모두 만족해야 한다.
        // 첫 월드 초기화는 컨텍스트를 만든 준비 뒤에만 오고 컨텍스트는 다시 비우지 않으므로, 초기화 뒤에는 컨텍스트를 확인하지 않는다.
        public bool SimulationReady => _initialized && Time.frameCount >= _navigationReadyFrame && !_transitioning &&
            !Interactions.IsDialogueOpen && Shared.Runtime.IsGameplayActive;
        // 앱이 살아 있고 Runtime이 세션을 가졌는지다. Runtime이 세션을 해제했거나 앱 루트가 먼저 파괴된 뒤 씬 정리 전까지는 false다.
        // 컨텍스트를 만든 준비 뒤에만 읽는다.
        bool HasLiveSession => GameManager.HasInstance && Shared.Runtime.HasSession;
        // 이번 프레임의 월드 단계를 돌려도 되는지다. 모터 비교는 Unity null 비교라 이미 파괴된 플레이어도 거른다.
        // 컨텍스트는 _prepared보다 먼저 서고 다시 비우지 않으므로, _prepared를 먼저 보는 순서가 컨텍스트 읽기를 지킨다.
        bool CanTickWorld => _prepared && Shared.Motor != null && HasLiveSession;

        // 레벨 알림 구독은 기반 클래스가 하고, 커스터마이징 열림과 닫힘 알림은 여기서 구독해 OnDestroy에서 푼다.
        // 게임플레이 루트가 씬 로드 때부터 켜져 있어 Awake가 레벨 초기화보다 먼저 온다.
        protected override void Awake()
        {
            base.Awake();
            CustomizationFlow.Opened += OnCustomizationOpened;
            CustomizationFlow.Closed += OnCustomizationClosed;
        }

        // SceneContext가 캐릭터와 월드를 준비한 뒤 기반 클래스가 한 번만 부른다. 캠페인 월드에 충돌 이동기를 붙이고 Runtime과 입력을
        // 연결한 뒤 전투 월드를 준비하고, 켜져 있으면 공용 표시까지 연결한다. 카탈로그의 원본 플레이어 정의가 아바타의 이동과 스태미나 스탯을 정한다.
        // 플레이어 준비는 실패하면 스스로 되돌리고, 성공하면 바로 필드에 기록해 전투 월드 준비가 실패해도 레벨 정리가 되돌린다.
        protected override void OnInitializeLevel()
        {
            // 꺼진 하위 씬의 풀까지 덩어리를 만들어, 장면 진입의 검은 화면 아래에서 모두 굽고 하위 씬을 바꿀 때는 굽지 않게 한다.
            OriginalTerrainGrass.CreateSceneChunks(gameObject.scene);
            // SceneContext는 Gameplay를 채운 뒤에만 레벨 초기화를 알리고 LevelInited를 세운다.
            SceneGameplayContext context = SceneContext.Instance!.Gameplay!;
            // 원본 플레이어 정의는 직렬화 참조가 아니라, SceneContext가 씬 서랍에 로드한 context.Catalog에서 찾는다.
            OriginalItemDefinition playerCharacter = context.Catalog.PlayerCharacter;
            // 입력 실행 사본, 오디오, 옵션 관리자는 레벨 초기화 때 앱에서 한 번 읽어 플레이어 준비와 전투 월드 준비에 함께 넘긴다.
            // 레벨 정리 때는 앱이 먼저 사라졌을 수 있어, 구독을 푸는 쪽은 정적 속성 대신 이렇게 받은 참조를 쓴다.
            InputActionAsset actions = GameManager.InputActions;
            GameAudioManager audio = GameManager.Audio;
            GameOptionsManager options = GameManager.Options;
            // 모터, 같은 루트의 지도, 게임 카메라, 움직임 감소 옵션 구독은 프롤로그와 같은 OriginalPlayerRig가 확보한다.
            // 리그는 원본 CharacterMainControl.SetItem처럼 플레이어를 생존 수치 소유자의 아바타로 연결해 정의의 스탯을 채운다.
            // 그래서 스태미나는 월드에 들어올 때마다 정의의 최대치로 시작하고 저장하지 않는다.
            var rig = new OriginalPlayerRig(this, context, _camera, _locomotion, _focusVolume, playerCharacter, actions, audio, options);
            _rig = rig;
            PrepareWorld(context, rig.Motor, rig.GameCamera, actions, audio, options);
            // 루트가 로드 때부터 켜져 있으면 OnEnable은 준비 전에 지나갔으므로, 공용 표시와 UI 액션을 여기서 연결한다.
            if (isActiveAndEnabled) ActivateLevel();
        }

        // 준비 문맥, 플레이어 준비가 만든 모터와 원본 게임 카메라, 레벨 초기화가 앱에서 읽은 입력, 오디오, 옵션으로 협력 객체를 만든다.
        // 모든 구독은 해제 동작과 함께 _subscriptions에 넣고, 마지막 단계까지 마친 뒤에만 _prepared를 세운다. 중간에 실패해도
        // 레벨 정리가 만든 협력 객체만 골라 되돌린다. 공용 HUD와 GameplayUI는 참조만 받고, 그 연결과 구독은 AttachPresentation이 한다.
        void PrepareWorld(SceneGameplayContext context, OriginalPlayerMotor motor, OriginalGameCamera gameCamera, InputActionAsset actions,
            GameAudioManager audio, GameOptionsManager options)
        {
            // 카탈로그는 SceneContext가 씬 서랍에 로드해 넘긴다. 씬 참조는 아래에서 만드는 컨텍스트에만 두므로 지역 변수로 든다.
            OriginalGameplayCatalog? catalog = context.Catalog;
            SharedHudView hud = context.Hud;
            OriginalGameplayView view = context.GameplayView;
            // 제작 도구가 기록한 씬 ID는 늘 논리 씬이라, 여기서 한 번 변환한 결과를 그대로 쓴다.
            // 하위 씬 루트와 이름은 같은 순서로 짝지어 저작되고, 사망과 탈출로 끝나는 월드는 하위 씬을 가진다.
            GameScenes.TryParse(_sceneId, out GameScene scene);
            _scene = scene;
            GameRuntime runtime = context.Runtime;
            // 게임플레이 입력은 입력 묶음이 앱의 입력 실행 사본에서 찾는다. 하나라도 없으면 다른 협력 객체를 만들기 전에 초기화가 멈춘다.
            // 낚아채기는 원작처럼 대시 키(Player/Jump)로 받는다. 입력 에셋을 바꾸지 않으려고 같은 액션을 쓴다.
            var inputs = new GameplayInputBindings(actions);
            _inputs = inputs;
            // 지연 상호작용 같은 행동 진행 여부는 플레이어 GAS 시스템의 행동 태그로 본다.
            // 앱 오디오 관리자는 여기서 한 번 컨텍스트에 넣어, 협력 객체가 매번 GameManager를 찾지 않게 한다.
            // 상자, 과제 위치, 낚시터가 어느 하위 씬 것인지 판정하도록 하위 씬 루트와 이름, 지금 켠 번호도 컨텍스트에 둔다.
            var shared = new GameplayWorldContext(runtime, catalog, hud, view, motor, context.Character, _camera, gameCamera,
                audio, context.World, _points, _interactables, _doors, _enemies,
                () => SimulationReady, this, _subscenes, _subsceneIds, () => _activeSubscene);
            _shared = shared;
            // 탄종 교체가 소지품에 못 넣은 탄을 떨구므로 바닥 아이템 생성 객체를 전투 시뮬레이션보다 먼저 만든다.
            var drops = new DroppedItemSpawner(shared, transform);
            _drops = drops;
            // 소지 칸과 펫 가방 칸이 줄어 넘친 아이템도 같은 바닥 아이템 생성 객체로 떨군다.
            _capacity = new InventoryCapacitySync(shared, drops);
            // 원작 CancelSkill은 ADS와 같은 우클릭이라 Player/Aim을 스킬 조준 취소에도 넘긴다.
            _combat = new CombatSimulation(shared, inputs.Attack, inputs.Aim, drops);
            // 원작 낚시 행동의 CanEditInventory false처럼 낚는 동안 인벤토리 편집과 사용을 막도록 낚시 여부를 더해 넘긴다.
            // 상호작용 객체는 이 화면을 받아 아래에서 만들어지므로 낚시 여부는 그 필드에서 늦게 읽는다.
            _inventoryScreen = new InventoryScreen(shared, new FishingInventoryActions(_combat, () => _interactions != null && _interactions.IsFishing));
            // 쪽지 창은 앱 수명 공용 HUD의 원본 NoteIndexView를 이 월드가 붙어 있는 동안에만 연다. 다른 화면처럼 월드 문맥으로만 읽는다.
            _notes = new NoteScreen(shared);
            // 기지 화면 묶음도 공용 HUD가 이 월드에 붙어 있는 동안에만 연다. 상호작용 호스트와 ESC, 일지 키가 함께 쓴다.
            // 레이드 상점 구매의 바닥 떨굼에 같은 떨굼 생성기를 쓰고, 화면은 월드를 컨텍스트로만 읽어 컨트롤러를 받지 않는다.
            var screens = new GameplayScreens(shared, drops);
            _screens = screens;
            // 생존 틱과 기아 처리는 프롤로그와 같은 규칙을 쓴다. 기아 피해의 피격 연출은 플레이어 피격 큐를 받는 전투 시뮬레이션이 낸다.
            // 생존 HUD 갱신은 같은 자리에서 만드는 상태 HUD가 맡는다.
            _survival = new PlayerSurvival(runtime, catalog);
            _statusHud = new PlayerStatusHud(runtime, motor, hud);
            // 원본처럼 퀵슬롯 칸, 문, 장비 외형, 묘비와 시체, 바닥 아이템은 각자 상태와 정리를 가진 객체가 맡고, 이 컨트롤러는 그 호출 순서와
            // 정리 순서를 정한다.
            _quickSlots = new QuickSlotHud(shared);
            _doorSync = new DoorSync(shared);
            _equipmentVisuals = new EquipmentVisualSync(shared);
            var bodies = new DeadBodySpawner(shared, _subsceneIds, transform);
            _bodies = bodies;
            // 대사 진행자는 GameplayUI의 공용 대사 상자를 쓰고, 글자 표시음은 플레이어 위치에서 재생한다.
            // 오디오가 꺼져 있으면 관리자가 재생을 조용히 무시한다.
            _dialogue = new DialogueSequenceRunner(view.DialogueBox, hud, motor, inputs.DialogueConfirm, inputs.Attack,
                path => shared.Audio.PlayOneShot(path, PlayerPosition));
            // 상호작용 객체는 F 상호작용을 시작하기 전에 전투 시뮬레이션에서 진행 중인 행동을 확인한다.
            // 시체와 유실물 묘비는 생성 객체가 채우는 목록으로 받고, 바닥 아이템 줍기는 바닥 아이템 생성 객체에 맡긴다.
            // 퀘스트 창구, 상점, 보관함 대상은 화면 묶음으로 연다. 기지 난이도 재선택 출구는 출격처럼 이 컨트롤러의 씬 전환을 넘긴다.
            // 낚시 물고기 추첨이 밤 여부와 날씨를 읽도록 시간대 볼륨도 넘긴다. 시간대가 없는 씬은 null이다.
            _interactions = new WorldInteractions(shared, _inventoryScreen, _notes, screens, _combat, _dialogue, inputs.Interact, DepartAsync,
                ReselectDifficultyAsync, FinishAsync, SwitchSubscene, bodies.RuntimeInteractables, drops, _timeOfDay);
            // 적과 스포너는 이 적 파사드로 전투, 시체, 상호작용 객체와 시간대 볼륨에 닿아 컨트롤러를 거꾸로 참조하지 않는다.
            // 활성 하위 씬은 이 컨트롤러가 바꾸므로 그때그때 읽는 위임으로 넘긴다.
            _enemyWorld = new OriginalEnemyWorld(shared, _combat, bodies, _interactions, _timeOfDay, () => ActiveSubsceneId);
            // 따로 저작한 _navigationData를 이 씬 전용 복제본으로 등록한다. 등록 규칙은 프롤로그와 같은 SceneNavigationData가 맡는다.
            _navigationInstance = SceneNavigationData.Register(this, _navigationData);
            // 원작 PetAI처럼 펫은 플레이어 모터를 주인으로 따라가고 시뮬레이션이 열린 동안에만 움직인다.
            // 원작은 기지 레벨에서 주인 순간 이동을 따라가지 않는다. 펫을 저작하지 않은 씬은 건너뛴다.
            PetCharacter? pet = context.Pet;
            if (pet != null)
            {
                _pet = pet;
                pet.Bind(motor, () => SimulationReady, scene != GameScene.BaseCamp);
            }
            // UI 액션은 입력 묶음이 찾기만 하고 ActivateLevel과 OnDisable에서 켜고 끈다. 나가는 씬은 새 씬을 불러오기 전에 언로드되므로
            // 그 해제가 새 씬이 쓰는 액션을 끄지 않는다. 공용 HUD 명령은 AttachPresentation이 구독하고, 여기서는 인벤토리와 일지 토글, 옵션만 구독한다.
            InventoryScreen screen = Inventory;
            InputAction inventory = inputs.Inventory;
            _subscriptions.Bind(() => inventory.performed += screen.Toggle, () => inventory.performed -= screen.Toggle);
            // 일지 키도 인벤토리 토글처럼 performed로 받는다. 열기와 닫기 조건은 화면 묶음이 원작 OnUIQuestViewInput대로 본다.
            InputAction questView = inputs.QuestView;
            _subscriptions.Bind(() => questView.performed += ToggleQuestView, () => questView.performed -= ToggleQuestView);
            // 옵션 적용에는 키 재바인딩이 들어 있을 수 있어 상호작용 키캡을 다시 계산한다. 레벨 정리 때는 앱이 먼저 사라졌을 수 있어,
            // 정적 속성 대신 레벨 초기화가 잡은 옵션 관리자로 구독을 푼다.
            _subscriptions.Bind(() => options.Applied += inputs.RefreshInteractKey, () => options.Applied -= inputs.RefreshInteractKey);
            // 원작 AudioManager.OnStartedLoadingScene처럼 씬 로드가 시작되면 화면을 덮기 전에 앰비언스를 바로 멈춘다.
            // 레벨 정리는 덮은 뒤에 오므로 따로 구독한다. 소리는 하위 씬 전환이 처음 낸다.
            var ambience = new SubsceneAmbience(audio);
            _ambience = ambience;
            Action<GameScene> stopAmbience = _ => ambience.Stop();
            _subscriptions.Bind(() => SceneFlow.LoadingStarted += stopAmbience, () => SceneFlow.LoadingStarted -= stopAmbience);
            // 원작 SpawnItemForTask 같은 과제 생성 도우미를 레벨 준비 끝에 연결한다(QuestTaskSpawner.Bind 계약). 도우미는 이 루트 아래 하나이고,
            // 과제 아이템은 바닥 아이템 생성 객체의 목록에 넣어 같은 줍기 경로를 쓴다.
            QuestTaskSpawner questTasks = GetComponentInChildren<QuestTaskSpawner>(true);
            if (questTasks != null)
            {
                _questTasks = questTasks;
                // 레벨 준비는 앱이 있을 때만 돌아 퀘스트 소유자를 GameManager에서 바로 읽어 넘긴다.
                questTasks.Bind(shared, GameManager.Quests, drops.SpawnQuestItem);
            }
            // 건설 영역이 지은 건물의 상호작용 대상을 F 탐색 목록에 넣도록 월드에 연결한다(BuildingArea.Bind 계약).
            BuildingArea[] buildingAreas = GetComponentsInChildren<BuildingArea>(true);
            _buildingAreas = buildingAreas;
            for (int i = 0; i < buildingAreas.Length; i++) buildingAreas[i].Bind(shared);
            // 실패할 단계가 남지 않은 마지막에 세운다. 이 값이 서야 Update, OnEnable, 알림 처리가 월드를 다룬다.
            _prepared = true;
        }

        // 준비를 마친 월드가 켜져 있을 때 ActivateLevel이 부른다. 앱 수명 공용 HUD와 GameplayUI를 이 월드에 연결하고
        // 원본 LootView 명령, 결과 화면음, 로캘, 대사 상자를 구독한다. 연결 표시를 먼저 세워 중간에 실패해도 DetachPresentation이 되돌리게 한다.
        void AttachPresentation()
        {
            _presentationAttached = true;
            // 공용 HUD와 GameplayUI는 준비 때 만든 컨텍스트만 가지고 있어 거기서 읽는다.
            GameplayWorldContext shared = Shared;
            SharedHudView hud = shared.Hud;
            hud.Attach(this);
            shared.View.Attach(this);
            // 원본 LootView의 버튼, 셀 클릭, 끌어 놓기 명령은 인벤토리 화면이 자기 처리기를 이 표시 구독 묶음에 건다.
            Inventory.BindPanel(_presentationSubscriptions);
            // 공용 HUD의 결과 화면음도 앱 오디오 관리자로 재생한다. 대사 표시음은 대사 진행자가 맡는다.
            _presentationSubscriptions.Bind(() => hud.Result.PresentationSound += PresentationSound,
                () => hud.Result.PresentationSound -= PresentationSound);
            _presentationSubscriptions.Bind(() => LocalizationSettings.SelectedLocaleChanged += LocaleChanged,
                () => LocalizationSettings.SelectedLocaleChanged -= LocaleChanged);
            _dialogue!.Attach();
            // 과제 완료 알림은 공용 HUD가 이 월드에 붙어 있는 동안만 듣는다. 해제는 DetachPresentation이 한다.
            _screens!.Attach();
            // 원작 Tabs/ViewButtons 탭 단추의 의도를 받는다. 단추 줄 도구(Author Shared HUD View Buttons)가 따로 연결하므로 연결 전 HUD에서는 건너뛴다.
            OriginalViewButtonsView viewButtons = hud.ViewButtons;
            if (viewButtons != null)
            {
                _viewButtons = viewButtons;
                _presentationSubscriptions.Bind(() => viewButtons.TabRequested += OpenViewTab, () => viewButtons.TabRequested -= OpenViewTab);
            }
        }

        // 일지 키 입력이다. 레벨 정리 뒤 늦게 온 입력은 준비 표시로 거르고, 나머지 조건은 화면 묶음이 본다.
        void ToggleQuestView(InputAction.CallbackContext context)
        {
            if (_prepared) _screens!.ToggleJournal();
        }

        // 원작 ViewsProxy.ShowInventoryView, ShowQuestView, ShowMapView, ShowKeyView와 NoteIndexView.DoOpen이다. 탭 단추 줄(OriginalViewButtonsView)이 알린다.
        // 원작 View.OnOpen처럼 지금 열린 탭 화면(인벤토리, 쪽지, 화면 묶음)을 먼저 닫고 고른 화면을 연다. 단추 줄은 탭 화면이 열린 동안에만 보이고,
        // 일시정지, 대사, 씬 전환 중의 늦은 클릭은 SimulationReady로 거른다. 능력치 단추는 제품 화면이 없어 누를 수 없으므로 오지 않는다.
        void OpenViewTab(ViewTab tab)
        {
            if (!_prepared || !GameManager.HasInstance || !SimulationReady) return;
            switch (tab)
            {
                case ViewTab.Inventory:
                    CloseTabScreensExcept(tab);
                    Inventory.ShowCarried();
                    break;
                case ViewTab.Quest:
                    CloseTabScreensExcept(tab);
                    _screens!.ShowJournal();
                    break;
                case ViewTab.Key:
                    CloseTabScreensExcept(tab);
                    _screens!.ShowMasterKeysIndex();
                    break;
                case ViewTab.Notes:
                    // 쪽지 단추도 제품 쪽지 창이 있어 잇는다. 다른 탭 화면의 입력 차단을 먼저 돌려줘야 쪽지 목록이 겹침 검사를 지난다.
                    CloseTabScreensExcept(tab);
                    _notes!.ShowIndex();
                    break;
                case ViewTab.Map:
                    // 지도는 앱 공통 UI(GameUiController)의 지도 화면이 맡는다. 지도 문맥이 없는 씬처럼 열 수 없으면 지금 화면을 닫지 않는다.
                    GameUiController ui = GameManager.Ui;
                    if (!ui.CanOpenMap) break;
                    CloseTabScreensExcept(tab);
                    ui.OpenMap();
                    break;
            }
        }

        // 고른 탭 말고 열린 탭 화면을 인벤토리, 쪽지, 화면 묶음 순으로 닫는다. 인벤토리 닫기는 HUD의 Tabs를 무조건 끄므로 열려 있을 때만
        // 부른다. 쪽지 창과 화면 묶음의 닫기는 닫힌 화면에서 아무것도 하지 않는다. 일지와 열쇠 도감은 화면 묶음의 Show가 다른 화면을 스스로 닫는다.
        void CloseTabScreensExcept(ViewTab keep)
        {
            if (keep != ViewTab.Inventory && Inventory.IsOpen) Inventory.Close();
            if (keep != ViewTab.Notes) _notes!.Close();
            if (keep != ViewTab.Quest && keep != ViewTab.Key) _screens!.Close();
        }

        // 원작 ViewTabDisplayEntry가 강조하는 탭이다. 원작 지도(MiniMapView)는 탭 영역 안이지만 제품 지도는 앱 공통 UI의 모달이라 단추 줄이 보이지 않아 넣지 않는다.
        // 인벤토리는 원작 InventoryView와 LootView를 한 화면(LootView)으로 쓰므로 열려 있으면 늘 가방 단추를 강조한다.
        ViewTab CurrentViewTab() => Inventory.IsOpen ? ViewTab.Inventory : _notes!.IsOpen ? ViewTab.Notes : _screens!.OpenTab;

        // OnDisable과 레벨 정리 첫 단계에서 AttachPresentation이 연결한 것을 되돌린다. 여러 번 불러도 되고 던지지 않는다.
        // 연결 표시를 먼저 내리고, 단계마다 Cleanup.Run이 실패를 기록한 뒤 계속한다. HUD와 View의 연결 해제는 이 월드가 소유자일 때만 동작하고,
        // Play Mode 종료로 먼저 파괴된 HUD와 View는 Unity null 검사로 건너뛴다.
        void DetachPresentation()
        {
            if (!_presentationAttached) return;
            _presentationAttached = false;
            InventoryScreen? screen = _inventoryScreen;
            DialogueSequenceRunner? dialogue = _dialogue;
            // 연결은 준비 뒤에만 시작하므로 컨텍스트가 있어야 하지만, 이 정리는 던지지 않도록 없으면 HUD와 View 해제만 건너뛴다.
            GameplayWorldContext? shared = _shared;
            OriginalGameplayView? view = shared != null ? shared.View : null;
            SharedHudView? hud = shared != null ? shared.Hud : null;
            if (screen != null) Cleanup.Run(screen.Close, this);
            // 쪽지 창도 HUD를 떼기 전에 닫아 입력 차단과 구독을 돌려준다.
            NoteScreen? notes = _notes;
            if (notes != null) Cleanup.Run(notes.Close, this);
            // 기지 화면 묶음도 HUD를 떼기 전에 알림 구독을 풀고 열린 화면을 닫는다.
            GameplayScreens? screens = _screens;
            if (screens != null) Cleanup.Run(screens.Detach, this);
            if (dialogue != null) Cleanup.Run(dialogue.Detach, this);
            // 탭 단추 줄의 의도 구독은 아래 구독 정리가 풀고, 강조는 HUD의 Detach가 지운다. 다음 프레임부터 강조를 쓰지 않게 참조를 먼저 비운다.
            _viewButtons = null;
            Cleanup.Run(_presentationSubscriptions.Clear, this);
            if (view != null) Cleanup.Run(() => view.Detach(this), this);
            if (hud != null) Cleanup.Run(() => hud.Detach(this), this);
        }

        // 씬의 프레임 순서만 정한다. 단계가 true를 돌려주면 이번 프레임을 끝낸다.
        // 조건부 문은 매 프레임이 아니라 확정 세션의 변경 알림에서만 문 동기화 객체가 연다.
        void Update()
        {
            // 준비 전이거나, 플레이어가 파괴됐거나, 읽을 세션이 없으면 기록 없이 건너뛴다.
            // 아래 단계는 세션과 GameManager 정적 속성이 있다고 보고 읽는다.
            if (!CanTickWorld) return;
            if (!_initialized && !TryInitializeWorld()) return;
            // 원본 BaseSceneUtilities처럼 Alt를 누르면 기지 자동 저장을 요청한다. 일시정지 중에도 알리며, 기지 밖에는 자동 저장이 없어 무시된다.
            // ESC는 GameUiController가 알린다. 요청은 기지 씬의 자동 저장 컴포넌트(BaseAutoSaver)가 받는다.
            if (Keyboard.current?.altKey.wasPressedThisFrame == true) BaseAutoSaver.NotifyUiActivity();
            TickClock();
            // 원본 시간대 컨트롤러처럼 시계가 단계 경계를 넘거나 날씨가 바뀌면 조명 프로필을 섞는다. 일시정지 중에는 deltaTime이 0이라 섞기도 멈춘다.
            // 날씨 FX는 원본처럼 실내 하위 씬에서 꺼지므로 지금 하위 씬을 넘긴다.
            if (_timeOfDay != null) _timeOfDay.Tick(Time.deltaTime, ActiveSubsceneId);
            // 원작 스포너처럼 같은 프레임의 시각과 날씨로 스폰 조건을 본다. 수면 뒤 바뀐 날씨도 바로 반영하려고 시간대 틱 뒤에 둔다.
            TickSpawns();
            // 생존 틱은 일시정지, 결과 화면, 세션 없음 외에 이 씬의 전환 중에도 멈춘다. 프롤로그는 전환 조건을 넘기지 않는다.
            Survival.Tick(_transitioning);
            // 원본 HUD 컴포넌트처럼 Update에서 갱신하며 프롤로그도 같은 자리에서 한다. 결과 화면과 일시정지 중에도 갱신한다.
            // 조준 대상 판정은 여기서 하지 않고 발사 때 한다. 갱신은 생존 처리와 따로 만든 상태 HUD가 맡는다.
            // 탄창 수를 먼저 읽어야 소지 수 문자열이 같은 프레임 값이 된다. C# 인수는 왼쪽부터 계산된다.
            StatusHud.RefreshHud(Player, Combat.AmmoText(), Combat.AmmoReserveText, Combat.Scatter, Combat.WeaponIndex);
            // 원본 ItemShortcutButton.Update처럼 손에 든 스킬 아이템의 퀵슬롯 칸 표시도 매 프레임 맞춘다. 바뀐 칸만 HUD에 보낸다.
            QuickSlots.ShowHeld(Campaign.Gameplay, Combat.HeldSkillItem);
            // 원작 ViewTabDisplayEntry처럼 지금 열린 탭 화면의 단추를 강조한다. 원작은 View 열림·닫힘 알림으로 바꾸지만 제품 화면은 알림이 없어
            // 매 프레임 지금 탭을 넘기고, 단추 줄은 바뀐 때만 쓴다. 아래 단계가 이번 프레임을 끝내기 전에 한다.
            OriginalViewButtonsView? viewButtons = _viewButtons;
            if (viewButtons != null) viewButtons.SetSelected(CurrentViewTab());
            // 원작 HUDManager처럼 상점, 보관함 같은 화면이 열린 동안 HUD를 숨긴다. 화면 묶음은 열림 알림이 없어 매 프레임 넘긴다.
            Shared.Hud.SetScreenOpen(_screens!.IsOpen);
            // 원작 CostTakerHUD_Entry.LateUpdate처럼 건설 지점 표시의 위치, 거리 판정, 가진 수를 매 프레임 맞춘다.
            _costTakerHud!.Tick();
            // 원작 InteractHUD.Update처럼 아래 단계가 이번 프레임을 끝내기 전에 전역 셰이더 값 Interactable을 맞춘다.
            // 머리 위 퀘스트 표시가 입력이 막히거나 행동 중인 프레임에 사라져야 해서, 상호작용 탐색보다 먼저 매 프레임 쓴다.
            Interactions.TickInteractableShader();
            // 원본 BulletTypeHUD는 게임플레이 입력이 꺼지면 탄종 목록을 닫는다. 아래 넷 중 하나가 입력을 건너뛰게 하는 프레임에 닫는다.
            if (TickGates() || Interactions.TickDialogue() || TickSimulation() || TickInventoryInput())
            {
                Combat.CloseBulletTypes();
                return;
            }
            // 건설 화면은 열린 동안 매 프레임 배치 입력과 미리보기를 갱신한다. 플레이어 입력보다 먼저 돈다.
            _screens!.Tick();
            TickGameplayInput();
        }

        // 시뮬레이션이 멈춘 동안에는 시계를 누적하지 않는다. 시계가 기록을 거절하면 누적값을 지우지 않고 다음 프레임에 함께 보낸다.
        void TickClock()
        {
            if (!SimulationReady) return;
            _clockStep += Time.deltaTime; _clockRealStep += Time.unscaledDeltaTime;
            // 시계 소유자가 세션 명령 게이트를 직접 거치므로 여기서는 세션 상태를 다시 검사하지 않는다.
            // 매 프레임 경로이고 Update가 앱이 있을 때만 오므로 GameManager에서 바로 읽는다.
            if (_clockRealStep >= _clockFlushRealSeconds && GameManager.Clock.TryAdvance(_clockStep, _clockRealStep))
            { _clockStep = 0; _clockRealStep = 0; }
        }

        // 원작 CharacterSpawnerRoot.Update다. 시뮬레이션이 멈춘 동안에는 레벨 시간도 판정도 멈춘다. 시각은 시간대 볼륨과 같은 시계 값이고,
        // 날씨는 시간대 볼륨이 이번 프레임에 고른 값이다. 시간대가 없는 씬은 원작 기본 날씨인 맑음으로 본다.
        void TickSpawns()
        {
            if (_spawnRoots.Length == 0 || !SimulationReady) return;
            _levelTime += Time.deltaTime;
            // 시각은 아직 기록하지 않았으면 기본 시각으로 보고, 시와 초 변환은 시계 상태의 규칙을 쓴다.
            double hour = GameManager.Clock.Current.SecondsOfDayOrDefault() / GameplayClockState._secondsPerHour;
            int weather = _timeOfDay != null ? (int)_timeOfDay.CurrentWeather : (int)OriginalWeather.Sunny;
            DifficultyState? difficulty = GameManager.Difficulty.Current;
            GameplayWorldContext shared = Shared;
            // 스폰 루트는 적 파사드로 시드와 월드 판정을 읽어 컨트롤러를 거꾸로 참조하지 않는다.
            OriginalEnemyWorld enemyWorld = EnemyWorld;
            foreach (OriginalEnemySpawnRoot root in _spawnRoots) root.Tick(enemyWorld, shared, difficulty, hour, _levelTime, weather);
        }

        // 시뮬레이션을 진행할 수 없으면 true를 돌려준다. 종료 상태면 결과 화면을 한 번 띄우고, 게임플레이 비활성이나 씬 전환 중이면
        // 상호작용 안내를 숨기며, 체력이 0이면 사망으로 월드를 끝낸다. 적 근접 피해는 적 Update에서 들어오므로 다음 프레임의 이 검사가 판정한다.
        bool TickGates()
        {
            // 매 프레임 불리므로 컨텍스트 준비 검사를 한 번만 거친다.
            GameplayWorldContext shared = Shared;
            GameRuntime runtime = shared.Runtime;
            if (runtime.SessionState == GameSessionState.Closure)
            {
                shared.Hud.HideInteraction();
                // 결과 화면은 월드 종료 명령이 끝나 Runtime이 한가해진 뒤에 한 번 띄운다.
                if (!_closurePresented && !runtime.IsBusy)
                {
                    _closurePresented = true;
                    PresentClosureAsync().Forget();
                }
                return true;
            }
            if (!runtime.IsGameplayActive || _transitioning) { shared.Hud.HideInteraction(); return true; }
            if (IsDead) { FinishAsync(RaidOutcome.Died).Forget(); return true; }
            return false;
        }

        // 탄환을 진행한 직후 체력이 0이면 사망으로 끝내고 true를 돌려준다. 같은 프레임에서는 사망이 철수와 지연 상호작용 완료보다 먼저다.
        // 원본 CountDownArea처럼 인벤토리나 지도가 열려도 철수 카운트다운이 진행되도록 여기서 판정한다.
        // 철수를 시작했거나 벙커 출입, 하위 씬 이동 같은 지연 상호작용이 진행 중이면 true를 돌려준다.
        bool TickSimulation()
        {
            Combat.TickProjectiles();
            // 탄 피해가 방금 확정된 생존 수치 소유자의 사망 태그로 판정한다.
            if (IsDead) { FinishAsync(RaidOutcome.Died).Forget(); return true; }
            if (Interactions.TickExtraction()) return true;
            Combat.TickAction();
            if (Interactions.TickTimedInteraction()) return true;
            // 원작 낚시 행동은 인벤토리를 열어도 계속되므로 인벤토리 게이트보다 먼저 진행한다. 낚는 동안에는 무기 전환, 퀵슬롯,
            // F 탐색, 사격을 건너뛴다.
            // 원작 InputManager.Dash는 InputActived일 때만 TryCatchFishInput을 부른다. 그래서 모달 입력 차단이 없고 창에 포커스가 있을 때만
            // Player/Jump 누름을 낚아채기로 넘긴다. 낚는 동안 모터는 낚시 억제 토큰 때문에 같은 누름으로 대시하지 않는다.
            bool catchPressed = !Shared.Runtime.GameplayInput.IsBlocked && Application.isFocused && Inputs.CatchFish.WasPressedThisFrame();
            return Interactions.TickFishing(catchPressed);
        }

        // 인벤토리 화면이 열려 있으면 true를 돌려준다. 열린 동안 숫자키는 포인터가 올라간 무기 아닌 소지품을 저장 그래프에 등록하므로 재시작 뒤에도 남는다.
        // 열린 전리품이 범위를 벗어나면 화면을 닫는다.
        bool TickInventoryInput()
        {
            if (!Inventory.IsOpen) return false;
            // 같은 단축키를 인벤토리에서는 칸 등록에 쓴다. 칸 수와 눌림은 입력 묶음이 맡는다.
            GameplayInputBindings inputs = Inputs;
            for (int i = 0; i < GameplayInputBindings._quickSlotCount; i++) if (inputs.QuickSlotPressed(i)) Inventory.BindShortcut(i);
            Inventory.CloseIfOutOfRange();
            return true;
        }

        // 입력이 막혔거나 포커스를 잃었으면 상호작용 안내만 숨긴다. 무기 선택과 장전은 아이템 사용 중이면 받지 않는다.
        // 퀵슬롯 사용은 원본 행동 우선순위대로 장전 중이면 장전을 취소하고 시작하고, 다른 사용 중이면 거절한다.
        // 원본 SetTrigger가 InputActived일 때만 발사하듯, 해제 뒤 2프레임 대기를 포함한 모터 입력 억제 중에는 사격하지 않는다.
        void TickGameplayInput()
        {
            // 매 프레임 불리므로 컨텍스트와 입력 묶음 준비 검사를 한 번만 거친다. 모달 입력 차단은 Runtime의 입력 차단 담당이 센다.
            GameplayWorldContext shared = Shared;
            GameplayInputBindings inputs = Inputs;
            // 원본처럼 입력이 막히면 탄종 목록을 닫는다. 포커스만 잃은 동안은 목록을 그대로 둔다.
            bool blocked = shared.Runtime.GameplayInput.IsBlocked;
            if (blocked || !Application.isFocused)
            {
                shared.Hud.HideInteraction();
                if (blocked) Combat.CloseBulletTypes();
                return;
            }
            if (inputs.PrimaryWeapon.WasPressedThisFrame()) Combat.SelectWeapon(GameplayWeaponSlots._primary);
            if (inputs.SecondaryWeapon.WasPressedThisFrame()) Combat.SelectWeapon(GameplayWeaponSlots._secondary);
            if (inputs.MeleeWeapon.WasPressedThisFrame()) Combat.SelectWeapon(GameplayWeaponSlots._melee);
            if (inputs.Reload.WasPressedThisFrame()) Combat.StartReload();
            for (int i = 0; i < GameplayInputBindings._quickSlotCount; i++)
                if (inputs.QuickSlotPressed(i))
                {
                    // 칸 조회는 퀵슬롯 표시와 같은 규칙을 쓰고 던지지 않는다. 구조 결함이면 칸마다 한 번 기록하고 이 칸은 사용하지 않는다.
                    // 원작 ShortCutInput처럼 쓸 수 있으면 사용하고 스킬 아이템이면 손에 드는 판단은 전투 시뮬레이션이 한다.
                    if (QuickSlots.TryEntry(Campaign.Gameplay, i, out ItemInstanceState? entry) && entry != null) Combat.TryUseShortcut(entry.InstanceId);
                }
            // 휠 한 칸은 입력 묶음이 원본 UpdateScollerInput 규칙으로 정한다. 탄종 목록과 상호작용 묶음 항목에 같은 휠을 함께 넘긴다.
            // 같은 프레임에 장전이나 사용이 시작됐으면 원본 CanOpenList처럼 목록이 닫힌다.
            int direction = inputs.ReadScrollStep();
            Combat.TickBulletTypes(inputs.SwitchBulletType.WasPressedThisFrame(), direction);
            // 원본 InputManager.SetSwitchInteractInput처럼 묶음 항목은 방향을 뒤집어, 휠 위는 앞 항목, 아래는 다음 항목을 고른다.
            if (direction != 0) Interactions.SwitchInteractable(-direction);
            // 상호작용 안내 키캡은 입력 묶음이 옵션 적용 때마다 다시 계산한 값이다.
            Interactions.TickPoints(inputs.InteractKey);
            // 사격 차단은 입력 억제 토큰을 가진 컨텍스트의 모터가 판정한다.
            if (!shared.Motor.InputSuppressed) Combat.TickAttack();
        }

        // 확정 세션이 이 월드에 들어온 첫 프레임의 초기화를 시도한다. 모든 단계가 끝난 뒤에만 세션 변경을 구독하고 _initialized를 세운다.
        // 게이트를 통과하면 _initializationStarted로 고정하므로, 예기치 못한 예외도 Unity가 한 번 기록하고 매 프레임 반복하지 않는다.
        // 릴리즈는 단계가 실패해도 던지지 않는다. 그래프 결함은 키마다 한 번 기록하고 건너뛴다.
        // 에디터와 DEBUG는 Log.Instance.Fail 보고가 예외를 던져 이 초기화를 멈춘다.
        bool TryInitializeWorld()
        {
            // 씬 참조와 Runtime은 준비 때 만든 컨텍스트에서 한 번 읽어 아래 단계에 함께 쓴다.
            // 세션은 GameManager가 내주며, Update가 앱이 있을 때만 이 단계에 온다.
            GameplayWorldContext shared = Shared;
            GameRuntime runtime = shared.Runtime;
            OriginalGameplayCatalog catalog = shared.Catalog;
            GameSessionManager? session = GameManager.SessionManager;
            // 일시적인 거절은 기록 없이 다음 프레임에 다시 시도한다.
            if (!CanStartWorldInit(runtime, session)) return false;
            // 여기부터의 실패는 카탈로그나 저장 소유물의 결함이라 다시 해도 같다. 실패한 쪽이 한 번 보고하므로 다시 시도하지 않고
            // 시뮬레이션도 열지 않는다.
            _initializationStarted = true;
            // 저장 그래프가 있으면 플레이어도 있어야 하므로, 이 명령은 생존 수치 소유자의 플레이어를 먼저 만들고 세션 그래프를 초기화한다.
            if (!GameplayCommands.TryInitializeWorld(session, runtime.Vitals, catalog)) return false;
            // 저장된 장비의 GAS 효과를 체력 회복, HUD 최대치 연결, 소지 칸 맞추기보다 먼저 건다. 늦으면 최대 체력이 장비 없이 채워지고,
            // 가방이 늘린 칸이 기본 칸 수로 줄어 아이템이 바닥에 떨어진다. 세션 그래프는 바로 위 명령이 만든다.
            runtime.Vitals.SyncEquipmentEffects(session.Snapshot().Campaign.Gameplay.Inventory.Items, catalog);
            // 원작 레벨 초기화 때의 특성 동작 재적용(상점 해금, 창고 확장, 제조식 해금)과 특성 스탯, 고른 재능 효과도 체력 회복과
            // HUD 최대치 연결보다 먼저 맞춘다. 늦으면 최대 체력이 특성과 재능 없이 채워진다. 셋 다 여러 번 불러도 같다.
            GameplayCommands.RestorePerkEffects(session, GameManager.Economy, GameManager.Perks, GameManager.Crafting, catalog);
            runtime.Vitals.SyncPerkStats(GameManager.Perks);
            runtime.Vitals.SyncEndowmentEffect(GameManager.Endowment.SelectedEffect);
            // 원본 LevelManager.InitLevel처럼 기지와 제로존에 들어오면 체력을 최대로 채운다. 다른 장소면 소유자가 아무것도 하지 않는다.
            // 장소는 위 게이트가 이 월드의 씬과 같음을 확인한 세션의 현재 장소다.
            runtime.Vitals.RestoreHealthOnLevelEntry(catalog, session.Snapshot().SceneId);
            // 하위 씬 활성화와 문 복원이 NavMesh에 반영되도록 시뮬레이션은 _navigationSettleFrames 뒤부터 연다.
            _navigationReadyFrame = Time.frameCount + _navigationSettleFrames;
            // 플레이어 GAS 어트리뷰트의 최대 체력, 스태미나, 에너지, 수분과 추적 대상을 상태 HUD가 공용 HUD에 연결한다.
            // 체력바는 스폰 후보 전체가 아니라 나온 적만 돌도록 적 파사드의 목록을 넘긴다. 아래에서 준비하는 적도 같은 목록에 들어온다.
            StatusHud.BindHud(shared.Camera, EnemyWorld.SpawnedEnemies);
            // 건설 지점 표시 항목도 같은 공용 HUD에 지점마다 만든다. HUD를 떼면 HUD가 항목을 지운다.
            _costTakerHud = new CostTakerHud(shared);
            // 원본처럼 처음에는 첫 하위 씬 하나만 켠다.
            if (_subscenes.Length > 0) SwitchSubscene(0);
            // 원작 LevelManager.InitLevel처럼 첫 하위 씬을 켠 뒤 지도 선택 출격이면 원작 출발점으로 옮긴다. 기지도 불러 남은 출격 방식을 비운다.
            // 출발점 추첨은 적 스폰과 같은 적 파사드의 시드 규칙을 쓴다.
            Interactions.PlaceAtRaidStart(TryGetComponent(out OriginalStartPoints starts) ? starts : null, EnemyWorld.Seed);
            // 원작 LootBoxLoader처럼 무작위 상자의 생성 여부와 내용을 정한다. 시드는 출격과 상자마다 같아 다시 불러도 같은 칸이다.
            Interactions.SetupLootBoxes(EnemyWorld.Seed);
            // 원작 스포너는 조건이 맞을 때 캐릭터를 만든다. 그래서 아래 적 준비보다 먼저 스폰 루트의 후보를 모두 끄고, 스폰은 TickSpawns가 한다.
            _spawnRoots = GetComponentsInChildren<OriginalEnemySpawnRoot>(true);
            foreach (OriginalEnemySpawnRoot spawnRoot in _spawnRoots) spawnRoot.Begin(_scene == GameScene.BaseCamp);
            // 적 체력과 반응 배율에 쓰는 난이도는 GameManager가 내주는 난이도 소유자(DifficultyRules)의 현재 값이다.
            DifficultyState? difficulty = GameManager.Difficulty.Current;
            // 적은 적 파사드로 월드에 닿는다. 준비 반복문이 끝나면 튜토리얼 하위 씬 전환이 준비된 적만 켜도록 표시를 세운다.
            OriginalEnemyWorld enemyWorld = EnemyWorld;
            foreach (OriginalEnemyController enemy in _enemies)
                if (enemy.gameObject.activeInHierarchy) enemy.Prepare(enemyWorld, catalog, difficulty);
            _enemiesPrepared = true;
            Combat.RefreshEquipment();
            // 장비 표시 뒤의 확정 상태로 이 월드 하위 씬에 속한 저장 유실물의 묘비를 빌린다.
            GameplayPersistenceState gameplay = Campaign.Gameplay;
            Bodies.Refresh(gameplay.Deaths.LostBodies);
            // 새로 만든 캐릭터에 저장된 장비 외형을 처음 입힌다. 기억을 비워 모든 부위를 한 번 적용한다.
            EquipmentVisuals.Reset();
            EquipmentVisuals.Apply(gameplay.Inventory.Items);
            // 첫 표시는 퀵슬롯 6칸과 무기 3칸을 모두 그린다.
            QuickSlots.Refresh(gameplay, true, true);
            // 저장된 문 상태를 소리 없이 복원한 뒤 문 소리를 구독한다. 그 구독은 문 동기화 객체가 가지고 레벨 정리가 푼다.
            Doors.Restore();
            // 원작 SetTimeOnEnterBaseFirstTime.Start처럼 이 저장으로 기지에 처음 들어왔을 때만 그 시각까지 시계를 넘긴다.
            // 시간대 시작보다 먼저 맞춰야 새 시각의 날씨와 조명으로 시작한다. 이미 맞춘 저장이면 소유자가 거절하고 아무것도 바꾸지 않는다.
            if (_firstVisitHour >= 0) GameManager.Clock.TryApplyFirstBaseVisit(_firstVisitHour);
            // 원본 TimeOfDayController.Start처럼 레벨에 들어올 때 고정 시각을 맞추고 날씨와 지금 단계의 조명 프로필을 바로 적용한다.
            // 폭풍 알림은 원본 NotificationText처럼 공용 HUD로 띄우고, 시계와 날씨 소리는 컨텍스트의 Runtime과 앱 오디오 관리자로 읽는다.
            if (_timeOfDay != null) _timeOfDay.Begin(shared.Notify, shared.Runtime, shared.Audio);
            // 실패할 단계가 남지 않은 마지막에 구독한다. 해제는 세션이 교체돼도 구독한 그 Manager에서 한다.
            _subscriptions.Bind(() => session.Changed += SessionChanged, () => session.Changed -= SessionChanged);
            // 원작 EXPManager.onLevelChanged의 레벨업 알림처럼, 처치와 퀘스트 보상이 올린 레벨을 이 월드가 공용 HUD로 알린다.
            // 정적 이벤트라 레벨 정리가 다른 구독과 함께 풀어야 다음 월드에 알림이 두 번 뜨지 않는다.
            _subscriptions.Bind(() => ExperienceManager.LevelChanged += LevelChanged, () => ExperienceManager.LevelChanged -= LevelChanged);
            // 특성 확정과 재능 선택은 이 월드가 열린 동안 기지 화면에서 바뀐다. 정적 이벤트라 레벨 정리가 다른 구독과 함께 푼다.
            _subscriptions.Bind(() => PerkTreeManager.Changed += PerksChanged, () => PerkTreeManager.Changed -= PerksChanged);
            _subscriptions.Bind(() => EndowmentManager.Changed += EndowmentChanged, () => EndowmentManager.Changed -= EndowmentChanged);
            _initialized = true;
            return true;
        }

        // 첫 월드 초기화를 시작할 수 있는지다. 확정 세션이 이 월드의 씬에 있고, 세션과 상태 소유자가 모두 명령을 받으며,
        // 아직 초기화를 시작하지 않았을 때만 true다.
        bool CanStartWorldInit(GameRuntime runtime, GameSessionManager? session) =>
            !_initializationStarted && runtime.State == GameRuntimeState.Running && !runtime.IsBusy && session != null &&
            session.AcceptsCommands && runtime.AcceptsSessionCommands && session.Snapshot().SceneId == _scene;

        // ESC 같은 씬 질의를 받아, 준비된 월드에 열린 인벤토리나 쪽지 창이 있으면 닫고 true를 돌려준다.
        // 퀘스트 창구, 상점, 보관함, 일지도 같은 ESC 경로로 닫는다(화면 묶음).
        public bool TryCancelPanel() => _prepared && (Inventory.TryClose() || _notes!.TryClose() || _screens!.TryClose());
        // 게임 카메라 입력을 켤지 정할 때 읽는다. 대사가 열려 있으면 입력을 비활성으로 본다.
        bool DialogueOpen => _prepared && Interactions.IsDialogueOpen;

        // 원작 SetActiveByPlayerDistance.FixedUpdate처럼 물리 틱마다 먼 적을 끈다. 튜토리얼은 첫 하위 씬 밖에서
        // SwitchSubscene이 적을 모두 꺼 두므로 그동안 갱신하지 않는다.
        void FixedUpdate()
        {
            if (SimulationReady && (_scene != GameScene.Tutorial || _activeSubscene == 0)) EnemyWorld.TickDistance();
        }

        // 원작 CharacterMainControl.LateUpdate처럼 프레임 끝에 소지 칸 수를 InventoryCapacity 스탯에 맞추고,
        // 원작 PetProxy처럼 펫 가방 칸 수도 맞춘다. 이어서 플레이어 이동 뒤 같은 프레임에 원본 게임 카메라를 진행하고,
        // 원본 GameCamera 아래에 있던 날씨 FX를 새 추종점에 둔다. 시뮬레이션이 준비된 동안에는 칸 맞추기 객체가 늘 있다.
        void LateUpdate()
        {
            if (SimulationReady) _capacity!.Tick();
            if (_rig == null) return;
            _rig.Tick(DialogueOpen);
            if (_timeOfDay != null) _timeOfDay.PlaceWeatherFx(_rig.GameCamera.FollowPoint);
        }

        // 이 씬의 확정 세션이 바뀌면 문부터 동기화한다. 사건만 바뀐 알림은 아래 참조 비교에서 걸러지므로 그 앞에서 해야 한다.
        void SessionChanged(GameSessionSnapshot snapshot)
        {
            // 아래 갱신은 세션을 GameManager에서 다시 읽으므로, 앱 루트가 먼저 파괴된 뒤 온 알림은 건너뛴다.
            if (snapshot.SceneId != _scene || !GameManager.HasInstance) return;
            Doors.Sync();
            var gameplay=snapshot.Campaign.Gameplay;
            // 유실물은 목록 참조가 바뀐 때만 확정 세션의 현재 목록으로 다시 맞춘다. 그래서 소유 그래프만 바뀌는 발사나 장전은 유실물을 다시 계산하지 않는다.
            if (!Bodies.Shows(gameplay.Deaths.LostBodies)) Bodies.Refresh(Campaign.Gameplay.Deaths.LostBodies);
            // 소유물, 단축키, 사망 토큰이 그대로면 사건만 바뀐 알림이라 인벤토리 칸과 드래그 연결을 다시 만들지 않는다.
            // 시계, 지도 마커, 경험치, 난이도, 체력과 생존 수치는 각 소유자가 따로 알리므로 이 알림에 오지 않는다.
            ItemOwnershipState? items=gameplay.Inventory.Items;
            if(DisplayUnchanged(gameplay,items))return;
            // 발사처럼 수량만 바뀐 그래프는 장착 슬롯이 그대로라 든 무기, 무기 아이콘, 장비 효과와 외형을 다시 맞추지 않는다.
            // 수량은 아래 인벤토리 화면과 퀵슬롯 칸이 다시 읽는다. 장비 효과는 화면의 최대 무게가 읽으므로 화면보다 먼저 맞춘다.
            bool sameShape=_displayItems!=null&&items!=null&&items.SharesPositions(_displayItems);
            _displayItems=items;_displayShortcuts=gameplay.Inventory.Shortcuts;
            _displayDeathToken=gameplay.Deaths.DeadCharacterToken;
            if(!sameShape){Combat.RefreshEquipment();SyncEquipment(items);}
            Inventory.Refresh();
            // 퀵슬롯은 확정 세션의 현재 소유 그래프에서 값이 바뀐 칸만 HUD에 다시 보낸다. 무기 칸은 장착 슬롯이 바뀔 수 있을 때만 읽는다.
            QuickSlots.Refresh(Campaign.Gameplay, false, !sameShape);
        }

        // 소유 그래프와 단축키 참조, 사망 토큰이 마지막으로 그린 것과 같은지다. 같으면 사건만 바뀐 세션 변경 알림이다.
        bool DisplayUnchanged(GameplayPersistenceState gameplay, ItemOwnershipState? items) =>
            ReferenceEquals(_displayItems, items) && ReferenceEquals(_displayShortcuts, gameplay.Inventory.Shortcuts) &&
            _displayDeathToken == gameplay.Deaths.DeadCharacterToken;

        // 원작 UI_LevelChangeNotification 문구의 {level}에 새 레벨을 넣어 공용 HUD 알림 대기열에 넣는다.
        // 정적 이벤트에서 오는 알림이라 SyncEquipment처럼 공용 HUD가 이 월드에 연결된 동안에만 HUD를 바꾼다.
        void LevelChanged(int oldLevel, int newLevel)
        {
            if (!Shared.PresentationAttached) return;
            string level = newLevel.ToString(CultureInfo.InvariantCulture);
            Shared.Hud.Notify(GameLocalization.Text("UI_LevelChangeNotification").Replace("{level}", level));
        }

        // 원작 PerkBehaviour의 해금 상태 변경처럼 특성 스탯 합을 Base에 다시 맞춘다. 최대 체력 같은 Base가 바뀌면 HUD 최대치도 다시 읽는다.
        // 공용 HUD가 이 월드에 연결된 동안에만 HUD를 바꾼다.
        void PerksChanged()
        {
            GameplayWorldContext shared = Shared;
            if (shared.Runtime.Vitals.SyncPerkStats(GameManager.Perks) && shared.PresentationAttached) StatusHud.RefreshHudMaxima();
        }

        // 원작은 재능을 확정하면 기지 씬을 다시 불러 효과를 바꾸지만, 제품은 그 자리에서 바꾼다(S4). 효과가 바뀌면 HUD 최대치도 다시 읽는다.
        // 공용 HUD가 이 월드에 연결된 동안에만 HUD를 바꾼다.
        void EndowmentChanged()
        {
            GameplayWorldContext shared = Shared;
            if (shared.Runtime.Vitals.SyncEndowmentEffect(GameManager.Endowment.SelectedEffect) && shared.PresentationAttached) StatusHud.RefreshHudMaxima();
        }

        // 원작 장비 스탯처럼 장비 칸의 GAS 효과를 확정 그래프에 맞추고 몸 외형을 다시 입힌다.
        // 효과가 바뀌면 최대 체력 같은 HUD 최대치도 다시 읽는다. 공용 HUD가 이 월드에 연결된 동안에만 HUD를 바꾼다.
        void SyncEquipment(ItemOwnershipState? items)
        {
            GameplayWorldContext shared = Shared;
            if (shared.Runtime.Vitals.SyncEquipmentEffects(items, shared.Catalog) && shared.PresentationAttached) StatusHud.RefreshHudMaxima();
            // 몸 외형은 장비 외형 객체가 부위마다 마지막 요청과 비교해 바뀐 부위만 입힌다.
            EquipmentVisuals.Apply(items);
        }

        // 출격이나 월드 종료 같은 씬 전환 하나를 실행한다. 전환 중에는 TickGates와 FinishAsync가 걸러 부르지 않는다.
        // 인벤토리를 닫고 이동 입력 억제 토큰을 쥔 채, 씬 파괴 토큰 없이 앱 수명으로 Runtime 명령을 실행한다.
        // 예외, 취소, 거절 처리는 SceneCommand가 맡는다. 실패는 기록만 하고 되돌리거나 다시 시도하지 않는다.
        // finally는 전환 표시를 내리고 이 토큰만 반납해 다른 소유자의 입력 억제를 지우지 않는다.
        async UniTask RunTransitionAsync(Func<UniTask<bool>> command)
        {
            _transitioning = true;
            IDisposable? suppression = null;
            try
            {
                Inventory.Close();
                // 사망 전환처럼 화면이 열린 채 시작할 수 있어 기지 화면 묶음도 닫는다.
                _screens!.Close();
                // 전환은 준비된 월드에서만 시작하므로 모터를 컨텍스트에서 바로 읽는다.
                suppression = Shared.Motor.SuppressInput();
                // 명령 결과는 쓰지 않는다. 거절과 실패의 기록은 SceneCommand와 Runtime이 맡는다.
                // 명령은 씬 흐름을 GameManager에서 찾는 람다라, 앱이 있는 Update 프레임 안에서 바로 실행된다.
                await SceneCommand.TryRunAsync(command, this);
            }
            // 예외 경계: fire-and-forget 전환 루트라 호출자가 결과를 기다리지 않으므로 SceneCommand 밖의 예기치 못한 실패를 여기서 기록한다.
            catch (Exception error)
            {
                Log.Instance.Exception(error, this);
            }
            finally { _transitioning = false; suppression?.Dispose(); }
        }
        // 출격을 GameManager가 내주는 씬 흐름의 캠페인 전환으로 실행한다. 저장 실패는 저장 관리자가 기록한 뒤 전환을 계속한다.
        // 벙커 문과 지도 선택이 같은 출구를 쓰고, 도착 씬이 출발점을 고르도록 출격 방식(RaidEntry)을 함께 넘긴다(5-G2).
        UniTask DepartAsync(RaidEntry entry) => RunTransitionAsync(() => GameManager.Scenes.BeginRaidAsync(entry));
        // 원작 기지 SelectDifficulty의 SceneLoaderProxy.LoadScene("Prepare")다. 출격과 같은 전환 루트로 떠나는 기지를 저장한 뒤 준비 씬을 연다.
        // 상호작용 호스트(WorldInteractions)의 ReselectDifficulty 출구가 생성자 위임으로 받는다.
        UniTask ReselectDifficultyAsync() => RunTransitionAsync(() => GameManager.Scenes.OpenDifficultyReselectAsync());
        // 사망, 탈출, 튜토리얼 완료로 이 월드를 한 번 끝낸다. 원본 CharacterDieTask와 NotifyEvacuated처럼 결과 화면보다 저장이 먼저다.
        // 결과 판정은 여기 한 곳이다. 탈출이라도 체력이 0이면 사망이다. 원본은 철수 중 무적이라 두 결과가 함께 나지 않는다.
        // 사망 위치와 유실물 소속이 될 활성 하위 씬 이름(예: Level_GroundZero_1)을 담아 월드 종료를 요청하고, 결과 화면은 Update 게이트가 띄운다.
        // .Forget() 루트의 동기 부분이라 던지지 않는다. 세션이 없으면 기록 없이 끝내고, 시작한 종료는 _worldEnding으로 한 번만 진행한다.
        UniTask FinishAsync(RaidOutcome outcome)
        {
            if (_worldEnding || _transitioning) return UniTask.CompletedTask;
            // 이 루트는 준비 뒤 시뮬레이션 단계의 사망 판정, 철수, 지연 상호작용 완료만 불러 컨텍스트가 늘 있다.
            // 던지지 않아야 하는 루트라 세션이 없으면 기록 없이 끝낸다.
            GameplayWorldContext shared = Shared;
            if (!shared.TrySession(out _)) return UniTask.CompletedTask;
            _worldEnding = true;
            // 사망과 탈출은 출격이나 튜토리얼 월드에서만 온다. 그 월드는 저작한 하위 씬이 있고 첫 초기화가 첫 하위 씬을 켜므로
            // 활성 하위 씬이 있다.
            string? subScene = ActiveSubsceneId;
            // 체력은 생존 수치 소유자의 현재 값으로 본다. PlayerState는 일반 클래스라 ?.를 써도 된다.
            RaidOutcome result = outcome == RaidOutcome.Extracted && shared.Runtime.Vitals.Current?.Health <= 0 ? RaidOutcome.Died : outcome;
            Vector3 p = PlayerPosition;
            // 오디오가 꺼져 있으면 관리자가 사망 음성을 조용히 무시한다.
            if (result == RaidOutcome.Died) shared.Audio.PlayOneShot(_playerDeathVoice, p);
            // 정산 규칙에 쓸 카탈로그는 이 월드 컨텍스트의 것이다. 월드 종료는 씬 흐름(GameManager.Scenes)이 맡는다.
            OriginalGameplayCatalog catalog = shared.Catalog;
            // 원작 LevelManager.NotifyEvacuated → QuestTask_Evacuate처럼 탈출 과제를 씬 전환과 저장보다 먼저 확정한다.
            // 세션을 위에서 확인했으므로 앱이 있다. 준비가 _scene을 정했으므로 값이 있다.
            if (result == RaidOutcome.Extracted) GameManager.Quests.NotifyEvacuated(_scene.Value);
            return RunTransitionAsync(() => GameManager.Scenes.EndWorldAsync(catalog, result, subScene, p.x, p.y, p.z));
        }

        // 종료 상태의 결과 화면을 공용 HUD에 띄우고, '계속'을 누르면 기지나 튜토리얼 재시작으로 떠난다.
        // 화면은 원본 ClosureView처럼 누적 경험치, 레벨, 색상과 현지화 제목을 보인다.
        // 화면 대기는 이 씬이 파괴되면 끝나고, 떠나는 명령은 앱 수명으로 실행한다. '계속'이 거부되면 다시 띄울 수 있게 한다.
        async UniTask PresentClosureAsync()
        {
            try
            {
                // 결과 화면은 준비된 월드의 Update 게이트만 띄우므로 공용 HUD를 컨텍스트에서, 앱 시스템을 GameManager에서 바로 읽는다.
                // 종료 결과와 떠나기 명령은 씬 흐름이, 화면은 공용 HUD의 결과 화면 부분이 맡는다. 화면 대기 뒤에는 앱이 사라졌을 수 있어
                // 씬 흐름은 대기 전에 잡아 둔다.
                GameplayWorldContext shared = Shared;
                SceneFlow scenes = GameManager.Scenes;
                bool died = scenes.ClosureOutcome == RaidOutcome.Died;
                // 블록 본문 람다라 SceneCommand의 Func<UniTask> 오버로드로 실행된다.
                // 누적 경험치와 획득 기준은 결과 화면을 띄울 때 경험치 소유자에서 읽는다. 기준은 출격 시작과 불러오기 때만 기록되므로
                // 출격 중 월드를 다시 초기화해도 그대로고, 튜토리얼 사망이 슬롯 파일을 다시 읽으면 기준도 함께 되돌아가 획득량이 0이다.
                if (await SceneCommand.TryRunAsync(async () =>
                    {
                        // 결과 화면이 소유자 전환으로 닫혔으면(씬 종료) 떠나기 명령을 보내지 않는다.
                        if (await shared.Hud.Result.ShowAsync(GameLocalization.Text(died ? "UI_Closure_Dead" : "UI_Closure_Escaped"),
                                GameManager.Experience.RaidStartExperience, GameManager.Experience.Experience,
                                GameLocalization.Text(died ? "gameplay.restart" : "UI_Continue"), died))
                            await scenes.LeaveClosureAsync();
                    }, this) == SceneCommandResult.Rejected)
                    _closurePresented = false;
            }
            // 예외 경계: fire-and-forget 결과 화면 루트라 호출자가 결과를 기다리지 않으므로 예기치 못한 실패를 여기서 기록한다.
            catch (Exception error)
            {
                Log.Instance.Exception(error, this);
            }
        }
        // 로캘이 바뀌면 초기화 뒤에만 인벤토리와 퀵슬롯 9칸을 모두 다시 그리고, 탄약 HUD 이름은 다음 갱신에서 다시 읽게 한다.
        void LocaleChanged(UnityEngine.Localization.Locale locale)
        {
            // 상호작용 대상 이름 캐시는 아래 조기 반환과 관계없이 비운다. 이 구독은 준비를 마친 월드의 AttachPresentation만 건다.
            Interactions.ClearLabelCache();
            // 정리 직전이나 앱 루트가 먼저 파괴된 뒤처럼 퀵슬롯이 GameManager에서 읽을 세션이 없으면 다시 그리지 않는다.
            // 첫 월드 초기화는 컨텍스트를 만든 준비 뒤에만 오므로, 초기화를 먼저 봐야 세션 판정이 컨텍스트를 읽을 수 있다.
            if (!_initialized || !HasLiveSession) return;
            Inventory.Refresh();
            // 칸 이름이 현재 로캘로 번역되도록 확정 세션의 소유 그래프에서 9칸을 모두 다시 그린다.
            QuickSlots.Refresh(Campaign.Gameplay, true, true);
            Combat.InvalidateAmmoText();
        }

        // 씬 로드 때 켜지는 첫 OnEnable은 준비 전이라 건너뛰고, 준비를 마친 뒤 다시 켜질 때만 연결한다.
        void OnEnable()
        {
            if (_prepared) ActivateLevel();
        }

        // 준비를 마친 월드가 켜져 있을 때 공용 표시를 연결하고, 꺼져 있는 UI 액션만 켜서 소유하며, 멈춰 둔 장전음을 재개한다.
        // 레벨 초기화 끝과 OnEnable이 부른다.
        void ActivateLevel()
        {
            AttachPresentation();
            // UI 액션 임대는 입력 묶음이 맡는다. 준비를 마친 뒤에만 오므로 입력 묶음이 있다.
            Inputs.AcquireUiActions();
            Combat.SetAudioPaused(false);
        }

        // 씬 언로드나 앱 종료로 루트가 꺼지면 인벤토리를 닫고, 다음 씬이 소유하도록 UI 액션을 돌려주고, 장전음을 일시정지한 뒤
        // 공용 표시를 뗀다. 파괴 순서가 정해져 있지 않아 단계마다 Cleanup.Run으로 실패를 기록하고 계속해, 표시 연결 해제는 늘 실행한다.
        void OnDisable()
        {
            if (_prepared)
            {
                InventoryScreen screen = Inventory;
                CombatSimulation combat = Combat;
                // 준비를 마친 월드라 입력 묶음이 있다. 그 묶음이 켠 UI 액션 임대만 돌려준다.
                GameplayInputBindings inputs = Inputs;
                Cleanup.Run(screen.Close, this);
                Cleanup.Run(inputs.ReleaseUiActions, this);
                Cleanup.Run(() => combat.SetAudioPaused(true), this);
            }
            DetachPresentation();
        }

        // 커스터마이징이 열리면 씬 루트는 켜 둔 채 인벤토리를 닫고, 장전음을 멈추고, 연결한 GameplayUI를 숨긴다.
        // 표시가 연결된 동안만 반응하므로 준비 전, 초기화 실패 뒤, 꺼진 동안에는 아무것도 하지 않는다.
        void OnCustomizationOpened()
        {
            if (!_presentationAttached) return;
            Inventory.Close();
            Combat.SetAudioPaused(true);
            OriginalGameplayView view = Shared.View;
            if (view != null && view.IsAttachedTo(this)) view.SetSuspended(true);
        }

        // 커스터마이징이 닫히면 열림 때 멈춘 장전음과 숨긴 GameplayUI를 되돌린다.
        void OnCustomizationClosed()
        {
            if (!_presentationAttached) return;
            Combat.SetAudioPaused(false);
            OriginalGameplayView view = Shared.View;
            if (view != null && view.IsAttachedTo(this)) view.SetSuspended(false);
        }
        // 공용 HUD 표시음을 플레이어 위치에서 재생한다. 준비 뒤 AttachPresentation만 구독하고 해제는 DetachPresentation이 맡는다.
        // 오디오가 꺼져 있으면 관리자가 재생을 조용히 무시한다.
        void PresentationSound(string path) => Shared.Audio.PlayOneShot(path,PlayerPosition);

        // 커스터마이징 알림 구독을 먼저 풀고, 기반 클래스가 레벨 알림 구독을 푼 뒤 레벨 정리를 마지막에 부른다.
        // 레벨 종료 알림 없이 파괴되는 앱 종료 같은 경우를 위한 정리이며, 이미 했으면 무동작이다.
        protected override void OnDestroy()
        {
            CustomizationFlow.Opened -= OnCustomizationOpened;
            CustomizationFlow.Closed -= OnCustomizationClosed;
            base.OnDestroy();
        }

        // 원작 onStartedLoadingScene 구독자처럼 씬을 내리기 직전에 레벨 초기화와 첫 월드 초기화가 확보한 것을 되돌린다. 기반 클래스가 한 번만 부른다.
        // 상태를 먼저 비우고 단계마다 Cleanup.Run으로 계속하며 던지지 않는다. OnDestroy 백업 경로에서는 먼저 파괴된 형제와 닫힌 World도 견딘다.
        // 첫 단계 DetachPresentation 뒤로는 공용 HUD와 GameplayUI를 부르지 않는다. World에서 빌린 탄 궤적, 입자, 시체, 유실물 지점은
        // 이 정리 직후 SceneContext가 씬이 살아 있을 때 World를 해제하며 반환한다.
        // 플레이어 준비는 전투 월드가 쓰므로 마지막에 옵션 구독, 카메라, 지도, 모터 순으로 푼다.
        protected override void OnReleaseLevel()
        {
            Cleanup.Run(DetachPresentation, this);
            // 한 번도 켜지지 않은 하위 씬의 풀은 OnDestroy를 받지 못하므로, 씬을 내리기 전에 레벨의 풀 덩어리를 여기서 모두 놓는다.
            Cleanup.Run(() => OriginalTerrainGrass.ReleaseSceneChunks(gameObject.scene), this);
            // 펫은 NavMesh 등록을 빼기 전에 멈추고, 플레이어 모터를 풀기 전에 순간 이동 구독을 푼다.
            // 백업 경로에서 먼저 파괴된 펫은 자기 OnDestroy가 풀었으므로 Unity null 비교로 건너뛴다.
            PetCharacter? pet = _pet;
            _pet = null;
            if (pet != null) Cleanup.Run(pet.Release, this);
            UnityEngine.AI.NavMeshData? navigationInstance = _navigationInstance;
            _navigationInstance = null;
            // 등록 해제와 파괴 단계는 프롤로그와 같은 SceneNavigationData가 맡는다.
            SceneNavigationData.Release(this, navigationInstance);
            _prepared = false;
            _initialized = false;
            // 정리 뒤 늦은 하위 씬 전환은 초기화 전처럼 튜토리얼 적을 모두 켜도록 적 준비 표시도 내린다.
            _enemiesPrepared = false;
            // 준비가 중간에 멈췄을 수 있어 _prepared 대신 만든 협력 객체만 골라 되돌린다. 협력 객체는 일반 클래스라 참조만 본다.
            DialogueSequenceRunner? dialogue = _dialogue;
            WorldInteractions? interactions = _interactions;
            CombatSimulation? combat = _combat;
            DoorSync? doors = _doorSync;
            DeadBodySpawner? bodies = _bodies;
            DroppedItemSpawner? drops = _drops;
            // 과제 생성 도우미는 바닥 아이템 목록과 생성 상호작용 대상 목록을 쓰므로 그 둘을 놓기 전에 끊는다.
            // 백업 경로에서 먼저 파괴된 도우미는 자기 OnDestroy가 끊었으므로 Unity null 비교로 건너뛴다.
            QuestTaskSpawner? questTasks = _questTasks;
            _questTasks = null;
            if (questTasks != null) Cleanup.Run(questTasks.Unbind, this);
            // 연결한 건설 영역을 끊어 건물 상호작용 대상을 이 월드에서 뺀다.
            BuildingArea[] areas = _buildingAreas;
            _buildingAreas = Array.Empty<BuildingArea>();
            foreach (BuildingArea area in areas) if (area != null) Cleanup.Run(area.Unbind, this);
            // 입력 묶음은 준비 도중에 실패했으면 없을 수 있다. 있으면 그 묶음이 켠 UI 액션 임대만 돌려준다.
            GameplayInputBindings? inputs = _inputs;
            if (inputs != null) Cleanup.Run(inputs.ReleaseUiActions, this);
            Cleanup.Run(_subscriptions.Clear, this);
            // 문 소리 구독은 입력과 세션 구독과 같은 단계에서 푼다. 생존 처리와 퀵슬롯 표시는 구독이 없어 정리할 것이 없다.
            if (doors != null) Cleanup.Run(doors.Dispose, this);
            if (dialogue != null) Cleanup.Run(dialogue.Dispose, this);
            if (interactions != null) Cleanup.Run(interactions.EndTimedInteraction, this);
            if (combat != null) Cleanup.Run(combat.ReleaseCombatObjects, this);
            // 빌린 묘비와 시체 지점은 SceneContext의 World 해제가 반환하거나 파괴하므로 생성 객체는 참조만 놓는다.
            if (bodies != null) Cleanup.Run(bodies.Release, this);
            // 바닥 아이템 지점도 World에서 빌리므로 시체 생성 객체와 같은 단계에서 놓는다.
            if (drops != null) Cleanup.Run(drops.Release, this);
            // 로드 시작 알림 없이 끝나는 정리(앱 종료 같은 OnDestroy 경로)에서도 앰비언스가 남지 않게 멈춘다. 이미 멈췄으면 아무것도 하지 않는다.
            SubsceneAmbience? ambience = _ambience;
            if (ambience != null) Cleanup.Run(ambience.Stop, this);
            OriginalPlayerRig? rig = _rig;
            _rig = null;
            if (rig != null) Cleanup.Run(rig.Release, this);
        }
    }
}
