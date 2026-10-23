#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;
using Game.Core;

namespace Game.App
{
    // 앱 공통 UI 명령 하나를 Runtime이나 GameManager의 관리자에 보내고 실패를 한 곳에서 안내한다. 저장 상태 조회는 전환과 따로 표시해 다른 명령을 막지 않는다.
    // 늦게 끝난 결과는 같은 바인딩과 같은 명령 세대일 때만 화면에 반영한다. 메뉴의 첫 저장 조회를 다음 UI 틱으로 미루는 일도 맡는다.
    internal sealed class UiCommandRunner
    {
        readonly GameUiController _ui;
        // 저장 상태 조회 중인지 나타낸다. 조회 중 표시는 따로 두어 모든 명령을 막는 잠금으로 쓰지 않는다.
        bool _refreshRunning;
        // 다음 PlayerLoop에 예약한 저장 상태 조회가 바인딩마다 하나만 있게 한다.
        bool _refreshScheduled;
        // 메뉴 준비와 런타임 시작이 겹치지 않게 저장 조회를 다음 UI 틱으로 미뤄 둔 요청이다. 조회를 예약할 때와 바인딩을 끊을 때 내린다.
        bool _saveRefreshRequested;
        // 메뉴에서 고른 저장 슬롯 번호다. 슬롯 선택이 실패해 오류 창에서 재시도하면 이 슬롯으로 다시 시도한다.
        int _pendingSaveSlot = 1;
        // 진행 중인 UI 전환 명령이다. 메뉴 파일 작업 여부와 로딩 문구 키가 이 명령 하나로 정해지므로 따로 저장하지 않는다.
        // 명령이 끝나면 비워, 씬 컨트롤러가 시작한 전환에는 기본 문구가 나오게 한다.
        UiCommand? _transition;
        // 명령 세대 번호다. 새 전환 뒤 늦게 도착한 저장이나 조회 결과가 최신 화면을 덮지 않게 한다.
        long _generation;

        internal UiCommandRunner(GameUiController ui) => _ui = ui;

        internal bool IsRefreshRunning => _refreshRunning;
        // 메뉴 파일 작업은 입력만 잠그고 화면 전환 덮개를 표시하지 않는다.
        internal bool IsMenuFileOperation => _transition is UiCommand.DeleteSave or UiCommand.SelectSaveSlot or UiCommand.RecoverBackup;
        // UI 명령이 정한 로딩 문구 키다. 진행 중인 전환 명령이 없으면 null이다.
        internal string? LoadingKey => _transition.HasValue ? UiCommandText.LoadingKey(_transition.Value) : null;

        // 끝난 바인딩의 명령은 finally에서 상태를 풀지 못하므로 바인딩을 끊을 때 조회 표시와 진행 중인 전환 명령을 여기서 푼다.
        // 세대를 올려, 같은 인스턴스를 다시 바인딩해도 늦게 끝난 명령의 결과를 적용하지 않는다. 끝난 메뉴의 조회 요청도 남기지 않는다.
        internal void Reset()
        {
            _refreshRunning = false;
            _transition = null;
            ++_generation;
            _saveRefreshRequested = false;
        }

        // 메뉴가 연결될 때 첫 저장 조회를 요청한다. 실제 조회는 다음 화면 갱신의 ScheduleSaveRefresh가 다음 UI 틱으로 예약한다.
        internal void RequestSaveRefresh() => _saveRefreshRequested = true;

        // 고른 슬롯 번호를 기억한 뒤 슬롯 선택 명령을 실행한다. 진입 거절도 RunAsync가 맡는다.
        internal UniTask SelectSaveSlotAsync(int slot)
        {
            _pendingSaveSlot = slot;
            return RunAsync(UiCommand.SelectSaveSlot);
        }

        // 바인딩을 끊으면 예약한 조회 표시를 먼저 내린다. 예약된 대기는 취소하지 않고, 대기 뒤 IsBoundTo가 끝난 바인딩의 예약을 거른다.
        internal void CancelScheduledRefresh() => _refreshScheduled = false;

