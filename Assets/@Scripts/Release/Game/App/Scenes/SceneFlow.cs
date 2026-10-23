#nullable enable annotations
using System;
using System.IO;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Game.App
{
    // 원작 GameManager 아래 SceneLoader처럼 GameManager 프리팹의 자식 컴포넌트로 하나만 둔다.
    // 현재 씬을 먼저 내린 뒤 다음 씬을 올리고, 로드 진행은 정적 플래그와 네 정적 이벤트로 알린다.
    // 원작 SceneLoader가 씬 로드 직전 저장을 맡듯 세션 진입, 메뉴 복귀, 캠페인 변경, 결과 화면까지 저장 지점과 함께 맡는다.
    // sealed가 아닌 것은 테스트 하위 클래스가 실제 씬 로드(LoadAsync, ExitAsync)를 대역으로 바꾸기 때문이다.
    [DisallowMultipleComponent]
    public class SceneFlow : MonoBehaviour
    {
        // 세션 규칙으로 후보를 만드는 Try* 준비 명령의 공통 형식이다. C# 9 람다에는 매개변수 특성을 붙일 수 없어,
        // 성공 후보가 null이 아님은 세션 준비 메서드의 [NotNullWhen(true)] 계약에 기댄다.
        delegate bool CampaignPrepare(SessionCampaignCommands commands, out GameSessionSnapshot? candidate);

        // 예약할 때 정한 후보와 돌아갈 상태를 본문에만 넘기는 불변 묶음이다. 작업이 끝나면 버려 흐름에 남지 않는다.
        sealed class CampaignChange
        {
            // 튜토리얼 재시작, 카탈로그, 복귀 플레이어는 월드 종료만, 난이도는 난이도 선택만, 로딩 화면은 씬을 바꾸는 변경만
            // 넘기므로 다른 캠페인 변경은 기본값으로 둔다.
            internal CampaignChange(GameSessionSnapshot candidate, GameSessionState origin, RaidOutcome? closure,
                bool restartsTutorial = false, OriginalGameplayCatalog? catalog = null, DifficultyState? difficulty = null,
                PlayerState? endingPlayer = null, SceneTransition transition = default)
            {
                Candidate = candidate;
                Origin = origin;
                Closure = closure;
                RestartsTutorial = restartsTutorial;
                Catalog = catalog;
                Difficulty = difficulty;
                EndingPlayer = endingPlayer;
                Transition = transition;
            }

            internal GameSessionSnapshot Candidate { get; }
            // 변경을 시작할 때의 플레이 또는 일시정지 상태다. 변경이 끝나거나 후보 채택 전에 실패하면 이 상태로 돌아간다.
            internal GameSessionState Origin { get; }
            // 월드 종료면 결과 화면에 보여 줄 결과이고, 그 밖에는 null이다.
            internal RaidOutcome? Closure { get; }
            // 튜토리얼 사망이면 참이다. 본문은 후보를 채택하지 않고 사망 직전 슬롯 파일로 튜토리얼을 다시 시작한다.
            internal bool RestartsTutorial { get; }
            // 월드 종료를 보고한 월드의 카탈로그다. 튜토리얼 재시작이 원본 플레이어 정의를 찾을 때 쓰고, 다른 변경은 null이다.
            internal OriginalGameplayCatalog? Catalog { get; }
            // 난이도 선택이 고른 난이도다. 후보를 채택한 직후 난이도 소유자에 반영하고, 다른 변경은 null이다.
            // 기지 뒤 재설정 확정도 같은 값으로 넘긴다.
            internal DifficultyState? Difficulty { get; }
            // 출격 사망의 복귀 플레이어다. 월드 종료 후보를 채택한 직후 생존 값 소유자에 반영하고, 플레이어를 그대로 두는 변경은 null이다.
            internal PlayerState? EndingPlayer { get; }
            // 씬을 바꿀 때 쓰는 원작 호출 지점의 로딩 화면 처리다. 월드 종료는 씬을 바꾸지 않아 기본값이다.
            internal SceneTransition Transition { get; }
        }

        // 튜토리얼 재시작 기록을 쓰지 못할 때의 오류 문구 앞부분이다. 뒤에 이유를 붙인다.
        const string _missingTutorialSave = "튜토리얼 저장이 없어 지금 상태에서 튜토리얼을 다시 시작합니다: ";
        // 원작 SceneLoader가 BlackScreen 덮기와 걷기에 넘기는 시간(BlackScreen 정적 메서드의 기본 인수)이다.
        const float _blackScreenDuration = .5f;
        // 원작 SceneLoader가 커튼을 보인 뒤 검은 화면을 걷기 전에 기다리는 시간이다.
        const float _curtainSettleTime = .1f;
        // 원작 SceneLoader가 클릭을 받으면 내는 소리다.
        const string _clickSound = "event:/UI/sceneloader_click";
        // 덮인 동안 한 프레임에 굽는 풀 덩어리 수다. 원작 TastyGrassShaderGlobalSettings.maxBakesPerFrame과 같다.
        const int _grassBakesPerFrame = 64;

        // 원작 GameManager의 SceneLoader 캔버스와 BlackScreenCanvas다. 같은 GameManager 프리팹 안에 저작한다.
        [SerializeField] SceneLoadingView _view;
        [SerializeField] OriginalBlackScreen _blackScreen;
        // 원작 SceneLoader.minimumLoadingTime이다. 커튼을 보인 때부터 이 시간이 지나야 다음 씬을 켠다.
        [SerializeField, Min(0f)] float _minimumLoadingTime = 3f;
        // 원작 SceneLoader.waitAfterSceneLoaded다. 레벨 초기화가 끝난 뒤 이 시간을 기다렸다가 검은 화면을 걷는다.
        [SerializeField, Min(0f)] float _waitAfterSceneLoaded = .09f;
        // 원작 SceneLoader.fadeCurve1부터 4다. 처음 덮기, 커튼 앞 걷기, 켜기 전 덮기, 마지막 걷기에 쓴다. 기본값이 원작 값이다.
        [SerializeField] AnimationCurve _fadeCurve1 = new AnimationCurve(new Keyframe(0f, 0f, 0f, 3.6870449f),
            new Keyframe(.24122225f, .88939726f, 0f, 0f), new Keyframe(.45912024f, .88f, 0f, 0f),
            new Keyframe(.64846367f, .88f, 0f, 0f), new Keyframe(1f, 1f, 1f, 1f));
        [SerializeField] AnimationCurve _fadeCurve2 = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));
        [SerializeField] AnimationCurve _fadeCurve3 = new AnimationCurve(new Keyframe(0f, 0f, 2f, 2f), new Keyframe(1f, 1f, 0f, 0f));
        [SerializeField] AnimationCurve _fadeCurve4 = new AnimationCurve(new Keyframe(0f, 0f, 0f, 1.4379184f),
            new Keyframe(.12122226f, .12996326f, 0f, 0f), new Keyframe(.3746758f, .120565996f, 0f, 0f),
            new Keyframe(.64846367f, .120565996f, 0f, 0f), new Keyframe(1f, 1f, 1f, 1f));

        // 원작 SceneLoader.IsSceneLoading처럼 LoadingStarted부터 LoadingFinished 직전까지 참이다.
        // 이 동안 Start가 돈 SceneContext는 LoadingFinished를 받은 뒤 레벨을 초기화한다.
        public static bool IsSceneLoading { get; private set; }
        // 현재 씬을 내리기 전에 알린다. 곧 사라질 메뉴 객체에 연결된 UI와 오디오가 객체가 살아 있을 때 연결을 끊는다.
        public static event Action<GameScene>? LoadingStarted;
        // 대상 씬이 올라와 Awake까지 돈 뒤, 활성 씬으로 지정하기 직전에 알린다.
        public static event Action<GameScene>? BeforeSceneActive;
        // 활성 씬을 지정하고 IsSceneLoading을 내린 뒤 알린다. 로드가 실패하거나 앱 종료로 취소되면 알리지 않는다.
        public static event Action<GameScene>? LoadingFinished;
        // 레벨 초기화가 끝났거나 실패했을 때, 또는 씬에 SceneContext가 없을 때 알린다.
        public static event Action<GameScene>? AfterSceneInitialized;

        // 아래 네 입력은 앱 구성 때 만들어지거나 게임플레이 데이터가 소유하므로 GameRuntime이 Configure로 넣는다.
        // 테스트가 _loader 이름을 리플렉션으로 읽으므로 이름을 유지한다.
        ISceneLoader _loader;
        // 논리 씬(GameScene)을 로드할 씬 참조로 바꾸는 표다.
        SceneTable _scenes;
        // SceneContext가 게임플레이 카탈로그를 씬 서랍에 넣는다. 이 흐름은 씬을 내린 뒤에 그 서랍을 반납한다.
        AssetLifetimes _assets;
        // 첫 세션 진입 때 앱 서랍에 로드한 아이템 정의 카탈로그다. 앱이 끝날 때까지 들고 있어 다시 로드하지 않는다.
        OriginalGameplayCatalog? _itemCatalog;
        // 이 흐름이 올린 현재 씬이다. 없으면 기본값이라 IsValid가 거짓이다.
        Scene _current;
        // 첫 로드 직전의 활성 씬이다. 종료할 때만 활성 씬을 이 씬으로 되돌린다.
        Scene _returnScene;
        // 진행 중이거나 마지막에 끝난 로드의 결과다. 실행 전에 게시해, 로드 안의 콜백에서 들어온 종료도 이 로드를 기다린다.
        Task<bool>? _load;
        // 종료를 한 번만 실행하고 모든 호출자가 같은 완료를 받는다. null이 아니면 종료가 시작된 것이다.
        ReleaseOnce? _exit;
        // 독점 예약과 세션 상태를 맡는 Runtime이다. 제품 구성과 테스트 하위 Runtime이 Bind로 넣는다.
        // 세션 관리자와 난이도 소유자는 이 Runtime이 아닌 GameManager가 내준다.
        GameRuntime _runtime;
        // 저장 지점과 슬롯 읽기를 맡는 저장 관리자다. 앱이 사라진 뒤 GameManager를 읽으면 검사에 실패하므로,
        // Bind 때 잡아 두고 await 뒤에도 이 참조를 쓴다.
        SaveManager _saves;
        // 월드 종료를 확정한 뒤 결과 화면이 보여 줄 결과다. 종료 상태를 떠나면 null이다.
        RaidOutcome? _closureOutcome;
        // 월드 종료 저장이 성공했는지 기록한다. 결과 화면을 떠날 때 이 저장이 실패했던 경우에만 다시 쓴다.
        // 종료 상태에 들어가기 직전에만 정하므로, 결과 화면에서 읽는 값은 늘 이번 월드 종료의 결과다.
        bool _closureSaved;
        // 지금 독점 작업이 월드 종료인지 나타낸다. 캠페인 명령의 apply가 세우고, 월드 종료 본문이 끝날 때 내린다.
        bool _endingWorld;
        // 원작 LevelManager.loadLevelBeaconIndex처럼 다음 출격 월드가 한 번 꺼내 쓰는 출격 방식이다. 저장하지 않는다.
        RaidEntry _raidEntry;
        // 지금 독점 작업이 원작 로딩 화면을 쓰는 전환인지 나타낸다. 예약의 apply가 세우고 본문이 끝날 때 내린다.
        bool _loadingScreenRun;

        // 구성 때는 콘텐츠를 로드하지 않고 로더, 씬 표, 서랍장만 연결한다. GameRuntime이 첫 로드 전에 한 번만 부른다.
        internal void Configure(ISceneLoader loader, SceneTable scenes, AssetLifetimes assets)
        {
            _loader = loader;
            _scenes = scenes;
            _assets = assets;
        }

        // 세션 전환과 캠페인 변경이 쓰는 Runtime과 저장 관리자를 묶는다. 제품 구성과 테스트 하위 Runtime이 첫 명령 전에 한 번 부른다.
        internal void Bind(GameRuntime runtime, SaveManager saves)
        {
            _runtime = runtime;
            _saves = saves;
        }

        // 씬 언로드 알림은 이 흐름이 사는 동안 받아 서랍장에 넘긴다. 정적 알림 구독을 컴포넌트 수명에 묶어 끊는 곳을 하나로 둔다.
        void Awake() => SceneManager.sceneUnloaded += OnSceneUnloaded;
        void OnDestroy() => SceneManager.sceneUnloaded -= OnSceneUnloaded;

        // 구성 전에는 서랍장이 없어 넘길 곳이 없다. 서랍장은 Unity 객체가 아니라 일반 null 비교로 본다.
        void OnSceneUnloaded(Scene scene)
        {
            if (_assets != null) _assets.NotifySceneUnloaded(scene);
        }

        // 결과 화면이 보여 줄 월드 종료 결과다. 결과 화면 상태가 아니면 null이다.
        public RaidOutcome? ClosureOutcome => _closureOutcome;
        // 월드 종료 확정이 진행 중인지 알린다. 이 작업은 씬을 바꾸지 않으므로 UI가 로딩 화면을 띄우지 않는다.
        // 예약 뒤 apply가 세우고 본문 finally가 예약을 풀기 전에 내리므로 참이면 늘 예약 중이다.
        internal bool IsEndingWorld => _endingWorld;
        // 원작 SceneLoader 로딩 화면을 쓰는 전환이 진행 중이면 참이다. UI는 이때 자체 로딩 모달과 일시정지 창을 띄우지 않는다.
        // 세션 진입과 메뉴 복귀는 Runtime의 예약 apply가 세션 상태를 Starting과 Returning으로 바꾸므로 그 상태로도 본다.
        internal bool IsShowingLoadingScreen => _loadingScreenRun
            || _runtime.SessionState == GameSessionState.Starting || _runtime.SessionState == GameSessionState.Returning;
        // 원작 BlackScreen.Instance(GameManager.BlackScreen)처럼 하위 씬 문도 씬 전환과 같은 검은 화면을 쓴다.
        // 덮기 요청 수를 한 객체가 세야 씬 전환과 겹쳐도 화면이 남지 않는다.
        internal OriginalBlackScreen BlackScreen => _blackScreen;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 값과 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            IsSceneLoading = false;
            LoadingStarted = null;
            BeforeSceneActive = null;
            LoadingFinished = null;
            AfterSceneInitialized = null;
        }

        // 메뉴와 게임 씬을 여는 유일한 진입점이다. 종료 뒤 요청은 기록 없이 false다.
        // 결과 완료를 먼저 게시한 뒤 로드를 시작해, 로드 안의 콜백에서 들어온 종료도 이 로드를 기다리게 한다.
        // transition은 원작 호출 지점의 로딩 화면 처리이고, 기본값은 로딩 화면 없는 앱 시작 메뉴 로드다.
        // 테스트 하위 클래스만 실제 씬 로드를 대역으로 바꾼다.
        public virtual UniTask<bool> LoadAsync(GameScene scene, SceneTransition transition = default)
        {
            if (_exit != null) return UniTask.FromResult(false);
            TaskCompletionSource<bool> completion = SharedCompletion.Create<bool>();
            _load = completion.Task;
            PublishResultAsync(LoadCoreAsync(scene, transition), completion).Forget(error => completion.TrySetOutcome(error, false));
            return completion.Task.AsUniTaskOnCurrentContext();
        }

        // 로드 결과를 미리 게시한 완료로 옮긴다. 취소와 실패는 Forget의 처리기가 같은 완료에 게시한다.
        static async UniTask PublishResultAsync(UniTask<bool> load, TaskCompletionSource<bool> completion)
        {
            bool loaded = await load;
            completion.TrySetResult(loaded);
        }

        // 내려간 씬의 서랍은 다음 레벨 초기화가 끝나거나 실패한 뒤 반납한다. 원작은 카탈로그를 앱 수명으로 들고 있어 씬을 바꿔도
        // 다시 읽지 않는데, 우리는 게임플레이 카탈로그를 씬 서랍에 두므로 다음 씬이 같은 카탈로그를 다시 잡기 전에 반납하면 매 전환마다 다시 로드한다.
        // 언로드에 실패해 인스턴스가 남은 서랍은 보류한 채 두고 앱 종료 때 AssetLifetimes가 반납한다. 둘 다 던지지 않는다.
        async UniTask<bool> LoadCoreAsync(GameScene scene, SceneTransition transition)
        {
            Scene previous = _current;
            // 언로드한 Scene 값은 IsValid가 false가 되므로 로드되어 있었는지를 미리 잡아 둔다.
            bool hadPrevious = previous.IsValid();
            try { return await SwitchSceneAsync(scene, previous, transition); }
            finally
            {
                if (hadPrevious && !previous.isLoaded) await _assets.ReleaseSceneAsync(previous);
            }
        }

        // 원작 SceneLoader 순서대로 현재 씬을 먼저 내리고 대상을 올린 뒤, 로드 플래그를 내리고 레벨 초기화를 기다린다.
        // 로딩 화면을 쓰는 전환은 그 사이에 검은 화면, 커튼, 미리 로딩과 켜기, 마지막 걷기를 원작 순서대로 끼운다.
        // 실패는 기록만 하고 되돌리지 않는다. 레벨 초기화가 실패했거나 언로드 중 강제 종료를 받았으면 false이고, SceneContext가 없는 메뉴는 true다.
        async UniTask<bool> SwitchSceneAsync(GameScene scene, Scene previous, SceneTransition transition)
        {
            bool screen = transition.ShowsLoadingScreen;
            // 로딩 화면을 덮은 뒤 다음 씬을 켜고 커튼을 내리기 전까지 참이다. 그 사이에 끝나면 finally가 화면을 정리한다.
            bool presenting = false;
            Scene curtain = default;
            // 원작 timeWhenLoadingStarted다. 커튼을 보인 시각이고, 최소 로딩 시간을 여기서부터 잰다.
            float curtainShownAt = 0f;
            // 커튼과 다음 씬을 읽는 동안 멈춘 uGUI 입력이다. 읽기가 끝나거나 전환이 중간에 끝나면 다시 켠다.
            EventSystem? pausedInput = null;
            IsSceneLoading = true;
            try
            {
                ObserverDispatch.InvokeEachValue(LoadingStarted, scene);
                // 원작처럼 로드 시작 알림 뒤 나가는 씬을 검은 화면으로 덮은 다음에 내린다.
                if (screen)
                {
                    presenting = true;
                    await CoverAsync(transition);
                }
                // 원작 SceneLoader 구독자처럼 나가는 레벨은 로드 시작 뒤 언로드 직전에 정리한다. 언로드 중 OnDestroy 순서는
                // 정해져 있지 않아서다. 서랍은 언로드 뒤에 반납해 인스턴스가 에셋보다 먼저 사라진다.
                _current = default;
                if (previous.IsValid())
                {
                    Cleanup.Run(SceneContext.EndCurrentLevel, this);
                    // 다음 씬이 같은 게임플레이 카탈로그를 다시 잡을 때까지 이 씬 서랍을 남긴다.
                    // 반납은 LoadCoreAsync가 다음 레벨 초기화 뒤에 한다.
                    _assets.HoldSceneRelease(previous);
                    await _loader.UnloadAsync(previous);
                    // 언로드를 기다리는 동안 강제 종료를 받았으면 다음 씬을 올리지 않는다. 프레임이 계속 도는 Play Mode 종료와
                    // 테스트에서 종료 중 씬 로드와 레벨 초기화를 막는다. 남긴 서랍은 LoadCoreAsync의 finally가 반납한다.
                    if (_runtime.IsAbandoned) return false;
                    // 원작은 커튼을 Single로 열어 이 지점에서 Unity의 미사용 에셋 정리가 한 번 돈다. 앞 씬이 남긴 텍스처를 내린다.
                    // 이 대기는 프레임을 넘기므로, Addressables가 앞 씬 핸들을 놓은 뒤에 다음 씬 로드가 같은 씬 번들을 다시 잡는다.
                    // 보류한 앞 씬 서랍의 에셋은 핸들이 잡고 있어 남는다.
                    await _loader.UnloadUnusedAssetsAsync();
                    if (_runtime.IsAbandoned) return false;
                }
                if (screen)
                {
                    // 로딩 스레드가 씬을 읽는 동안 메인 스레드가 그 잠금을 기다리지 않게 uGUI 입력을 멈춘다. 이유는 UiFocus.SuspendInput에 있다.
                    pausedInput = UiFocus.SuspendInput();
                    curtain = await RaiseCurtainAsync(transition);
                    curtainShownAt = Time.unscaledTime;
                    await WaitUntilAsync(() => Time.unscaledTime - curtainShownAt >= _curtainSettleTime);
                    await HideBlackAsync(_fadeCurve2, 0f);
                }
                Scene loaded;
                try
                {
                    // 로딩 화면 전환은 원작 allowSceneActivation = false처럼 다음 씬을 켜기 직전까지만 올려 둔다.
                    loaded = screen ? await _loader.PreloadAsync(_scenes[scene]) : await _loader.LoadAsync(_scenes[scene]);
                }
                // 예외 경계: 씬 로드 실패는 여기서 한 번 기록하고 false로 알린다. 로딩 화면은 finally가 걷는다.
                catch (Exception error)
                {
                    Log.Instance.Exception(error, this);
                    return false;
                }
                // 다음 씬을 다 읽었으므로 클릭해서 계속을 받기 전에 입력을 다시 켠다.
                UiFocus.ResumeInput(pausedInput);
                pausedInput = null;
                _current = loaded;
                if (screen)
                {
                    await WaitBeforeActivationAsync(transition, curtainShownAt);
                    // 원작 LevelInitializingIndicator는 켜기 직전(onBeforeSetSceneActive)에 보이고 레벨 초기화 뒤에 숨는다.
                    _view.ShowLevelInitializing();
                    loaded = await _loader.ActivateAsync(loaded);
                    _current = loaded;
                }
                ObserverDispatch.InvokeEachValue(BeforeSceneActive, scene);
                if (!_returnScene.IsValid()) _returnScene = SceneManager.GetActiveScene();
                // 활성 씬 지정 실패는 기록만 하고 로드를 이어 간다.
                if (!SceneContexts.TryMakeActive(loaded))
                    Log.Instance.Error("활성 씬을 지정하지 못했습니다: " + scene, this);
                // 원작처럼 다음 씬을 활성 씬으로 둔 뒤 커튼을 걷어 내린다.
                if (curtain.IsValid()) await _loader.UnloadAsync(curtain);
                presenting = false;
            }
            finally
            {
                IsSceneLoading = false;
                // 읽는 중에 실패하거나 강제 종료를 받아도 멈춘 입력을 남기지 않는다.
                UiFocus.ResumeInput(pausedInput);
                // 로드 실패나 강제 종료로 중간에 끝나면 남은 커튼과 표시를 정리하고 검은 화면을 걷는다.
                // 앱이 이미 파괴한 화면은 정리하지 않는다. MonoBehaviour라 Unity null 비교를 쓴다.
                if (presenting && _view != null) await AbortLoadingScreenAsync(curtain);
            }
            ObserverDispatch.InvokeEachValue(LoadingFinished, scene);
            // 원작 SceneLoader처럼 레벨이 초기화되거나 실패할 때까지 프레임마다 기다린다. 앱이 끝나면 대기도 함께 버려진다.
            while (SceneContext.LevelInitPending)
                await UniTask.NextFrame();
            SceneContext? level = SceneContext.Instance;
            bool initialized = level == null || !level.LevelInitFailed;
            ObserverDispatch.InvokeEachValue(AfterSceneInitialized, scene);
            // 원작은 waitAfterSceneLoaded 뒤 fadeCurve4로 검은 화면을 걷는다. 원작처럼 걷는 동안 게임은 이미 돌고 있으므로
            // 걷기를 기다리지 않고 검은 화면이 스스로 마친다.
            if (screen)
            {
                // 지형 풀이 있는 씬은 덮인 채로 풀 덩어리를 모두 구운 뒤에 걷는다. 걷은 뒤에 구우면 첫 몇 초가 끊긴다.
                // 규칙: Docs/game-runtime.md#loading-screen
                while (OriginalGrassRenderer.PendingBakes > 0 && !_runtime.IsAbandoned)
                {
                    OriginalGrassRenderer.BakePending(_grassBakesPerFrame);
                    await UniTask.NextFrame();
                }
                _view.HideLevelInitializing();
                _blackScreen.Hide(_fadeCurve4, transition.CircleFade ? 1f : 0f, _blackScreenDuration, _waitAfterSceneLoaded);
            }
            return initialized;
        }

        // 원작 SceneLoader의 첫 덮기다. fadeCurve1로 덮은 뒤 로딩 표시를 켠다.
        async UniTask CoverAsync(SceneTransition transition)
        {
            _blackScreen.Show(_fadeCurve1, transition.CircleFade ? 1f : 0f, _blackScreenDuration);
            await WaitUntilAsync(() => !_blackScreen.IsFading);
            _view.ShowLoadingIndicator();
        }

        // 커튼 씬을 올려 활성 씬으로 둔다. 커튼은 화면 연출이라 로드에 실패하면 기록만 하고 검은 화면 위에서 로드를 잇는다.
        async UniTask<Scene> RaiseCurtainAsync(SceneTransition transition)
        {
            Scene curtain = default;
            try
            {
                curtain = await _loader.LoadAsync(_scenes.Curtain(transition.Curtain));
            }
            // 예외 경계: 커튼 씬 로드 실패는 여기서 한 번 기록하고 커튼 없이 진행한다.
            catch (Exception error)
            {
                Log.Instance.Exception(error, this);
            }
            SceneContexts.TryMakeActive(curtain);
            return curtain;
        }

        // 원작 SceneLoader의 켜기 전 단계다. 최소 로딩 시간을 채운 뒤, 클릭해서 계속이면 로딩 표시를 내리고
        // 클릭 표시를 보여 화면 클릭을 기다린다. 아니면 로딩 표시만 내린다. 마지막으로 fadeCurve3로 다시 덮는다.
        async UniTask WaitBeforeActivationAsync(SceneTransition transition, float curtainShownAt)
        {
            await WaitUntilAsync(() => Time.unscaledTime - curtainShownAt >= _minimumLoadingTime);
            if (transition.ClickToContinue)
            {
                _view.HideLoadingIndicator();
                await WaitUntilAsync(() => !_view.IsLoadingIndicatorFading);
                _view.ShowClickIndicator();
                await WaitUntilAsync(() => !_view.IsClickIndicatorFading);
                _view.BeginClickWait();
                await WaitUntilAsync(() => _view.Clicked);
                _view.EndClickWait();
                if (_view.Clicked) GameManager.Audio.PlayOneShot(_clickSound);
                _view.HideClickIndicator();
            }
            else _view.HideLoadingIndicator();
            _blackScreen.Show(_fadeCurve3, 0f, _blackScreenDuration);
            await WaitUntilAsync(() => !_blackScreen.IsFading);
        }

        // 원작 SceneLoader의 커튼 앞 걷기다.
        async UniTask HideBlackAsync(AnimationCurve curve, float circleFade)
        {
            _blackScreen.Hide(curve, circleFade, _blackScreenDuration);
            await WaitUntilAsync(() => !_blackScreen.IsFading);
        }

        // 로드가 중간에 끝난 로딩 화면을 정리한다. 원작에는 없는 실패 경로라 표시를 바로 숨기고 커튼을 내린 뒤 검은 화면을 걷는다.
        async UniTask AbortLoadingScreenAsync(Scene curtain)
        {
            _view.EndClickWait();
            _view.HideLoadingIndicator();
            _view.HideClickIndicator();
            _view.HideLevelInitializing();
            await _loader.UnloadAsync(curtain);
            _blackScreen.Hide(_fadeCurve4, 0f, _blackScreenDuration);
        }

        // 로딩 화면의 시간 대기와 표시 대기다. 강제 종료를 받으면 기다리지 않고 바로 끝나, 미리 올린 씬도 곧바로 켜고 정리한다.
        // 켜지 않은 씬을 남기면 Unity가 다음 씬 연산을 모두 미루기 때문이다.
        async UniTask WaitUntilAsync(Func<bool> done)
        {
            while (!done() && !_runtime.IsAbandoned)
                await UniTask.NextFrame();
        }

        // 언로드에 성공했을 때만 서랍을 반납하고, 언로드 알림이 먼저 반납했으면 무동작이다. 둘 다 던지지 않는다.
        // 언로드 실패는 로더가 한 번 기록하고, 인스턴스가 남은 그 서랍은 앱 종료 때 AssetLifetimes가 반납한다.
        async UniTask UnloadSceneAsync(Scene scene)
        {
            await _loader.UnloadAsync(scene);
            if (!scene.isLoaded) await _assets.ReleaseSceneAsync(scene);
        }

        // 동시 호출, 콜백 재진입, 늦은 호출은 모두 같은 종료 완료를 공유한다. 종료 뒤 로드 요청은 false다.
        // 정리라 던지지 않고, 단계별 실패는 ReleaseOnce와 Cleanup이 기록한다. 테스트 하위 클래스만 대역으로 바꾼다.
        public virtual UniTask ExitAsync() => (_exit ??= new ReleaseOnce(ExitCoreAsync, this)).ReleaseAsync();

        // 진행 중인 로드가 끝나길 기다리고, 활성 씬을 되돌린 뒤 현재 씬을 내리고 그 서랍을 반납한다.
        async UniTask ExitCoreAsync()
        {
            Task<bool>? load = _load;
            // 로드의 결과와 실패는 원래 호출자가 받으므로 종료는 WhenAny로 끝나기만 기다리고 정리를 계속한다.
            if (load != null && !load.IsCompleted)
                await Task.WhenAny(load).AsUniTaskOnCurrentContext();
            Scene current = _current;
            _current = default;
            Cleanup.Run(RestoreReturnScene, this);
            if (current.IsValid())
            {
                // 로드 경로와 같이 나가는 레벨의 정리와 World 해제를 씬 객체가 살아 있는 언로드 직전에 끝낸다.
                Cleanup.Run(SceneContext.EndCurrentLevel, this);
                await UnloadSceneAsync(current);
            }
        }

        // 종료에서만 첫 로드 직전의 활성 씬으로 되돌린다. 로드한 적이 없거나 그 씬이 내려갔으면 그대로 둔다.
        // 종료 정리라 되돌리지 못해도 결과를 보지 않고 다음 단계로 간다.
        void RestoreReturnScene()
        {
            SceneContexts.TryMakeActive(_returnScene);
        }

        // 원작이 아이템 카탈로그를 앱 수명으로 들고 있듯 앱 서랍에 한 번 로드한다. Runtime 시작이 메뉴 전에 먼저 부르고, 세션 진입은
        // 캐시를 받는다. 씬은 같은 에셋을 자기 서랍에 다시 잡으므로 다시 읽지 않는다. 시작과 세션 진입은 모두 독점 작업이라 동시에 두 번 로드하지 않는다.
        // 테스트 하위 클래스는 대역을 돌려준다.
        internal virtual async UniTask<OriginalGameplayCatalog?> LoadItemCatalogAsync()
        {
            if (_itemCatalog == null)
                _itemCatalog = await _assets.LoadAsync<OriginalGameplayCatalog>(GameplayDataSettings.ItemAssets.ItemCatalog);
            return _itemCatalog;
        }

        // 세션 진입(새 게임과 이어하기)의 본문이다. 파일은 쓰지 않는다. 저장 관리자의 불러오기로 세션을 만든 뒤 그 씬을 로드하고,
        // 로드나 레벨 초기화가 실패해도 이미 한 번 기록했으므로 플레이를 연다. 세션을 만들기 전의 읽기 실패와 강제 종료만 메뉴로 돌아간다.
        // 예약과 알림은 GameRuntime이 독점 작업으로 맡는다.
        internal async Task EnterSessionAsync(bool newGame)
        {
            bool entered = false;
            try
            {
                GameSessionSnapshot desired = _runtime.DefaultSession;
                // 이어하기 읽기와 이번 세션의 저장 지점이 아이템을 카탈로그와 대조하도록, 세션을 열기 전에 카탈로그를 넘긴다.
                _saves.UseItemCatalog(await LoadItemCatalogAsync());
                if (_runtime.IsAbandoned) return;
                if (!newGame)
                {
                    GameSessionSnapshot? existing;
                    // 파일이 없을 때만 null이고, 손상과 입출력 실패는 예외로 온다.
                    try { existing = await _saves.ReadSelectedAsync(); }
                    // 예외 경계: 저장 읽기 실패를 슬롯 조회와 같은 SaveManager 규칙으로 분류해 게시한 뒤 다시 던진다.
                    catch (Exception error)
                    {
                        // 정상 읽기와 같이, 기다리는 동안 버려졌으면 상태를 게시하지 않고 실패만 호출자에게 넘긴다.
                        if (!_runtime.IsAbandoned) _saves.PublishState(SaveManager.Classify(error));
                        throw;
                    }
                    if (_runtime.IsAbandoned) return;
                    if (existing == null)
                    {
                        _saves.PublishState(GameSaveState.Missing);
                        throw new FileNotFoundException("이어할 게임 저장이 없습니다.");
                    }
                    _saves.PublishState(GameSaveState.Available);
                    // 저장 파일은 건드리지 않고 이어하기 후보만 계산한다. 형식과 아이템 카탈로그 대조는 읽기 단계가 이미 마쳤다.
                    desired = CampaignRules.PrepareContinueSnapshot(existing);
                }
                // 불러오기 알림을 받은 LoadHubSections가 세션을 만들고, 이어하기만 파일에서 읽은 기록으로 알린다.
                // 세션을 읽은 뒤에 진입을 표시하므로, 세션이 생기지 않는 결함이면 finally가 메뉴 상태로 되돌린다.
                _saves.Load(GameSaveData.FromRecord(desired), !newGame);
                GameScene scene = _runtime.Snapshot().SceneId;
                entered = true;
                await LoadAsync(scene, newGame ? SceneTransition.NewGame : SceneTransition.Continue(scene));
                if (_runtime.IsAbandoned) return;
            }
            finally
            {
                // 진입은 세션 없는 메뉴에서만 시작하므로, 세션을 만들기 전에 끝났으면 메뉴 상태만 되돌리면 된다.
                if (!entered) _runtime.SessionState = GameSessionState.Menu;
            }
            // 바꾼 세션을 조작 가능한 상태로 공개하되, 로드를 기다리는 동안 종료가 요청됐으면 상태 적용이 재개하지 않는다.
            _runtime.ApplySessionState(GameSessionState.Playing);
        }

        // 원작처럼 씬을 내리기 전에 저장하고, 그 뒤 메뉴를 로드해 세션을 버린다. 저장과 로드의 실패는 기록만 하고 메뉴로 간다.
        // 세션을 버리기 전에 강제 종료가 오면 결과 화면이나 일시정지 화면 상태로 돌아간다. saving이 false면 기지에서도 저장하지 않는다.
        internal async Task ReturnToMenuAsync(bool saving)
        {
            bool loading = false;
            try
            {
                // 저장과 메뉴 로드 동안 플레이를 멈추고, 메뉴에서도 멈춘 채 둔다.
                _runtime.PausePlay();
                // 저장 실패는 저장 관리자가 한 번 기록하므로 결과를 보지 않고 메뉴로 간다. 진행 중인 저장 뒤에 쓰는 순서는 저장 관리자가 지킨다.
                if (saving && SavePointPolicy.SavesOnMenuOrQuit(_runtime.Snapshot().SceneId))
                    await _saves.SaveAtPointAsync();
                // 메뉴 슬롯 명령은 남은 저장이 없다고 보므로, 저장하지 않는 복귀도 남은 저장(기지 자동 저장 포함)이 끝난 뒤에
                // 메뉴를 로드해 늦은 저장 결과가 메뉴의 저장 상태를 덮지 않게 한다. 복귀 예약이 세션 명령을 닫아 그동안 세션은 바뀌지 않는다.
                await TrackedTask.ObservePreviousAsync(_saves.CurrentSave);
                // 메뉴 로드가 되돌릴 수 없는 지점이라 바로 앞에서 버려졌는지 본다.
                if (_runtime.IsAbandoned) return;
                loading = true;
                await LoadAsync(GameScene.MainMenu, SceneTransition.MainMenu);
                if (_runtime.IsAbandoned) return;
            }
            finally
            {
                // 로드 전에 끝난 복귀는 세션을 그대로 두고, 월드가 끝났으면 결과 화면으로 아니면 일시정지로 돌아간다.
                // 결과는 이 흐름이 들고 있어 루트가 파괴된 뒤에도 그대로 읽는다. 플레이는 이미 멈춰 있어 다시 멈추지 않는다.
                if (!loading) _runtime.SessionState = _closureOutcome != null ? GameSessionState.Closure : GameSessionState.Paused;
            }
            _runtime.ReplaceSession(null);
            // 메뉴에서도 플레이 정지를 유지한다. 복귀를 시작할 때 이미 멈췄고 전환 중에는 재개 경로가 막혀 있어 다시 멈추지 않는다.
            _runtime.SessionState = GameSessionState.Menu;
        }

        // 아래 캠페인 명령(난이도 선택과 재설정 포함)은 세션 준비 거절을 예외 대신 false로 돌려준다.
        // 원본의 난이도 선택 확정과 영상 진입도 다른 캠페인 변경과 같은 저장과 씬 로드 흐름을 쓴다.
        // 고른 난이도는 후보 채택 직후 난이도 소유자에 반영하도록 변경 묶음에 함께 넘긴다.
        public UniTask<bool> SelectDifficultyAsync(DifficultyState difficulty) =>
            // 난이도는 허브 사본에 넣지 않고 변경 묶음으로만 넘겨 소유자 한 곳에 반영한다.
            // 원작 화면이 잠그는 규칙(해금 전 분노)은 명령도 거절하도록 두 소유자를 보는 GameplayCommands로 후보를 만든다.
            BeginCampaignCommand((SessionCampaignCommands s, out GameSessionSnapshot? c) =>
                GameplayCommands.TryPrepareDifficultySelection(s, GameManager.Difficulty, GameManager.GlobalProgress, difficulty, out c),
                _ => SceneTransition.FromPrepare, difficulty);

        // 원작 기지 SelectDifficulty(SceneLoaderProxy.LoadScene("Prepare"))다. 출격처럼 떠나는 기지를 저장한 뒤 준비 씬을 연다.
        // 진행 단계는 그대로라 준비 씬 컨트롤러가 재설정 화면으로 연다. 기지 월드 컨트롤러의 전환 루트가 부른다.
        internal UniTask<bool> OpenDifficultyReselectAsync() =>
            BeginCampaignCommand((SessionCampaignCommands s, out GameSessionSnapshot? c) => s.TryPrepareDifficultyReselect(out c),
                _ => SceneTransition.DifficultyReselect);

        // 원작 GamePrepareProcess의 goToBaseSceneIfVisted다. 재설정 화면의 확정이 고른 난이도를 반영하고 기지로 돌아간다.
        // 준비 씬은 저장 지점이 아니라 파일은 쓰지 않고, 고른 난이도는 다음 기지 저장이 기록한다(원작도 준비 씬에서 저장하지 않는다).
        internal UniTask<bool> ReselectDifficultyAsync(DifficultyState difficulty) =>
            BeginCampaignCommand((SessionCampaignCommands s, out GameSessionSnapshot? c) =>
                GameplayCommands.TryPrepareDifficultyReselectReturn(s, GameManager.Difficulty, GameManager.GlobalProgress, difficulty, out c),
                _ => SceneTransition.FromPrepare, difficulty);

        // 도입부의 바로 다음 단계인지 세션 규칙으로 검사한 뒤 저장과 씬 로드를 시작한다.
        public UniTask<bool> AdvanceAsync(CampaignStage next) =>
            BeginCampaignCommand((SessionCampaignCommands s, out GameSessionSnapshot? c) =>
                s.TryPrepareAdvance(next, out c), SceneTransition.Advance);

        // 출격 식별자는 준비할 때 한 번 만들어 후보에 담는다.
        // 인수 없는 출격은 기지 벙커 문 출격이다. 호출자: OriginalGameplayController 출격 전환과 테스트.
        public UniTask<bool> BeginRaidAsync() => BeginRaidAsync(RaidEntry.BunkerDoor);

        // 원작 MapSelectionView의 SceneLoader.LoadScene처럼 출격 방식을 함께 받는다. 출격 준비를 통과한 출격만 방식을 기록해,
        // 거절된 지도 선택이 다음 출격의 출발점을 바꾸지 않게 한다. 호출자: 위 벙커 문 출격, 지도 선택 화면(전투 월드 출격 전환 경유).
        internal UniTask<bool> BeginRaidAsync(RaidEntry entry) =>
            BeginCampaignCommand((SessionCampaignCommands s, out GameSessionSnapshot? c) =>
            {
                // 출격 식별자는 저장 기록에 남으므로 다른 영구 식별자와 같은 형식으로 만든다.
                if (!s.TryPrepareRaidStart(PersistentIds.New(), out c)) return false;
                // 원작 RaidUtilities.OnNewRaid의 CacheExp처럼 출격 준비를 통과한 출격만 결과 화면의 획득 경험치 기준을 기록한다.
                // 출격 중 월드를 다시 초기화해도 기준이 바뀌지 않게 월드 초기화가 아니라 이 명령에서 한 번 기록한다.
                GameManager.Experience.RecordRaidStart();
                _raidEntry = entry;
                return true;
            }, _ => SceneTransition.Raid(entry));

        // 원작 GetPlayerStartLocation이 loadLevelBeaconIndex를 읽고 0으로 되돌리듯, 기록한 출격 방식을 한 번 꺼내고 벙커 문으로 되돌린다.
        // 호출자: 전투 월드 첫 초기화(WorldInteractions.PlaceAtRaidStart). 기지 월드도 불러 남은 값을 비운다.
        internal RaidEntry TakeRaidEntry()
        {
            RaidEntry entry = _raidEntry;
            _raidEntry = RaidEntry.BunkerDoor;
            return entry;
        }

        // 원작 LevelManager의 사망과 탈출 처리처럼 사망, 탈출, 튜토리얼 완료를 결과 화면보다 먼저 한 후보로 확정하고 저장한 뒤
        // 종료 상태(Closure)로 둔다. 로딩 화면은 없고 월드와 씬은 그대로다. 저장에 실패해도 기록만 하고 결과 화면을 연다.
        // 튜토리얼 사망 표시는 공통 준비 형식에 없는 값이라 세션 준비를 직접 부르고, 거절과 예약은 다른 캠페인 명령과 같다.
        internal UniTask<bool> EndWorldAsync(OriginalGameplayCatalog catalog, RaidOutcome outcome, string subScene, float x, float y, float z)
        {
            // 출격 사망의 유실물 규칙은 난이도 소유자의, 사망 판정과 복귀 플레이어의 에너지와 수분은 생존 값 소유자의 현재 값을 쓴다.
            // 복귀 플레이어는 변경 묶음에 실어 후보를 채택한 직후 생존 값 소유자에 반영한다.
            if (!GameManager.SessionManager!.Campaign.TryPrepareWorldEnding(catalog, outcome, subScene, x, y, z,
                    GameManager.Difficulty.Current, _runtime.Vitals.Current,
                    out GameSessionSnapshot? candidate, out bool restartTutorial, out PlayerState? endingPlayer))
                return UniTask.FromResult(false);
            return ScheduleCampaignChange(new CampaignChange(candidate!, _runtime.SessionState, outcome, restartTutorial, catalog,
                endingPlayer: endingPlayer));
        }

        // 원작 ClosureView의 '계속'처럼 기지나 다시 시작할 튜토리얼을 새 월드로 로드하고 조작 상태로 돌아간다.
        // 월드 종료 저장이 실패했을 때만 씬을 내리기 전에 다시 기록한다. 결과 화면만 부르고, 그 화면은 실행 중 종료 상태에서 Runtime이
        // 한가할 때만 떠 레벨 종료와 소유자 전환에 닫히므로 상태를 다시 보지 않는다. 월드 종료 본문이 _endingWorld를 이미 내렸다.
        // apply는 원작 로딩 화면 전환임을 UI가 첫 갱신부터 알도록 표시만 세운다.
        public UniTask LeaveClosureAsync() =>
            _runtime.Operations.Run(() => _loadingScreenRun = true, () => RunWithLoadingScreenAsync(LeaveClosureCoreAsync));

        // 세션을 교체할 때 떠나는 세션의 결과 화면 정보를 지운다.
        internal void ClearClosure() => _closureOutcome = null;

        // 세션 규칙으로 후보를 준비한 뒤 예약한다. 전환 중에는 세션 명령 허용이 꺼져 있어 준비가 거절된다.
        // 거절은 바로 끝나는 false다. 규칙 거절은 기록하지 않고 불변식 위반은 세션이 한 번 기록하므로 SceneCommand도 다시 기록하지 않는다.
        // difficulty는 난이도 첫 선택과 기지 뒤 재설정 확정만 넘긴다. transition은 후보의 씬으로 원작 호출 지점의 로딩 화면을 고른다.
        UniTask<bool> BeginCampaignCommand(CampaignPrepare prepare, Func<GameScene, SceneTransition> transition,
            DifficultyState? difficulty = null)
        {
            if (!prepare(GameManager.SessionManager!.Campaign, out GameSessionSnapshot? candidate)) return UniTask.FromResult(false);
            // 성공한 준비는 후보를 반드시 만든다. 돌아갈 상태는 예약 직전의 플레이 또는 일시정지 상태다.
            return ScheduleCampaignChange(new CampaignChange(candidate!, _runtime.SessionState, null, difficulty: difficulty,
                transition: transition(candidate!.SceneId)));
        }

        // 월드 종료는 결과 화면 본문을, 그 밖의 변경은 씬 변경 본문을 독점 작업으로 예약한다. 완료하면 true다.
        // 후보 채택 전 실패만 호출자에게 전파된다. 예약은 async 메서드 밖에서 시작하므로, 예약 중 동기 예외는 그대로 던져진다.
        // apply는 UI가 첫 갱신부터 월드 종료인지, 원작 로딩 화면 전환인지 알도록 두 표시를 세운다.
        UniTask<bool> ScheduleCampaignChange(CampaignChange change)
        {
            bool endsWorld = change.Closure != null;
            return _runtime.Operations.Run(() =>
                    {
                        _endingWorld = endsWorld;
                        _loadingScreenRun = change.Transition.ShowsLoadingScreen;
                    },
                    () => endsWorld ? EndWorldCoreAsync(change) : RunWithLoadingScreenAsync(() => ChangeCampaignAsync(change)))
                .ContinueWith(() => true);
        }

        // 떠나는 씬 저장, 후보 채택, 튜토리얼 도착 저장, 씬 로드 순서로 진행한다. 원본처럼 상태를 먼저 바꾼 뒤
        // 저장하고, 저장 실패는 저장 관리자가 기록만 한 채 진행한다. 진행 중인 저장(기지 자동 저장 포함) 뒤에 쓰는 순서는 저장 관리자가 지킨다.
        // 후보 채택이 되돌릴 수 없는 지점이다. 그 전에 실패하거나 Runtime이 버려지면 세션과 씬은 그대로이고 변경 전 상태로 돌아간다.
        async Task ChangeCampaignAsync(CampaignChange change)
        {
            GameSessionSnapshot candidate = change.Candidate;
            GameScene current = _runtime.Snapshot().SceneId;
            bool changesScene = candidate.SceneId != current;
            bool adopted = false;
            try
            {
                // 저장과 씬 전환 동안 플레이를 멈춘다. 끝나면 RestorePlayState가 변경 전 상태로 되돌린다.
                _runtime.PausePlay();
                // 떠나는 씬이 저장 지점이면 바뀌기 전 세션을 기록한다. 프롤로그는 튜토리얼 도착 저장이 대신한다.
                if (changesScene && SavePointPolicy.SavesOnLeave(current))
                    await _saves.SaveAtPointAsync();
                // 채택 직전의 확인이다. 버려졌으면 세션과 씬은 변경 전 그대로 둔다.
                if (_runtime.IsAbandoned) return;
                GameManager.SessionManager!.Commit(candidate);
                adopted = true;
                // 원본 GameRulesManager처럼 고른 난이도를 난이도 소유자에 바로 반영해, 이어지는 저장이 고른 난이도를 쓰게 한다.
                if (change.Difficulty != null) GameManager.Difficulty.Apply(change.Difficulty);
            }
            finally
            {
                // 채택 전 실패는 세션과 씬을 바꾸지 않았으므로 멈춘 플레이 상태만 되살리고, 실패는 그대로 호출자에게 간다.
                if (!adopted) RestorePlayState(change.Origin);
            }
            if (changesScene)
            {
                // 튜토리얼 도착은 채택한 세션을 기록해, 이어하기가 튜토리얼 처음부터 시작하게 한다.
                if (SavePointPolicy.SavesOnArrival(candidate.SceneId))
                    await _saves.SaveAtPointAsync();
                if (_runtime.IsAbandoned) return;
                // 로드나 레벨 초기화 실패는 LoadAsync가 이미 기록했으므로 결과와 관계없이 진행한다.
                await LoadAsync(candidate.SceneId, change.Transition);
                if (_runtime.IsAbandoned) return;
            }
            RestorePlayState(change.Origin);
        }

        // 예약이 풀리기 전에 로딩 화면 표시를 내려, 예약 해제 알림으로 갱신하는 UI가 내린 값을 읽게 한다.
        // 본문이 중간에 끝나거나 실패해도 내린다.
        async Task RunWithLoadingScreenAsync(Func<Task> body)
        {
            try { await body(); }
            finally { _loadingScreenRun = false; }
        }

        // 원작처럼 월드 종료 후보를 먼저 채택하고 기록해 결과 화면 상태로 둔다. 결과 화면 동안 현재 월드를 유지한다.
        // 튜토리얼 사망은 후보 대신 사망 직전 슬롯 파일로 되돌린다. 진행 중인 저장 뒤에 쓰는 순서는 저장 관리자가 지킨다.
        // 저장 실패는 저장 관리자가 기록만 하고, 그 결과를 _closureSaved에 남겨 결과 화면을 떠날 때 다시 기록한다.
        async Task EndWorldCoreAsync(CampaignChange change)
        {
            bool adopted = false;
            try
            {
                // 월드 종료 확정과 저장 동안 플레이를 멈춘다. 결과 화면 상태도 멈춘 채 둔다.
                _runtime.PausePlay();
                // 튜토리얼 사망은 세션을 바꾸기 전에 되돌아갈 슬롯 기록을 읽는다. 쓸 수 없으면 null이고 지금 상태에서 다시 시작한다.
                GameSessionSnapshot? restart = change.RestartsTutorial ? await ReadTutorialRestartAsync() : null;
                // 채택 직전의 확인이다. 버려졌으면 세션은 변경 전 그대로 둔다.
                if (_runtime.IsAbandoned) return;
                if (change.RestartsTutorial)
                {
                    // 원본은 튜토리얼에서 죽으면 새 출격으로 다시 들어오므로 사망 직전 파일로 세션 전체를 되돌린다.
                    // 불러오기 알림을 받은 세션 허브와 상태 소유자가 기록을 채택한다. 파일이 없어도 같은 재시작 명령으로 그래프와 생존 값을 채운다.
                    // 재시작 명령은 실패하지 않으므로 이 분기를 지나면 늘 채택한 것으로 본다.
                    if (restart != null) _saves.Load(GameSaveData.FromRecord(restart), true);
                    GameplayCommands.RestartTutorial(GameManager.SessionManager!, _runtime.Vitals, change.Catalog!);
                }
                else
                {
                    GameManager.SessionManager!.Commit(change.Candidate);
                    // 원본처럼 복귀 플레이어를 저장 전에 생존 값 소유자에 반영해, 이어지는 월드 종료 저장이 복귀 값을 기록하게 한다.
                    if (change.EndingPlayer != null) _runtime.Vitals.Apply(change.EndingPlayer);
                }
                adopted = true;
                _closureSaved = await _saves.SaveAtPointAsync();
                if (_runtime.IsAbandoned) return;
                EnterClosure(change.Closure!.Value);
            }
            finally
            {
                // 다음 독점 작업의 로딩 화면을 가리지 않게 월드 종료 표시를 내린다. 채택 전 실패는 변경 전 상태로 돌아간다.
                _endingWorld = false;
                if (!adopted) RestorePlayState(change.Origin);
            }
        }

        // 튜토리얼 사망이 되돌아갈 사망 직전 슬롯 기록을 읽는다. 이번 세션이 읽거나 쓴 파일이고 튜토리얼 저장일 때만 돌려준다.
        // 새 게임의 튜토리얼 도착 저장이 실패했으면 슬롯에 다른 게임의 파일이 남아 있을 수 있어 읽지 않는다.
        // 쓸 기록이 없으면 null이다. 손상은 저장 관리자가 이미 보고했으므로, 그 밖의 이유만 붙여 오류로 한 번 기록한다.
        async UniTask<GameSessionSnapshot?> ReadTutorialRestartAsync()
        {
            // 슬롯 읽기는 슬롯 잠금을 잡지 않으므로, 마지막으로 쓴 파일과 세션 파일 여부를 보도록 남은 저장이 끝난 뒤에 판단한다.
            await TrackedTask.ObservePreviousAsync(_saves.CurrentSave);
            string reason;
            if (!_saves.HasSessionFile) reason = "이번 세션이 아직 슬롯 파일을 읽거나 쓰지 않았습니다.";
            else
            {
                GameSessionSnapshot? record;
                try { record = await _saves.ReadSelectedAsync(); }
                // 예외 경계: 손상이나 입출력 실패로 슬롯 파일을 읽지 못하면 지금 상태에서 다시 시작한다.
                catch (Exception error)
                {
                    // 같은 슬롯을 두 층에서 기록하지 않도록, 읽기 소유자가 보고하지 않는 입출력 같은 Error 분류만 여기서 기록한다.
                    if (SaveManager.Classify(error) != GameSaveState.Corrupt)
                        Log.Instance.Error(_missingTutorialSave + error.Message, _runtime);
                    return null;
                }
                if (record != null && CampaignRules.IsTutorialSession(record)) return record;
                reason = record == null ? "슬롯 파일이 없습니다." : "슬롯 파일이 튜토리얼 저장이 아닙니다.";
            }
            Log.Instance.Error(_missingTutorialSave + reason, _runtime);
            return null;
        }

        // 결과 화면을 떠나 세션의 씬을 새 월드로 로드한다. 튜토리얼 사망 뒤처럼 같은 논리 씬이어도 다시 로드한다.
        // 세션은 월드 종료 때 이미 목적지로 채택했으므로 다시 채택하지 않는다.
        async Task LeaveClosureCoreAsync()
        {
            // 월드 종료 저장이 실패했을 때만 다시 기록한다. 이 저장의 실패도 저장 관리자가 기록만 하고,
            // 진행 중인 저장 뒤에 쓰는 순서도 저장 관리자가 지킨다.
            if (!_closureSaved) await _saves.SaveAtPointAsync();
            // 씬 로드 호출이 되돌릴 수 없는 지점이라 바로 앞에서 확인한다. 버려졌으면 결과 화면 상태가 그대로 남는다.
            if (_runtime.IsAbandoned) return;
            // 로드나 레벨 초기화 실패는 LoadAsync가 이미 기록했으므로 결과와 관계없이 조작 상태로 돌아간다.
            // 결과 화면은 월드 종료가 늘 결과를 남긴 뒤에만 열리므로 _closureOutcome이 있다.
            GameScene target = _runtime.Snapshot().SceneId;
            await LoadAsync(target, SceneTransition.LeaveClosure(_closureOutcome!.Value, target));
            if (_runtime.IsAbandoned) return;
            _closureOutcome = null;
            RestorePlayState(GameSessionState.Playing);
        }

        // 변경을 시작할 때의 플레이 또는 일시정지 상태로 돌아간다. 캠페인 변경의 끝, 후보 채택 전 실패, 결과 화면 이탈이 부른다.
        // 모두 결과 화면 밖에서 시작했거나 결과 화면을 막 떠난 직후라 종료 상태를 덮어쓸 일이 없다.
        void RestorePlayState(GameSessionState origin)
        {
            // 종료 상태 검사를 두지 않는다. 에디터와 DEBUG에서 검사가 던지면 호출자의 남은 단계를 건너뛰어 상태가 어긋난다.
            _runtime.ApplySessionState(origin);
        }

        // 월드 종료를 확정한 뒤 결과 화면의 결과를 기록하고, 종료 상태로 두며 상태 적용이 시뮬레이션을 멈춘다.
        void EnterClosure(RaidOutcome outcome)
        {
            _closureOutcome = outcome;
            _runtime.ApplySessionState(GameSessionState.Closure);
        }
    }
}
