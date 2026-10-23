#nullable enable annotations
using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace Game.Core
{
    // 여러 대기자가 함께 기다리는 TaskCompletionSource를 만들고, 결과를 게시하고, UniTask로 기다리는 규칙을 한곳에 둔다.
    public static class SharedCompletion
    {
        // 결과를 게시하는 쪽의 스택에서 대기자의 이어지는 코드가 바로 돌지 않도록 연속 작업을 비동기로 실행한다.
        public static TaskCompletionSource<T> Create<T>() =>
            new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Unity 메인 스레드처럼 동기화 문맥이 있으면 그 문맥에서 대기자를 재개한다. 워커나 순수 테스트처럼 문맥이 없으면
        // FromCurrentSynchronizationContext 예외를 피해 현재 스케줄러에서 재개한다.
        public static UniTask AsUniTaskOnCurrentContext(this Task task) =>
            task.AsUniTask(useCurrentSynchronizationContext: SynchronizationContext.Current != null);

        // 결과가 있는 작업도 같은 문맥 규칙으로 기다린다.
        public static UniTask<T> AsUniTaskOnCurrentContext<T>(this Task<T> task) =>
            task.AsUniTask(useCurrentSynchronizationContext: SynchronizationContext.Current != null);

        // 실패가 없으면 결과를, 실패면 예외를 게시한다. 취소 토큰을 쓰지 않으므로 취소 결과는 없다.
        public static bool TrySetOutcome<T>(this TaskCompletionSource<T> completion, Exception? failure, T result)
        {
            return failure == null ? completion.TrySetResult(result) : completion.TrySetException(failure);
        }
    }
}
