#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;
using DuckCharacter;
using Game.Core;
using Game.App.World;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Game.App
{
    // 원작 LevelManager처럼 게임 씬마다 하나 두고, 씬이 로드되면 캐릭터, 월드, 씬 게임플레이 의존성을 만들어 레벨을 초기화한다.
    // 씬 컨트롤러는 LevelInitialized를 받아 준비하고, SceneFlow가 씬을 내리기 직전에 끝내는 레벨의 LevelEnding을 받아 정리한다.
    [DisallowMultipleComponent]
    public sealed class SceneContext : MonoBehaviour
    {
        // 씬 컨트롤러, 카메라, 표현을 담은 루트다. 동적 월드 객체와 Spawner 보관 루트도 이 아래에 만든다.
        [SerializeField] GameObject _gameplayRoot = null!;
        // 생성한 캐릭터를 둘 위치다. 게임플레이 루트 아래에 있어야 한다.
        [SerializeField] Transform _characterMount = null!;
        // 원본 게임플레이 카탈로그 참조다. 원본 게임플레이가 없는 씬은 비워 둔다.
        // 있으면 레벨 초기화 때 이 씬의 서랍에 로드해 SceneGameplayContext.Catalog로 넘긴다.
        [SerializeField] AssetReferenceT<OriginalGameplayCatalog> _gameplayCatalog = null!;
        // 캐릭터를 보이게 둘지 정한다. 난이도 준비 씬과 인트로 연출 씬은 false다.
        // false면 캐릭터를 꺼 둔 채 초기화하고 VisibleCharacter로 공개하지 않는다. 이때 오디오 청취자는 카메라다.
        [SerializeField] bool _characterVisible = true;
        // 원작 LevelManager.petPreset처럼 레벨마다 만들 펫 프리팹이다. 펫이 없는 씬은 비워 둔다.
        [SerializeField] PetCharacter? _petPrefab;

        // 원작 LevelManager의 instance처럼 지금 로드된 게임 씬의 컨텍스트다. Awake가 채우고 OnDestroy가 비운다.
        public static SceneContext? Instance { get; private set; }
        // 원작 LevelManager.LevelInited처럼 현재 컨텍스트가 레벨 초기화를 마쳤는지 알린다.
        public static bool LevelInited
        {
            get
            {
                SceneContext? level = Instance;
                return level != null && level._levelInited;
            }
        }
        // 원작 OnLevelInitialized와 OnAfterLevelInitialized처럼 Gameplay를 채운 뒤 차례로 알린다.
        // Start보다 늦게 구독하는 컨트롤러는 LevelInited를 먼저 확인한다.
        public static event Action? LevelInitialized;
        public static event Action? AfterLevelInitialized;
        // 원작 SceneLoader.onStartedLoadingScene 구독자처럼 나가는 레벨이 씬을 내리기 직전에 정리하라는 알림이다.
        // 알림 안에서는 씬 객체와 World가 아직 살아 있어 컨트롤러가 빌린 월드 객체를 돌려줄 수 있다. World는 알림 뒤에 닫는다.
        public static event Action? LevelEnding;

        // 레벨 초기화가 실패했거나 취소됐는지다. SceneFlow는 이 값이나 LevelInited가 서면 레벨 대기를 끝낸다.
        internal bool LevelInitFailed { get; private set; }
        // 현재 컨텍스트가 있고 레벨 초기화가 끝나지도 실패하지도 않았으면 참이다. SceneFlow가 이 값이 내려갈 때까지 프레임마다 기다린다.
        // 컨텍스트가 파괴됐으면 Unity null 비교로 기다리지 않는다.
        internal static bool LevelInitPending
        {
            get
            {
                SceneContext? level = Instance;
                return level != null && !level._levelInited && !level.LevelInitFailed;
            }
        }
        internal bool CharacterVisible => _characterVisible;
        // 레벨 초기화가 생성한 캐릭터의 연결부다. 씬 기능과 검증 코드는 이 연결부를 통해 캐릭터를 다룬다.
        GameCharacterBinding? _character;
        internal GameCharacterBinding? Character => _character;
        // 레벨 초기화가 만든 펫이다. 커스터마이징 숨김이 모델을 끄고 켜며, 씬 언로드가 게임플레이 루트와 함께 파괴한다.
        PetCharacter? _pet;
        // 레벨 초기화 때 게임플레이 루트에서 한 번 찾아 둔 지도 컨텍스트다. UI는 CurrentMap으로 읽는다.
        SceneMapContext? _map;
        // 레벨 종료 때 해제할 이 씬의 월드다. 씬 기능은 같은 객체를 Gameplay.World로 읽는다.
        WorldObjectService? _world;
        // 씬 컨트롤러가 레벨 초기화 처리에서 읽는 의존성 묶음이다. LevelInitialized를 알리기 전에 채우고,
        // 해제한 World를 든 채 남지 않게 레벨 종료 때 비운다. 레벨 초기화 전과 레벨 종료 뒤에는 null이다.
        internal SceneGameplayContext? Gameplay { get; private set; }
        // UI의 ESC가 일시정지보다 먼저 묻는 패널 처리기다. Awake가 게임플레이 루트 아래에서 한 번 찾아 둔다.
        ISceneCancelHandler[] _cancelHandlers = Array.Empty<ISceneCancelHandler>();
        bool _levelInited;
        // 레벨 종료를 한 번만 실행하게 한다. 종료 뒤에 초기화 로드가 늦게 끝나도 반납할 주인이 없는 World를 만들지 않는다.
        bool _levelEnded;

        // UI가 읽는 현재 씬의 지도다. 컨텍스트나 지도가 파괴됐으면 Unity null 비교로 null을 돌려준다.
        internal static SceneMapContext? CurrentMap
        {
            get
            {
                SceneContext? level = Instance;
                if (level == null) return null;
                SceneMapContext? map = level._map;
                return map != null ? map : null;
            }
        }
        // 현재 씬에 보이는 플레이어 연결부다. 준비나 인트로처럼 캐릭터를 숨기는 씬이거나 파괴됐으면 null이다.
        // null이면 오디오 청취자는 카메라를 쓴다.
        internal static GameCharacterBinding? VisibleCharacter
        {
            get
            {
                SceneContext? level = Instance;
                if (level == null || !level._characterVisible) return null;
                GameCharacterBinding? character = level._character;
                return character != null ? character : null;
            }
        }
        // 캐릭터 표시 실패를 저장 상태와 분리해 UI에 알린다. 보이는 플레이어 연결부의 실패 여부만 본다.
        internal static bool CharacterPresentationFailed
        {
            get
            {
                GameCharacterBinding? character = VisibleCharacter;
                return character != null && character.PresentationFailed;
            }
        }

        // 레벨은 앱 없이도 열 수 있고 앱 정리 중에도 알림이 오므로, 살아 있는 GameManager에 확정 세션이 있을 때만 Runtime을 돌려준다.
        // Runtime은 Unity 객체라 ?. 대신 Unity null 비교로 확인한다.
        static GameRuntime? SessionRuntime()
        {
            GameRuntime? runtime = GameManager.HasInstance ? GameManager.Live.Runtime : null;
            return runtime != null && runtime.HasSession ? runtime : null;
        }

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 값과 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            Instance = null;
            LevelInitialized = null;
            AfterLevelInitialized = null;
            // 레벨 종료 구독자도 지난 세션의 파괴된 컨트롤러를 가리킬 수 있어 함께 비운다.
            LevelEnding = null;
        }

        // 씬이 로드되며 Awake가 돌 때 현재 컨텍스트가 된다. 편집 화면이 열린 동안에는 플레이어 리그를 숨기고,
        // 외형 소유자가 외형을 바꾸면 캐릭터에 다시 적용한다. 원본 매니저처럼 Awake에서 구독하고 OnDestroy에서 끊는다.
        void Awake()
        {
            Instance = this;
            _cancelHandlers = _gameplayRoot.GetComponentsInChildren<ISceneCancelHandler>(true);
            CustomizationFlow.Opened += OnCustomizationOpened;
            CustomizationFlow.Closed += OnCustomizationClosed;
            // 외형 소유자의 변경마다 현재 외형을 다시 적용한다.
            PlayerAppearance.Changed += ApplyCurrentAppearance;
        }

        // 원작 LevelManager.Start처럼 씬 로드 중이면 SceneFlow의 로드 완료를 받은 뒤 초기화한다.
        void Start()
        {
            if (SceneFlow.IsSceneLoading) SceneFlow.LoadingFinished += OnLoadingFinished;
            else InitLevelAsync().Forget();
        }

        // 로드 완료는 한 번만 받으면 되므로 먼저 구독을 끊는다.
        void OnLoadingFinished(GameScene scene)
        {
            SceneFlow.LoadingFinished -= OnLoadingFinished;
            InitLevelAsync().Forget();
        }

        // 원작 LevelManager.InitLevel처럼 아무도 기다리지 않는 초기화 루트다. 확정 세션을 표시할 캐릭터와 월드를 만들고 알린다.
        // 캐릭터와 파츠 라이브러리는 앱 수명 게임플레이 데이터에서 읽고, 씬마다 로드하는 카탈로그만 이 씬의 서랍에 넣어
        // SceneFlow가 씬을 내린 뒤 서랍째 반납한다. 실패는 되돌리지 않고 LevelInitFailed로 알린다.
        async UniTaskVoid InitLevelAsync()
        {
            try
            {
                // Runtime이 로드 전에 세션을 확정하므로, GameManager나 세션이 없으면 이 씬이 표시할 값이 없어 초기화를 실패로 끝낸다.
                GameRuntime? runtime = SessionRuntime();
                if (runtime == null)
                {
                    LevelInitFailed = true;
                    Log.Instance.Error("SceneContext: 확정된 게임 세션이 없어 레벨을 초기화하지 못했습니다.", this);
                    return;
                }
                AssetLifetimes assets = runtime.Assets;
                // 로드를 기다리는 동안 앱이 사라져도 GameManager와 게임플레이 데이터를 다시 읽지 않도록, 살아 있음을 확인한 지금
                // 외형 소유자, 파츠 라이브러리, 캐릭터 프리팹을 잡아 둔다. 두 데이터 연결은 앱 시작 검증이 확인했다.
                PlayerAppearance appearance = GameManager.Appearance;
                DuckAvatarPartLibrary library = GameplayDataSettings.CustomFaceData;
                GameCharacterBinding prefab = GameplayDataSettings.Prefabs.CharacterPrefab;
                // 카탈로그는 키가 있을 때만 이 씬 서랍에 로드하고, 씬과 함께 반납된다.
                OriginalGameplayCatalog? catalog = _gameplayCatalog != null && _gameplayCatalog.RuntimeKeyIsValid()
                    ? await assets.LoadAsync<OriginalGameplayCatalog>(_gameplayCatalog, gameObject.scene)
                    : null;
                // 로드를 기다리는 동안 씬이 파괴됐으면 아무것도 만들지 않는다. 로드한 에셋은 씬 서랍이 반납한다.
                if (this == null) return;
                // AssetLifetimes 로드는 메인 스레드에서 끝나므로 이어서 Unity API를 바로 쓴다.
                // 로드를 기다리는 동안 레벨이 끝났으면 캐릭터와 World를 만들지 않는다. 기록 없이 실패로 알린다.
                if (_levelEnded)
                {
                    LevelInitFailed = true;
                    return;
                }
                // 프리팹을 연결부 타입으로 참조하므로 복제본에 연결부가 있는지 따로 찾지 않는다.
                GameCharacterBinding character = Instantiate(prefab, _characterMount, false);
                character.gameObject.SetActive(true);
                _character = character;
                // 씬은 외형 소유자의 현재 값만 표시하고 외형을 만들거나 소유하지 않는다. 캐릭터를 숨기는 씬은 초기화한 캐릭터를 꺼 둔다.
                // 로드를 기다리는 동안의 외형 변경은 아직 적용하지 않았으므로 여기서 지금 값을 읽는다.
                character.Prepare(appearance.Current, library);
                if (!_characterVisible) character.gameObject.SetActive(false);
                // 원작 InitLevel처럼 캐릭터 다음에 펫을 만든다. 하위 씬 전환이 끄지 않도록 하위 씬 루트가 아닌 게임플레이 루트 바로 아래에 둔다.
                // 원작 주인 SetPosition처럼 캐릭터의 월드 앞 0.4m에 두고, NavMesh 위 배치는 펫이 게이트가 열린 뒤 마친다.
                PetCharacter? pet = null;
                if (_petPrefab != null)
                {
                    Vector3 petPosition = character.transform.position + Vector3.forward * .4f;
                    pet = Instantiate(_petPrefab, petPosition, Quaternion.identity, _gameplayRoot.transform);
                    _pet = pet;
                    pet.PlaceAt(petPosition);
                }
                // 동적 객체의 월드 루트와 Spawner 보관 루트는 게임플레이 루트 아래에 두어 씬 언로드 때 함께 파괴한다.
                var worldRoot = new GameObject("WorldObjects");
                worldRoot.transform.SetParent(_gameplayRoot.transform, false);
                // 레벨 종료가 해제하도록 필드에 두고, 씬 기능에는 의존성 묶음으로만 내준다.
                var world = new WorldObjectService(new Spawner(_gameplayRoot.transform), worldRoot.transform);
                _world = world;
                // 캐릭터는 파괴 소유권을 넘기지 않고 조회 대상으로만 등록한다.
                world.RegisterExternal(character.gameObject);
                _gameplayRoot.TryGetComponent(out _map);
                // 펫이 없는 씬은 null을 넘긴다.
                Gameplay = new SceneGameplayContext(runtime, _character, world, catalog, pet);
                _levelInited = true;
                ObserverDispatch.InvokeEach(LevelInitialized);
                ObserverDispatch.InvokeEach(AfterLevelInitialized);
            }
            // 예외 경계: 기다리는 쪽이 없는 초기화 루트라 실패를 여기서 한 번 기록한다.
            catch (Exception error)
            {
                LevelInitFailed = true;
                Log.Instance.Exception(error, this);
            }
        }

        // UI의 ESC 처리다. 현재 씬의 처리기에 역순으로 묻고, 처음 패널을 닫은 처리기에서 멈춘다.
        // 씬 객체의 파괴 순서는 정해지지 않으므로 파괴된 처리기는 Unity null 비교로 건너뛴다.
        internal static bool TryCancelCurrentPanel()
        {
            SceneContext? level = Instance;
            if (level == null) return false;
            ISceneCancelHandler[] handlers = level._cancelHandlers;
            for (int index = handlers.Length - 1; index >= 0; index--)
            {
                ISceneCancelHandler handler = handlers[index];
                if (handler is UnityEngine.Object unityObject && unityObject == null) continue;
                if (handler.TryCancelPanel()) return true;
            }
            return false;
        }

        // 외형 소유자의 변경 알림과 표시 재시도 명령(GameRuntime.TryRetryCharacterPresentation)이 부르며, 외형이나 저장 데이터는 바꾸지 않는다.
        // 레벨 초기화 전이면 Prepare가 그때의 외형을 쓰므로 적용하지 않는다. 표시 실패는 연결부의 PresentationFailed에 남겨
        // 외형 명령의 성공과 분리한다. 캐릭터는 Unity 객체라 ?. 대신 Unity null 비교로 확인한다.
        internal void ApplyCurrentAppearance()
        {
            if (!_levelInited || _character == null) return;
            // 앱 정리 중에 온 알림이 사라진 앱이나 끝난 세션을 읽지 않게 한다.
            if (SessionRuntime() == null) return;
            _character.TryApply(GameManager.Appearance.Current);
        }

        // 편집 화면이 열린 동안 플레이어 리그와 펫 모델만 숨긴다. 월드 등록, 위치, 컨트롤러는 그대로 두어 닫으면 같은 상태로 이어진다.
        void OnCustomizationOpened() => SetRigShown(false);
        void OnCustomizationClosed() => SetRigShown(true);

        // 캐릭터를 숨기는 씬은 리그를 건드리지 않는다. 레벨 초기화 전이거나 리그가 파괴됐으면 아무것도 하지 않는다.
        // 원작 Interact_CustomFace처럼 펫 모델도 함께 끄고 켠다. 펫이 없거나 먼저 파괴됐으면 Unity null 비교로 건너뛴다.
        void SetRigShown(bool shown)
        {
            PetCharacter? pet = _pet;
            if (pet != null) pet.SetModelShown(shown);
            if (!_characterVisible || _character == null) return;
            DuckCharacterRig rig = _character.Rig;
            if (rig != null) rig.gameObject.SetActive(shown);
        }

        // SceneFlow가 현재 씬을 내리기 직전에 부른다. 언로드 중 OnDestroy 순서는 정해져 있지 않으므로,
        // 컨트롤러 정리와 World 해제를 씬 객체가 모두 살아 있는 지금 끝낸다. 레벨이 없거나 이미 끝났으면 아무것도 하지 않는다.
        internal static void EndCurrentLevel()
        {
            SceneContext? level = Instance;
            if (level != null) level.EndLevel();
        }

        // 한 번만 실행하고 던지지 않는다. LevelInited를 먼저 내리고, 현재 레벨일 때만 LevelEnding을 알린다.
        // 다른 컨텍스트가 넘겨받은 레벨의 컨트롤러를 정리하지 않기 위해서다. 구독자 실패는 알림 격리가 기록하고,
        // World는 구독자가 먼저 반납하도록 알림 뒤에 닫는다.
        void EndLevel()
        {
            if (_levelEnded) return;
            _levelEnded = true;
            _levelInited = false;
            if (ReferenceEquals(Instance, this)) ObserverDispatch.InvokeEach(LevelEnding);
            WorldObjectService? world = _world;
            _world = null;
            // 해제할 World를 든 의존성 묶음도 함께 비운다. 묶음은 레벨 초기화 처리에서만 읽혀 레벨 종료 알림 뒤에는 쓰지 않는다.
            Gameplay = null;
            if (world != null) Cleanup.Run(world.Dispose, this);
        }

        // 씬 언로드로 파괴될 때 구독을 끊는다. SceneFlow를 거치지 않은 앱 종료 같은 경우를 위해 레벨 종료를 한 번 더 부르며,
        // 이미 끝났으면 무동작이다. 캐릭터 연결부는 자기 OnDestroy에서 정리하고, 이 씬의 에셋 서랍은 SceneFlow가 언로드 뒤에 반납한다.
        void OnDestroy()
        {
            SceneFlow.LoadingFinished -= OnLoadingFinished;
            CustomizationFlow.Opened -= OnCustomizationOpened;
            CustomizationFlow.Closed -= OnCustomizationClosed;
            // 외형 알림은 정적 이벤트라, 끊지 않으면 파괴된 컨텍스트에 다음 씬의 외형 변경이 닿는다.
            PlayerAppearance.Changed -= ApplyCurrentAppearance;
            EndLevel();
            if (ReferenceEquals(Instance, this)) Instance = null;
            _character = null;
            // 펫은 게임플레이 루트와 함께 파괴되므로 참조만 놓는다.
            _pet = null;
            _map = null;
            _cancelHandlers = Array.Empty<ISceneCancelHandler>();
        }
    }
}
