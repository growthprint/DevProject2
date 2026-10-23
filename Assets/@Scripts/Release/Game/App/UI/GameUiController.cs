#nullable enable annotations
using Cysharp.Threading.Tasks;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.App
{
    // GameManager 프리팹에 중첩된 앱 공통 UI다. Unity 수명, Runtime 바인딩, 화면 전체 갱신, ESC 처리, 모달 스택만 소유하고
    // 화면별 동작은 역할별 프레젠터에 맡긴다. 명령 실행(UiCommandRunner), 오류 창(ErrorDialogPresenter), 확인 창(ConfirmDialogPresenter),
    // 일시정지 화면(PauseMenuPresenter), 메인 메뉴(MainMenuPresenter), 설정 패널(OptionsPanelPresenter), 지도(GameMapScreen)가 그것이다.
    // 프레젠터는 이 컨트롤러를 중재자로 삼아 Runtime, View, 모달 스택, 다른 프레젠터를 찾는다. 최종 해제는 IAsyncReleasable 계약을 따른다.
    [RequireComponent(typeof(GameUguiView))]
    [DisallowMultipleComponent]
    public sealed class GameUiController : MonoBehaviour, IAsyncReleasable
    {
        // 아래 두 의존성은 Configure가 함께 넣고 비우지 않는다. 그래서 바인딩 동안 null이 아니다.
        // 메뉴, 지도, 캐릭터 표시, 게임 패널은 씬 문맥의 정적 접근자로 읽으므로 씬 흐름 참조를 두지 않는다.
        GameRuntime? _runtime;
        // 키보드를 직접 읽지 않고 프로젝트 입력 사본의 UI 액션을 쓴다. UI/Cancel, UI/Submit, UI/ToggleMap을 여기서 찾는다.
        InputActionAsset? _actions;
        // 공통 프리팹의 uGUI View다. RequireComponent가 보장하고 Bind가 대입한 뒤 비우지 않으므로 바인딩 동안 null이 아니다.
        // 테스트는 View 필드를 채운 뒤 이 필드에 주입한다.
        GameUguiView _view = null!;
        // Bind가 잡아 둔 옵션 관리자다. 루트가 파괴될 때 GameManager 접근자가 먼저 비워질 수 있어 구독 해제는 이 참조로 한다.
        GameOptionsManager _optionsManager = null!;
        // 열린 모달의 순서를 중복 없이 관리한다.
        readonly GameModalStack _modals = new GameModalStack();
        // 현재 바인딩을 가리키는 식별 객체이자 바인딩 표시다. 취소 토큰 대신, 늦게 도착한 비동기 결과가 같은 바인딩인지
        // 참조로 비교한다. 바인딩 여부를 따로 두지 않아 두 상태가 어긋나는 구간이 없다.
        object? _lifetime;
        // 최종 해제 여부다. 저장 상태 조회 중 표시는 명령 실행기(UiCommandRunner)가 따로 둔다.
        bool _released;
        // 최종 해제는 재구성과 별개로 한 번만 실행한다. 실패는 던지지 않고 기록만 한다.
        ReleaseOnce? _release;
        // 같은 입력 프레임에서 취소를 두 번 처리하지 않게 마지막 처리 프레임을 기억한다.
        // _noCancelFrame은 아직 처리한 프레임이 없다는 뜻이다.
        const int _noCancelFrame = -1;
        int _lastCancelFrame = _noCancelFrame;
        // 상주 버튼과 입력 액션 구독이다. 화면별 구독 묶음과 같은 소유권 규칙을 따른다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 상주 모달과 일시정지 화면의 동적 문구 번역이다. 메뉴 문구는 메뉴 화면이 따로 소유한다.
        readonly UiDynamicText _text = new UiDynamicText();
        // 지도 입력과 블러는 GameMapScreen이 맡고, 앱 UI는 만들고 갱신하고 해제하는 순서만 정한다.
        GameMapScreen? _mapScreen;
        // 역할별 프레젠터다. 이 컨트롤러를 넘겨 처음 쓸 때 한 번 만들고, 바인딩이 바뀌어도 같은 객체를 쓴다.
        UiCommandRunner? _commands;
        ErrorDialogPresenter? _errors;
        ConfirmDialogPresenter? _confirm;
        PauseMenuPresenter? _pause;
        MainMenuPresenter? _menu;
        OptionsPanelPresenter? _options;

        internal GameRuntime? Runtime => _runtime;
        internal InputActionAsset? Actions => _actions;
        internal GameUguiView View => _view;
        internal GameModalStack Modals => _modals;
        // Bind가 잡아 둔 옵션 관리자다. 설정 패널과 메뉴는 바인딩 동안에만 연결되므로 GameManager 접근자 대신 이 참조를 쓴다.
        internal GameOptionsManager OptionsManager => _optionsManager;
        // 현재 바인딩의 식별 객체다. 바인딩 밖에서는 null이다.
        internal object? Lifetime => _lifetime;
        // 수명 객체가 있는 동안이 바인딩 구간이다.
        bool IsBound => _lifetime != null;
        internal UiCommandRunner Commands => _commands ??= new UiCommandRunner(this);
        internal ErrorDialogPresenter Errors => _errors ??= new ErrorDialogPresenter(this);
        internal ConfirmDialogPresenter Confirm => _confirm ??= new ConfirmDialogPresenter(this);
        internal PauseMenuPresenter Pause => _pause ??= new PauseMenuPresenter(this);
        internal MainMenuPresenter Menu => _menu ??= new MainMenuPresenter(this);
        internal OptionsPanelPresenter Options => _options ??= new OptionsPanelPresenter(this);

        // 필수 의존성만 연결한다. 이 UI 에셋의 생성과 파괴는 상위 소유자가 맡는다.
        public void Configure(GameRuntime runtime, InputActionAsset actions)
        {
            // 최종 해제 뒤에는 해제가 실패했더라도 다시 구성하지 않고 호출을 조용히 무시한다.
            if (_released)
                return;

            Unbind();
            // 두 의존성을 함께 대입하므로 Bind는 _runtime 하나로 구성 여부를 판단한다.
            _runtime = runtime;
            _actions = actions;
            // 비활성 호스트는 OnEnable이 연결한다. 재구성은 최종 해제 전에만 허용하므로 _released는 초기화하지 않는다.
            if (isActiveAndEnabled)
                Bind();
        }

        // 같은 인스턴스의 재활성화에서도 콜백과 입력을 한 번씩만 연결한다.
        public void Bind()
        {
            // 호출자는 Configure와 Unity 수명 콜백뿐이라 늘 메인 스레드다.
            // 두 의존성은 Configure가 함께 넣으므로, 구성 전에 온 OnEnable은 _runtime 하나로 거른다.
            if (IsBound || _released || _runtime == null)
                return;
            // 아래 설정 패널 연결과, 중간에 실패한 연결을 UnbindCore가 정리할 때도 쓰도록 수명 객체를 세우기 전에 잡는다.
            _optionsManager = GameManager.Options;
            // View는 RequireComponent가, UI/Cancel 액션은 프로젝트 입력 에셋이 보장하므로 둘 다 null 아님으로 받는다.
            // 액션이 있는지는 ProductUiLocalizationTests가, View 참조 완전성은 EditMode 저작 테스트가 확인한다.
            _view = GetComponent<GameUguiView>();
            InputAction cancel = _actions!.FindAction("UI/Cancel");

            Options.BindPause();
            // 설정 화면이 있을 때만 설정 버튼을 보인다. 모달은 모두 닫힌 채 시작한다.
            UiVisibility.Set(_view.PauseOptionsButton.gameObject, Options.Current != null);
            _subscriptions.Bind(_view.PauseOptionsButton.onClick, Options.Open);
            _view.PauseModal.SetActive(false);
            _view.OverwriteModal.SetActive(false);
            _view.ErrorModal.SetActive(false);
            _view.LoadingModal.SetActive(false);
            _view.Options.gameObject.SetActive(false);
            // 준비가 끝난 뒤 수명 객체를 세워 바인딩 구간을 연다. 이후 연결이 실패해도 UnbindCore가 정리한다.
            _lifetime = new object();
            // 동적 상태 문구만 이 수명 객체가 소유하고 고정 문구는 LocalizedUguiText가 갱신한다.
            _text.Bind();
            _lastCancelFrame = _noCancelFrame;
            Pause.Bind(_view, _subscriptions);
            Confirm.Bind(_view, _subscriptions);
            Errors.Bind(_view, _subscriptions);
            // Runtime 상태가 바뀌면 현재 Runtime 값으로 UI 전체를 다시 계산한다.
            _runtime.StateChanged += Refresh;
            // 옵션 관리자의 적용 알림 구독도 앱 UI 수명 안에서 한 번만 유지한다. 해제와 같은 객체에 구독하도록 잡아 둔 참조를 쓴다.
            _optionsManager.Applied += Menu.ApplyOptions;
            // 메뉴 씬 문맥은 Awake에서 자신을, 로딩 시작에서 null을 알린다. 정적 이벤트라 해제도 UnbindCore가 반드시 한다.
            MenuSceneContext.CurrentChanged += Menu.Bind;
            // 취소 입력은 상주 구독 묶음이 구독과 켜기를 맡고, 다른 소비자가 이미 켠 입력은 해제 때 끄지 않는다.
            _subscriptions.Bind(cancel, OnCancel);
            // 원본 BaseSceneUtilities처럼 모달을 열고 닫을 때 기지 자동 저장을 요청한다.
            _modals.Changed += OnModalChanged;

            // 구성보다 먼저 올라온 메뉴도 놓치지 않도록 지금 메뉴에 바로 연결한다. 메뉴 씬이 아니면 null이다.
            Menu.Bind(MenuSceneContext.Current);
            // 지도와 미니맵 연결은 제작기가 만들고 EditMode 테스트가 확인하므로 준비 여부를 따지지 않고 바로 만든다.
            // 같은 모달 스택, 입력 에셋, 바인딩 수명에 연결한다.
            _mapScreen = new GameMapScreen(_view, _runtime, _actions, _modals);
            Refresh();
        }

        // 입력, 런타임, 메뉴 구독과 진행 중인 UI 요청을 함께 끊는다.
        public void Unbind()
        {
            // 호출자는 Configure와 Unity 수명 콜백뿐이라 늘 메인 스레드다.
            // 최종 해제가 시작된 뒤에는 실패했더라도 Unity 콜백이 같은 정리를 다시 하지 않는다.
            if (_released) return;
            UnbindCore();
        }

        // 일반 연결 해제와 일회성 최종 종료가 같은 정리 본문을 사용한다.
        void UnbindCore()
        {
            if (!IsBound) return;
            // 런타임과 모달 참조를 놓기 전에 지도의 커서와 블러를 되돌린다.
            GameMapScreen? map = _mapScreen;
            _mapScreen = null;
            map?.Dispose();
            // 수명 객체를 먼저 비워 정리 중에 다시 들어온 해제와 갱신을 막고, 진행 중인 비동기 결과가 IsBoundTo에서 걸러지게 한다.
            _lifetime = null;
            Commands.CancelScheduledRefresh();
            // 구독 때와 같은 Refresh 메서드 그룹이라 같은 대리자로 비교되어 빠진다.
            _runtime!.StateChanged -= Refresh;
            // 해제된 메뉴가 옵션 관리자의 적용 알림을 받지 않게 한다. 루트 파괴 중에도 끊을 수 있도록
            // GameManager 접근자 대신 Bind가 잡아 둔 참조를 쓴다.
            _optionsManager.Applied -= Menu.ApplyOptions;
            // 정적 이벤트는 이 UI보다 오래 살아서, 남겨 두면 해제된 UI가 다음 메뉴에 다시 연결된다.
            MenuSceneContext.CurrentChanged -= Menu.Bind;
            // 해제된 UI의 모달 변화는 자동 저장 요청으로 알리지 않는다.
            _modals.Changed -= OnModalChanged;
            // 상주 UI의 콜백과 입력 구독, 프리팹 OnClick이 부르는 확인 모달 [취소] 명령을 끊는다. 이 화면이 켠 취소 입력도
            // 여기서 꺼지고, 지도 토글 입력은 위의 지도 화면 해제가 끈다. _view는 Bind가 수명 객체를 세우기 전에 대입하고 비우지 않으므로 null 검사가 없다.
            _subscriptions.Clear();
            Confirm.Unbind(_view);
            // 일시정지 설정과 메뉴(설정 포함)를 각각 해제해 씬이 바뀐 뒤 구독이 남지 않게 한다.
            Options.UnbindPause();
            Menu.Unbind();
            // 해제된 상주 UI에는 로캘 변경 콜백이 도착하지 않게 한다.
            _text.Unbind();
            _modals.Clear();
            // 다시 바인딩할 때 끝난 바인딩의 조회, 파일 작업 표시, 로딩 문구, 메뉴 조회 요청, 오류 창 명령이 남지 않게 한다.
            Commands.Reset();
            Errors.Reset();
        }

        // 메인 스레드에서 도는 GameRuntime의 앱 자원 범위 해제에서만 호출되므로 스레드 전환 없이 구독과 입력을 정리한다.
        public async UniTask ReleaseAsync()
        {
            // 해제 객체는 처음 호출 때 한 번만 만든다. ReleaseOnce는 던지지 않고, 실패 로그 문맥으로 이 컴포넌트를 쓴다.
            _release ??= new ReleaseOnce(ReleaseCoreAsync, this);
            await _release.ReleaseAsync();
        }

        // 정리 중에 Unity 콜백이 다시 들어와도 무시되도록 _released를 먼저 세운다.
        UniTask ReleaseCoreAsync()
        {
            _released = true;
            UnbindCore();
            return UniTask.CompletedTask;
        }

        // 구성 후 다시 켠 uGUI 호스트도 같은 연결 경로를 사용한다.
        void OnEnable() => Bind();
        // 비활성 uGUI 호스트가 입력을 계속 수신하지 않게 한다.
        void OnDisable() => Unbind();
        // 외부 파괴에도 명시적 해제와 같은 구독 정리를 보장한다.
        void OnDestroy() => Unbind();
        // 플레이어가 움직인 뒤에 지도를 갱신한다. 지도 화면은 바인딩 동안에만 있으므로 바인딩 여부는 검사하지 않는다.
        void LateUpdate() => _mapScreen?.Tick();

        // 전투 월드 탭 단추 줄의 지도 단추(원작 ViewsProxy.ShowMapView)가 쓴다. 지도 화면은 바인딩 동안에만 있어 바인딩 밖에서는 열 수 없다.
        // 단추를 받은 컨트롤러가 CanOpenMap으로 확인하고 열린 게임 화면을 닫은 뒤 OpenMap을 부르므로, OpenMap 때는 지도 화면이 늘 있다.
        // 입력 차단과 모달은 지도 화면이 맡는다.
        internal bool CanOpenMap => _mapScreen != null && _mapScreen.CanOpen;
        internal void OpenMap() => _mapScreen!.Open();

        // 화면 전체를 현재 Runtime 값으로 다시 계산한다. 전환만 로딩 모달로 잠그고 저장 중은 일시정지 화면의 비차단 상태 문구로 표시한다.
        internal void Refresh()
        {
            // 강제 중단 중에는 화면이 곧 파괴되므로 갱신하지 않는다. 앱 종료 때 Unity가 리셋한 Localization을 번역 구독이 다시
            // 초기화하면, 폐기된 Addressables 구현이 Edit Mode에서 다시 만들어진다. 바인딩 동안 _runtime은 null이 아니다.
            if (!IsBound || _runtime!.IsAborting) return;
            bool busy = _runtime.IsBusy;
            // 실제 씬 전환만 로딩 화면으로 덮는다. 저장 삭제나 슬롯 선택 같은 메뉴 파일 작업은 메뉴에 머문다.
            // 사망이나 탈출 뒤 저장은 결과 화면 전의 짧은 저장이라 로딩 화면 없이 진행하고, 입력은 전환 잠금이 막는다. 월드 종료는 씬 흐름이 맡는다.
            // 원작 SceneLoader 로딩 화면을 쓰는 전환은 씬 흐름이 그 화면을 직접 보이므로 이 모달을 띄우지 않는다.
            // 루트가 파괴될 때는 위의 강제 중단 검사가 먼저 갱신을 멈추므로, 여기서 GameManager 접근자를 읽어도 된다.
            SceneFlow scenes = GameManager.Scenes;
            bool loaderScreen = scenes.IsShowingLoadingScreen;
            bool showLoading = busy && !Commands.IsMenuFileOperation && !scenes.IsEndingWorld && !loaderScreen;
            // 전환 실패는 창 없이 기록만 하므로 모달 갱신에는 로딩 표시 여부만 넘긴다.
            RefreshModals(showLoading, loaderScreen);
            Pause.Refresh(busy);
            Menu.Refresh(busy, showLoading);
            // 모달이 닫히고 메뉴의 CanvasGroup 잠금이 풀린 뒤에, 모달을 열기 전의 선택을 되돌린다.
            _modals.RestorePendingFocus();
            // 이번 갱신이 열린 모달의 선택된 버튼을 숨기거나 잠갔으면 최상위 모달의 기본 선택으로 되돌린다.
            // 저장 슬롯 조회 중 잠금이나 외형 재적용 성공이 그런 경우다.
            _modals.RecoverTopFocus();
            Commands.ScheduleSaveRefresh();
        }

        // 일시정지와 로딩 모달을 열고 닫는다. 전환 실패는 Runtime이 기록만 하고 다음 상태로 진행하므로 복구 창을 띄우지 않는다.
        // Refresh만 부르고 그쪽에서 바인딩 여부를 확인하므로 _runtime은 null이 아니다.
        // 원작 Button_LoadMainMenu가 로드를 시작하며 PauseMenu.Hide를 부르듯, 원작 로딩 화면 전환 동안에는 일시정지 창을 닫는다.
        void RefreshModals(bool showLoading, bool loaderScreen)
        {
            if (_runtime!.SessionState == GameSessionState.Paused && !loaderScreen)
            {
                // 복귀가 실패해 일시정지로 돌아와도 이미 열린 오류 창이 위에 남도록 일시정지 모달은 맨 아래에 연다.
                // 갱신마다 대리자와 현재 선택 인수를 만들지 않도록 이미 열린 모달은 Open 전에 거른다.
                if (!_modals.Contains(GameModalId.Pause))
                    _modals.Open(GameModalId.Pause, _view.PauseModal, Pause.Resume, UiFocus.Current(), _view.ResumeButton, true);
            }
            else _modals.Remove(GameModalId.Pause);
            if (showLoading)
            {
                // 난이도나 캠페인처럼 UI 밖에서 시작한 전환도 현재 작업에 맞는 안내를 띄운다. 모달을 열 때 프리팹의 정적 번역
                // loading.ready가 다시 켜져 명령별 문구를 덮지 않도록, 정적 번역을 끄는 Text를 쓴다.
                Text(_view.LoadingMessage, Commands.LoadingKey ?? "loading.ready", "loading.message");
                // 문구는 갱신마다 바꾸고, 모달 열기 인수는 아직 열리지 않았을 때만 만든다. 결과는 Open의 중복 거절과 같다.
                // 씬 로드는 시작하면 되돌리지 않으므로 취소 동작을 두지 않는다. 취소 입력은 모달이 소비만 한다.
                if (!_modals.Contains(GameModalId.Loading))
                    _modals.Open(GameModalId.Loading, _view.LoadingModal, null, UiFocus.Current(), null);
            }
            else _modals.Remove(GameModalId.Loading);
        }

        // 사용자가 보는 모달을 열고 닫을 때마다 기지 씬의 자동 저장을 요청한다. 전환 로딩 화면은 사용자 화면이 아니라 제외한다.
        void OnModalChanged(GameModalId id)
        {
            if (id == GameModalId.Loading) return;
            BaseAutoSaver.NotifyUiActivity();
        }

        // ESC 처리다. 시작한 전환은 취소할 수 없으므로 전환 중에는 아무것도 하지 않는다. 그 밖에는 맨 위 창부터 닫는다.
        void OnCancel(InputAction.CallbackContext context)
        {
            // 취소 액션은 바인딩 동안에만 구독하므로 바인딩 여부는 보지 않고 같은 프레임의 중복만 거른다.
            if (_lastCancelFrame == Time.frameCount)
                return;

            _lastCancelFrame = Time.frameCount;
            // 원본 BaseSceneUtilities처럼 ESC 입력 자체를 기지 자동 저장 요청으로 알린다. 기지 밖에는 자동 저장이 없어 무시된다.
            BaseAutoSaver.NotifyUiActivity();
            // 전환 중 ESC는 받을 명령이 없다. 로딩 모달이 위에 있어도 닫지 않는다. 바인딩 동안에만 구독하므로 _runtime은 null이 아니다.
            if (_runtime!.IsBusy) return;
            // 설정 창은 마지막 쓰기가 끝나야 닫히므로 설정 패널에 맡긴다. 펼쳐진 드롭다운 목록이 있으면 ESC는 목록만 닫는다.
            if (_modals.TopId == GameModalId.Options)
            {
                Options.HandleCancel();
                return;
            }
            if (_modals.CancelTop())
            {
                Refresh();
                return;
            }
            // 결과 화면에서는 ESC를 무시하고 결과 화면의 '계속' 버튼만 받는다.
            if (_runtime.SessionState == GameSessionState.Closure) return;
            // 인벤토리를 닫는 ESC가 일시정지 메뉴까지 열지 않도록, 현재 레벨에 열린 패널부터 닫는다.
            if (SceneContext.TryCancelCurrentPanel()) return;
            // 외형 편집 중 ESC는 편집 취소 명령이고, 실패 안내는 명령 실행기가 띄운다. 재개나 일시정지가 거절되면 행동 실패를 안내한다.
            // 성공하면 상태 알림이 바로 화면을 갱신한다. 안쪽 if에 아래 else가 붙지 않도록 중괄호를 둔다.
            if (_runtime.SessionState == GameSessionState.Customizing)
                Commands.RunAsync(UiCommand.CancelCustomization).Forget();
            else if (_runtime.SessionState == GameSessionState.Paused)
            {
                if (!_runtime.TryResume()) Errors.ShowActionFailure();
            }
            else if (_runtime.SessionState == GameSessionState.Playing)
            {
                if (!_runtime.TryPause()) Errors.ShowActionFailure();
            }
            // 메뉴처럼 바꿀 상태가 없으면 상태 알림이 오지 않는다. 직접 갱신해 잃어버린 키보드나 패드 선택을 초기 포커스로 되돌린다.
            else Refresh();
        }

        // 저장 중에도 현재 모달을 닫을 수 있다. 시작한 전환은 취소할 수 없으므로 전환 중에는 모달을 그대로 둔다.
        // 호출자인 확인 모달과 오류 창의 취소는 바인딩 동안에만 연결되므로 바인딩 여부를 검사하지 않는다.
        internal void CancelModal()
        {
            if (_runtime!.IsBusy) return;
            _modals.CancelTop();
            Refresh();
        }

        // 공통 명령은 저장 완료를 기다리지 않고, 충돌하는 전환이 진행 중일 때만 막는다. 바인딩 동안 _runtime은 null이 아니다.
        internal bool CanRun() => IsBound && !_runtime!.IsBusy;

        // 늦게 끝난 비동기 결과가 같은 바인딩에서 온 것인지 판정한다. 해제 뒤에는 수명 객체가 null이라 어떤 결과도 통과하지 않는다.
        internal bool IsBoundTo(object lifetime) => ReferenceEquals(_lifetime, lifetime);

        // 상태에 따라 바뀌는 TMP 문구를 번역해 넣는다. 규칙은 UiDynamicText에 있다.
        internal void Text(TMP_Text element, string key, string id) => _text.Set(element, key, id);
    }
}
