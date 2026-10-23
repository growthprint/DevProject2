#nullable enable annotations
using System;
using System.Threading.Tasks;
using Game.Core;

namespace Game.App
{
    // Runtime의 예약 작업과 옵션 관리자가 함께 쓰는 실행과 대기 도우미다. 모두 메인 스레드에서 시작하고 끝나는 내부 Task를 다룬다.
    internal static class TrackedTask
    {
        // 독점 작업의 본문을 실행하고, 예약을 푼 뒤 알림과 공유 완료를 게시한다.
        // 본문이 기다리는 씬 흐름과 저장 관리자는 메인 스레드에서 끝나므로 여기서 스레드를 다시 바꾸지 않는다.
        internal static async Task RunAsync(TaskCompletionSource<bool> completion, Func<Task> body, Action release, Action publish)
        {
            Exception? failure = null;
            try { await body(); }
            // 예외 경계: 본문 실패를 보관했다가 예약 해제와 알림 뒤 공유 완료로 호출자에게 전달한다.
            catch (Exception error) { failure = error; }
            finally
            {
                release();
                publish();
                completion.TrySetOutcome(failure, true);
            }
        }

        // 앞서 시작한 저장, 조회, 전환, 옵션 적용이 끝나기만 기다린다. 메인 스레드에서 끝나는 내부 Task라 메인으로 돌아온다.
        internal static async Task ObservePreviousAsync(Task? task)
        {
            if (task == null) return;
            try { await task; }
            // 예외 경계: 앞선 작업이 끝나기만 기다리므로 실패는 버린다. 실패 처리는 그 작업의 소유자가 맡는다.
            catch (Exception) { return; }
        }
    }
}
