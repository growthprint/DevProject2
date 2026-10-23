#nullable enable annotations
using System;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Localization.Settings;

namespace Game.App
{
    // 프롤로그 씬의 카메라, F 상호작용 드라이버, 대사, 인벤토리와 상자 화면을 다룬다. 저장과 진행 확정은 씬이 직접 하지 않고
    // Runtime 명령에 맡긴다. 원본 LevelManager의 레벨 초기화 알림은 공통 수명이 받고, 커스터마이징 열림과 닫힘 알림으로 거울 편집 동안의
    // 표시를 맞춘다. 거울, 출구, 은신처 상자가 끝났을 때 맡기는 일(IInteractionHost)은 PrologueInteractions가 한다.
    [DisallowMultipleComponent]
    public sealed class OriginalPrologueController : SceneLevelController, ISceneCancelHandler
    {
        // 원본 씬에서 복원한 렌더 카메라다.
        [SerializeField] Camera _camera = null!;
        [SerializeField] GameObject _exitsRoot = null!;
        // 원본 Prologue_1의 F 상호작용 대상이다. 거울(Interact_CustomFace 2090), 튜토리얼 출구(Go 1910),
        // 은신처 상자(LootBox_Hidden_Special 2372)다.
        [SerializeField] InteractableBase[] _interactables = Array.Empty<InteractableBase>();
        // 원본 ToPrisonChecker(1908)가 첫 방문 경로에서 켜는 튜토리얼 출구 ToPrison(targetObject 42)이다.
        // 이 참조는 DuckovOpeningContentBuilder.PatchPrologue만 연결한다.
        [SerializeField] GameObject _tutorialExitRoot = null!;
        [SerializeField] RuntimeAnimatorController _locomotion = null!;
        // 원본 시작 구역 trigger와 프롤로그 검정 화면 View다. 플레이어가 시작 구역에 들어가면 거울 대사를 시작한다.
        [SerializeField] Collider _openingTrigger = null!;
        [SerializeField] PrologueUguiView _view = null!;
        // 원본 LevelManager HUD에 해당하는 앱 수명 공용 HUD다. 직렬화하지 않고 레벨 초기화가 준비 문맥에서 받으며,
        // AttachPresentation으로 연결된 동안에만 바꾼다.
        SharedHudView _hud = null!;
        // 전투 월드와 같은 앱 수명 GameplayUI다. 거울과 문 대사를 이 대사 상자에 띄운다. 직렬화하지 않고 레벨 초기화가 준비 문맥에서
        // 받는다. 씬 루트 아래가 아니라서 커스터마이징 열림과 닫힘 알림이 숨김을 직접 맞춘다.
        OriginalGameplayView _gameplayView = null!;
        // 원본 캐릭터(0번)의 최대 수치를 읽는 카탈로그로, 튜토리얼과 같은 것을 쓴다. 직렬화하지 않고
        // SceneContext가 로드한 것을 레벨 초기화가 준비 문맥에서 받는다.
        OriginalGameplayCatalog? _catalog;
        // 원본 MainVirtualCamera 전용 초점 Volume이다. 게임 카메라가 프로필 복제본에서 초점을 대상 거리에 맞추고,
        // 대사 카메라로 블렌드할 때 가중치를 넘긴다.
        [SerializeField] UnityEngine.Rendering.Volume? _focusVolume;
        // 원본 CutScene_CustomFace(4줄)와 CutScene_GoOut(3줄) 대사 트리의 줄 키와 줄별 카메라 지시(VCam_Mirror, VCam_Door)다.
        // 원본 대사 트리처럼 대사 순서와 카메라 지시를 데이터로 두며, 제작 도구 Restore Prologue Dialogue Cameras가 채운다.
        [SerializeField] OriginalDialogueScript _customFaceDialogue = new OriginalDialogueScript();
        [SerializeField] OriginalDialogueScript _doorDialogue = new OriginalDialogueScript();
        // 캠페인 월드와 같은 플레이어 준비로 이동 모터, 같은 루트의 지도 연결, 원본 게임 카메라를 가진다.
        // 레벨 초기화가 만들고 레벨 정리(ReleaseLevel)가 해제한다. 테스트가 리플렉션으로 읽으니 이름을 지킨다.
        OriginalPlayerRig? _rig;
        // 거울과 문 대사를 진행하며, 대사 중 HUD 숨김과 이동 입력 억제도 맡는다. 레벨 초기화가 만들고 레벨 정리가 해제한다.
        DialogueSequenceRunner? _dialogue;
        bool _openingPlayed, _doorPlayed;
        // 원본은 모든 레벨에서 같은 생존 규칙을 쓰므로 전투 월드와 같은 생존 틱, 갈증과 기아 보정 객체와 생존 HUD 갱신 객체를 쓴다.
        // 레벨 초기화가 플레이어 준비 직후, _runtime보다 먼저 만든다. 구독이 없어 정리할 것이 없다.
        PlayerSurvival? _survival;
        PlayerStatusHud? _statusHud;
        string _binding = "";
        // 앱이 소유한 서비스다. 씬은 참조만 받고 수명은 관리하지 않는다.
        GameRuntime? _runtime;
        // 레벨 초기화가 적용 알림을 구독한 앱의 옵션 관리자다. 레벨 정리는 앱이 먼저 사라졌을 수 있어 정적 속성 대신 이 참조로 푼다.
        GameOptionsManager? _options;
        // 앱 수명 오디오 관리자라 레벨 정리가 비우지 않는다. 표시음은 레벨 정리가 해제하는 대사 진행자로만 불리고,
        // 오디오가 꺼져 있거나 관리자가 해제된 뒤에는 관리자가 명령을 조용히 무시한다.
        GameAudioManager _audio = null!;
        // 원본 amb_default 배경 반복음이다. 오디오 명령은 던지지 않고 실패는 GameAudioManager가 한 번 기록하므로
        // 예외 경계를 두지 않는다.
        SceneLoopAudio? _ambience;
        InputAction? _interact;
        // 원본 DialogueUI는 Space로 대사를 확인한다(클릭은 Player/Attack).
        InputAction? _dialogueConfirm;
        // 앱은 UI 맵 액션을 켜지 않는다. 그래서 활성화 때 꺼져 있던 경우에만 켜고, 비활성화 때 그 경우만 다시 끄는 임대로 소유한다.
        InputActionEnableReleaseHandle? _dialogueConfirmLease;
        // 직전에 시작 구역을 판정한 탐색점이다. 판정을 건너뛴 프레임의 이동도 다음 판정 구간에 들어간다. 첫 판정 전에는 없다.
        Vector3? _lastOpeningProbe;
        // F 상호작용 탐색과, 대상이 끝났을 때 맡기는 일이다. 레벨 초기화가 인벤토리 화면 다음, _runtime보다 먼저 만든다.
        PrologueInteractions? _interactions;
        // 플레이어 상태가 아직 저장되지 않은 새 게임의 HUD 표시값을 한 번만 만든다.
        PlayerState? _newGamePlayer;
        // 원본 플레이어 정의를 레벨 초기화 때 한 번 찾아 둔다. 아직 저장 전인 새 게임의 HUD 표시값을 이 정의로 만든다.
        OriginalItemDefinition _playerCharacter = null!;
        // 원작 프롤로그도 LevelManager의 LootView로 인벤토리와 은신처 상자를 연다. 전투 월드와 같은 인벤토리 화면을 쓰려고
        // 같은 씬 참조 묶음을 만든다. 무기와 소비품이 없어 행동 상태는 NoInventoryActions다. 레벨 초기화가 만든다.
        GameplayWorldContext? _world;
        InventoryScreen? _inventoryScreen;
        InputAction? _inventoryToggle;
        InputActionEnableReleaseHandle? _inventoryToggleLease;
        // 공용 HUD 인벤토리 패널 명령과 세션 변경 알림 구독이다. AttachPresentation이 걸고 DetachPresentation이 푼다.
        readonly UiEventSubscriptions _inventoryBindings = new UiEventSubscriptions();
        // 프롤로그 인벤토리에도 원작 탭 단추 줄(Tabs/ViewButtons)이 보이므로 가방 단추 의도를 받고 강조를 맞춘다. BindInventory가 채우고
        // DetachPresentation이 비운다. 단추 줄 도구가 따로 연결하므로 연결 전 HUD에서는 비어 있다.
        OriginalViewButtonsView? _viewButtons;
        // 펫이 걷는 프롤로그 NavMesh다. 전투 월드처럼 같은 GameObject의 NavMeshSurface에 이 씬 전용 복제본(_navigationInstance)을
        // 등록하고 레벨 정리가 등록을 빼고 파괴한다. 제작 도구가 "_navigationData"로 연결한다.
        [SerializeField] UnityEngine.AI.NavMeshData? _navigationData;
        UnityEngine.AI.NavMeshData? _navigationInstance;
        // 레벨 초기화가 묶은 이 씬의 펫이다. 레벨 정리가 플레이어 준비보다 먼저 푼다. 펫을 저작하지 않았으면 비어 있다.
        PetCharacter? _pet;

