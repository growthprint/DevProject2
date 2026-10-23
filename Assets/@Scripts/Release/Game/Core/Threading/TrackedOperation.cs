#nullable enable annotations
using System;
using System.Threading.Tasks;

namespace Game.Core
{
    // 한 번에 하나만 도는 비동기 작업의 공유 완료를 소유한다. 메인 스레드 전용이라 잠금이 없다.
    // 본문보다 완료를 먼저 게시해 재진입 호출자도 같은 Task를 기다리고, 끝날 때는 자리 해제, 종료 알림, 결과 게시 순서를 지킨다.
    // 원본처럼 취소 토큰은 쓰지 않는다. 소유자가 사라졌는지는 본문이 await 뒤에 직접 확인한다.
    public sealed class TrackedOperation
    {
        // 진행 중인 실행의 공유 완료와 실행 여부다. 유휴 상태면 Current가 null이고, 종료 알림이 불릴 때는 이미 비어 있다.
        public Task? Current { get; private set; }
        public bool IsRunning => Current != null;

        // 중복 시작은 호출자 결함이라 보고만 한다. 에디터와 DEBUG는 그 예외를 던지고, 릴리즈는 진행 중인 완료를 돌려주어 자리를 덮지 않는다.
        // started는 자리를 잡은 뒤 본문보다 먼저 부른다. 본문의 실패는 돌려준 Task로 전달한다.
        public Task Start(Func<Task> body, Action? started = null, Action<Exception?>? finished = null)
        {
            Task? running = Current;
            if (running != null)
            {
                Log.Instance.Fail(new InvalidOperationException("이미 실행 중인 작업이 있습니다."));
                return running;
            }
            TaskCompletionSource<bool> completion = SharedCompletion.Create<bool>();
            // 무엇이든 실행하기 전에 자리를 잡아, started 안에서 재진입한 호출자도 실행 중으로 보고 같은 완료를 기다리게 한다.
            Current = completion.Task;
            if (started != null)
            {
                try
                {
                    started();
                }
                // 예외 경계: 시작 알림이 실패하면 본문 없이 자리를 비우고, 같은 완료를 기다리던 재진입 호출자에게도 실패를 게시한 뒤 다시 던진다.
                catch (Exception error)
                {
                    Current = null;
                    completion.TrySetOutcome(error, true);
                    throw;
                }
            }
            _ = RunAsync(body, completion, finished);
            return completion.Task;
        }

        // 아무도 기다리지 않는 실행 뿌리라 예외를 밖으로 내지 않고, 어떤 경우에도 공유 완료를 게시한다.
        // 본문의 연속 작업은 시작한 메인 스레드로 돌아오므로 여기서 스레드를 바꾸지 않는다.
        async Task RunAsync(Func<Task> body, TaskCompletionSource<bool> completion, Action<Exception?>? finished)
        {
            Exception? failure = null;
            try
            {
                await body();
            }
            // 예외 경계: 본문 실패를 보관했다가 자리 해제와 종료 알림 뒤 공유 완료로 호출자에게 전달한다.
            catch (Exception error)
            {
                failure = error;
            }

            // 종료 알림 안에서 다음 실행을 시작할 수 있도록 알림보다 먼저 자리를 비운다.
            // 중복 시작은 자리를 덮지 않으므로 이 시점의 자리는 항상 이 실행의 것이다.
            Current = null;
            if (finished != null)
            {
                try
                {
                    finished(failure);
                }
                // 예외 경계: 종료 알림의 실패는 한 번 기록만 하고, 대기자가 멈추지 않도록 공유 완료는 그대로 게시한다.
                catch (Exception error)
                {
                    Log.Instance.Exception(error);
                }
            }
            completion.TrySetOutcome(failure, true);
        }
    }
}
