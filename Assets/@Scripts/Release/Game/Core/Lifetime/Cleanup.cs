#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;

namespace Game.Core
{
    // 여러 단계로 정리할 때 한 단계의 실패가 나머지 단계를 막지 않게 한다. 실패는 여기서 한 번 기록하므로
    // 호출자는 단계마다 Run이나 RunAsync를 부르고 결과를 따로 모으지 않는다.
    public static class Cleanup
    {
        // 동기 정리 단계의 실패와 취소는 기록만 하고 돌아온다. 성공하면 true다.
        public static bool Run(Action step, UnityEngine.Object? context = null)
        {
            try
            {
                step();
                return true;
            }
            // 예외 경계: 정리 단계 실패를 기록하고 다음 단계를 계속한다.
            catch (Exception error)
            {
                Log.Instance.Exception(error, context);
                return false;
            }
        }

        // 비동기 정리 단계를 기다린다. 시작하자마자 던지거나 기다리는 중에 실패하거나 취소돼도 기록만 하고 돌아온다. 성공하면 true다.
        public static async UniTask<bool> RunAsync(Func<UniTask> step, UnityEngine.Object? context = null)
        {
            try
            {
                await step();
                return true;
            }
            // 예외 경계: 정리 단계 실패를 기록하고 다음 단계를 계속한다.
            catch (Exception error)
            {
                Log.Instance.Exception(error, context);
                return false;
            }
        }
    }
}