        // 준비 전이나 해제 뒤에는 null이다. 매 프레임 경로가 Unity null 비교로 먼저 파괴된 모터도 거르도록 모터 형식으로 돌려준다.
        OriginalPlayerMotor? Motor => _rig != null ? _rig.Motor : null;

        // 월드 협력 객체와 펫이 함께 쓰는 진행 게이트다. 게임플레이가 활성이고 대사와 상호작용 명령이 없을 때만 참이다.
        // 대사 진행자와 상호작용 처리는 _runtime보다 먼저 생기고 레벨 정리가 _runtime을 먼저 비우므로, _runtime이 있으면 둘도 있다.
        bool SimulationActive => _runtime != null && _runtime.IsGameplayActive && !_dialogue!.IsOpen && !_interactions!.IsInteracting;

        // 공통 수명의 레벨 알림 구독에 더해 커스터마이징 열림과 닫힘 알림을 구독하고 OnDestroy에서 푼다.
        protected override void Awake()
        {
            base.Awake();
            CustomizationFlow.Opened += OnCustomizationOpened;
            CustomizationFlow.Closed += OnCustomizationClosed;
        }

        // SceneContext가 캐릭터와 월드를 준비한 뒤 공통 수명이 한 번만 부른다. 캐릭터 계층은 그대로 두고 이동 모터와
        // 게임 카메라를 붙인 다음, 대사, 입력, 구독을 준비하고, 켜져 있으면 공용 표시까지 연결한다.
        // 확보한 것은 바로 필드에 기록해 두어, 중간에 실패해도 레벨 정리가 역순으로 되돌릴 수 있다.
        protected override void OnInitializeLevel()
        {
            // SceneContext는 Gameplay를 채운 뒤에만 레벨 초기화를 알리고 LevelInited를 세운다.
            SceneGameplayContext context = SceneContext.Instance!.Gameplay!;
            // 카탈로그는 이 씬의 SceneContext가 씬 서랍에서 로드해 넘긴다.
            _catalog = context.Catalog;
            // 공용 HUD와 GameplayUI는 여기서 참조만 들고, 연결은 AttachPresentation이 한다.
            _hud = context.Hud;
            _gameplayView = context.GameplayView;
            GameRuntime runtime = context.Runtime;
            // 입력은 저장한 키 설정을 따르는 앱의 입력 실행 사본에서 찾는다. 레벨 초기화는 앱이 있을 때만 온다.
            InputActionAsset actions = GameManager.InputActions;
            // 앱 오디오 관리자는 오디오가 꺼져 있어도 GameManager에 늘 있으며, 꺼져 있으면 명령을 조용히 무시한다.
            GameAudioManager audio = GameManager.Audio;
            // 옵션 관리자도 여기서 한 번 읽어 플레이어 준비에 넘기고, 아래 옵션 구독도 같은 관리자에 건다.
            GameOptionsManager options = GameManager.Options;
            // 원본 플레이어 정의는 카탈로그에 늘 저작되어 있고 EditMode 저작 검사가 지킨다.
            _playerCharacter = _catalog.PlayerCharacter;
            // 모터, 같은 루트의 지도, 게임 카메라, 움직임 감소 옵션 구독은 캠페인 월드와 같은 OriginalPlayerRig가 확보한다.
            // 리그가 플레이어를 생존 수치 소유자의 아바타로 연결하므로 스태미나는 HUD와 같은 기준인 원본 정의의 최대치로 시작한다.
            // 프롤로그 Volume 암 값은 35/-90/30 등이다.
            var rig = new OriginalPlayerRig(this, context, _camera, _locomotion, _focusVolume, _playerCharacter, actions, audio, options);
            _rig = rig;
            // 비출격 맵의 생존 틱은 에너지를 최대치의 25% 아래로 내리지 않아 프롤로그는 기아에 닿지 않는다.
            // 그래서 기아 버프가 이어 거는 주기 피해도 생기지 않아, 피격 표시와 사망 처리를 두지 않는다. 생존 HUD는 같은 모터와 공용 HUD로 갱신한다.
            _survival = new PlayerSurvival(runtime, _catalog);
            _statusHud = new PlayerStatusHud(runtime, rig.Motor, _hud);
            // 여러 곳이 찾는 입력 경로는 공용 상수로 쓴다.
            _interact = actions.FindAction(GameInputPaths._playerInteract, true);
            // 키캡은 입력 자산의 현재 바인딩에서 읽어 실제 키와 맞춘다. 원본 InputIndicator처럼 키보드 바인딩만 보이며,
            // 옵션이 적용되면 ApplyOptions가 다시 계산한다.
            _binding = GameLocalization.KeyboardBindingLabel(_interact);
            // 대사 상자 클릭은 Player/Attack이다. 켜고 끄는 것은 앱이 맡는다.
            InputAction attack = actions.FindAction(GameInputPaths._playerAttack, true);
            // 대사 확인 액션은 여기서 찾기만 하고 ActivateLevel과 OnDisable에서 켜고 끈다. 나가는 씬은 새 씬을 불러오기 전에
            // 언로드되므로, 나가는 씬이 다음 씬이 쓸 액션을 끄지 않는다.
            _dialogueConfirm = actions.FindAction(GameInputPaths._uiDialogueConfirm, true);
            _audio = audio;
            // 배경음은 초기화 때 만들고, 켜진 레벨의 ActivateLevel이 재생을 시작한다.
            _ambience = new SceneLoopAudio(audio, "event:/Amb/amb_default");
            // 거울과 문 대사는 전투 월드와 같은 앱 수명 GameplayUI 대사 상자로 보여 준다. 글자 표시음은 첫 글자에 dialogue_start,
            // 이후 dialogue_bump이며 플레이어 위치에서 재생한다.
            _dialogue = new DialogueSequenceRunner(_gameplayView.DialogueBox, _hud, rig.Motor, _dialogueConfirm, attack, PlayDialogueSound);
            // 전투 월드와 같은 씬 참조 묶음과 인벤토리 화면이다. 프롤로그에는 영역 지점, 문, 적이 없다.
            // 진행 게이트는 펫과 같은 SimulationActive다.
            var world = new GameplayWorldContext(runtime, _catalog, _hud, _gameplayView, rig.Motor, context.Character, _camera, rig.GameCamera,
                audio, context.World, Array.Empty<OriginalGameplayPoint>(), _interactables, Array.Empty<Door>(), Array.Empty<OriginalEnemyController>(),
                () => SimulationActive, this);
            _world = world;
            // 펫이 걸을 NavMesh를 전투 월드와 같은 SceneNavigationData 규칙으로 이 씬 전용 복제본으로 등록한다.
            _navigationInstance = SceneNavigationData.Register(this, _navigationData);
            // 원작 PetAI처럼 펫은 플레이어 모터를 따라가고, 월드 협력 객체와 같은 게이트가 열린 동안에만 움직인다.
            // 프롤로그는 기지 레벨이 아니라 원작처럼 주인 순간 이동을 따라간다. 펫을 저작하지 않았으면 건너뛴다.
            PetCharacter? pet = context.Pet;
            if (pet != null)
            {
                _pet = pet;
                pet.Bind(rig.Motor, () => SimulationActive, true);
            }
            InventoryScreen inventory = new InventoryScreen(world, NoInventoryActions._instance);
            _inventoryScreen = inventory;
            // F 상호작용 탐색과 대상이 맡기는 일은 같은 씬 참조 묶음과 인벤토리 화면으로 처리한다. 대상은 이 씬에 저작한 배열이다.
            _interactions = new PrologueInteractions(world, _interactables, inventory);
            // 인벤토리 키는 전투 월드와 같은 UI 액션이다. 켜고 끄는 것은 ActivateLevel과 OnDisable의 임대가, 구독 해제는 레벨 정리가 한다.
            _inventoryToggle = actions.FindAction(GameInputPaths._uiToggleInventory, true);
            _inventoryToggle.performed += inventory.Toggle;
            // Runtime은 생존 처리, 상호작용 처리, 배경음, 대사 진행자를 만든 뒤 대입한다. 매 프레임 경로는 _runtime 하나로 이들이 있는지까지 본다.
            _runtime = runtime;
            runtime.StateChanged += OnRuntimeChanged;
            // 확정된 옵션을 한 번 적용하고, 이후에는 적용 알림만 받아 매 프레임 다시 대입하지 않는다. 키캡만 여기서 갱신한다.
            // 옵션 관리자는 구독 전에 필드에 잡아 두어 레벨 정리가 같은 관리자에서 푼다.
            _options = options;
            ApplyOptions(options.Current);
            options.Applied += ApplyOptions;
            // 로캘이 바뀌면 캐시한 문구를 비운다. 해제는 레벨 정리가 초기화 여부와 관계없이 한다.
            LocalizationSettings.SelectedLocaleChanged += LocaleChanged;
            // 준비 직후 저장된 진행 단계로 출구 표시를 맞춘다.
            RefreshExit();
            // 루트가 로드 때부터 켜져 있으면 OnEnable은 초기화 전에 지나갔으므로, 공용 표시와 입력, 배경음을 여기서 연결한다.
            if (isActiveAndEnabled) ActivateLevel();
        }

