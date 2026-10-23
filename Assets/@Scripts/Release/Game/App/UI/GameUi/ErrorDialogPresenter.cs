#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine.UI;

namespace Game.App
{
    // 공통 오류 창이다. 번역 키로 안내하고, [다시 시도]는 실패한 그 명령만 다시 실행한다. 두 번째 버튼은 평소에는 창만 닫고,
    // Runtime이 이미 멈춘 치명 오류에서만 저장 없는 종료를 실행한다. 복구할 수 없는 상태에서는 재시도를 끈다.
    internal sealed class ErrorDialogPresenter
    {
        readonly GameUiController _ui;
        // 재시도 버튼은 실패한 그 명령만 다시 실행한다.
        UiCommand? _retryCommand;
        // 오류 창 두 번째 버튼의 명령이다. 오류 종류별 값은 Show가 정하고, null이면 창만 닫는다.
        UiCommand? _secondary;

        internal ErrorDialogPresenter(GameUiController ui) => _ui = ui;

        // 오류 창 두 버튼을 상주 구독 묶음에 연결한다. 두 번째 버튼은 Show가 정한 명령을 실행한다.
        internal void Bind(GameUguiView view, UiEventSubscriptions subscriptions)
        {
            subscriptions.Bind(view.RetryButton.onClick, Retry);
            subscriptions.Bind(view.CancelErrorButton.onClick, CancelError);
        }

        // 바인딩을 끊을 때 명령 상태를 비운다.
        internal void Reset()
        {
            _retryCommand = null;
            _secondary = null;
        }

        // 명령 실패 안내다. 오류 문구는 ErrorKey가 정한다. 종료의 저장 실패는 Runtime이 기록만 하고 종료를 계속하므로 여기 오지 않는다.
        internal void ShowCommandFailure(UiCommand command) => Show(UiCommandText.ErrorKey(command), command);

        // 재개, 일시정지, 캐릭터 표시 재시도 같은 Try* 상태 전환이 거절된 안내다. 거절되면 상태 알림이 오지 않으므로
        // 안내를 띄운 뒤 화면을 직접 갱신한다.
        internal void ShowActionFailure()
        {
            Show("error.action", null);
            _ui.Refresh();
        }

        // 오류 창을 연다. key는 번역 키다. 복구할 수 없는 상태에서는 같은 작업을 끝없이 재시도하지 않도록 재시도를 끈다.
        internal void Show(string key, UiCommand? command)
        {
            GameRuntime runtime = _ui.Runtime!;
            GameUguiView view = _ui.View;
            // 번역 구독이라 언어를 바꿔도 문구가 따라 바뀐다. 구독 식별자는 제품 객체 이름과 같은 고정 문자열이다.
            _ui.Text(view.ErrorMessage, key, "element.ErrorMessage");
            // Runtime이 이미 멈췄으면 닫기나 재시도 대신 명시적인 비저장 종료만 제공한다.
            bool fatal = runtime.State == GameRuntimeState.Stopped;
            // 두 번째 버튼 명령을 여기서 한 번 정하고, ESC 동작과 CancelError가 이 값을 쓴다. 치명 오류만 종료이고 null이면 창만 닫는다.
            _secondary = fatal ? UiCommand.AbortQuit : (UiCommand?)null;

            _retryCommand = fatal ? null : command;
            view.RetryButton.interactable = _retryCommand.HasValue;
            // 명령에 따라 바뀌는 두 번째 버튼 라벨은 제작기가 버튼 아래에 만든 TMP라 열 때 찾아 그 명령의 문구를 넣는다.
            // 구독 식별자는 제품 버튼 이름과 같은 고정 문자열이다.
            _ui.Text(view.CancelErrorButton.GetComponentInChildren<TMP_Text>(true), fatal ? "quit" : "cancel", "element.CancelError");
            // 두 번째 명령이 있으면 ESC로 아무것도 고르지 않고 창을 유지하고, 없으면 창만 닫는다.
            Action? cancel = _secondary.HasValue ? null : CloseOnly;
            Selectable initialFocus = _retryCommand.HasValue ? view.RetryButton : view.CancelErrorButton;
            // 오류 창이 이미 맨 위면 다시 열지 않고 ESC 동작과 기본 선택만 바꿔, 사용자가 옮긴 선택과 복귀 대상을 지킨다.
            // 그렇지 않으면 위에 다른 창이 있어도 오류 창을 맨 위로 다시 연다.
            GameModalStack modals = _ui.Modals;
            if (modals.TryUpdateTop(GameModalId.Error, cancel, initialFocus)) return;
            Selectable? previous = UiFocus.Current();
            modals.Remove(GameModalId.Error);
            modals.Open(GameModalId.Error, view.ErrorModal, cancel, previous, initialFocus);
        }

        // 실패한 동작만 재실행하고 오류 창은 새 작업 전에 닫는다. Show가 재시도 명령과 버튼 활성을 함께 정하므로
        // 버튼이 눌렸으면 명령이 있다.
        void Retry()
        {
            if (!_ui.CanRun()) return;
            UiCommand command = _retryCommand.Value;
            _ui.Modals.Remove(GameModalId.Error);
            _ui.Commands.RunAsync(command).Forget();
        }

        // 오류 창 두 번째 버튼이다. Show가 정한 명령을 실행한다.
        void CancelError()
        {
            UiCommand? secondary = _secondary;
            // 두 번째 명령이 없는 오류는 창만 닫는다.
            if (!secondary.HasValue)
            {
                _ui.CancelModal();
                return;
            }
            // 두 번째 명령은 치명 오류의 종료뿐이라 오류 창을 연 채 실행한다.
            _ui.Commands.RunAsync(secondary.Value).Forget();
        }

        // 두 번째 명령이 없는 오류 창의 ESC 동작이다. 모달 스택이 창만 닫고 게임 상태는 바꾸지 않는다.
        static void CloseOnly() { }
    }
}
