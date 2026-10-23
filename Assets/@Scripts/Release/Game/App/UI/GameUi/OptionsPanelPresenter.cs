#nullable enable annotations
using Game.Core;

namespace Game.App
{
    // 메뉴와 일시정지의 원본 설정 패널을 연다. 원본처럼 두 화면의 설정 패널이 서로 달라서 각 화면의 수명에 맞춰 따로 소유하고,
    // 같은 설정 정책을 쓰되 메뉴가 있으면 메뉴 패널, 없으면 일시정지 패널만 입력을 받는다. 설정 화면은 옵션 저장소가 있는 구성에서만 만든다.
    internal sealed class OptionsPanelPresenter
    {
        readonly GameUiController _ui;
        GameOptionsScreen? _pauseScreen, _menuScreen;

        internal OptionsPanelPresenter(GameUiController ui) => _ui = ui;

        // 지금 입력을 받는 설정 화면이다. 설정 화면이 없는 구성이면 null이라 설정 버튼을 숨긴다.
        internal GameOptionsScreen? Current => _menuScreen ?? _pauseScreen;

        // 옵션을 저장하는 구성(IsPersistent)에서만 게임 중 일시정지용 원본 설정 패널을 만든다.
        // 앱 UI의 Bind가 옵션 관리자를 잡아 둔 뒤에만 부르므로 그 참조를 설정 화면에 넘긴다.
        internal void BindPause()
        {
            GameOptionsManager options = _ui.OptionsManager;
            if (options.IsPersistent)
                _pauseScreen = new GameOptionsScreen(_ui.View.Options, _ui.Runtime!, options, Close);
        }

        // 씬이 바뀐 뒤 일시정지 설정 화면의 구독이 남지 않게 한다.
        internal void UnbindPause()
        {
            _pauseScreen?.Dispose();
            _pauseScreen = null;
        }

        // 메인 메뉴 설정은 원본 메뉴 Canvas에 만든 별도 패널을 쓴다. 메뉴가 바뀔 때마다 새로 만들고, 앱 UI가 잡아 둔 옵션 관리자를 넘긴다.
        internal void BindMenu(GameOptionsUguiView view) =>
            _menuScreen = new GameOptionsScreen(view, _ui.Runtime!, _ui.OptionsManager, Close);

        // 떠나는 메뉴의 열린 설정 모달을 닫고, 메뉴 설정 화면의 구독과 번역도 끝낸다. 앱 종료 때 메뉴 패널이 이미 파괴됐으면
        // 숨기기가 실패해도 스택 항목은 먼저 빠지므로, Cleanup.Run으로 기록만 하고 남은 정리를 이어 간다.
        internal void UnbindMenu()
        {
            Cleanup.Run(() => _ui.Modals.Remove(GameModalId.Options), _ui);
            _menuScreen?.Dispose();
            _menuScreen = null;
        }

        // 메뉴와 일시정지의 설정 버튼이 함께 쓰는 열기 명령이다.
        internal void Open()
        {
            // 두 설정 버튼은 설정 화면이 있을 때만 보이므로 Current는 null이 아니다. Open은 실패하지 않는다.
            if (!_ui.CanRun() || _ui.Modals.Contains(GameModalId.Options))
                return;

            GameOptionsScreen screen = Current!;
            screen.Open();
            // 취소 동작을 null로 둬 모달 스택이 바로 닫지 않게 한다. ESC는 HandleCancel이 RequestClose로 저장을 기다린 뒤 닫는다.
            _ui.Modals.Open(GameModalId.Options, screen.Root, null, UiFocus.Current(), screen.InitialFocus);
            _ui.Refresh();
        }

        // 설정 창은 마지막 쓰기가 끝나야 닫히므로 RequestClose에 맡긴다. 펼쳐진 드롭다운 목록이 있으면 ESC는 목록만 닫는다.
        internal void HandleCancel()
        {
            GameOptionsScreen? screen = Current;
            if (screen != null && !screen.TryCollapseDropdown()) screen.RequestClose();
        }

        // 설정 화면의 닫기 완료 콜백이다. GameOptionsScreen이 저장을 마쳤거나 저장 실패를 보인 뒤에만 부르므로
        // 저장 중인지 다시 보지 않는다.
        void Close()
        {
            _ui.Modals.Remove(GameModalId.Options);
            _ui.Refresh();
        }
    }
}