        // 켜진 레벨에서 공용 HUD와 GameplayUI를 이 프롤로그에 연결하고, 캐릭터 상태 표시와 대사 표시음 구독을 건다.
        // 프롤로그에는 적이 없고, 최대치는 모터와 같은 기준인 플레이어 GAS 어트리뷰트에서 읽는다.
        void AttachPresentation()
        {
            _hud.Attach(this);
            _gameplayView.Attach(this);
            // 캐릭터 최대치와 추적 대상은 공용 HUD의 상태 표시 부분이 받는다. 모터는 플레이어 리그가 소유한다.
            _statusHud!.BindHud(_camera, Array.Empty<OriginalEnemyController>());
            RefreshHud();
            _dialogue!.Attach();
            BindInventory();
        }

        // 원본 LootView의 버튼, 셀 클릭, 끌어 놓기 명령을 전투 월드와 같이 인벤토리 화면에 잇고, 세션이 바뀌면 열린 화면을 다시 그린다.
        void BindInventory()
        {
            InventoryScreen screen = _inventoryScreen!;
            // 패널 명령 구독은 전투 월드와 같은 인벤토리 화면의 패널 연결이 이 구독 묶음에 건다.
            screen.BindPanel(_inventoryBindings);
            // 세션 관리자는 일반 클래스라 구독할 때 잡아 둔 참조로 푼다. 세션은 씬 참조 묶음과 같은 규칙으로 찾고,
            // 없으면 다시 그릴 일도 없다.
            if (_world!.TrySession(out GameSessionManager? session))
            {
                Action<GameSessionSnapshot> refresh = _ => screen.Refresh();
                _inventoryBindings.Bind(() => session.Changed += refresh, () => session.Changed -= refresh);
            }
            // 원작 BackpackButton(ViewsProxy.ShowInventoryView)만 전투 월드처럼 소지품 화면으로 잇는다. 프롤로그에는 일지, 지도, 열쇠 도감,
            // 쪽지 화면이 없어 나머지 단추 의도는 받아도 아무것도 하지 않는다(원작 차이). 열기 조건은 인벤토리 키와 같은 ShowCarried가 본다.
            OriginalViewButtonsView viewButtons = _hud.ViewButtons;
            if (viewButtons != null)
            {
                _viewButtons = viewButtons;
                Action<ViewTab> openTab = tab =>
                {
                    if (tab == ViewTab.Inventory) screen.ShowCarried();
                };
                // 구독과 해제 동작 등록은 공용 구독 묶음이 함께 한다.
                _inventoryBindings.Bind(() => viewButtons.TabRequested += openTab, () => viewButtons.TabRequested -= openTab);
            }
        }

