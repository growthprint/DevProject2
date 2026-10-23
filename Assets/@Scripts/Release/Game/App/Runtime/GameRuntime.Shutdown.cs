#nullable enable annotations
using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Game.Core;

namespace Game.App
{
    // 원작 GameManager처럼 앱 루트가 정상 종료, 저장 없는 종료, 강제 종료와 앱 자원 정리를 직접 맡는다.
    // 여러 종료 요청은 하나의 종료 결과와 1회 정리를 함께 기다린다.
    public partial class GameRuntime
    {
        // 종료 중 설정 저장 실패 경고의 머리말이다. 테스트가 이 문구로 경고를 찾는다.
        internal const string _optionsStopWarning = "설정을 저장하지 못해 마지막 설정 변경 없이 종료합니다: ";

        // 동시에 요청한 종료는 하나의 완료 결과를 공유한다.
        Task? _shutdownTask;
        // 정상 종료 중 강제 종료가 요청되면 저장 없이 정리한다. 취소 토큰 대신 진행 중인 작업이 이 표시(IsAbandoned)를
        // await 뒤에 보고 조용히 멈춘다.
        bool _abortRequested;
        // 저장 없는 정상 종료를 요청받으면 기지에서도 최종 저장을 하지 않는다. 종료는 실행 상태로 되돌아오지 않아 다시 끄지 않는다.
        bool _stopWithoutSaving;
        // 시작 실패와 종료가 겹쳐도 소유 자원을 한 번만 정리하고, 뒤의 정지 요청은 같은 완료를 기다린다.
        // 정리 실패는 기록만 하고 던지지 않는다. 인스턴스 메서드를 넘겨야 해서 생성자가 만든다.
        readonly ReleaseOnce _cleanup;

        // 앱 자원 정리가 시작됐는지 알린다. 입력 차단과 일시정지 어댑터는 그 뒤로 건드리지 않는다.
        // 1회 정리는 본문을 부르기 전에 시작을 표시하므로 정리 콜백이 동기로 재진입해도 참이다.
        internal bool CleanupStarted => _cleanup.Started;

        // 모든 종료 요청이 같은 종료 결과를 기다리게 한다.
        UniTask RequestStop(bool abort, bool withoutSaving = false)
        {
            // 정리는 종료 결과를 게시하기 전에 Stopped를 세운다. 그 뒤의 요청도 진행 중인 종료 결과나 1회 정리 완료를 기다린다.
            if (State == GameRuntimeState.Stopped)
                return _shutdownTask != null ? _shutdownTask.AsUniTaskOnCurrentContext() : _cleanup.ReleaseAsync();

            // 저장 없는 정상 종료 요청은 정지되지 않은 Runtime에서만 표시한다.
            if (withoutSaving)
                _stopWithoutSaving = true;

            TaskCompletionSource<bool>? completion = null;
            Task? operation = null;
            Task? save = null;
            Task? query = null;
            if (_shutdownTask == null)
            {
                State = GameRuntimeState.Stopping;
                completion = SharedCompletion.Create<bool>();
                _shutdownTask = completion.Task;
                operation = Operations.Current;
                // 기지 씬 자동 저장의 대기 루프만 멈추고, 이미 시작한 저장과 조회는 끝까지 기다린다.
                // 루트가 파괴된 뒤 늦게 온 정지 요청이면 저장 관리자도 함께 사라졌으므로 읽지 않는다.
                BaseAutoSaver.CancelCurrentLoop();
                if (GameManager.HasInstance)
                {
                    save = GameManager.Saves.CurrentSave;
                    query = GameManager.Saves.QueryTask;
                }
            }
            // 재진입한 요청도 같은 종료 결과를 기다리도록 지금의 공유 결과를 잡아 둔다.
            Task shared = _shutdownTask;
            // 정상 종료는 진행 중인 독점 작업이 끝나기를 기다린다. 강제 종료는 표시를 세우고, 작업은 다음 await 뒤에 멈춘다.
            // 앱 종료에서는 첫 await 뒤의 정리가 씬 객체가 파괴된 뒤에 돌거나 아예 돌지 않는다. 그래서 첫 강제 종료 요청이 현재 레벨을
            // 바로 끝내, 씬 컨트롤러가 GameManager와 앱 수명 구독을 모두 살아 있는 지금 놓게 한다. 씬 흐름이 나중에 다시 끝내도 무동작이다.
            // 세션 관문은 아래 Publish가 닫으므로 레벨 종료 구독자는 세션 명령을 내지 않는다.
            if (abort && !_abortRequested)
            {
                _abortRequested = true;
                Cleanup.Run(SceneContext.EndCurrentLevel, this);
            }

            // 종료 요청보다 먼저 시작한 저장 조회의 늦은 결과는 게시하지 않는다.
            // 루트가 이미 파괴됐으면 게시할 저장 관리자도 함께 사라졌으므로 건너뛴다.
            if (GameManager.HasInstance) GameManager.Saves.InvalidateStatus();
            Publish();
            if (completion != null) _ = StopCoreAsync(operation, save, query, completion);
            return shared.AsUniTaskOnCurrentContext();
        }

