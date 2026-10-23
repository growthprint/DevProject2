using Cysharp.Threading.Tasks;

namespace Game.Core
{
    // 에셋과 씬 핸들, 서비스, 수명 범위가 함께 쓰는 한 번뿐인 비동기 종료 계약이다.
    public interface IAsyncReleasable
    {
        // 구현체는 실제 해제를 한 번만 하고, 단계 실패는 기록한 뒤 나머지 단계를 계속하며, 실패한 해제를 다시 시도하지 않는다.
        /// <summary>해제를 시작하거나, 이미 시작된 해제의 완료를 돌려준다. 메인 스레드에서 부르며 던지지 않는다.</summary>
        /// <remarks>여러 번 불러도 모든 호출자가 같은 완료를 기다린다. 완료는 구현체의 해제 책임이 끝났다는 뜻이며,
        /// GC 회수나 엔진 메모리 정리까지 보장하지 않는다.</remarks>
        UniTask ReleaseAsync();
    }
}