        // OnDisable과 레벨 정리 첫 단계에서 AttachPresentation이 연결한 것을 되돌린다. 여러 번 불러도 되고 던지지 않는다.
        // 진행 중인 대사를 먼저 닫고 GameplayUI, 공용 HUD 순으로 연결을 푼다. 소유자가 아니면 그대로 두고, 먼저 파괴된 것은 Unity null 검사로
        // 건너뛴다.
        void DetachPresentation()
        {
            DialogueSequenceRunner? dialogue = _dialogue;
            OriginalGameplayView view = _gameplayView;
            SharedHudView hud = _hud;
            // 인벤토리 화면은 HUD를 떼기 전에 닫아 입력 차단을 돌려주고, 패널 명령 구독을 푼다.
            InventoryScreen? inventory = _inventoryScreen;
            if (inventory != null) Cleanup.Run(inventory.Close, this);
            // 탭 단추 의도 구독은 아래 구독 정리가 풀고, 강조는 HUD의 Detach가 지운다. 다음 프레임부터 강조를 쓰지 않게 참조를 먼저 비운다.
            _viewButtons = null;
            Cleanup.Run(_inventoryBindings.Clear, this);
            if (dialogue != null) Cleanup.Run(dialogue.Detach, this);
            if (view != null) Cleanup.Run(() => view.Detach(this), this);
            if (hud != null) Cleanup.Run(() => hud.Detach(this), this);
        }

