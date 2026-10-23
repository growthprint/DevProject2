#nullable enable annotations
using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace Game.Core
{
    // 소유자의 정리를 한 번만 실행하고 모든 호출자에게 같은 완료를 돌려준다. 정리 실패는 기록만 하고 던지지 않는다.
    // 메인 스레드 전용이라 잠금이 없다. 대기자는 SharedCompletion 규칙에 따라 나중에 메인 스레드에서 재개한다.
    public sealed class ReleaseOnce : IAsyncReleasable
    {
        readonly Func<UniTask> _release;
        // 정리 실패 로그에 붙일 소유 객체다. 없어도 된다.
        readonly UnityEngine.Object? _context;
        // 최초 호출이 만든 공유 완료다. null이면 아직 시작하지 않았다.
        Task? _completion;

        public ReleaseOnce(Func<UniTask> release, UnityEngine.Object? context = null)
        {
            _release = release!;
            _context = context;
        }

        // 해제를 시작했는지 알려 준다. 소유자가 늦은 요청을 거부할 때 쓴다.
        public bool Started => _completion != null;

        // 공유 완료를 먼저 저장한 뒤 정리를 시작하므로, 정리 도중 다시 불려도 같은 완료를 기다린다.
        // 정리가 동기로 끝났으면 연속 작업을 예약하지 않고 바로 완료를 돌려준다.
        public UniTask ReleaseAsync()
        {
            if (_completion == null)
            {
                TaskCompletionSource<bool> source = SharedCompletion.Create<bool>();
                _completion = source.Task;
                RunAsync(source).Forget();
            }
            return _completion.Status == TaskStatus.RanToCompletion
                ? UniTask.CompletedTask
                : _completion.AsUniTaskOnCurrentContext();
        }

        // 해제는 던지지 않는 계약이라 실패와 취소는 Cleanup 규칙대로 한 번 기록하고, 결과와 관계없이 성공으로 완료한다.
        // 정리가 동기로 끝나면 Cleanup.RunAsync도 완료된 채 돌아와 완료가 동기로 정해진다.
        async UniTaskVoid RunAsync(TaskCompletionSource<bool> source)
        {
            await Cleanup.RunAsync(_release, _context);
            source.TrySetResult(true);
        }
    }
}
