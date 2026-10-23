#nullable enable annotations
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // Spawner 풀에서 재사용하는 탄 궤적 템플릿인 원본 BulletPresentation의 Trail 자식에 붙는다.
    // 풀 대여와 반환, 그리고 전투가 탄을 재사용하는 동안의 발사, 이동, 종료, 보관을 모두 이 컴포넌트로 거쳐 잔상 초기화가 한곳에만 있다.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TrailRenderer))]
    public sealed class PooledTrail : MonoBehaviour, IPoolable
    {
        // 같은 객체의 궤적 렌더러 캐시다. Trail 속성이 처음 쓸 때 채운다.
        TrailRenderer _trail;

        // 대여 직후 꺼진 상태에서 불리며, 아직 Awake 전일 수 있다.
        void IPoolable.OnSpawn() => ResetTrail();

        void IPoolable.OnDespawn() => ResetTrail();

        // 탄 하나의 발사다. 남은 잔상을 지운 뒤 발사 위치에서 방출을 시작한다.
        internal void Begin(Vector3 position)
        {
            transform.position = position;
            gameObject.SetActive(true);
            ResetTrail();
            TrailRenderer trail = Trail;
            trail.emitting = true;
            trail.AddPosition(position);
        }

        // 날아가는 탄을 따라 옮기고 지난 점을 남긴다.
        internal void MoveTo(Vector3 position)
        {
            transform.position = position;
            Trail.AddPosition(position);
        }

        // 명중 프레임은 궤적을 옮기지 않고 실제 충돌점만 더한다.
        internal void AddPoint(Vector3 point) => Trail.AddPosition(point);

        // 탄이 끝나면 방출만 멈추고, 남은 잔상이 사라지는 데 걸리는 원본 Trail 수명을 돌려준다.
        internal float End()
        {
            TrailRenderer trail = Trail;
            trail.emitting = false;
            return trail.time;
        }

        // 잔상이 사라진 뒤 탄 풀로 돌아갈 때 지우고 끈다. World 풀에는 돌려주지 않고 다음 발사의 Begin이 다시 켠다.
        internal void Park()
        {
            ResetTrail();
            gameObject.SetActive(false);
        }

        // RequireComponent로 같은 객체에 있다. Awake 전에도 불릴 수 있어 처음 쓸 때 한 번만 찾는다.
        TrailRenderer Trail
        {
            get
            {
                if (ReferenceEquals(_trail, null)) _trail = GetComponent<TrailRenderer>();
                return _trail;
            }
        }

        void ResetTrail()
        {
            TrailRenderer trail = Trail;
            trail.emitting = false;
            trail.Clear();
        }
    }
}
