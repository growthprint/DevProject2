#nullable enable annotations
using System;

namespace Game.App
{
    // 원작 View처럼 열린 동안 게임플레이 입력을 막는 화면의 공통 바탕이다. 이 바탕은 입력 차단 핸들의 수명과 열기 앞부분만 맡는다.
    // 구독, 창 숨기기, 자동 저장 알림의 순서는 화면마다 달라 각 화면의 Show와 Close가 정한다.
    // GameplayScreens는 이 형식의 배열로 열린 화면 판정, ESC 닫기, 다른 화면 닫기를 한 순서로 돈다.
    internal abstract class GameplayScreen
    {
        // 열린 동안 쥔 입력 차단 핸들이다. 핸들이 있으면 열린 화면이다.
        IDisposable? _block;

        internal bool IsOpen => _block != null;

        // 씬 정리가 무조건 불러도 되고 여러 번 불러도 된다. 닫혀 있으면 아무것도 하지 않는다.
        internal abstract void Close();

        // ESC 처리가 묻는다. 열려 있었으면 닫고 true를 돌려준다. 원작 TryQuit을 바꾼 창(일부만 닫거나 ESC만 받는 창)은 이것을 바꾼다.
        internal virtual bool TryClose()
        {
            bool open = IsOpen;
            Close();
            return open;
        }

        // 원작 View.Open의 앞부분을 여러 화면이 같은 순서로 쓴다. 공용 HUD 연결과 세션을 확인하고, 열려 있으면 닫은 뒤
        // 입력 차단을 얻는다. 확인에 실패하면 열린 창을 그대로 두고, 하나라도 못 하면 false라 호출자는 창을 열지 않는다.
        private protected bool TryBeginShow(GameplayWorldContext world, out GameSessionManager? session)
        {
            if (!world.TryPresentSession(out session)) return false;
            Close();
            return TryBlockInput(world.Runtime);
        }

        // 입력 차단을 얻어 연 상태로 만든다. Runtime이 정리 중이라 거절하면 false이고, 호출자는 창을 열지 않는다.
        private protected bool TryBlockInput(GameRuntime runtime)
        {
            if (!runtime.GameplayInput.TryAcquire(out IDisposable? block)) return false;
            _block = block;
            return true;
        }

        // 입력 차단을 돌려주고 닫힌 상태로 만든다. 정리 경로에서도 불리므로 닫힌 상태에서는 아무것도 하지 않는다.
        // 마지막 차단이 풀리면 Player 입력이 돌아오므로, 그보다 먼저 풀어야 할 입력 임대는 호출자가 먼저 푼다.
        private protected void ReleaseInput()
        {
            _block?.Dispose();
            _block = null;
        }
    }
}