        // 명령 하나를 실행한다. 진입 조건은 CanRun이고, 전환 명령은 화면을 갱신하기 전에 세대 번호와 진행 중인 전환 명령을
        // 정한다. finally가 같은 세대일 때만 되돌린다.
        internal async UniTask RunAsync(UiCommand command)
        {
            // CanRun은 수명 객체가 있는지로 바인딩을 판정하므로 통과한 뒤 수명은 null이 아니다.
            if (!_ui.CanRun())
                return;

            if (command == UiCommand.Refresh && _refreshRunning)
                return;

            object lifetime = _ui.Lifetime!;
            bool transition = command != UiCommand.Refresh;
            if (transition)
            {
                ++_generation;
                _transition = command;
            }
            else _refreshRunning = true;

            long generation = _generation;
            try
            {
                // Forget으로 부르는 작업이라 화면 갱신도 예외 경계 안에서 한다. 갱신이 실패해도 finally가 조회와 로딩 표시를 푼다.
                _ui.Refresh();
                await ExecuteAsync(command, lifetime, generation);
            }
            // 예외 경계: Runtime 쪽 소유자가 실패 상태와 정리를 마치고 넘긴 예외를 UI 명령 경계에서 받는다.
            catch (Exception error)
            {
                // 예외 원문은 여기서 한 번만 로그에 남기고, 사용자에게는 번역된 안내 문구만 보인다.
                // 저장 손상은 슬롯을 읽은 저장 관리자가 이미 한 번 보고했으므로 같은 슬롯을 다시 기록하지 않는다.
                if (SaveManager.Classify(error) != GameSaveState.Corrupt) Log.Instance.Exception(error);
                // 종료 정리가 끝난 뒤의 오류면 GameApplicationExit가 이미 실제 종료를 요청했으므로 치명 오류 창 없이 끝낸다.
                if (IsQuitAfterStop(command, _ui.Runtime))
                    return;
                // Try* 메서드가 거절했을 때와 같은 경로로 실패를 안내한다.
                ShowFailure(command, lifetime, generation);
            }
            finally
            {
                if (_ui.IsBoundTo(lifetime))
                {
                    if (!transition) _refreshRunning = false;
                    // 늦게 끝난 명령이 그 뒤에 시작한 명령의 표시를 지우지 않도록 같은 세대일 때만 푼다.
                    if (transition && generation == _generation) _transition = null;
                    _ui.Refresh();
                }
            }
        }

        // 정상 종료나 저장 없는 종료가 Runtime 정지까지 마친 뒤 실패했는지 판정한다.
        // Runtime은 MonoBehaviour라 ?. 대신 Unity null 비교로 파괴 여부까지 확인한다.
        static bool IsQuitAfterStop(UiCommand command, GameRuntime? runtime) =>
            (command == UiCommand.Quit || command == UiCommand.QuitWithoutSaving)
            && runtime != null && runtime.State == GameRuntimeState.Stopped;

