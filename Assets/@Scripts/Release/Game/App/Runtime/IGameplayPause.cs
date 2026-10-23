using Game.Core;

namespace Game.App
{
    // 앱 규칙이 엔진 시간과 입력을 직접 만지지 않게 하는 일시정지 계약이다. 정지 전 시간, 입력, 커서 상태는 구현이 보관한다.
    // ReleaseAsync는 구성 전 상태를 한 번 복원하고, 그 뒤의 정지와 입력 차단 요청은 무시한다.
    public interface IGameplayPause : IAsyncReleasable
    {
        // 같은 값을 반복해 적용해도 정지 전에 보관한 복원 상태를 덮어쓰지 않는다.
        void SetPaused(bool paused);
        // 지도와 인벤토리용이다. 시간은 멈추지 않고 Player 입력만 막고 커서를 보인다.
        void SetPlayerInputBlocked(bool blocked);
    }
}
