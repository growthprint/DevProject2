#nullable enable annotations
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game.App
{
    // 원작 GameManager처럼 게임 전체 싱글톤 시스템을 담은 프리팹의 루트다. 자식의 앱 시스템을 직렬화 필드로 소유하고
    // 정적 속성으로 내준다. 하나만 남기기, 씬 전환에도 남기기, 게임플레이 데이터 등록, 로드한 프리팹 반납도 맡는다.
    // 앱 시스템의 구성, 시작, 종료는 같은 루트의 GameRuntime이 맡는다.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GameRuntime))]
    public sealed class GameManager : MonoBehaviour
    {
        // Game 그룹에 있는 GameManager 프리팹의 주소다. AddressablesContentPolicyTests가 같은 주소를 확인한다.
        const string _address = "game.manager";

        // 프리팹 자식에 둔 앱 매니저다. 저장소, 로더, 입력 사본 같은 구성 값은 GameRuntime.ComposeAsync가 넘긴다.
        // 테스트는 대역 컴포넌트를 이 필드에 이름으로 넣으므로 이름을 바꾸지 않는다.
        [SerializeField] SaveManager _saves;
        [SerializeField] GameOptionsManager _options;
        [SerializeField] GameAudioManager _audio;
        [SerializeField] SceneFlow _scenes;
        [SerializeField] CustomizationFlow _customizationFlow;
        [SerializeField] GameplayPauseController _gameplayPause;
        // 원작 매니저들처럼 세션 상태를 나눠 소유하는 자식 컴포넌트다. 각자 저장 이벤트를 구독하고, 명령 관문은 구성 때 받는다.
        [SerializeField] ExperienceManager _experience;
        [SerializeField] DifficultyRules _difficulty;
        [SerializeField] PlayerAppearance _appearance;
        [SerializeField] GameClock _clock;
        [SerializeField] MapMarkers _markers;
        // 원작 GameManager.NoteIndex 자리의 쪽지 소유자와, 원작 SavesSystem 키로 남는 부서진 벽과 건설 지점의 소유자다.
        [SerializeField] NoteIndex _notes;
        [SerializeField] SavedLevelObjects _levelObjects;
        // 사용자 결정 D1 저장 구역의 소유자다. 특성 트리, 등록 열쇠, 선택 재능, 비콘 해금, 처치 수, 낚시 경험과 컷신 재생 기록을 맡는다.
        [SerializeField] PerkTreeManager _perks;
        [SerializeField] MasterKeysManager _masterKeys;
        [SerializeField] EndowmentManager _endowment;
        [SerializeField] BeaconManager _beacons;
        [SerializeField] KillCounter _killCounts;
        [SerializeField] CampaignFlags _flags;
        // 원작 EconomyManager 자리의 계좌 돈·상품 해금·상점 기록 소유자와, 슬롯 밖 공통 파일(사용자 결정 D2)의 소유자다.
        [SerializeField] EconomyManager _economy;
        [SerializeField] GlobalProgress _globalProgress;
        // 원작 GameManager 아래 QuestManager 자리다. 퀘스트 진행과 campaign.quests 저장을 혼자 맡는다.
        [SerializeField] QuestManager _quests;
        // 원작 LevelManager 아래 BuildingManager 자리를 앱 수명 자식으로 둔다. 건설 영역의 설치 건물과 반환 토큰을 맡는다.
        [SerializeField] BuildingManager _buildings;
        // 원작 CraftingManager 자리의 해금 제조식 소유자다. 기지 작업대와 특성 해금, 제로존 설계도 자료함 조건이 함께 쓴다.
        [SerializeField] CraftingManager _crafting;
        // 프리팹에 중첩한 앱 공통 UI와, 입력 모듈을 가진 원작 eventSystem 자리다.
        // 루트가 씬 전환에도 남으므로 실행 중 EventSystem은 이 하나뿐이다.
        [SerializeField] GameUiController _ui;
        [SerializeField] EventSystem _eventSystem;
        // 원작 GameManager가 들고 있는 게임플레이 데이터 묶음 자리다. Awake가 등록해 게임 전체가 GameplayDataSettings의
        // 정적 구역으로 읽게 한다. 에셋은 이 프리팹이 참조하므로 프리팹을 로드하는 동안 함께 살아 있다.
        [SerializeField] GameplayDataSettings _gameplayDataSettings;

        static GameManager? _instance;

        // Create가 이 인스턴스를 만들 때 로드한 프리팹 봉투다. 인스턴스가 실제로 파괴된 뒤에 반납한다.
        AssetScope<GameObject>? _prefab;
        GameRuntime _runtime;

        // 같은 루트의 앱 수명 소유자다. 앱 상태, 세션 전환, 일시정지 명령은 이 Runtime으로 쓰고, 앱 시스템은 아래 정적 속성으로 찾는다.
        public GameRuntime Runtime => _runtime;

        // 원작처럼 앱 시스템을 정적 속성으로 내준다. 앱 없이 돌 수 있는 코드(레벨 초기화, 정리 경로)는 HasInstance를 먼저 보고,
        // 구독 해제와 해제 경로는 정적 속성을 다시 읽지 않고 구독할 때 잡아 둔 참조를 쓴다.
        public static SaveManager Saves => Live._saves;
        // 저장소가 없는 구성이면 IsPersistent가 false이고 Current는 기본값이다. 첫 적용은 시작 단계의 관리자 초기화가 한다.
        public static GameOptionsManager Options => Live._options;
        public static SceneFlow Scenes => Live._scenes;
        public static CustomizationFlow Customization => Live._customizationFlow;
        // 세션 상태 소유자의 값은 GameRuntime.HasSession이 참일 때만 의미가 있다.
        public static ExperienceManager Experience => Live._experience;
        public static DifficultyRules Difficulty => Live._difficulty;
        public static PlayerAppearance Appearance => Live._appearance;
        public static GameClock Clock => Live._clock;
        public static MapMarkers Markers => Live._markers;
        // 쪽지 창과 쪽지 상호작용, 파괴물과 건설 지점이 읽고 쓰는 앱 수명 소유자다.
        public static NoteIndex Notes => Live._notes;
        public static SavedLevelObjects LevelObjects => Live._levelObjects;
        // D1 저장 구역의 앱 수명 소유자다. 특성(3-K1), 열쇠(3-M), 재능(3-E), 비콘(5-C, 5-G), 처치 수(1-X, 2-Q1), 플래그(5-F1)가 읽고 쓴다.
        public static PerkTreeManager Perks => Live._perks;
        public static MasterKeysManager MasterKeys => Live._masterKeys;
        public static EndowmentManager Endowment => Live._endowment;
        public static BeaconManager Beacons => Live._beacons;
        public static KillCounter KillCounts => Live._killCounts;
        public static CampaignFlags Flags => Live._flags;
        // 경제는 Economy 구역 소유자로 1-E2 지불 명령과 2-P1 상점이 쓴다. 공통 파일 값은 세션과 상관없이 의미가 있고 3-E 재능이 쓴다.
        public static EconomyManager Economy => Live._economy;
        public static GlobalProgress GlobalProgress => Live._globalProgress;
        // 퀘스트 창구, 과제 생성기, 처치와 탈출 알림, 퀘스트 보상 명령이 읽고 쓰는 앱 수명 소유자다.
        public static QuestManager Quests => Live._quests;
        // 기지 건설 영역과 건설 화면이 쓰고, 제로존의 퀘스트 건설 과제도 읽으므로 앱 수명 소유자다.
        public static BuildingManager Buildings => Live._buildings;
        // 제조, 설계도 등록, 특성 제조식 해금 명령과 "등록됨" 표시가 읽고 쓰는 앱 수명 소유자다.
        public static CraftingManager Crafting => Live._crafting;
        public static EventSystem EventSystem => Live._eventSystem;
        // 세션을 만들고 바꾸는 일은 GameRuntime이 맡는다. 씬과 UI는 이 Manager로 명령만 보내고, 세션이 없으면 null이다.
        public static GameSessionManager? SessionManager => Live._runtime.Session;
        // 아래는 같은 어셈블리만 쓰므로 internal이다. 오디오는 끈 구성이나 FMOD 시작 실패에서도 돌려주며,
        // 씬 코드가 null 확인 없이 부르고 열리지 않은 관리자는 소리 명령을 무시한다.
        internal static GameAudioManager Audio => Live._audio;
        internal static GameplayPauseController GameplayPause => Live._gameplayPause;
        internal static GameUiController Ui => Live._ui;
        // 원작 mainPlayerInput 자리인 입력 실행 사본이다. 씬 코드가 프로젝트 입력 에셋 대신 이것을 읽어야 저장한 키 설정을 따른다.
        internal static InputActionAsset InputActions => Live._runtime.InputActionsCopy;

        // 앱에 하나뿐인 GameManager다. 읽어도 만들지 않는다. 만들게 하면 정리 중의 늦은 읽기가 앱 전체를 다시 로드하고,
        // 등록을 빠뜨린 테스트도 바로 실패하지 않고 진짜 프리팹을 로드한다. 만드는 일은 GameBootstrap의 CreateAsync만 한다.
        // 없을 때 읽으면 다음 멤버 접근이 NullReferenceException으로 멈추므로, 앱 없이 도는 코드는 HasInstance를 먼저 본다.
        public static GameManager Live => _instance!;

        // 앱 없이 돌 수 있는 코드가 Live를 읽기 전에 본다. 파괴된 객체는 Unity null 비교로 없는 것으로 본다.
        public static bool HasInstance => _instance != null;

        // Domain Reload를 끄면 정적 값이 Play 세션을 넘어 남으므로 세션 시작마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        // GameBootstrap이 Addressables를 준비한 뒤 한 번 부른다. 동기 로드는 메인 스레드를 멈추므로 프리팹 로드를 기다린 뒤,
        // 기다림이 끝난 같은 호출에서 생성한다. 생성 중에 루트의 Awake가 Live를 잡는다.
        // 로드 실패는 원래 오류로, 로드 중 앱 종료는 취소로 끝나며 둘 다 아무것도 만들지 않는다. 기록은 GameBootstrap이 한 번 한다.
        // 살아 있는 GameManager가 있으면 복제본이 Awake에서 스스로 파괴돼 Runtime이 비므로 그때는 부르지 않는다.
        // 제품 Bootstrap 씬에 GameBootstrap이 하나뿐임은 EditMode 테스트가 확인한다.
        internal static async UniTask<GameManager> CreateAsync(CancellationToken token)
        {
            AssetScope<GameObject> prefab = await AddressablesAssetProvider.LoadByAddressAsync<GameObject>(_address, token);
            GameObject created = Instantiate(prefab.Asset);
            GameManager manager = created.GetComponent<GameManager>();
            manager._prefab = prefab;
            return manager;
        }

        // 원작 GameManager.Awake처럼 살아 있는 인스턴스가 이미 있으면 새 것을 파괴하고, 아니면 씬 전환에도 남긴다.
        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            _runtime = GetComponent<GameRuntime>();
            // 남는 인스턴스만 게임플레이 데이터를 등록하고, 앱 시작과 씬 코드가 읽기 전인 여기서 끝낸다.
            // 프리팹 안 Awake 순서는 정해져 있지 않으므로 자식 컴포넌트는 Awake와 OnEnable에서 정적 구역을 읽지 않는다.
            GameplayDataSettings.Register(_gameplayDataSettings);
            DontDestroyOnLoad(gameObject);
        }

        // 프리팹은 이 인스턴스의 파괴가 끝난 다음 프레임에 반납한다. 먼저 반납하면 파괴 중인 인스턴스가 쓰던 자원이 먼저 풀린다.
        // 에디터에서 Play Mode를 끝내는 중이면 기다리지 않고 바로 반납하며, Unity가 이미 회수한 핸들은 반납이 건너뛴다.
        void OnDestroy()
        {
            if (ReferenceEquals(_instance, this))
            {
                // 강제 종료의 동기 구간이 Live와 앱 시스템을 읽을 수 있도록 Live를 비우기 전에 건다.
                _runtime.AbortForRootDestroy();
                _instance = null;
                // 아래 프리팹 반납이 프리팹이 참조한 이 에셋도 풀 수 있어 등록을 거둔다. 같은 에셋을 참조하는 중복 사본이
                // 살아 있는 등록을 지우지 않도록 남은 인스턴스만 거둔다.
                GameplayDataSettings.Unregister(_gameplayDataSettings);
            }
            AssetScope<GameObject>? prefab = _prefab;
            _prefab = null;
            if (prefab != null)
                Cleanup.RunAsync(async () =>
                {
                    await UnityObjects.WaitForDestroyAsync();
                    await prefab.ReleaseAsync();
                }).Forget();
        }
    }
}
