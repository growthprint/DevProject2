#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;
using Game.Core;
using Object = UnityEngine.Object;

namespace Game.App
{
    // 씬 명령의 결과다. Rejected는 예외를 여기서 한 번 기록했거나 명령이 false로 거절한 경우다.
    // 실패는 기록만 하고 되돌리거나 다시 시도하지 않는다. 취소 토큰을 쓰지 않으므로 취소 결과는 없다.
    internal enum SceneCommandResult { Completed, Rejected }

    // 씬 입력이 시작한 Runtime 명령의 예외 처리를 한곳에 둔다. 연산은 Runtime이 소유하고,
    // 씬 전환 명령은 시작한 씬을 언로드한 뒤에도 끝까지 이어진다.
    internal static class SceneCommand
    {
        // bool을 돌려주는 캠페인 명령용이다. 세션이 요청을 거절하면 명령이 false를 돌려준다.
        // false는 세션이 이미 기록했거나 기록할 필요 없는 규칙 거절이므로, 다시 기록하지 않고 Rejected로 돌려준다.
        // 예외는 아래 오버로드가 처리한다. 블록 본문 람다라서 Func<UniTask> 오버로드로 연결된다.
        internal static async UniTask<SceneCommandResult> TryRunAsync(Func<UniTask<bool>> command, Object? context)
        {
            bool accepted = true;
            // 예외 처리 결과에 세션 거절을 덧붙인다.
            SceneCommandResult runResult = await TryRunAsync(async () => { accepted = await command(); }, context);
            return runResult == SceneCommandResult.Completed && !accepted ? SceneCommandResult.Rejected : runResult;
        }

        // 실패는 context를 붙여 한 번 기록한다.
        internal static async UniTask<SceneCommandResult> TryRunAsync(Func<UniTask> command, Object? context)
        {
            // 대리자는 내부 호출자의 람다만 넘어오므로 null을 검사하지 않는다.
            try
            {
                await command();
                return SceneCommandResult.Completed;
            }
            // 예외 경계: 씬 입력 명령의 실패가 입력 콜백 밖으로 나가지 않도록 한 번 기록하고 Rejected로 돌려준다.
            catch (Exception error)
            {
                Log.Instance.Exception(error, context);
                return SceneCommandResult.Rejected;
            }
        }
    }
}
