#nullable enable annotations

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Game.App
{
    // 앱 수명 동안 하나뿐인 소유자로서 여러 플레이 세션과 씬 전환을 조정한다. GameManager 루트에 붙어 씬이 바뀌어도 남으며,
    // GameManager.Live.Runtime으로 찾는다. 앱 수명 상태, 세션 상태, 확정 세션, 상태 알림, 일시정지 판정과 앱 시스템의 구성과 정리를 맡는다.
    // 앱 시스템은 GameManager가 소유해 정적 속성으로 내주고, 각 시스템은 이 Runtime을 중재자로 삼는다.
    // Unity 메인 스레드 전용이라 잠금 없이 상태를 바꾼다. sealed가 아닌 것은 테스트 하위 클래스 TestGameRuntime 때문이고,
    // private protected 멤버는 그 클래스가 대역을 넣고 읽는 자리다.
    [DisallowMultipleComponent]
    public partial class GameRuntime : MonoBehaviour
    {
        // 원작 Health처럼 플레이어 생존 수치를 소유하는 컴포넌트로, GameManager 프리팹 자식을 연결해 둔다.
        // 테스트 하위 클래스는 이 private 필드를 이름으로 채운다.
        [SerializeField] PlayerVitals _vitals;
        // 앱 소유 자원을 만든 순서대로 등록하는 범위다. 정리는 등록의 역순으로 해제한다.
        // Begin을 부르지 않는 테스트 하위 클래스도 바로 등록하도록 컴포넌트를 만들 때 함께 만든다.
        private protected readonly ResourceScope _appResources = new ResourceScope();
        // 새 게임이 공유하는 불변 기본 스냅샷이라 복제하지 않는다. 제품에서는 구성이 게임플레이 데이터를 검증한 뒤 한 번 대입하고,
        // 테스트 하위 클래스는 컴포넌트를 붙인 뒤 직접 대입한다.
        private protected GameSessionSnapshot _defaultSession;
        // 시간 배율이나 입력을 직접 만지지 않고 일시정지 계약으로 플레이를 멈춘다. 제품은 구성 때 GameManager의 일시정지 컴포넌트를 넣고,
        // 계약 타입으로 따로 두는 것은 테스트 하위 클래스가 대역을 넣기 때문이다. 원래 상태 복원은 앱 자원 범위의 해제가 맡는다.
        private protected IGameplayPause? _pause;
        // GameManager 프리팹에 중첩한 공용 HUD다. 소유 씬이 붙기 전에는 보이지 않게 구성 때 끈다.
        // 씬을 내리면 소유 컨트롤러가 떼어 내므로 따로 해제하지 않고, 루트와 함께 파괴된다.
        [SerializeField] SharedHudView _hud;
        // GameManager 프리팹에 중첩한 대사 상자, 말풍선, 피해 숫자 표시다. 공용 HUD와 같은 소유 규칙을 따른다.
        [SerializeField] OriginalGameplayView _gameplayView;
        // Begin이 한 번 대입하는 앱 자원 구성 값이라 readonly로 둘 수 없다. 테스트 하위 클래스는 Begin을 부르지 않아
        // 이 값이 비고, ComposeAsync를 빈 단계로 재정의해 읽지도 않는다. 오디오 사용 여부는 Bootstrap 씬의 GameBootstrap이 정한다.
        bool _enableAudio;
        // 구성 때 만든 앱 수명 에셋 서랍장이다. 씬 컨텍스트가 Assets로 받아 자기 씬 서랍에 게임플레이 카탈로그를 로드한다.
        // 테스트 하위 클래스는 구성을 건너뛰므로 비어 있다.
        AssetLifetimes _assets;
        // 키 설정을 적용한 입력 실행 사본이다. 만들고 해제하는 일은 이 Runtime이 맡고,
        // 씬 코드도 GameManager.InputActions로 같은 사본을 읽어야 키 설정을 따른다.
        InputActionAsset _inputActions;
        // Begin을 받은 제품 Runtime만 Start에서 스스로 시작하게 하는 표시다. Begin은 GameBootstrap이 한 번만 부른다.
        bool _begun;
        // 확정한 세션만 담는다. 실제 씬 연결부가 같은 객체를 공유한다.
        GameSessionManager? _session;
        // 알림 배달 중인지 표시해 재진입 발행을 중첩 호출하지 않는다.
        bool _publishing;
        // 배달 중에 들어온 발행 요청을 하나로 병합해 현재 배달이 끝난 뒤 한 번 더 알린다.
        bool _publishPending;

        // 원작 LevelManager.OnLevelInitialized처럼 제품 Runtime이 메뉴까지 시작을 마치면 한 번 알린다.
        // 늦게 구독하는 쪽은 IsInitialized를 먼저 확인한다. 구독자 하나의 실패는 ObserverDispatch가 막고 기록한다.
        public static event Action? Initialized;

        // Initialized를 보낸 뒤 제품 Runtime이 정리되거나 파괴될 때까지 참이다. Begin을 받지 않은 테스트 Runtime은 바꾸지 않는다.
        public static bool IsInitialized { get; private set; }

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 값과 구독자가 남으므로 세션 시작마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsInitialized = false;
            Initialized = null;
        }

        // 생성자는 역할별 협력 객체만 만들고 Unity API는 부르지 않는다. 구성 값은 Begin이, 앱 자원은 ComposeAsync가 만든다.
        // 테스트 하위 클래스는 Awake가 불리지 않는 비활성 호스트에 붙으므로 생성 때 만들어야 늘 있다.
        public GameRuntime()
        {
            Operations = new RuntimeOperations(this);
            GameplayInput = new GameplayInputBlocker(this);
            // 1회 정리는 인스턴스 메서드를 넘겨야 하므로 필드 초기화 대신 생성자에서 만든다.
            _cleanup = new ReleaseOnce(CleanupCoreAsync);
        }

        // 원작 GameManager와 LevelManager처럼 스스로 시작한다. Begin을 받지 않은 테스트 Runtime은 시작하지 않고,
        // 첫 프레임 전에 도착한 종료가 이기도록 Created 상태에서만 시작한다. 시작 예외는 재시도나 실패 화면 없이 여기서 한 번만 기록한다.
        // 메뉴 로드 실패는 씬 흐름이 이미 기록하고 예외 없이 정리로 끝난다. 종료로 멈춘 시작은 예외 없이 끝나 기록되지 않는다.
        void Start()
        {
            if (!_begun || State != GameRuntimeState.Created)
                return;
            StartAndNotifyAsync().Forget(exception => Log.Instance.Exception(exception, this));
        }

        // 시작 독점 작업이 끝나 잠금이 풀린 뒤에 알린다. 그래야 받는 쪽이 곧바로 명령을 보내도 IsBusy 검사에 걸리지 않는다.
        // 메뉴 로드 중 종료가 요청됐거나 메뉴 로드 실패로 정리돼 Running이 되지 못했으면 알리지 않는다. Begin을 받은 제품 Runtime만 여기 온다.
        async UniTask StartAndNotifyAsync()
        {
            await StartAsync();
            if (State != GameRuntimeState.Running)
                return;

            IsInitialized = true;
            ObserverDispatch.InvokeEach(Initialized);
        }

        // 제품 초기화다. GameBootstrap이 GameManager.CreateAsync로 앱을 만들고 저장 관리자를 구성한 직후 한 번 불러 오디오 사용 여부를 넘긴다.
        // 자원과 새 게임 기본 세션은 만들지 않고, 시작은 이 Runtime의 Start가 맡는다. UI는 이 프리팹에 중첩돼 있어 둘 씬을 받지 않는다.
        internal void Begin(bool enableAudio)
        {
            _begun = true;
            _enableAudio = enableAudio;
        }

        // 기본 외형을 건드리지 않는 새 게임 세션을 만든다. 새 게임은 소유 아이템 없이 원본의 난이도 준비 단계(Prepare)부터 시작하고,
        // 아이템은 튜토리얼 진입 때 만드는 캠페인 소유 그래프에만 있다. 원작처럼 새 게임 외형은 CustomFaceData의 기본 외형이다.
        // 제품 구성은 GameplayDataSettings.TryValidate가 기본 외형을 확인한 뒤에만 부르므로 '!'로 null 경고를 닫는다.
        internal static GameSessionSnapshot CreateDefaultSession() =>
            new GameSessionSnapshot(GameScene.Prepare, GameplayDataSettings.CustomFaceData.DefaultPreset!, CampaignState.CreateNew());

        // 루트가 파괴될 때 GameManager.OnDestroy가 Live를 비우기 전에 부른다. 강제 종료를 거는 순간 앱 시스템이 모두 살아 있게 하려고,
        // 같은 루트에서 OnDestroy 순서가 정해지지 않는 이 컴포넌트가 따로 받지 않는다. 기다릴 호출자가 없으므로 실패는 Cleanup이 기록만 한다.
        // Begin을 받지 않은 Runtime과 이미 정지한 Runtime은 정리할 것이 없다. 시작 완료 표시를 먼저 내려,
        // 파괴된 Runtime을 늦은 구독자가 시작된 것으로 보지 않게 한다.
        internal void AbortForRootDestroy()
        {
            if (!_begun) return;
            ClearInitialized();
            if (State != GameRuntimeState.Stopped)
                Cleanup.RunAsync(AbortAsync, this).Forget();
        }

        // 시작 완료 표시는 Begin을 받은 제품 Runtime만 세우므로 그 Runtime만 내린다. 파괴와 앱 정리 완료(CleanupCoreAsync)가 부른다.
        // 그래야 테스트 Runtime의 정리가 실행 중인 제품 Runtime의 표시를 지우지 않는다.
        void ClearInitialized()
        {
            if (_begun) IsInitialized = false;
        }

        // 상태가 바뀌었다는 신호만 메인 스레드에서 보낸다. 구독자는 State 같은 현재 값을 직접 읽는다.
        public event Action? StateChanged;
        // 앱 수명 상태다. 시작, 종료, 정리 단계만 바꾼다.
        public GameRuntimeState State { get; internal set; }
        // 메뉴와 플레이 상태를 앱 수명 상태와 따로 조회한다. 확정 세션이 없는 메뉴 상태도 값으로 표현한다.
        public GameSessionState SessionState { get; internal set; }
        // 독점 작업이나 종료처럼 충돌하는 작업 중에만 공통 입력을 막는다. 명시 저장과 조회는 따로 진행하므로 넣지 않는다.
        public bool IsBusy => Operations.IsReserved || State == GameRuntimeState.Stopping;
        // 호스트 파괴나 앱 종료로 강제 중단을 받았는지 같은 어셈블리의 표시 코드에 알린다.
        internal bool IsAborting => _abortRequested;
        // 취소 토큰 대신 비동기 작업이 await 뒤마다 보는 판정이다. 이 Runtime이 파괴됐거나 강제 종료를 받았으면 참이고,
        // 그때 작업은 결과를 반영하지 않고 조용히 끝난다. 정상 종료는 작업이 끝나기를 기다리므로 여기에 들지 않는다.
        internal bool IsAbandoned => this == null || _abortRequested;
        // 파일 쓰기는 GameManager의 저장 관리자가 소유하므로 그 진행 여부를 그대로 읽는다.
        // IsBusy와 따로 두어 저장 중에도 플레이 재개를 막지 않는다.
        public bool IsSaving => GameManager.Saves.IsSaving;

        // 아래는 역할별 협력 객체다. 공개 객체는 UI와 씬이 그 역할의 명령과 상태를 직접 쓰는 창구다.
        public GameplayInputBlocker GameplayInput { get; }
        internal RuntimeOperations Operations { get; }

        // 협력 객체가 읽는 구성 결과다. 제품은 ComposeAsync가 채우고, 테스트 하위 클래스는 같은 필드에 대역을 넣는다.
        internal IGameplayPause? Pause => _pause;
        internal ResourceScope AppResources => _appResources;
        internal GameSessionSnapshot DefaultSession => _defaultSession;

        // 씬 컨텍스트가 자기 씬 서랍에 에셋을 로드할 때 쓰는 앱 수명 서랍장이다. 제품에서는 구성이 끝난 뒤에만 읽힌다.
        internal AssetLifetimes Assets => _assets;
        // GameManager.InputActions만 읽는 입력 실행 사본이다. 다른 코드는 GameManager를 거쳐 하나의 경로로 찾는다.
        internal InputActionAsset InputActionsCopy => _inputActions;
        // 앱 수명 공용 HUD다. 한 번에 한 씬 컨트롤러만 붙여 쓰고, 그 씬이 내려가면 떼어 낸다.
        internal SharedHudView Hud => _hud;
        // 앱 수명 대사 상자, 말풍선, 피해 숫자 표시다. 공용 HUD와 같은 소유 규칙을 따른다.
        internal OriginalGameplayView GameplayView => _gameplayView;
        // 체력과 생존 수치는 세션 기록의 플레이어 사본이 아니라 이 소유자에서 읽고 바꾼다.
        public PlayerVitals Vitals => _vitals;

        // GameManager.SessionManager만 읽는 확정 세션이다. 세션을 만들고 바꾸는 일은 이 Runtime이 맡는다.
        internal GameSessionManager? Session => _session;

        // 확정 세션의 불변 스냅샷을 공유 참조로 돌려주며, 편집과 전환 중에도 읽을 수 있다. 세션이 있을 때만 부른다.
        // 세션 유무가 바뀔 수 있는 곳은 HasSession이나 null 가능한 GameManager.SessionManager를 먼저 본다.
        public GameSessionSnapshot Snapshot() => _session!.Snapshot();

        // 세션이 있고 기지 밖이면 메뉴 복귀나 종료가 진행을 저장 없이 버린다. UI는 이 값으로 이탈 확인을 띄울지 정한다.
        public bool LeaveDiscardsProgress =>
            _session != null && !SavePointPolicy.SavesOnMenuOrQuit(_session.Snapshot().SceneId);

        // 실행 중이고 플레이 상태이며 전환이 없을 때만 게임플레이 시뮬레이션을 돌린다.
        // 포커스, 대사, 상호작용 같은 씬별 조건은 호출부가 더한다.
        public bool IsGameplayActive => CanMutateSession && SessionState == GameSessionState.Playing;

        // 게임플레이가 활성이어도 지도나 UI가 입력을 막고 있으면 플레이어 입력을 받지 않는다.
        public bool AcceptsGameplayInput => IsGameplayActive && !GameplayInput.IsBlocked;

        // 메뉴를 준비해 띄우는 데까지 하며, 저장이 없어도 파일을 만들지 않는다. 제품 Runtime은 Start에서 스스로 한 번 부른다.
        // Start는 Created 상태에서만 부르고 테스트도 새 Runtime마다 한 번만 부르므로, 중복 시작 검사는 두지 않는다.
        public UniTask StartAsync() => Operations.Run(() => State = GameRuntimeState.Starting, StartCoreAsync);

        // 앱 자원을 구성하고 옵션과 슬롯 상태를 준비한 뒤 메뉴 씬을 로드한다. 실패하면 그때까지 등록한 앱 자원을 역순으로 정리한다.
        // 제품 Runtime은 Begin을 받은 뒤에만 Start에서 스스로 시작하므로 구성 단계를 분기 없이 부른다.
        async Task StartCoreAsync()
        {
            try
            {
                // 제품은 Begin이 받은 값과 등록된 게임플레이 데이터로 앱 자원을 만든다. 테스트 하위 클래스는 Create에서 대역을 넣고
                // 빈 단계로 재정의한다.
                await ComposeAsync();
                // 저장할 때는 세션 허브가 자기 몫의 저장 구역을 채우고, 새 게임이나 저장 파일을 불러오면 허브가 세션을 만든다.
                // 정적 알림이라 앱 자원 정리(CleanupCoreAsync)가 구독을 끊어, 정지한 Runtime에 알림이 닿지 않게 한다.
                SaveManager.CollectSaveData += CollectHubSections;
                SaveManager.FileLoaded += LoadHubSections;

                // 메뉴를 보이기 전에 저장된 언어와 화면 설정을 적용한다.
                await GameManager.Options.InitializeAsync();
                // 옵션 파일을 읽는 사이 루트가 파괴됐으면 저장 관리자도 함께 사라졌으므로 슬롯을 읽지 않는다.
                // 정리는 루트 파괴가 건 강제 종료가 맡는다.
                if (!GameManager.HasInstance) return;
                // 재능과 분노 난이도 해금은 슬롯과 상관없이 첫 세션 전에 있어야 하므로 옵션 다음에 공통 파일을 읽는다.
                // 읽기 결과는 시작을 막지 않는다. 읽는 사이 루트가 파괴됐으면 옵션과 같은 이유로 슬롯을 읽지 않는다.
                await GameManager.GlobalProgress.InitializeAsync();
                if (!GameManager.HasInstance) return;
                // 여섯 슬롯 목록을 읽고 선택 슬롯 상태를 게시한 뒤 첫 메뉴를 공개한다.
                await GameManager.Saves.RefreshAllAsync();
                // 새 게임이나 이어하기를 누른 순간 멈추지 않도록, 앱 수명으로 드는 아이템 카탈로그를 메뉴를 보이기 전에 읽어 둔다.
                // 첫 세션 진입은 같은 씬 흐름 캐시를 써서 다시 읽지 않는다. Editor Fast Mode에서는 이 읽기가 메인 스레드를 0.5초쯤 막는다.
                await GameManager.Scenes.LoadItemCatalogAsync();
                if (!GameManager.HasInstance) return;
                // 메뉴 로드가 되돌릴 수 없는 지점이라 바로 앞에서 버려졌는지 본다. 정리는 강제 종료가 맡는다.
                if (IsAbandoned) return;
                // 메뉴에서는 플레이 입력과 시뮬레이션을 멈춘다. 멈추기 전의 원래 상태는 앱을 끝낼 때만 복원한다.
                // 바로 앞의 구성을 마친 뒤라 일시정지 계약이 늘 있다.
                PausePlay();
                // 로드 실패는 씬 흐름이 이미 기록했으므로 다시 기록하지 않고 앱 자원만 정리한다.
                // 정리가 Stopped를 세우므로 Initialized는 나가지 않는다.
                if (!await GameManager.Scenes.LoadAsync(GameScene.MainMenu))
                {
                    await CleanupAsync();
                    return;
                }
                SessionState = GameSessionState.Menu;
                if (State == GameRuntimeState.Starting) State = GameRuntimeState.Running;
            }
            // 예외 경계: 시작 실패는 앱 자원을 정리한 뒤 원래 시작 오류를 그대로 전달한다.
            catch (Exception)
            {
                // 정리는 던지지 않고 단계별 실패를 기록만 하므로 원래 시작 오류가 가려지지 않는다.
                await CleanupAsync();
                throw;
            }
        }

        // 새 게임은 기본 세션으로 바로 바꾸고 저장 없이 시작한다. 슬롯 파일은 프롤로그를 떠나 튜토리얼에 도착하는
        // 첫 저장 지점에서 처음 기록하므로, 그때까지 슬롯에 있던 저장 파일은 그대로 남는다.
        public UniTask NewGameAsync() => BeginSession(true);
        // 이어하기는 매번 슬롯 저장을 새로 읽고, 파일이 없으면 실패한다. 출격 정리, 결과 정산, 단계 씬 보정 같은
        // 복구 변경은 메모리에만 두고 다음 저장 지점이 기록한다.
        public UniTask ContinueAsync() => BeginSession(false);

        // 새 게임과 이어하기가 같은 예약을 쓰고, 본문은 GameManager의 씬 흐름이 맡는다. 예약하면 그 전에 시작한 저장 조회의 결과는 반영하지 않는다.
        UniTask BeginSession(bool newGame) => Operations.Run(
            () => SessionState = GameSessionState.Starting, () => GameManager.Scenes.EnterSessionAsync(newGame));

        // 저장 작업과 별개인 전환 잠금을 잡는다. 원본처럼 기지에서는 씬을 내리기 전에 저장하고,
        // 저장에 실패해도 기록만 하고 메뉴로 간다. 기지 밖에서는 저장 없이 메뉴로 가 마지막 저장 지점으로 돌아간다.
        public UniTask ReturnToMenuAsync() => ReturnToMenu(true);
        // 기지에서도 저장하지 않고 메뉴로 돌아간다. 기지 밖 이탈 확인 뒤의 메뉴 복귀가 쓴다.
        public UniTask ReturnToMenuWithoutSavingAsync() => ReturnToMenu(false);

        // 메뉴 복귀 예약의 공통 진입점이고, 본문은 GameManager의 씬 흐름이 맡는다. saving이 false면 기지에서도 저장하지 않는다.
        UniTask ReturnToMenu(bool saving) => Operations.Run(
            () => SessionState = GameSessionState.Returning, () => GameManager.Scenes.ReturnToMenuAsync(saving));

        // 정상 종료다. 진행 중인 작업이 끝나기를 기다린 뒤 기지 세션이면 최종 저장하고, 저장에 실패해도 기록만 하고 종료한다.
        public UniTask ShutdownAsync() => RequestStop(false);
        // 정상 종료와 같지만 최종 저장을 하지 않는다. 기지 밖 종료 확인이 쓴다.
        public UniTask ShutdownWithoutSavingAsync() => RequestStop(false, withoutSaving: true);
        // 호스트 강제 종료는 저장 없이 모든 소유 자원을 회수한다.
        public UniTask AbortAsync() => RequestStop(true);

        // 시간과 입력을 먼저 멈춘 뒤 일시정지 상태를 공개한다. 거부 조건은 TrySetPlayPaused와 같다.
        public bool TryPause() => TrySetPlayPaused(true);

        // 진행 중인 저장은 재개를 막지 않는다. 거부 조건은 TrySetPlayPaused와 같다.
        public bool TryResume() => TrySetPlayPaused(false);

        // 플레이 세션이 아니거나 전환 중이면 false이고 호출자가 안내한다. 결과 화면도 거부한다.
        // 이미 목표 상태면 아무것도 바꾸지 않고 true다.
        bool TrySetPlayPaused(bool paused)
        {
            // 플레이와 일시정지는 확정 세션이 있을 때만 세우므로 세션 명령 허용 조건만 본다.
            if (!CanMutateSession) return false;
            GameSessionState target = paused ? GameSessionState.Paused : GameSessionState.Playing;
            if (SessionState == target) return true;
            // 실행 중 플레이 세션에서만 오므로 구성이 넣은 일시정지 계약이 늘 있다.
            _pause!.SetPaused(paused);
            SessionState = target;
            Publish();
            return true;
        }

        // 현재 외형으로 캐릭터 표시를 다시 적용하며, 외형이나 저장 데이터는 바꾸지 않는다.
        // 거부 조건은 TrySetPlayPaused와 같다. 레벨이 없으면 적용할 캐릭터가 없어 그대로 성공한다.
        public bool TryRetryCharacterPresentation()
        {
            // 플레이와 일시정지는 확정 세션이 있을 때만 세우므로 세션 명령 허용 조건만 본다.
            if (!CanMutateSession) return false;
            // 씬 컨텍스트는 Unity 객체라 ?.를 쓰지 않고, 파괴된 객체도 거르는 Unity null 비교로 확인한 뒤 현재 외형을 다시 적용하게 한다.
            SceneContext? context = SceneContext.Instance;
            if (context != null) context.ApplyCurrentAppearance();
            Publish();
            return true;
        }

        // 세션을 바꾸는 명령의 허용 조건이다. 전환 중에는 닫고, 종료 상태(결과 화면)는 '계속'만 받으므로
        // 플레이와 일시정지에서만 연다. 두 상태는 확정 세션이 있을 때만 세우므로 세션 유무를 따로 보지 않는다.
        internal bool CanMutateSession => State == GameRuntimeState.Running && !IsBusy &&
            (SessionState == GameSessionState.Playing || SessionState == GameSessionState.Paused);

        // 옵션 저장은 전환이 없고 메뉴나 일시정지 화면일 때만 허용한다. 옵션 관리자가 이 조건을 저장 허용 판정으로 쓰고,
        // 테스트 하위 클래스의 옵션 관리자도 같은 조건을 쓴다.
        internal bool CanEditOptions => State == GameRuntimeState.Running && !IsBusy &&
            (SessionState == GameSessionState.Menu || SessionState == GameSessionState.Paused);

        // 독점 작업이 진행 중인지 검사만 한다. Debug의 TestGameRuntime.SaveAsync가 저장 지점 입구를 부르기 전에 쓴다.
        // 호출 위치를 넘겨 로그가 실제 호출자를 가리키게 한다.
        internal void AssertIdle([CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
            Log.Instance.Assert(!IsBusy, "다른 게임 작업이 진행 중입니다.",
                callerFile: callerFile, callerLine: callerLine);

        // 세션 명령 허용을 갱신한 뒤 상태 변경 신호를 보낸다. 배달 중에 다시 불려도 허용은 바로 갱신한다.
        // 작업을 예약한 직후의 알림이 허용을 끈다. 허용을 켜는 곳은 여기와 편집 확정 동안의 외형 확정(CustomizationFlow)뿐이다.
        // 구독자 하나의 실패는 ObserverDispatch가 그 구독자에서 막고 한 번 기록한다.
        internal void Publish()
        {
            // 전환과 편집과 결과 화면 동안 세션 명령을 잠가, 진행 중인 작업이 기준으로 삼은 세션이 바뀌지 않게 한다.
            _session?.SetCommandsAllowed(CanMutateSession);
            if (_publishing)
            {
                _publishPending = true;
                return;
            }
            _publishing = true;
            try
            {
                do
                {
                    _publishPending = false;
                    ObserverDispatch.InvokeEach(StateChanged);
                }
                while (_publishPending);
            }
            finally { _publishing = false; }
        }

        // 실행 중 플레이나 외형 편집 상태일 때만 시간을 흐르게 하고 그 밖에는 멈춘다. 호출자는 상태를 먼저 대입한 뒤 부른다.
        // 편집 중에도 월드 시간은 흐르게 두고, 플레이어 입력은 편집이 따로 건 게임플레이 입력 차단이 막는다.
        // 호출자는 모두 구성 뒤의 세션 작업과 편집 종료라 일시정지 계약이 늘 있다.
        internal void ApplyPlayPause() =>
            _pause!.SetPaused(State != GameRuntimeState.Running ||
                (SessionState != GameSessionState.Playing && SessionState != GameSessionState.Customizing));

        // 씬 흐름과 편집 흐름이 세션 상태를 바꿀 때 상태 대입과 시간 적용을 늘 이 순서로 함께 하게 한다.
        internal void ApplySessionState(GameSessionState state)
        {
            SessionState = state;
            ApplyPlayPause();
        }

        // 메뉴 진입과 세션 작업이 저장과 씬 전환 동안 플레이를 멈춘다. 모두 구성 뒤에만 불려 일시정지 계약이 늘 있다.
        internal void PausePlay() => _pause!.SetPaused(true);

        // 물러나는 세션의 명령 권한과 결과 화면 정보를 함께 끝낸다. 기지 자동 저장은 기지 씬이 내려갈 때 스스로 멈춘다.
        // 새 세션은 진입 중의 불러오기 알림을 받은 LoadHubSections가 만들고, 메뉴 복귀와 앱 정리는 null로 비운다.
        internal void ReplaceSession(GameSessionSnapshot? snapshot)
        {
            // 물러나는 Manager를 영구히 닫아, 뒤늦은 명령이나 Changed 알림이 새 세션을 바꾸지 못하게 한다.
            _session?.Close();
            _session = snapshot == null ? null : new GameSessionManager(snapshot);

            // 결과 화면 정보는 씬 흐름이 들고 있으므로 연결 때 잡아 둔 흐름에서 지운다. 연결 전에 실패한 시작의 정리와
            // 루트 파괴 뒤의 정리에서는 흐름이 없거나 함께 사라졌으므로 건너뛴다.
            if (_scenes != null) _scenes.ClearClosure();
        }

        // 시작 첫 단계(StartCoreAsync)가 부르는 제품 구성이다. 테스트 하위 클래스는 대역을 Create에서 넣고
        // 이 메서드를 빈 단계로 재정의한다. 각 자원은 만든 직후 등록한다. 중간 단계가 실패해도 시작 실패 정리가 등록된 자원만 역순으로 해제한다.
        // 연결은 맨 끝, 옵션 초기화보다 먼저 한다. 그래야 Applied 구독자가 저장된 옵션의 첫 적용을 받는다.
        internal virtual async UniTask ComposeAsync()
        {
            // 앱 자원을 만들기 전에 GameManager 프리팹이 등록한 게임플레이 데이터를 한 번 검증한다. 첫 결함은 검증이 이미
            // 보고했으므로 고정 문구 예외로 시작을 멈추고, 정리는 StartCoreAsync의 시작 실패 경로가 맡는다.
            if (!GameplayDataSettings.TryValidate())
                throw new InvalidOperationException("게임플레이 데이터 설정 검증에 실패했습니다.");
            // 새 게임 기본 세션은 기본 외형을 검증한 뒤에만 만든다.
            _defaultSession = CreateDefaultSession();

            // 에셋 Provider를 가장 먼저 등록해 가장 나중에 반납한다. 그래야 모든 인스턴스가 파괴된 뒤 프리팹을 반납한다.
            var assets = new AssetLifetimes(new AddressablesAssetProvider());
            await _appResources.RegisterReleaseHandleAsync(assets);
            // 해제 등록이 끝난 뒤에 내줘, 씬 컨텍스트가 로드한 에셋이 앱 정리에서 반드시 반납되게 한다.
            _assets = assets;
            var loader = new SceneLoader();

            // 오디오는 Bootstrap 씬의 옵션으로 활성화를 결정하고, UI보다 먼저 등록해 UI의 화면 구독이 끊긴 뒤에 해제한다.
            bool audioOpened = _enableAudio && GameManager.Audio.Open();
            if (audioOpened)
                await _appResources.RegisterReleaseHandleAsync(GameManager.Audio);

            // 키 설정은 프로젝트 입력 에셋이 아닌 실행 사본에 적용한다. UI보다 먼저 등록해 UI가 해제된 뒤 파괴한다.
            // 공용 입력 에셋 연결은 맨 앞의 GameplayDataSettings.TryValidate가 확인했다.
            InputActionAsset inputActions = Object.Instantiate(GameplayDataSettings.InputActions);
            await _appResources.RegisterReleaseHandleAsync(CreateInputActionsLifetime(inputActions));
            // 해제 등록이 끝난 사본만 씬 코드에 내줘, 씬이 읽은 사본도 앱 정리에서 반드시 파괴되게 한다.
            _inputActions = inputActions;

            // 앱 공통 UI는 GameManager 루트와 함께 파괴되므로, 정리 때는 화면 구독과 입력만 끊는다.
            // 입력 사본 뒤에 등록해, 입력 사본을 파괴하기 전에 UI가 입력 구독을 놓는다.
            await _appResources.RegisterReleaseHandleAsync(GameManager.Ui);

            // 공용 HUD와 게임플레이 월드 표시는 프리팹에 중첩한 앱 수명 인스턴스다. 소유 씬이 붙기 전까지 보이지 않게 숨긴다.
            // 공용 HUD는 루트를 켜 둔 채 Canvas만 숨겨, 첫 레벨도 HUD 전체를 다시 켜지 않는다.
            // 정리 때 씬 흐름이 씬을 내리면 소유 컨트롤러가 먼저 떼어 내고 파괴는 루트가 맡으므로 해제 대상으로 등록하지 않는다.
            _hud.HideUntilAttached();
            _gameplayView.HideUntilAttached();

            // 저장 관리자는 GameBootstrap이 시작 전에 구성했다. 옵션 저장소가 없으면 옵션을 저장하지 않는 구성이다.
            ISaveStorage? optionsStorage = GameManager.Saves.OptionsStorage;
            (GameInputOptions inputOptions, GameGraphicsOptions? graphicsOptions) =
                await CreateOptionAdaptersAsync(inputActions, optionsStorage != null);

            // 일시정지 컴포넌트는 구성 때 원래 입력 상태를 기록하고, 등록이 성공한 뒤에만 Player 입력을 처음 켠다.
            // 방금 구성한 컴포넌트는 아직 정지나 해제 전이라 첫 활성화 검사를 통과한다. 그 뒤로는 _pause 계약으로만 멈추고 재개한다.
            GameManager.GameplayPause.Configure(inputActions);
            await _appResources.RegisterReleaseHandleAsync(GameManager.GameplayPause);
            _pause = GameManager.GameplayPause;
            GameManager.GameplayPause.EnablePlayerInput();

            // 슬롯 밖 공통 파일(사용자 결정 D2)은 슬롯과 같은 저장소의 다른 키라 저장 관리자의 저장소를 넘긴다. 해제할 자원이 없어 등록하지 않는다.
            GameManager.GlobalProgress.Configure(GameManager.Saves.Storage);
            // 상태 소유자는 저장 이벤트를 Awake에서 이미 구독했다. 첫 세션이 열리기 전에 저장 관리자와 명령 관문을 연결한다.
            ConnectChildren();

            // 두 흐름은 게임플레이 데이터의 씬 표로 씬을 찾는다. 주 월드 흐름은 씬을 내린 뒤 그 씬 서랍을 반납하도록 서랍장을 받는다.
            // 씬 코드는 입력 사본과 오디오를 GameManager 정적 속성으로, 공용 HUD와 게임플레이 표시는 SceneGameplayContext가 이 Runtime의 내부 속성으로 읽는다.
            GameManager.Scenes.Configure(loader, GameplayDataSettings.SceneManagement, assets);
            // 파츠 라이브러리는 앱 수명 게임플레이 데이터라 편집 흐름은 편집 씬 서랍에 로드할 것이 없다.
            // 편집 씬은 자기 HUD가 없어서 앱 수명 공용 HUD로 HUD를 숨기고 알림을 띄운다.
            GameManager.Customization.Configure(loader, GameplayDataSettings.SceneManagement, _hud);
            foreach (IAsyncReleasable owner in FlowOwners())
                await _appResources.RegisterReleaseHandleAsync(owner);

            // 옵션 관리자는 흐름보다 늦게 등록해야 정리 때 흐름보다 먼저 해제된다.
            await ConfigureOptionsAsync(optionsStorage, graphicsOptions, inputOptions);

            // UI를 이 Runtime의 명령에 연결하고 같은 입력 사본도 넘긴다. 오디오 관리자는 열렸을 때만 옵션을 구독하게 연결하고,
            // 메뉴와 지도와 청취자는 각자 씬의 정적 알림으로 찾는다. 소리를 낼 수 없는 관리자가 옵션과 메뉴를 구독할 이유가 없다.
            GameManager.Ui.Configure(this, inputActions);
            if (audioOpened)
                GameManager.Audio.Bind();
        }

        // 옵션 어댑터는 입력 사본 뒤에 등록해, 바인딩 복원이 입력 사본 파괴보다 먼저 실행되게 한다.
        // 그래픽 어댑터는 옵션을 저장소에 둘 때만 만든다. 두 어댑터의 첫 적용은 옵션 관리자 초기화가 한 번 한다.
        async UniTask<(GameInputOptions, GameGraphicsOptions?)> CreateOptionAdaptersAsync(InputActionAsset inputActions,
            bool useStoredOptions)
        {
            var inputOptions = new GameInputOptions(inputActions);
            await _appResources.RegisterReleaseHandleAsync(inputOptions);
            GameGraphicsOptions? graphicsOptions = null;
            if (useStoredOptions)
            {
                graphicsOptions = new GameGraphicsOptions();
                await _appResources.RegisterReleaseHandleAsync(graphicsOptions);
            }
            return (inputOptions, graphicsOptions);
        }

        // 옵션은 저장 관리자가 내준 옵션 저장소(진행 저장과 같은 저장소)에서 옵션 키만 따로 쓰고, 저장소가 없으면 저장하지 않는다.
        // 옵션 파일이 없으면 원본처럼 장치의 가장 큰 해상도가 기본값이다.
        // 관리자는 흐름 뒤에 등록해 정리에서 가장 먼저 해제되고, 연결보다 먼저 구성해 UI와 오디오가 Applied를 구독하게 한다.
        // 원본 ResolutionSetter처럼 빌드에서는 Alt+Enter 같은 실제 화면 모드 변경을 관리자가 직접 감시해 저장한다.
        // Editor는 화면에 적용하지 않으므로 감시하지 않는다.
        async UniTask ConfigureOptionsAsync(ISaveStorage? optionsStorage, GameGraphicsOptions? graphicsOptions,
            GameInputOptions inputOptions)
        {
            // 해상도 기본값은 옵션을 저장할 때만 넘긴다. 저장하지 않는 구성은 빈 기본값으로 시작해야 하기 때문이다.
            bool storesOptions = optionsStorage != null;
            GameManager.Options.Configure(() => CanEditOptions, optionsStorage,
                storesOptions ? GameResolutions.CreateDefaultOptions() : null,
                graphicsOptions, inputOptions, watchScreenMode: storesOptions && !Application.isEditor);
            await _appResources.RegisterReleaseHandleAsync(GameManager.Options);
        }

        // 주 월드 종료와 편집 공간 닫기를 각각 한 번만 실행하는 해제로 감싼다. 실패는 기록만 하고 던지지 않는다.
        // 이 순서로 등록하므로 정리는 편집 씬을 먼저, 주 월드를 나중에 닫는다. 테스트 하위 클래스도 같은 순서로 등록하려고 이 목록을 쓴다.
        // 해제는 루트가 파괴된 뒤에도 돌므로 두 흐름을 만들 때 잡아 두고, 해제 때 GameManager 정적 속성을 다시 읽지 않는다.
        private protected IAsyncReleasable[] FlowOwners()
        {
            SceneFlow scenes = GameManager.Scenes;
            CustomizationFlow customization = GameManager.Customization;
            return new IAsyncReleasable[]
            {
                new ReleaseOnce(() => scenes.ExitAsync()),
                new ReleaseOnce(() => customization.CloseSpaceAsync()),
            };
        }

        // 입력 실행 사본을 한 번만 해제한다. 이미 파괴됐으면 건너뛰고, Disable이 실패해도 기록만 하고 파괴를 이어 간다.
        // 실제 파괴를 기다리되 Play Mode를 끝내는 중이면 UnityObjects가 기다리지 않는다. 테스트는 하위 클래스(TestGameRuntime)를 거쳐 이 팩터리를 쓴다.
        private protected static IAsyncReleasable CreateInputActionsLifetime(InputActionAsset actions) =>
            new ReleaseOnce(async () =>
            {
                if (actions == null) return;
                Cleanup.Run(actions.Disable, actions);
                await UnityObjects.DestroyAndWaitAsync(actions);
            }, actions);

        // 기지 씬의 자동 저장(BaseAutoSaver)이 쓰는 시계와 대기다. 제품은 시간 배율과 무관한 실시간을 쓰고,
        // TestGameRuntime만 테스트용으로 재정의한다. 가상 호출이라 재정의가 호출 시점에 적용된다.
        internal virtual double AutoSaveNow() => UnityEngine.Time.realtimeSinceStartupAsDouble;
        internal virtual UniTask AutoSaveDelayAsync(TimeSpan delay) =>
            UniTask.Delay(delay, DelayType.Realtime, PlayerLoopTiming.Update);
    }
}
