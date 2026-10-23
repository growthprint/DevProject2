#nullable enable annotations
using System;
using UnityEngine;
using UnityEngine.Pool;

namespace Game.Core
{
    // Spawner가 만든 인스턴스의 루트에 붙는 소유 표식이다. 핸들의 생존 판정과 반납 대상 확인에 쓴다.
    // 상태를 직렬화하지 않으므로 이 객체를 복제한 사본에는 소유자가 없다.
    // Unity는 한 번이라도 활성화된 객체에만 OnDestroy를 부른다. 활성화된 적 없이 외부에서 파괴된 인스턴스는
    // Spawner가 풀이나 사용 중 목록에서 파괴된 표식을 발견할 때 보고한다.
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class SpawnTag : MonoBehaviour
    {
        // 표식의 수명 상태다. InUse만 핸들이 살아 있고, Destroying은 Spawner가 직접 파괴를 시작했거나 외부 파괴를 이미 알았다는 뜻이다.
        internal enum SpawnState { Pooled, InUse, Destroying }

        // 이 인스턴스를 만든 Spawner다.
        Spawner _owner;
        // 이 인스턴스를 만든 생성 규칙이다.
        SpawnRule _rule;
        // 반납할 규칙별 Unity 풀이다. 풀을 쓰지 않는 규칙이면 비어 있고 반납 때 파괴한다. Spawner만 쓰므로 직렬화하지 않는다.
        ObjectPool<SpawnTag> _pool;
        // 표식이 붙은 게임 오브젝트다. 표식 컴포넌트만 파괴돼도 Spawner가 객체를 찾아 파괴할 수 있게 관리 참조로 둔다.
        GameObject _instance;
        // 생성 때 한 번 모은 재사용 초기화 컴포넌트다(비활성 자식 포함).
        IPoolable[] _poolables = Array.Empty<IPoolable>();
        // 반납할 때마다 올리는 세대다. 핸들은 받은 세대와 같을 때만 살아 있으므로 지난 핸들이 재사용된 객체를 건드리지 못한다.
        int _generation;
        SpawnState _state;
        // 템플릿의 로컬 위치, 회전, 배율이다. 반납 때 이 값으로 되돌린다.
        Vector3 _localPosition;
        Quaternion _localRotation;
        Vector3 _localScale;

        internal Spawner Owner => _owner;
        internal SpawnRule Rule => _rule;
        // 비어 있으면 Spawner가 반납 대신 파괴한다.
        internal ObjectPool<SpawnTag> Pool => _pool;
        internal GameObject Instance => _instance;
        internal IPoolable[] Poolables => _poolables;
        internal int Generation => _generation;
        internal SpawnState State => _state;

        // Spawner가 비활성 보관 루트 아래에서 복제한 직후 한 번 부른다. 복제 직후의 로컬 자세가 곧 템플릿 값이라 여기서 저장한다.
        internal void Initialize(Spawner owner, SpawnRule rule, ObjectPool<SpawnTag> pool, IPoolable[] poolables)
        {
            _owner = owner;
            _rule = rule;
            _pool = pool;
            _instance = gameObject;
            _poolables = poolables;
            _state = SpawnState.Pooled;
            Transform root = transform;
            _localPosition = root.localPosition;
            _localRotation = root.localRotation;
            _localScale = root.localScale;
        }

        // 상태를 바꾼다. 전이 규칙은 Spawner가 지킨다.
        internal void SetState(SpawnState state) => _state = state;

        // 반납 때 세대를 올려 지난 핸들을 무효로 만든다(넘침은 감싸 돈다).
        internal void AdvanceGeneration() => _generation = unchecked(_generation + 1);

        // 반납 때 로컬 위치, 회전, 배율을 템플릿 값으로 되돌린다.
        internal void ResetLocalPose()
        {
            Transform root = transform;
            root.SetLocalPositionAndRotation(_localPosition, _localRotation);
            root.localScale = _localScale;
        }

        // 씬 정리나 외부 Destroy처럼 Spawner 밖의 파괴를 소유자에게 알려 누수로 보고하게 한다.
        // Spawner가 직접 파괴할 때는 상태를 먼저 Destroying으로 두므로 여기서 걸러진다.
        void OnDestroy()
        {
            if (_owner == null || _state == SpawnState.Destroying)
                return;
            _owner.Forget(this);
        }
    }
}