        // 1회 정리는 던지지 않고 완료를 공유한다. 시작 실패도 이 경로로 그때까지 등록한 앱 자원을 정리한다.
        internal UniTask CleanupAsync() => _cleanup.ReleaseAsync();

        // 앞선 작업 대기, 설정 저장, 공통 파일 쓰기 대기, 편집 초안 폐기, 최종 저장, 정리 순서로 종료하고 결과를 한 번 게시한다.
        // 각 단계는 실패를 스스로 기록하고 던지지 않으므로, 종료는 실행 상태로 돌아가지 않고 늘 정리까지 간다.
        async Task StopCoreAsync(Task? operation, Task? save, Task? query, TaskCompletionSource<bool> completion)
        {
            Exception? failure = null;
            try
            {
                // 종료하는 동안 플레이를 멈춘다. 정리가 이미 시작됐으면 해제 중인 일시정지를 다시 건드리지 않는다.
                // 구성 전에 온 종료에는 일시정지 계약이 없어 참조만 확인한다. 파괴된 구현은 해제 뒤 요청을 무시하므로 Unity 파괴 확인이 필요 없다.
                if (!CleanupStarted && _pause != null) _pause.SetPaused(true);
                // 종료 예약 전에 시작한 전환, 저장, 조회가 끝나기를 기다린다. 그 실패는 각 작업의 소유자가 처리한다.
                await TrackedTask.ObservePreviousAsync(operation);
                await TrackedTask.ObservePreviousAsync(save);
                await TrackedTask.ObservePreviousAsync(query);
                await FlushOptionsAsync();
                // 공통 파일은 확정 때마다 바로 쓰므로 종료 전에 남은 쓰기가 끝나야 마지막 해금을 잃지 않는다.
                await FlushGlobalProgressAsync();
                await DiscardEditingAsync();
                await SaveFinalSessionAsync();
                // 1회 정리(ReleaseOnce)는 던지지 않으므로 직접 기다린다.
                await CleanupAsync();
            }
            // 예외 경계: 아무도 직접 기다리지 않는 종료 작업의 뿌리라, 예상 밖 실패를 보관했다가 종료 결과로 전달한다.
            catch (Exception error) { failure = error; }
            Publish();
            completion.TrySetOutcome(failure, true);
        }

        // 종료 전 설정과 공통 파일 쓰기 대기를 건너뛸 조건이다. 강제 종료 중이거나 정리가 이미 시작됐으면 다시 쓰지 않고,
        // 루트가 파괴됐으면 두 소유자도 함께 사라졌으므로 건너뛴다.
        bool SkipsStopFlush => _abortRequested || CleanupStarted || !GameManager.HasInstance;

        // 남은 설정 쓰기를 모두 기다린다. 설정 저장 실패는 종료를 막지 않고 경고 한 번만 남긴다. 구성 전이면 남은 쓰기가 없어 바로 끝난다.
        async Task FlushOptionsAsync()
        {
            if (SkipsStopFlush) return;
            try { await GameManager.Options.FlushAsync(); }
            // 예외 경계: 종료 전 설정 저장 실패는 경고로 한 번 남기고 종료를 계속한다. 강제 종료가 합류했으면 남기지 않는다.
            catch (Exception error)
            {
                if (!_abortRequested)
                    Log.Instance.Warning(_optionsStopWarning + error);
            }
        }

