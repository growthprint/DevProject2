#nullable enable annotations
using Cysharp.Threading.Tasks;
using Game.Core;
using TMPro;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.App
{
    // 메뉴 씬의 메인 메뉴 화면이다. 새 게임, 이어하기, 저장 삭제, 백업 복구, 저장 슬롯 창, 메뉴 설정 버튼과 저장 상태 문구를 맡는다.
    // 메뉴 씬 문맥에 직렬화된 uGUI View만 쓰고 전역 객체를 찾지 않는다. 메뉴 씬 문맥이 바뀔 때마다(MenuSceneContext.CurrentChanged) 다시 연결한다.
    internal sealed class MainMenuPresenter
    {
        readonly GameUiController _ui;
        // 메뉴 버튼과 메뉴가 켠 제출 입력의 구독이다. 상주 구독과 따로 모아 메뉴가 바뀔 때마다 해제한다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 씬마다 바뀌는 메뉴 View라 공통 uGUI와 수명을 따로 둔다.
        MainMenuUguiView? _view;
        // 현재 메뉴의 타이틀 공개 상태와 카메라 설정만 연결한다.
        OriginalMenuPresentation? _presentation;
        // 원본 저장 슬롯 뷰다. 현재 메뉴와 수명이 같다.
        MainMenuSaveSlotsView? _saveSlotsView;
        // 클릭으로 삭제가 우회되지 않도록 길게 누르기 판정만 연결한다.
        HoldToConfirmButton? _deleteHold;
        // 저장 상태처럼 바뀌는 메뉴 문구의 번역 구독이다. 고정 문구는 LocalizedUguiText가 맡는다.
        LocalizedTextBindings? _text;
        // 메뉴 첫 포커스를 이미 두었는지다.
        bool _focused;

        internal MainMenuPresenter(GameUiController ui) => _ui = ui;

        // 메뉴 View를 연결한다. 메뉴가 없는 씬은 연결하지 않는다. 바인딩 동안에만 불리므로 바인딩 여부는 검사하지 않는다.
        // 메뉴 문맥의 Awake에서 불리므로 메뉴 씬의 다른 컴포넌트는 아직 Awake 전일 수 있다.
        internal void Bind(MenuSceneContext? menu)
        {
            Unbind();
            if (menu == null) return;
            // 메뉴 View는 MenuSceneContext에 저작되고 ProductUiLocalizationTests가 연결을 확인한다.
            _view = menu.View;

            // 타이틀이 열린 다음에만 메뉴의 키보드 입력과 초기 포커스를 허용한다.
            _presentation = menu.Presentation;

            if (_presentation != null)
                _presentation.MenuReady += _ui.Refresh;

            _text = new LocalizedTextBindings();
            // 옵션 저장소가 있는 구성에서만 확정 옵션을 메뉴 연출에 적용하고 메뉴 설정 패널을 만든다.
            // 메뉴는 앱 UI가 바인딩된 동안에만 연결되므로 앱 UI가 바인딩 때 잡아 둔 옵션 관리자를 쓴다.
            GameOptionsManager options = _ui.OptionsManager;
            if (options.IsPersistent)
            {
                ApplyOptions(options.Current);
                GameOptionsUguiView menuOptions = _view.GetComponentInChildren<GameOptionsUguiView>(true);
                _ui.Options.BindMenu(menuOptions);
                menuOptions.gameObject.SetActive(false);
            }
            // 설정 화면이 있을 때만 설정 버튼을 보인다.
            UiVisibility.Set(_view.OptionsButton.gameObject, _ui.Options.Current != null);
            _subscriptions.Bind(_view.OptionsButton.onClick, _ui.Options.Open);
            // 복구 명령은 늘 연결한다. 정상 백업이 있을 때만 보이는 판정은 Refresh가 한다.
            _subscriptions.Bind(_view.RecoverBackupButton.onClick, RecoverBackup);
            _subscriptions.Bind(_view.NewGameButton.onClick, NewGame);
            _subscriptions.Bind(_view.ContinueButton.onClick, Continue);
            // 저장 삭제는 원본처럼 3초 동안 눌러야 확정된다. 유지 컴포넌트와 UI/Submit 액션은 ProductUiLocalizationTests가 확인한다.
            _deleteHold = _view.DeleteSaveButton.GetComponent<HoldToConfirmButton>();
            // 길게 누르기가 읽는 제출 입력은 EventSystem 쪽이 켰다고 기대하지 않고 메뉴 구독 묶음으로 직접 켠다. 액션은 입력 에셋에
            // 늘 있어 null 아님으로 받는다. 다른 소비자가 이미 켠 입력은 메뉴 해제 때 끄지 않는다.
            InputAction submit = _ui.Actions!.FindAction("UI/Submit");
            _subscriptions.Enable(submit);
            _deleteHold.Configure(submit);
            _deleteHold.Confirmed += DeleteSave;
            // 원본 슬롯 선택 화면을 같은 모달 스택과 메뉴 수명에 연결한다. 슬롯 View는 제작기가 메뉴 View와 같은 객체에 붙이고
            // ProductUiLocalizationTests가 확인한다.
            _saveSlotsView = _view.GetComponent<MainMenuSaveSlotsView>();
            _saveSlotsView.Bind();
            _subscriptions.Bind(_saveSlotsView.OpenButton.onClick, OpenSaveSlots);
            _subscriptions.Bind(_saveSlotsView.CloseButton.onClick, CloseSaveSlots);
            _subscriptions.Bind(_saveSlotsView.RestoreButton.onClick, RecoverBackup);
            _saveSlotsView.Selected += SelectSaveSlot;
            _subscriptions.Bind(_view.QuitButton.onClick, _ui.Pause.Quit);
            // 메뉴 준비와 런타임 시작이 겹치지 않게 첫 저장 조회는 명령 실행기에 요청만 하고 다음 UI 틱에 실행한다.
            _ui.Commands.RequestSaveRefresh();
            _focused = false;
            _ui.Refresh();
        }

        // 씬 교체와 앱 종료가 함께 쓰는 메뉴 정리다. 연결된 메뉴가 없을 때도 불리므로 여러 번 불러도 안전해야 한다.
        // 앱 종료는 메뉴 씬을 UI 해제보다 먼저 내려 알림 없이 View가 파괴될 수 있다. 그래서 메뉴 객체는 Unity null 비교 뒤에 만지고,
        // 메뉴 패널을 숨기는 모달 제거는 Cleanup.Run으로 감싼다.
        internal void Unbind()
        {
            // 메뉴 View 참조를 가장 먼저 비워, 아래 모달 제거가 갱신을 부르더라도 정리 중인 메뉴를 읽지 않게 한다.
            // 메뉴 갱신은 _view가 있으면 _saveSlotsView와 문구 바인딩도 있다고 가정한다.
            _view = null;
            // 떠나는 메뉴의 클릭 구독, 메뉴가 켠 제출 입력, 번역 이벤트를 함께 끊는다.
            _subscriptions.Clear();
            _text?.Dispose();
            _text = null;

            // 떠나는 씬의 타이틀 공개 알림을 끊는다.
            if (_presentation != null)
                _presentation.MenuReady -= _ui.Refresh;
            _presentation = null;

            // 떠나는 메뉴의 열린 설정 모달을 닫고, 메뉴 설정 화면의 구독과 번역도 끝낸다.
            _ui.Options.UnbindMenu();

            // 숨은 메뉴에 삭제 유지 입력이나 슬롯 콜백을 남기지 않는다.
            if (_deleteHold != null) { _deleteHold.Confirmed -= DeleteSave; _deleteHold.Configure(null); }
            _deleteHold = null;
            if (_saveSlotsView != null)
            {
                _saveSlotsView.Selected -= SelectSaveSlot;
                _saveSlotsView.Unbind();
            }
            _saveSlotsView = null;
            // 떠나는 메뉴의 열린 슬롯 창을 닫는다. 숨은 메뉴 버튼의 interactable은 다음 갱신이 다시 정하므로 되돌리지 않는다.
            // 설정 모달과 같은 이유로 파괴된 패널의 숨기기 실패는 기록만 한다.
            Cleanup.Run(() => _ui.Modals.Remove(GameModalId.SaveSlots), _ui);
        }

        // 현재 씬의 포인터 연출에 확정된 카메라 설정만 전달한다. 옵션 관리자의 적용 알림이 부른다.
        internal void ApplyOptions(GameOptions options)
        {
            if (_presentation != null)
                _presentation.ReduceMotion = options.ReduceCameraMotion;
        }

        // 메뉴 입력 잠금, 보조 화면 덮개, 메뉴 버튼, 슬롯 창, 저장 안내, 초기 포커스를 갱신한다. 화면 갱신(GameUiController.Refresh)만 부른다.
        internal void Refresh(bool busy, bool showLoading)
        {
            GameRuntime runtime = _ui.Runtime!;
            GameModalStack modals = _ui.Modals;
            // 원본 타이틀이 메뉴를 공개하기 전에는 숨은 메뉴 버튼을 탭이나 엔터로 실행하지 못하게 한다.
            bool menuReady = runtime.SessionState == GameSessionState.Menu && !busy && modals.Count == 0 &&
                (_presentation == null || _presentation.IsMenuOpen);
            // 원본 보조 화면인 설정과 저장 슬롯, 그리고 로딩은 메뉴를 숨긴다. 전환 중이거나 모달이 열려 있으면 메뉴 입력을 막는다.
            // MonoBehaviour라 ?. 대신 Unity null 비교를 쓴다.
            if (_presentation != null)
            {
                bool covered = showLoading || modals.Contains(GameModalId.Options) || modals.Contains(GameModalId.SaveSlots);
                _presentation.SetOverlayState(covered, busy || modals.Count != 0);
            }
            // Bind가 _view, 슬롯 뷰, 문구 바인딩을 함께 연결하고 Unbind가 _view부터 끊으므로 이 null 검사 하나로 충분하다.
            if (_view == null) return;
            // 저장 관리자의 저장 상태 하나로 메뉴 구성을 정한다. 조회 전인 Unknown에서는 새 게임과 이어하기를 모두 숨긴다.
            // 이 갱신은 앱 UI의 바인딩 확인 뒤에만 불려 GameManager가 살아 있다.
            SaveManager saves = GameManager.Saves;
            GameSaveState availability = saves.State;
            bool hasContinuableSave = availability == GameSaveState.Available;
            // 위에서 잡은 저장 관리자로 빈 슬롯 백업 판정을 한 번만 한다.
            bool emptyWithBackup = EmptySlotWithBackup(saves);
            // 새 게임과 백업 복구 버튼이 첫 자리에 겹치므로, 복구 가능 상태에서는 백업 복구만 보여 중복 입력을 막는다.
            bool showNewGame = availability is GameSaveState.Missing or GameSaveState.Error or
                GameSaveState.Corrupt || emptyWithBackup;
            UiVisibility.Set(_view.NewGameButton.gameObject, showNewGame);
            _view.NewGameButton.interactable = menuReady && showNewGame;
            UiVisibility.Set(_view.ContinueButton.gameObject, hasContinuableSave);
            _view.ContinueButton.interactable = menuReady && hasContinuableSave;
            // 빈 슬롯에서도 삭제 위치는 유지하고 입력만 비활성화한다.
            UiVisibility.Set(_view.DeleteSaveButton.gameObject, true);
            _view.DeleteSaveButton.interactable = menuReady && hasContinuableSave;
            _view.QuitButton.interactable = menuReady;
            // 설정도 메뉴의 공통 입력 잠금과 모달 순서를 따른다.
            _view.OptionsButton.interactable = menuReady;
            // 선택 화면은 확정 슬롯 요약만 표시하며 메뉴와 별도의 입력 소유권을 갖는다.
            _saveSlotsView!.OpenButton.interactable = menuReady;
            // 슬롯 창이 열려 있을 때만 여섯 행을 다시 그리고, 닫혀 있으면 열기 버튼의 현재 슬롯 라벨만 갱신한다.
            // 숨은 행은 창을 열 때 OpenSaveSlots가 최신 요약으로 다시 그린다. 요약과 선택 번호는 저장 관리자가 소유한다.
            if (modals.Contains(GameModalId.SaveSlots))
                _saveSlotsView.Render(saves.Summaries, saves.SelectedSlot, busy || _ui.Commands.IsRefreshRunning);
            else _saveSlotsView.RenderLabel(saves.SelectedSlot);
            // 빈 슬롯의 백업 복구는 저장 선택 창에서 제공해 첫 메뉴 버튼과 겹치지 않게 한다.
            bool showRecover = availability == GameSaveState.Recoverable && !emptyWithBackup;
            UiVisibility.Set(_view.RecoverBackupButton.gameObject, showRecover);
            _view.RecoverBackupButton.interactable = menuReady;
            // 원본에 없는 정상 상태 안내는 숨기고 읽기 오류와 복구 가능 상태만 보인다.
            // 사용자가 지운 저장에 남은 정상 백업은 오류로 보이지 않는다.
            TMP_Text status = _view.SaveStatus;
            bool showStatus = !emptyWithBackup && (availability is GameSaveState.Error or GameSaveState.Corrupt or
                GameSaveState.Recoverable);
            UiVisibility.Set(status.gameObject, showStatus);
            // 저장 상태 문구는 메뉴 수명의 번역 바인딩으로 갱신하고, 번역 키는 슬롯 행과 같은 StatusKey로 고른다.
            // 위 _view 검사를 지났으면 바인딩이 있다. 해제된 바인딩은 콜백을 부르지 않으므로 람다는 지역 참조만 잡는다.
            _text!.Set("element.saveStatus", MainMenuSaveSlotsView.StatusKey(availability), value => status.text = value);
            // 갱신이 선택된 메뉴 버튼을 숨기거나 잠가 선택을 잃었으면 초기 포커스를 다시 정한다. 그러지 않으면 키보드와 패드 탐색이 멈춘다.
            // 백업 복구가 성공했거나 조회 결과로 이어하기와 새 게임 버튼이 바뀐 경우가 그렇다.
            if (menuReady && _focused && !UiFocus.CanFocus(UiFocus.Current())) _focused = false;
            if (menuReady && !_focused)
            {
                // 실제로 보이는 첫 세션 버튼에 초기 포커스를 둔다. 이어하기, 새 게임, 백업 복구 순으로 고른다.
                Selectable? initial = hasContinuableSave ? _view.ContinueButton : showNewGame ? _view.NewGameButton :
                    availability == GameSaveState.Recoverable ? _view.RecoverBackupButton : null;
                if (initial != null)
                {
                    UiFocus.TryFocus(initial);
                    _focused = true;
                }
            }
        }

        // 슬롯 창을 닫는 명령에는 게임 시작 부작용을 넣지 않는다. 슬롯 선택이 끝난 명령도 이 경로로 닫는다.
        internal void CloseSaveSlots()
        {
            _ui.Modals.Remove(GameModalId.SaveSlots);
            _ui.Refresh();
        }

        // 주 저장이 없는 슬롯은 사용자가 삭제한 슬롯을 포함해, 백업이 남아 있어도 빈 슬롯으로 보고 새 게임을 허용한다.
        // 새 게임 버튼 표시와 덮어쓰기 확인 생략이 이 판정 하나를 함께 쓴다. 상태와 요약은 넘겨받은 저장 관리자에서 읽는다.
        static bool EmptySlotWithBackup(SaveManager saves) => saves.State == GameSaveState.Recoverable &&
            saves.SelectedSummary is { PrimaryMissing: true };

        // 덮어쓸 주 저장이 없다고 확인된 경우다. 빈 슬롯에 남은 백업은 따로 복구할 수 있게 두고, 새 게임은 막지 않는다.
        static bool SkipsOverwriteConfirm(SaveManager saves) =>
            saves.State == GameSaveState.Missing || EmptySlotWithBackup(saves);

        // 덮어쓸 주 저장이 없다고 확인된 경우에만 덮어쓰기 확인 없이 새 게임을 시작한다.
        void NewGame()
        {
            if (!_ui.CanRun())
                return;

            // 새 게임 버튼 표시와 같은 빈 슬롯 판정을 써서, 백업만 남은 빈 슬롯도 확인 없이 시작한다.
            if (SkipsOverwriteConfirm(GameManager.Saves))
                _ui.Commands.RunAsync(UiCommand.NewGame).Forget();
            // 덮어쓰기 확인도 공용 확인 모달을 덮어쓰기 문구로 연다.
            else _ui.Confirm.Open(UiCommand.NewGame, "overwrite.title", "overwrite.message", "overwrite.confirm");
        }

        // 이어하기는 새 게임 생성 경로로 대체하지 않는다.
        void Continue() => _ui.Commands.RunAsync(UiCommand.Continue).Forget();

        // 삭제 뒤 바뀐 메뉴 구성으로 초기 포커스를 다시 정한다. 길게 누르는 삭제 버튼은 메뉴가 입력을 받을 때만 확정을 내고,
        // RunAsync가 CanRun을 다시 확인하므로 여기서는 검사하지 않는다.
        void DeleteSave()
        {
            _focused = false;
            _ui.Commands.RunAsync(UiCommand.DeleteSave).Forget();
        }

        // 백업 복구는 자동 수행하지 않고 사용자의 명시적 선택을 받는다.
        void RecoverBackup() => _ui.Commands.RunAsync(UiCommand.RecoverBackup).Forget();

        // 원본 슬롯 창은 지금 가진 목록을 먼저 보이고, 새 조회는 따로 실행해 갱신한다.
        // 열기 버튼과 슬롯 View는 메뉴와 함께 연결되므로 _saveSlotsView는 null이 아니다.
        void OpenSaveSlots()
        {
            if (!_ui.CanRun() || _ui.Modals.Contains(GameModalId.SaveSlots))
                return;

            // GameManager의 저장 관리자가 마지막으로 읽은 요약을 그린다.
            SaveManager saves = GameManager.Saves;
            _saveSlotsView!.Render(saves.Summaries, saves.SelectedSlot, false);
            _ui.Modals.Open(GameModalId.SaveSlots, _saveSlotsView.Panel, CloseSaveSlots, _saveSlotsView.OpenButton,
                _saveSlotsView.InitialFocus);
            _ui.Commands.RunAsync(UiCommand.Refresh).Forget();
            _ui.Refresh();
        }

        // 고른 슬롯 번호는 명령 실행기가 기억하고, 선택 변경은 Runtime의 독점 메뉴 작업에 맡긴다.
        void SelectSaveSlot(int slot)
        {
            // 슬롯 버튼은 전환이나 조회 중에 잠기고, RunAsync가 CanRun을 다시 본다.
            // RunAsync는 이 명령에서 조회 중 여부를 보지 않으므로 그 검사만 여기서 한다.
            if (_ui.Commands.IsRefreshRunning) return;
            _ui.Commands.SelectSaveSlotAsync(slot).Forget();
        }
    }
}
