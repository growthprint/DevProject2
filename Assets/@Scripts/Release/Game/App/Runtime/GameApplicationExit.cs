#nullable enable annotations
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game.App
{
    // UI 종료 명령(UiCommandRunner)이 쓰는 종료 정책이다. Player 종료와 Editor 플레이 종료의 차이는 이곳에서만 다룬다.
    public static class GameApplicationExit
    {
        // Editor에서는 플레이를 끝내고 Player에서는 실제 프로세스 종료를 요청한다.
        public static void Request()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // 저장하고 종료한다. 원작처럼 저장 실패는 Runtime이 기록만 하고 종료를 막지 않는다. 정리가 끝나 정지한 뒤의 오류도 종료를 막지 않는다.
        public static UniTask ShutdownAndExitAsync(GameRuntime runtime) =>
            ShutdownAndExitAsync(runtime, Request);

        // 테스트가 실제 프로세스를 끝내지 않고 실패 정책을 검증하도록 종료 요청을 주입받는다.
        internal static UniTask ShutdownAndExitAsync(GameRuntime runtime, System.Action requestExit) =>
            StopThenExitAsync(runtime, requestExit, true);

        // 저장 없이 종료한다. 기지 밖에서 나가기를 확인한 경우다.
        // 이때도 정리가 끝나 정지했을 때만 실제 종료를 요청한다.
        public static UniTask ShutdownWithoutSavingAndExitAsync(GameRuntime runtime) =>
            ShutdownWithoutSavingAndExitAsync(runtime, Request);

        // 테스트가 실제 프로세스를 끝내지 않고 실패 정책을 검증하도록 종료 요청을 주입받는다.
        internal static UniTask ShutdownWithoutSavingAndExitAsync(GameRuntime runtime, System.Action requestExit) =>
            StopThenExitAsync(runtime, requestExit, false);

        // 두 종료의 공통 본문이다. 런타임 종료가 메인 스레드로 돌아오므로 스레드를 전환하지 않는다.
        static async UniTask StopThenExitAsync(GameRuntime runtime, System.Action requestExit, bool saving)
        {
            try { await (saving ? runtime.ShutdownAsync() : runtime.ShutdownWithoutSavingAsync()); }
            finally
            {
                if (runtime.State == GameRuntimeState.Stopped) requestExit();
            }
        }

        // 복구할 수 없는 상태의 종료는 저장 없이 정리를 기다리고, 정리가 실패해도 실제 종료를 요청한 뒤 실패를 전달한다.
        public static UniTask AbortAndExitAsync(GameRuntime runtime) =>
            AbortAndExitAsync(runtime, Request);

        // 테스트가 실제 프로세스를 끝내지 않고 실패 정책을 검증하도록 종료 요청을 주입받는다.
        internal static async UniTask AbortAndExitAsync(GameRuntime runtime, System.Action requestExit)
        {
            try { await runtime.AbortAsync(); }
            finally { requestExit(); }
        }
    }
}