        // 씬 로드 때 켜지는 첫 OnEnable은 초기화 전이라 건너뛰고, 초기화를 마친 뒤 다시 켜질 때만 연결한다.
        void OnEnable()
        {
            if (_runtime != null) ActivateLevel();
        }

        // 초기화를 마친 레벨이 켜져 있을 때 공용 표시를 연결하고, 대사 확인 액션을 소유하고, 출구와 배경음을 맞춘다.
        // 처음 켜지면 배경음을 시작하고, 다시 켜지면 같은 소리를 이어서 재생한다. 레벨 초기화 끝과 OnEnable이 부른다.
        void ActivateLevel()
        {
            AttachPresentation();
            // 꺼져 있던 대사 확인 액션만 켜고 소유한다. 확인 액션은 초기화가 _runtime보다 먼저 세우고, 임대는 OnDisable과
            // 레벨 정리가 늘 비운다.
            _dialogueConfirmLease = InputActionEnableReleaseHandle.Acquire(_dialogueConfirm!);
            // 인벤토리 키도 꺼져 있던 경우에만 켜고 소유한다. OnDisable이 돌려준다.
            _inventoryToggleLease = InputActionEnableReleaseHandle.Acquire(_inventoryToggle!);
            RefreshExit();
            // 배경음은 초기화가 _runtime보다 먼저 준비하고 레벨 정리가 함께 비우므로, 초기화를 마친 레벨에서는 늘 있다.
            _ambience!.Start();
            RefreshAudioPause();
        }

        // 씬 언로드나 앱 종료로 루트가 꺼지면 대사 확인 액션을 돌려주어 다음 씬이 소유하게 하고, 재생 위치를 버리지 않고
        // 배경음을 멈춘 뒤 공용 표시를 뗀다. 상호작용 안내는 공용 HUD를 뗄 때 함께 지워진다.
        // 파괴 순서가 정해져 있지 않아 단계마다 Cleanup.Run으로 실패를 기록하고 계속해, 표시 연결 해제는 늘 실행한다.
        void OnDisable()
        {
            // 초기화 전에도 꺼질 수 있어 상호작용 처리가 아직 없으면 비울 대상도 없다.
            _interactions?.ClearTarget();
            Cleanup.Run(ReleaseDialogueConfirm, this);
            // 초기화 전이나 레벨 정리 뒤에도 꺼지므로 배경음이 없을 수 있다.
            SceneLoopAudio? ambience = _ambience;
            if (ambience != null) Cleanup.Run(() => ambience.SetPaused(true), this);
            DetachPresentation();
        }

        // 거울 편집이 열리면 씬 루트는 켜 둔 채 상호작용 안내와 연결한 GameplayUI를 숨긴다. 배경음은 Runtime 상태 알림이 멈춘다.
        // 공용 HUD와 GameplayUI는 이 프롤로그가 소유자일 때만 바꾸며, Play Mode 종료로 먼저 파괴된 것은 건드리지 않는다.
        void OnCustomizationOpened()
        {
            if (_hud != null && _hud.IsAttachedTo(this)) _hud.HideInteraction();
            if (_gameplayView != null && _gameplayView.IsAttachedTo(this)) _gameplayView.SetSuspended(true);
        }

        // 거울 편집이 닫히면 숨긴 GameplayUI를 되돌리고, 원본 Interact_CustomFace.Hide처럼 검정 화면이 걷히며 복귀한다.
        // 닫힘 알림은 편집이 실제로 열렸을 때만 오고, 검정 화면 View는 씬에 직렬화한 참조라 다시 검사하지 않는다.
        void OnCustomizationClosed()
        {
            if (_runtime == null) return;
            if (_gameplayView != null && _gameplayView.IsAttachedTo(this)) _gameplayView.SetSuspended(false);
            _view.BlackScreen.Play();
        }

        // 임대가 켠 대사 확인 액션만 끄고 다른 소유자가 켠 상태는 보존한다.
        void ReleaseDialogueConfirm()
        {
            _dialogueConfirmLease?.Dispose();
            _dialogueConfirmLease = null;
            // 인벤토리 키 임대도 같은 시점에 돌려준다.
            _inventoryToggleLease?.Dispose();
            _inventoryToggleLease = null;
        }

        // Runtime 상태가 바뀌면 출구 표시와 배경음 일시정지를 다시 맞춘다.
        void OnRuntimeChanged()
        {
            RefreshExit();
            RefreshAudioPause();
        }

        // 원본처럼 거울 편집을 확인하거나 취소한 뒤에만 출구를 켜고, 씬은 자동으로 넘기지 않는다.
        void RefreshExit()
        {
            // 앱 종료 중 Runtime 상태 알림에서도 불리므로, Runtime이나 앱 루트가 먼저 파괴됐으면 세션이 없는 것으로 본다.
            // Runtime은 Unity null 비교로 파괴 여부까지 확인하고, 세션은 씬 참조 묶음과 같은 규칙으로 찾는다. 씬 참조 묶음은 _runtime보다 먼저 생긴다.
            GameSessionSnapshot? snapshot = _runtime != null && _world!.TrySession(out GameSessionManager? session) ? session!.Snapshot() : null;
            // 프롤로그 단계는 아직 Base를 방문하지 않은 경로라, 원본 ToPrisonChecker의 inverse 조건대로 ToPrison을 켠다.
            _tutorialExitRoot.SetActive(snapshot != null && snapshot.Campaign.Progress.Stage == CampaignStage.Prologue);
            // 거울 세션이고 거울 닫힘 체크포인트에 닿았는지는 세션 규칙으로 판정한다.
            _exitsRoot.SetActive(snapshot != null && CampaignRules.IsPrologueMirrorClosed(snapshot));
        }

