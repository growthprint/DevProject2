#nullable enable annotations
using Cysharp.Threading.Tasks;

namespace Game.App
{
    // 게임 중 일시정지 화면이다. 계속하기, 기지 외형 편집, 캐릭터 표시 재시도, 메뉴 복귀, 종료 버튼과 상태 문구를 맡는다.
    // 원본처럼 일시정지 메뉴에는 저장 버튼이 없다. 저장은 씬 로드 직전, 월드 종료 정산, 기지 자동 저장, 기지에서 메뉴 복귀나 종료 때 한다.
    internal sealed class PauseMenuPresenter
    {
        readonly GameUiController _ui;

        internal PauseMenuPresenter(GameUiController ui) => _ui = ui;

        // 일시정지 버튼을 상주 구독 묶음에 연결한다. 메뉴 복귀와 종료는 기지 밖 이탈 확인을 거친다.
        internal void Bind(GameUguiView view, UiEventSubscriptions subscriptions)
        {
            subscriptions.Bind(view.ResumeButton.onClick, Resume);
            subscriptions.Bind(view.CustomizeButton.onClick, Customize);
            subscriptions.Bind(view.RetryAppearanceButton.onClick, RetryAppearance);
            subscriptions.Bind(view.MainMenuButton.onClick, ReturnToMenu);
            subscriptions.Bind(view.PauseQuitButton.onClick, Quit);
        }

        // 전환 중에는 버튼을 잠그고, 저장 중은 비차단 상태 문구로만 표시한다. 기지 자동 저장 실패는 Runtime이 기록만 하고 경고를 띄우지 않는다.
        // 일시정지 화면 참조는 Bind도 검사 없이 쓰는 같은 View 참조라 여기서도 null 검사를 하지 않는다.
        internal void Refresh(bool busy)
        {
            GameUguiView view = _ui.View;
            GameRuntime runtime = _ui.Runtime!;
            view.ResumeButton.interactable = !busy;
            // 프롤로그 거울과 다른 씬에는 기지 전용 일시정지 편집 버튼을 노출하지 않는다.
            bool canCustomize = CanCustomizeFromPause();
            UiVisibility.Set(view.CustomizeButton.gameObject, canCustomize);
            view.CustomizeButton.interactable = !busy && canCustomize;
            // 현재 레벨의 캐릭터 표시가 실패했을 때만 복구 버튼을 보인다. 이 판정은 아래 일시정지 상태 문구도 함께 쓴다.
            bool presentationFailed = SceneContext.CharacterPresentationFailed;
            UiVisibility.Set(view.RetryAppearanceButton.gameObject, presentationFailed);
            view.RetryAppearanceButton.interactable = !busy;
            view.MainMenuButton.interactable = !busy;
            view.PauseQuitButton.interactable = !busy;
            // 전환이 시작된 동안 설정 진입을 허용하지 않는다.
            view.PauseOptionsButton.interactable = !busy;
            // 일시정지 상태 문구는 정적 번역을 끄고 이 갱신만 쓴다. 저장 중, 표시 실패의 재적용 안내, 준비 문구 순으로 고른다.
            // 구독 식별자 "element.PauseStatus"는 제품 객체 이름과 테스트 키와 같은 값이다.
            _ui.Text(view.PauseStatus, runtime.IsSaving && !busy ? "save.running"
                : presentationFailed ? "appearance.failed" : "pause.ready", "element.PauseStatus");
        }

        // 계속하기는 모달 제거와 별개로 실제 플레이 일시정지를 해제한다. 일시정지 모달의 취소 동작이기도 하다.
        internal void Resume()
        {
            if (!_ui.CanRun())
                return;

            // 성공하면 상태 알림이 곧바로 화면을 갱신하므로 거절될 때만 행동 실패를 안내한다.
            // 일시정지 모달을 취소로 닫는 경로는 CancelTop을 부른 쪽이 갱신한다.
            if (!_ui.Runtime!.TryResume()) _ui.Errors.ShowActionFailure();
        }

        // 버튼이 숨겨지기 전에 들어온 입력도 기지 일시정지 상태일 때만 편집 공간으로 넘긴다.
        void Customize()
        {
            if (CanCustomizeFromPause())
                _ui.Commands.RunAsync(UiCommand.Customize).Forget();
        }

        // 프롤로그는 씬의 거울이 편집 진입을 맡으므로, 공통 메뉴는 기지 일시정지에서만 허용한다.
        // 세션이 없으면 GameManager의 SessionManager가 null이라 씬 비교가 거짓이 된다.
        bool CanCustomizeFromPause()
        {
            return _ui.Runtime!.SessionState == GameSessionState.Paused
                && GameManager.SessionManager?.Snapshot().SceneId == GameScene.BaseCamp;
        }

        // 확정 스냅샷을 다시 표시할 뿐 편집 권한을 열지 않는다.
        void RetryAppearance()
        {
            if (!_ui.CanRun()) return;
            // 성공하면 상태 알림이 곧바로 일시정지 안내를 다시 계산하므로 거절될 때만 행동 실패를 안내한다.
            if (!_ui.Runtime!.TryRetryCharacterPresentation()) _ui.Errors.ShowActionFailure();
        }

        // 저장 성공 여부와 씬 정리는 런타임의 메뉴 복귀 명령에 맡긴다.
        void ReturnToMenu() => _ui.Confirm.RequestLeave(UiCommand.MainMenu);

        // 정상 종료가 저장과 정리를 끝낸 뒤에만 실제 종료를 요청한다. 메인 메뉴의 종료 버튼도 같은 경로를 쓴다.
        internal void Quit() => _ui.Confirm.RequestLeave(UiCommand.Quit);
    }
}