        // 명령별 실행부다. 예외는 잡지 않고 RunAsync의 예외 경계로 넘긴다.
        async UniTask ExecuteAsync(UiCommand command, object lifetime, long generation)
        {
            GameRuntime runtime = _ui.Runtime!;
            switch (command)
            {
            case UiCommand.NewGame: await runtime.NewGameAsync(); break;
            case UiCommand.Continue: await runtime.ContinueAsync(); break;
            // 슬롯을 고르면 슬롯 창만 닫고 메뉴에 남는다. 저장된 게임은 아직 시작하지 않는다. 슬롯은 마지막으로 고른 번호다.
            case UiCommand.SelectSaveSlot:
                await GameManager.Saves.SelectAsync(_pendingSaveSlot);
                if (_ui.IsBoundTo(lifetime)) _ui.Menu.CloseSaveSlots();
                break;
            // 파일 삭제와 상태 확정은 저장 관리자가 한 번에 하나씩 처리하는 메뉴 명령에 맡긴다.
            case UiCommand.DeleteSave: await GameManager.Saves.DeleteAsync(); break;
            case UiCommand.MainMenu: await runtime.ReturnToMenuAsync(); break;
            // 기지 밖에서 나가기를 확인한 메뉴 복귀라 저장하지 않는다.
            case UiCommand.MainMenuWithoutSaving: await runtime.ReturnToMenuWithoutSavingAsync(); break;
            // 외형 편집의 진입과 취소는 앱 수명 편집 흐름이 맡으므로 GameManager에서 읽는다.
            case UiCommand.Customize: await GameManager.Customization.OpenAsync(); break;
            case UiCommand.CancelCustomization: await GameManager.Customization.CancelAsync(); break;
            // 백업 복구도 저장 관리자의 메뉴 명령이다.
            case UiCommand.RecoverBackup: await GameManager.Saves.RecoverBackupAsync(); break;
            // 정상 종료는 GameApplicationExit가 정리한 뒤 실제 종료를 요청한다. 설정 저장 실패는 경고만 한다.
            case UiCommand.Quit:
                await GameApplicationExit.ShutdownAndExitAsync(runtime);
                break;
            // 저장 없는 종료도 같은 정리 뒤 실제 종료를 요청한다.
            case UiCommand.QuitWithoutSaving:
                await GameApplicationExit.ShutdownWithoutSavingAndExitAsync(runtime);
                break;
            // 복구할 수 없는 상태의 종료다. 저장 없이 정리를 기다리고, 정리가 실패해도 실제 종료를 요청한다.
            case UiCommand.AbortQuit:
                await GameApplicationExit.AbortAndExitAsync(runtime);
                break;
            default:
                // 조회 실패는 예외 대신 GameSaveState.Error 상태로 온다. 예상 밖 예외는 저장 관리자가 이미 기록했으니 안내만 띄운다.
                // 조회를 기다리는 동안 앱이 사라질 수 있어, 결과는 대기 전에 잡은 저장 관리자에서 읽는다.
                SaveManager saves = GameManager.Saves;
                await saves.RefreshStatusAsync();
                if (saves.State == GameSaveState.Error) ShowFailure(command, lifetime, generation);
                break;
            }
        }

        // 비동기 예외와 Try* 거절의 실패 안내를 한 곳에서 띄운다. 늦게 온 결과는 같은 바인딩과 같은 명령 세대일 때만 띄운다.
        void ShowFailure(UiCommand command, object lifetime, long generation)
        {
            if (!_ui.IsBoundTo(lifetime) || generation != _generation) return;
            _ui.Errors.ShowCommandFailure(command);
        }

        // 메뉴 준비 알림 안에서 다시 들어가지 않도록 저장 조회를 다음 프레임으로 미루고, 조회 중에도 메뉴 입력은 잠그지 않는다.
        // 호출자 GameUiController.Refresh가 바인딩을 확인하고 Bind가 그 전에 Runtime과 수명 토큰을 넣으므로 null 검사가 없다.
        internal void ScheduleSaveRefresh()
        {
            GameRuntime runtime = _ui.Runtime!;
            if (!_saveRefreshRequested || _refreshScheduled || !CanQuerySaves(runtime))
                return;

            _refreshScheduled = true;
            RefreshSaveNextFrameAsync(_ui.Lifetime!).Forget();
        }

        // 저장 조회는 전환이 없고 다른 조회가 돌지 않는 메뉴 상태에서만 한다. 예약할 때와 예약한 틱에서 같은 조건을 본다.
        bool CanQuerySaves(GameRuntime runtime) =>
            !runtime.IsBusy && !_refreshRunning && runtime.SessionState == GameSessionState.Menu;

        // 현재 바인딩으로 다음 PlayerLoop(Update)의 저장 조회를 예약한다. 그사이 바인딩이 끝났으면 IsBoundTo가 거른다.
        async UniTask RefreshSaveNextFrameAsync(object lifetime)
        {
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
                // Runtime은 Configure 뒤 비워지지 않으므로 바인딩과 수명만 다시 확인한다.
                if (!_ui.IsBoundTo(lifetime) || !CanQuerySaves(_ui.Runtime!))
                    return;

                _saveRefreshRequested = false;
                RunAsync(UiCommand.Refresh).Forget();
            }
            // 예외 경계: Forget으로 시작한 비동기 루트라 예상하지 못한 실패를 잃지 않도록 기록하고 끝낸다.
            catch (Exception error) { Log.Instance.Exception(error, _ui); }
            finally
            {
                if (ReferenceEquals(_ui.Lifetime, lifetime))
                    _refreshScheduled = false;
            }
        }
    }
}