        // 씬이 꺼졌거나 앱 중단 등으로 게임플레이가 멈추면 배경음을 일시정지한다. ActivateLevel과 Runtime 상태 알림만 부르며,
        // 둘 다 배경음을 만든 뒤에 오고 레벨 정리가 알림 구독을 함께 푼다.
        void RefreshAudioPause() => _ambience!.SetPaused(!isActiveAndEnabled || _runtime == null || !_runtime.IsGameplayActive);

        // 조작할 수 있는 프레임에서만 근접 검색과 상호작용 입력을 처리하고, 대사 중에는 대사만 진행한다.
        // 일시정지, 지도, 상호작용 중, 포커스 상실로 입력이 막히면 대사 상자와 현재 줄은 두고 확인 입력만 무시한다.
        void Update()
        {
            // 모터를 한 번 읽어 이번 프레임의 판정과 근접 검색이 같은 참조를 쓴다.
            OriginalPlayerMotor? motor = Motor;
            if (_runtime == null || motor == null) return;
            // 원본처럼 생존 수치는 대사 중에도 진행한다. HUD는 원본 HUD 컴포넌트와 전투 월드처럼 생존 틱 직후 Update에서 갱신한다.
            TickSurvival();
            RefreshHud();
            // 전투 월드처럼 열린 상자가 3m를 벗어나면 화면을 닫는다.
            _inventoryScreen!.CloseIfOutOfRange();
            // 전투 월드처럼 탭 단추 줄에 지금 탭을 매 프레임 넘겨 원작 ViewTabDisplayEntry 강조를 맞춘다. 프롤로그의 탭 화면은 인벤토리뿐이고,
            // 단추 줄은 바뀐 때만 쓴다. 아래 대사·입력 분기가 이번 프레임을 끝내기 전에 한다.
            OriginalViewButtonsView? viewButtons = _viewButtons;
            if (viewButtons != null) viewButtons.SetSelected(_inventoryScreen.IsOpen ? ViewTab.Inventory : ViewTab.None);
            // Runtime이 게임플레이 입력을 받을 때만 진행해 지도 클릭이 F 상호작용이나 대사로 흘러가지 않게 한다.
            // 상호작용 처리는 초기화가 _runtime보다 먼저 만든다.
            PrologueInteractions interactions = _interactions!;
            bool acceptsInput = _runtime.AcceptsGameplayInput && !interactions.IsInteracting && Application.isFocused;
            if (_dialogue.IsOpen || !acceptsInput)
            {
                interactions.ClearTarget();
                if (!acceptsInput) _hud.HideInteraction();
                _dialogue.Tick(acceptsInput);
                return;
            }
            // 원본 DreamGoOut처럼 출구가 켜지면 문 대사를, DreamCustomFace처럼 시작 구역에 들어가면 거울 대사를 한 번씩 재생한다.
            if (!_doorPlayed && _exitsRoot.activeSelf)
            {
                _doorPlayed = _openingPlayed = true;
                // 문 대사의 줄과 카메라 전환은 씬에 저작한 원본 CutScene_GoOut 데이터가 정한다.
                BeginDialogue(_doorDialogue);
                return;
            }
            // 이번 프레임 위치만 보지 않고 지나온 구간까지 판정해, 프레임이 길어도 구역 진입을 놓치지 않는다.
            // 시작 구역은 씬에 저작한 참조이고 EditMode 프롤로그 저작 테스트가 연결을 확인한다.
            if (!_openingPlayed && EnteredOpening(motor))
            {
                _openingPlayed = true;
                // 거울 대사의 줄과 카메라 전환은 씬에 저작한 원본 CutScene_CustomFace 데이터가 정한다.
                BeginDialogue(_customFaceDialogue);
                return;
            }
            // 모터는 이번 프레임에 확인한 것을 넘기고, 찾은 대상은 상호작용 처리가 지금 대상으로도 기억한다.
            InteractableBase? target = interactions.UpdateTarget(motor);
            if (target == null)
            {
                _hud.HideInteraction();
                return;
            }
            // 원본 InteractHUD처럼 대상의 interactMarkerOffset 지점을 따라가며 이름과 키캡을 따로 표시한다. 키캡은 옵션 적용 때 갱신한
            // _binding을 쓴다. 전투 월드와 같은 목록 안내 경로를 써서 단일 안내 래퍼가 필요 없게 한다. 프롤로그 대상은 묶음이 아니라 0번 하나다.
            _hud.ShowInteraction(interactions.PromptLabels(target), 0, _binding, target.MarkerPosition, _camera);
            // 프롤로그의 거울, 출구, 은신처 상자는 상호작용 시간이 0이라 F를 누른 프레임에 원작 StartInteract와 FinishInteract를 함께 한다.
            // 대상이 끝났을 때 맡기는 일은 상호작용 처리가 받는다.
            if (_interact!.WasPressedThisFrame() && target.TryStart(interactions)) target.Complete(interactions);
        }

        // 원본 OnTriggerEnter처럼 시작 구역에 들어갔는지 본다. 이번 탐색점이 구역 안이거나, 직전 탐색점에서 온 선분이 구역을 지나면 참이다.
        // 구역은 두께 1m라 달리기(7.8m/s) 중 한 프레임이 0.13초를 넘으면 그 프레임 위치만으로는 구역을 건너뛴다.
        bool EnteredOpening(OriginalPlayerMotor motor)
        {
            // 전투 월드의 대사·철수 영역과 같은 원작 캐릭터 루트 캡슐 중심 높이를 쓰도록 한 상수를 공유한다.
            Vector3 probe = motor.transform.position + Vector3.up * OriginalInteractionSearch._areaProbeHeight;
            Vector3? last = _lastOpeningProbe;
            _lastOpeningProbe = probe;
            Bounds bounds = _openingTrigger.bounds;
            if (bounds.Contains(probe)) return true;
            if (last == null) return false;
            Vector3 step = probe - last.Value;
            float length = step.magnitude;
            return length > 0f && bounds.IntersectRay(new Ray(last.Value, step / length), out float distance) && distance <= length;
        }

