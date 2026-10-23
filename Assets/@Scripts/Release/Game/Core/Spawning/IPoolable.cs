#nullable enable annotations

namespace Game.Core
{
    // Spawner가 인스턴스를 내주거나 돌려받을 때 템플릿 컴포넌트가 재사용 상태를 스스로 초기화하는 계약이다.
    // 두 호출 모두 인스턴스가 비활성일 때 오며, 객체를 활성화하거나 파괴하면 안 된다. Play Mode의 Destroy는 프레임 끝까지 미뤄져
    // Spawner가 같은 호출 안에서 알아채지 못하기 때문이다. 던진 예외는 Spawner가 기록하고 그 인스턴스를 파괴한다.
    public interface IPoolable
    {
        // 처음 만들 때를 포함해 인스턴스를 내줄 때마다, 호출자가 값을 넣기 전에 부른다.
        // Awake보다 먼저 올 수 있으므로 Awake가 준비한 상태에 기대면 안 된다.
        void OnSpawn();

        // 반납되어 비활성이 된 뒤, 보관 루트로 옮기기 전에 부른다. 반납 뒤 바로 파괴되는 인스턴스도 받는다.
        // 사용 중에 Spawner.Dispose로 파괴되는 인스턴스는 받지 않는다. 초기화가 덜 된 상태에서도 안전하게 정리해야 한다.
        void OnDespawn();
    }
}
