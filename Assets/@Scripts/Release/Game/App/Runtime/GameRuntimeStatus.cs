#nullable enable annotations

// 앱과 세션의 상태 값이다. GameRuntime.StateChanged는 값 없이 신호만 보내므로 UI는 State와 SessionState를 직접 읽는다.
namespace Game.App
{
    // 앱 수명은 반복 가능한 플레이 세션 수명과 별도로 관리한다.
    public enum GameRuntimeState { Created, Starting, Running, Stopping, Stopped }

    // 반복 가능한 플레이 세션의 상태다. 전환 실패는 기록만 하고 진행하므로 따로 실패 상태를 두지 않는다.
    // Closure는 사망, 탈출, 튜토리얼 완료를 확정한 뒤 결과 화면을 보여 주는 상태로, 결과 화면의 '계속'만 받는다.
    public enum GameSessionState { None, Menu, Starting, Playing, Paused, Returning, Customizing, Closure }
}
