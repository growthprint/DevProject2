#nullable enable annotations
using System;

namespace Game.App
{
    // 원작 NoteIndexView.ShowNote처럼 쪽지를 해금하고 쪽지 창을 연다. 창은 공용 HUD의 원본 NoteIndexView다.
    // 열린 동안 게임플레이 입력을 막고, ESC(원작 OnCancel)로 닫는다. 입력 차단 핸들은 화면 공통 바탕(GameplayScreen)이 쥐고,
    // 공용 HUD 연결, Runtime, 앱 루트 유무는 전투 월드 문맥으로 본다.
    internal sealed class NoteScreen : GameplayScreen
    {
        readonly GameplayWorldContext _world;
        // 열린 동안의 해금과 로캘 구독이다. 닫을 때 한 번에 푼다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();

        // 전투 월드 컨트롤러가 화면 묶음과 같은 월드 문맥을 넘긴다. 공용 HUD, Runtime, 연결 소유자를 그 문맥 하나에서 읽는다.
        internal NoteScreen(GameplayWorldContext world)
        {
            _world = world;
        }

        // 원작 NoteInteract.OnInteractFinished의 해금과 창 열기다. 해금은 세션 관문이 닫혔으면 거절되고 창도 열지 않는다.
        // 앱 루트가 없거나 공용 HUD가 이 월드에 붙어 있지 않으면 다른 소유자의 HUD를 바꾸지 않도록 열지 않는다.
        internal void Show(string key)
        {
            if (!_world.CanPresent) return;
            NoteIndex notes = GameManager.Notes;
            if (!notes.TryUnlock(key)) return;
            // 입력 차단과 구독은 탭 단추의 목록 열기(ShowIndex)와 같은 규칙이라 TryBegin으로 모았다.
            if (!TryBegin()) return;
            _world.Hud.Notes.Show(notes, key);
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다. 기지 밖에는 자동 저장이 없어 무시된다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 NoteIndexButton의 NoteIndexView.DoOpen이다. 탭 단추 줄의 쪽지 단추가 부르며, 해금 없이 고른 쪽지 없는 목록을 연다.
        // 이미 열려 있으면 보이던 쪽지를 지우지 않도록 그대로 둔다. 탭 단추를 받은 컨트롤러가 다른 탭 화면을 먼저 닫으므로, 그 뒤에도
        // 다른 화면이 입력을 막고 있으면 인벤토리의 ShowCarried처럼 겹쳐 열지 않는다.
        internal void ShowIndex()
        {
            if (IsOpen || !_world.CanPresent || _world.Runtime.GameplayInput.IsBlocked) return;
            if (!TryBegin()) return;
            _world.Hud.Notes.Show(GameManager.Notes, null);
            BaseAutoSaver.NotifyUiActivity();
        }

        // 처음 열 때만 입력 차단을 얻고 해금·로캘 변경 구독을 건다. Runtime이 정리 중이라 거절하면 false를 돌려주고 창을 열지 않는다.
        // 두 구독은 구독 묶음이 붙인 순서대로 뗀다.
        bool TryBegin()
        {
            if (IsOpen) return true;
            if (!TryBlockInput(_world.Runtime)) return false;
            Action refresh = Refresh;
            NoteIndex.Changed += refresh;
            _subscriptions.Add(() => NoteIndex.Changed -= refresh);
            _subscriptions.BindLocaleChanged(refresh);
            return true;
        }

        // 입력 차단과 구독을 돌려주고 창을 닫는다. 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다.
        // ESC(원작 OnCancel)는 공통 바탕의 TryClose로 이 닫기를 부른다.
        internal override void Close()
        {
            if (!IsOpen) return;
            ReleaseInput();
            _subscriptions.Clear();
            if (_world.PresentationAttached) _world.Hud.Notes.Hide();
            BaseAutoSaver.NotifyUiActivity();
        }

        // 열린 동안 해금이나 로캘이 바뀌면 이 월드의 공용 HUD 쪽지 창을 다시 그린다.
        void Refresh() => _world.Hud.Notes.Refresh();
    }
}