        // 로캘이 바뀌면 캐시한 문구를 비워 다음 표시 때 새 로캘로 번역한다. 레벨 초기화가 상호작용 처리를 만든 뒤 구독하고
        // 레벨 정리가 해제한다. 캐시는 상호작용 처리의 InteractPromptLabels가 가진다.
        void LocaleChanged(UnityEngine.Localization.Locale locale) => _interactions!.ClearLabels();

        // 대사를 봤는지는 저장하지 않는다. 화자, 줄별 카메라 전환, 끝난 뒤 플레이 카메라 복귀는 저작 데이터의 재생 경로가
        // 맡으므로 이 컨트롤러는 어느 대사를 언제 시작할지만 정한다. Update가 준비된 뒤에만 부르므로 상호작용 처리도 있다.
        void BeginDialogue(OriginalDialogueScript script)
        {
            _interactions!.ClearTarget();
            // Update가 준비된 뒤에만 부르므로 플레이어 준비의 게임 카메라가 있다.
            script.Begin(_dialogue!, _rig!.GameCamera, null);
        }

        // 대사 진행자가 고른 글자 표시음을 플레이어 위치에서 재생한다. 모터가 없으면 이 컨트롤러 위치에서 낸다. PlayOneShot은 던지지 않는다.
        void PlayDialogueSound(string path)
        {
            // 모터는 플레이어 리그가 소유하므로 리그를 거쳐 한 번 읽는다. 오디오가 꺼져 있으면 관리자가 재생을 조용히 무시한다.
            OriginalPlayerMotor? motor = Motor;
            _audio.PlayOneShot(path, motor != null ? motor.transform.position : transform.position);
        }

        // ESC 같은 씬 질의다. 열린 인벤토리나 상자 화면이 있으면 닫고 true를 돌려준다.
        public bool TryCancelPanel() => _inventoryScreen != null && _inventoryScreen.TryClose();

        // 이동 계산 뒤 같은 프레임에 게임 카메라를 진행한다. 시간 배율이 0이어도 조준 오프셋 복귀와 대사 블렌드는 실시간으로 진행한다.
        // 입력 활성은 원본 InputManager.InputActived에 해당한다. 지도 커서를 조준으로 읽지 않도록 플레이어 준비가 Runtime 입력과 포커스를,
        // 여기서 넘기는 값으로 대사를 함께 본다.
        void LateUpdate()
        {
            if (_runtime == null) return;
            // 초기화가 플레이어 준비와 대사 진행자를 _runtime보다 먼저 만들고, 레벨 정리는 _runtime을 먼저 비우므로 둘 다 늘 있다.
            _rig!.Tick(_dialogue!.IsOpen);
        }

        // 원본 HUD 상태 표시다. 프롤로그는 장비와 퀵슬롯이 비어 있어 생존 수치, 시계, 조준점만 갱신한다.
        // 플레이어가 아직 저장되지 않은 새 게임은 튜토리얼 초기화와 같은 기본값을 표시한다. 모터가 먼저 파괴되었으면 건너뛴다.
        void RefreshHud()
        {
            // 호출자는 살아 있는 Runtime을 확인한 뒤에만 부르고, 생존 HUD 객체는 Runtime보다 먼저 생긴다. 세션이 없으면 플레이어와 시계를 읽지 않는다.
            // 모터는 플레이어 리그를 거쳐 Unity null 비교로 보고, 표시는 같은 모터를 쥔 생존 HUD 객체가 한다.
            PlayerStatusHud statusHud = _statusHud!;
            if (!_runtime!.HasSession || Motor == null) return;
            // PlayerState는 일반 클래스라 ??를 쓴다.
            statusHud.RefreshHud(_runtime.Vitals.Current ?? NewGamePlayer(_playerCharacter), "", "", 0f, -1);
        }

        // 원본 Prologue_Main(isRaidMap 0, saveCharacter 1)의 생존 수치 처리다. 새 게임 플레이어를 생존 수치 소유자에 한 번 만들어
        // 회복한 값이 튜토리얼로 이어지게 한다. 틱과 갈증, 기아 보정은 전투 월드와 같은 생존 처리 객체가 같은 Runtime 조건으로 한다.
        // 프롤로그에는 생존을 멈출 씬 전환 조건이 없어 넘기지 않는다.
        void TickSurvival()
        {
            // Update는 레벨 초기화가 _runtime까지 세운 뒤에만 부르고, 생존 처리 객체는 그보다 먼저 생긴다. 세션 관리자는 같은 루트에서 읽는다.
            // 플레이어가 아직 없으면 생존 처리가 진행되는 프레임에 만들고, 소유자가 거절하면 다음 프레임에 다시 시도한다.
            PlayerSurvival survival = _survival!;
            PlayerVitals vitals = _runtime!.Vitals;
            // 원작 프롤로그 캐릭터도 소지품을 가지므로, 플레이어를 만든 프레임에 튜토리얼 초기화와 같은 빈 소유 그래프도 만든다.
            // 은신처 상자에서 집은 아이템은 이 그래프에 들어가 튜토리얼 도착 저장으로 이어진다. 플레이어 만들기 시도는 조건을 모두 본 뒤 맨 끝에 한다.
            GameSessionManager? session = GameManager.SessionManager;
            if (NeedsNewGamePlayer(vitals, survival, session) && vitals.TryEnsurePlayer(_catalog)) session!.Inventory.EnsureGraph(_catalog!);
            survival.Tick(false);
        }