        // 남은 공통 파일 쓰기를 기다린다. 설정 저장과 같은 조건으로 건너뛰고, 구성 전이면 남은 쓰기가 없어 바로 끝난다.
        // 소유자 연결은 프리팹 검사가 맡아 준비 여부를 따로 보지 않는다. 쓰기 실패는 GlobalProgress가 이미 경고하고
        // 완료는 실패하지 않으므로 예외 경계를 두지 않는다.
        async Task FlushGlobalProgressAsync()
        {
            if (SkipsStopFlush) return;
            await GameManager.GlobalProgress.FlushAsync();
        }

        // 정상 종료 전에 편집 초안을 버리고 편집 공간을 닫는다. 강제 종료는 편집 표시만 끄고, 공간은 앱 자원 정리가 닫는다.
        // 편집 종료는 던지지 않으므로 실패 복구 없이 바로 기다린다. 루트가 파괴됐으면 편집 흐름도 함께 사라졌으므로 건너뛰고,
        // 흐름은 한 번만 찾아 잡아 둔 참조로 끝까지 쓴다.
        async Task DiscardEditingAsync()
        {
            if (!GameManager.HasInstance) return;
            CustomizationFlow customization = GameManager.Customization;
            if (!customization.IsEditing) return;
            if (_abortRequested)
            {
                customization.EndEditLifetime();
                return;
            }
            await customization.DiscardForStopAsync();
        }

        // 정상 종료 전에 기지 세션을 최종 저장한다. 기지 밖이거나 저장 없는 종료면 쓰지 않고,
        // 루트가 파괴됐으면 저장 관리자도 함께 사라졌으므로 건너뛴다. 원본 종료처럼 저장 결과와 관계없이 종료를 계속하므로 던지지 않는다.
        async Task SaveFinalSessionAsync()
        {
            GameSessionManager? session = _session;
            if (_abortRequested || session == null || !GameManager.HasInstance) return;
            // 저장 여부는 지금 확정된 세션의 씬으로 판정한다. 플레이가 멈춰 있어 판정과 저장 사이에 세션이 바뀌지 않는다.
            if (_stopWithoutSaving || !SavePointPolicy.SavesOnMenuOrQuit(session.Snapshot().SceneId)) return;
            // 쓰기 실패는 저장 관리자가 한 번 기록하고 false로 돌려주므로 결과를 보지 않는다.
            // StopCoreAsync가 이미 플레이를 멈췄으므로 다시 멈추지 않는다.
            try { await GameManager.Saves.SaveAtPointAsync(); }
            // 예외 경계: 던지면 종료가 정리를 건너뛰므로, 예상 밖 실패는 한 번 기록하고 종료를 계속한다.
            catch (Exception error)
            {
                Log.Instance.Exception(error, this);
            }
        }

        // 앱 자원을 등록 역순으로 해제한 뒤 Runtime 상태를 초기화한다. 등록 순서는 ComposeAsync가 정한다.
        // 해제는 한 단계가 실패해도 끝까지 가고 실패는 기록만 한다.
        async UniTask CleanupCoreAsync()
        {
            // 저장 관리자의 정적 알림은 Runtime보다 오래 살므로 가장 먼저 끊어, 정리 중이나 정지 뒤의 저장과 불러오기가
            // 이 Runtime의 세션에 닿지 않게 한다. 시작 도중 실패해 구독하지 않았으면 끊어도 아무 일이 없다.
            SaveManager.CollectSaveData -= CollectHubSections;
            SaveManager.FileLoaded -= LoadHubSections;
            await AppResources.ReleaseAsync();
            // 앱 자원 해제가 끝난 뒤에만 늦게 해제될 입력 차단 수를 0으로 리셋한다.
            GameplayInput.Reset();
            ReplaceSession(null);
            SessionState = GameSessionState.None;
            State = GameRuntimeState.Stopped;
            // 정지한 Runtime을 늦은 구독자가 시작된 것으로 보지 않게 시작 완료 표시를 내린다.
            ClearInitialized();
        }
    }
}
