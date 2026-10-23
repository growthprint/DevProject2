#nullable enable
using UnityEngine;

namespace Game.Core
{
    // 월드 인스턴스 하나의 생성 규칙을 담는 에셋이다. 풀 상태는 Spawner가 소유하므로 여러 월드가 같은 규칙을 함께 써도 된다.
    public sealed class SpawnRule : ScriptableObject
    {
        // 복제할 템플릿이다. 프리팹 루트뿐 아니라 원본 BulletPresentation의 Trail처럼 프리팹 안의 자식일 수도 있다.
        [SerializeField] GameObject _template = null!;
        // 반납한 인스턴스를 비활성으로 보관해 다시 쓸지 여부다. false면 반납 때 파괴한다.
        [SerializeField] bool _pooled = true;
        // 비활성 보관 최대 수다. 사용 중 인스턴스 수는 제한하지 않는다. 0이면 풀을 쓰지 않고 반납 때 파괴한다.
        [SerializeField, Min(0)] int _maxInactive = 32;

        // 복제할 템플릿이다. 비어 있으면 Spawner가 결함으로 보고하고 생성하지 않는다.
        public GameObject Template => _template;

        // 반납한 인스턴스를 다시 쓰는지 여부다.
        public bool Pooled => _pooled;

        // 비활성 보관 최대 수다.
        public int MaxInactive => _maxInactive;
    }
}