        // 아직 플레이어가 없고, 생존 처리가 진행되며, 세션이 있을 때만 새 게임 플레이어를 만든다.
        static bool NeedsNewGamePlayer(PlayerVitals vitals, PlayerSurvival survival, GameSessionManager? session) =>
            vitals.Current == null && survival.Active && session != null;

        // 새 게임 플레이어를 튜토리얼 초기화와 같은 규칙으로 한 번 만든다. 저작한 원본 플레이어 정의 수치는
        // 빌드 데이터라 런타임에 검증하지 않고 EditMode 카탈로그 검사가 확인한다.
        PlayerState NewGamePlayer(OriginalItemDefinition character)
        {
            if (_newGamePlayer != null) return _newGamePlayer;
            _newGamePlayer = OriginalCharacter.NewOriginalPlayer(character);
            return _newGamePlayer;
        }

        // 키 재바인딩도 옵션 적용으로 오므로 상호작용 키캡을 다시 계산한다. 카메라 움직임 감소는 플레이어 준비가 따로 받는다.
        void ApplyOptions(GameOptions options)
        {
            // 초기화가 _interact를 찾은 뒤에만 부르거나 구독하고, 이 컨트롤러는 _interact를 비우지 않는다.
            _binding = GameLocalization.KeyboardBindingLabel(_interact!);
        }

        // 커스터마이징 알림 구독을 푼 뒤, 공통 수명이 레벨 알림 구독을 풀고 백업 레벨 정리를 마지막에 부른다.
        protected override void OnDestroy()
        {
            CustomizationFlow.Opened -= OnCustomizationOpened;
            CustomizationFlow.Closed -= OnCustomizationClosed;
            base.OnDestroy();
        }

        // 원작 onStartedLoadingScene 구독자처럼 씬을 내리기 직전에 레벨 초기화가 확보한 것을 공통 수명이 한 번 부를 때 되돌린다.
        // 먼저 공용 HUD를 놓고 펫, NavMesh 복제본, 구독, 대사, 확인 액션, 배경음 순으로 풀며,
        // 플레이어 준비는 가장 먼저 확보하므로 마지막에 푼다. 단계마다 Cleanup.Run으로 계속하므로 던지지 않는다.
        // OnDestroy 백업 경로에서는 먼저 파괴된 Runtime과 형제도 견딘다.
        // 정리 뒤 씬이 내려가기 전의 프레임에서는 _runtime이 비어 Update, LateUpdate, OnEnable이 아무것도 하지 않는다.
        protected override void OnReleaseLevel()
        {
            Cleanup.Run(DetachPresentation, this);
            // 펫은 NavMesh 등록을 빼기 전에 멈추고, 플레이어 모터를 풀기 전에 순간 이동 구독을 푼다.
            // 백업 경로에서 먼저 파괴된 펫은 자기 OnDestroy가 풀었으므로 Unity null 비교로 건너뛴다.
            PetCharacter? pet = _pet;
            _pet = null;
            if (pet != null) Cleanup.Run(pet.Release, this);
            // NavMesh 복제본의 등록 해제와 파괴는 전투 월드와 같은 SceneNavigationData가 맡는다.
            UnityEngine.AI.NavMeshData? navigationInstance = _navigationInstance;
            _navigationInstance = null;
            SceneNavigationData.Release(this, navigationInstance);
            GameRuntime? runtime = _runtime;
            GameOptionsManager? options = _options;
            DialogueSequenceRunner? dialogue = _dialogue;
            SceneLoopAudio? ambience = _ambience;
            OriginalPlayerRig? rig = _rig;
            _runtime = null;
            _options = null;
            _dialogue = null;
            _ambience = null;
            _rig = null;
            // 앱 수명 구독은 구독할 때 잡아 둔 Runtime과 옵션 관리자로 푼다. 정적 속성은 앱이 먼저 파괴될 수 있어 읽지 않는다.
            // 먼저 파괴된 쪽도 C# 이벤트가 이 핸들러를 들고 있어 늦은 알림이 올 수 있으므로, Unity null 비교 대신 참조만 보고 푼다.
            if (!ReferenceEquals(runtime, null)) Cleanup.Run(() => runtime.StateChanged -= OnRuntimeChanged, this);
            // 인벤토리 키 구독은 입력 실행 사본의 액션에 걸었으므로 그 액션에서 푼다.
            InputAction? toggle = _inventoryToggle;
            InventoryScreen? inventory = _inventoryScreen;
            if (toggle != null && inventory != null) Cleanup.Run(() => toggle.performed -= inventory.Toggle, this);
            if (!ReferenceEquals(options, null)) Cleanup.Run(() => options.Applied -= ApplyOptions, this);
            // 정적 로캘 구독은 Runtime 파괴나 초기화 실패와 관계없이 푼다. 구독 전이면 해제는 아무 일도 하지 않는다.
            Cleanup.Run(() => LocalizationSettings.SelectedLocaleChanged -= LocaleChanged, this);
            if (dialogue != null) Cleanup.Run(dialogue.Dispose, this);
            Cleanup.Run(ReleaseDialogueConfirm, this);
            if (ambience != null) Cleanup.Run(ambience.Dispose, this);
            if (rig != null) Cleanup.Run(rig.Release, this);
        }
    }
}
