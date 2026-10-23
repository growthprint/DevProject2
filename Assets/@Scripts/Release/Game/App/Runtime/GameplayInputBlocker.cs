#nullable enable annotations
using System;

namespace Game.App
{
    // 지도와 인벤토리가 거는 게임플레이 입력 차단을 센다. 첫 차단에서 Player 입력을 막고 마지막 해제에서 되돌린다.
    // 시간은 멈추지 않으며, 실제 입력과 커서 처리는 일시정지 계약(IGameplayPause)이 맡는다.
    public sealed class GameplayInputBlocker
    {
        readonly GameRuntime _runtime;
        // 지금 걸려 있는 입력 차단 수다. 앱 정리 때 0으로 되돌린다.
        int _count;

        internal GameplayInputBlocker(GameRuntime runtime) => _runtime = runtime;

        public bool IsBlocked => _count > 0;

        // 첫 차단에서만 Player 입력을 막고 해제 핸들을 돌려준다. 마지막 핸들이 해제되면 입력이 돌아온다.
        // 앱 정리가 시작됐으면 차단 수를 바꾸지 않고 false를 돌려주며, 호출자는 화면을 열지 않는다.
        internal bool TryAcquire(out IDisposable? block)
        {
            block = null;
            // 정리 시작 여부는 종료를 맡는 Runtime이 직접 알린다.
            if (_runtime.CleanupStarted) return false;
            // 지도, 인벤토리, 편집은 구성 뒤 게임 씬에서만 차단을 걸므로 일시정지 계약이 늘 있다.
            if (++_count == 1) _runtime.Pause!.SetPlayerInputBlocked(true);
            block = new Handle(this);
            return true;
        }

        // 앱 정리가 끝난 뒤 늦게 해제될 차단 수를 0으로 되돌린다.
        internal void Reset() => _count = 0;

        // 한 번만 해제되는 입력 차단 핸들이다. 앱 정리가 시작된 뒤 늦게 해제되면
        // 이미 0으로 되돌린 차단 수와 해제된 일시정지 어댑터를 건드리지 않는다.
        sealed class Handle : IDisposable
        {
            GameplayInputBlocker? _owner;
            internal Handle(GameplayInputBlocker owner) { _owner = owner; }
            public void Dispose()
            {
                if (_owner == null) return;
                // 메인 스레드에서만 해제한다고 전제하므로 잠금을 쓰지 않는다.
                GameplayInputBlocker owner = _owner; _owner = null;
                if (owner._runtime.CleanupStarted) return;
                // 건 차단만 풀므로 일시정지 계약이 늘 있다. 파괴된 구현은 해제 뒤 요청을 무시해 Unity 파괴 확인이 필요 없다.
                if (--owner._count == 0) owner._runtime.Pause!.SetPlayerInputBlocked(false);
            }
        }
    }
}
