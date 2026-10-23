#nullable enable annotations
using Cysharp.Threading.Tasks;
using TMPro;

namespace Game.App
{
    // 원본 ConfirmDialogue를 공용 확인 모달로 쓴다. 덮어쓰기 확인과 기지 밖 이탈 확인이 같은 창을 쓰며, 제목, 본문, 확인 라벨을
    // 용도별 번역 키로 채우고 확인 때 실행할 명령을 기억한다. 취소나 ESC는 명령 없이 닫는다.
    internal sealed class ConfirmDialogPresenter
    {
        readonly GameUiController _ui;
        // 확인 모달의 확인 버튼이 실행할 명령이다. 모달을 열 때 정하고, 닫거나 실행하면 비운다.
        UiCommand? _command;

        internal ConfirmDialogPresenter(GameUiController ui) => _ui = ui;

        // 확인 버튼은 모달을 열 때 정한 명령을 실행한다. 확인 모달 [취소]는 명령이 늘 같아서, 프리팹 OnClick이
        // View.OnConfirmCancelClicked로 공용 취소 경로를 부른다. 숨은 모달과 아래 모달은 입력을 받지 못해 확인 모달이 맨 위일 때만 불린다.
        internal void Bind(GameUguiView view, UiEventSubscriptions subscriptions)
        {
            subscriptions.Bind(view.ConfirmNewGameButton.onClick, Confirm);
            view.BindConfirmCancel(_ui.CancelModal);
        }

        // 프리팹 OnClick이 부르는 확인 모달 [취소] 명령을 끊는다.
        internal void Unbind(GameUguiView view) => view.BindConfirmCancel(null);

        // 원본 Button_LoadMainMenu와 Button_QuitGame처럼 기지에서는 확인 없이 저장하고 떠난다. 진행이 저장되지 않는 기지 밖에서는
        // 진행도가 삭제된다는 확인 모달을 열고, 확인했을 때만 저장 없이 떠난다. 메뉴 화면의 종료는 세션이 없어 바로 실행한다.
        internal void RequestLeave(UiCommand command)
        {
            if (!_ui.CanRun())
                return;

            if (_ui.Runtime!.LeaveDiscardsProgress)
                Open(command == UiCommand.MainMenu ? UiCommand.MainMenuWithoutSaving : UiCommand.QuitWithoutSaving,
                    "UI_Leave", "UI_QuitGame_ProgressNote", "UI_Confirm");
            else _ui.Commands.RunAsync(command).Forget();
        }

        // 확인 모달을 용도별 번역 키로 채워 열고 확인 때 실행할 명령을 기억한다.
        internal void Open(UiCommand command, string title, string message, string confirm)
        {
            GameUguiView view = _ui.View;
            _command = command;
            _ui.Text(view.ConfirmTitle, title, "confirm.title");
            _ui.Text(view.ConfirmMessage, message, "confirm.message");
            // 용도에 따라 바뀌는 확인 버튼 라벨은 제작기가 버튼 아래에 만든 TMP라 열 때 찾는다.
            // 구독 식별자는 제품 버튼 이름 Confirm과 같은 값이다.
            _ui.Text(view.ConfirmNewGameButton.GetComponentInChildren<TMP_Text>(true), confirm, "element.Confirm");
            _ui.Modals.Open(GameModalId.Overwrite, view.OverwriteModal, Clear, UiFocus.Current(), view.CancelNewGameButton);
            _ui.Refresh();
        }

        // 확인 모달을 명령 없이 닫으면 기억한 명령을 지운다.
        void Clear() => _command = null;

        // 사용자가 확인했을 때만 확인 모달이 기억한 명령을 실행한다. 새 게임 시작이나 저장 없는 이탈이 여기에 온다.
        // 확인 버튼은 Open이 명령을 정하고 연 모달이 보일 때만 눌리고, 명령은 모달이 닫힐 때만 비우므로 늘 있다.
        void Confirm()
        {
            if (!_ui.CanRun())
                return;

            UiCommand command = _command.Value;
            _command = null;
            _ui.Modals.Remove(GameModalId.Overwrite);
            _ui.Commands.RunAsync(command).Forget();
        }
    }
}
