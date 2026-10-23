#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace Game.Core
{
    // 월드 인스턴스 생성기다. 생성 규칙마다 비활성 보관 풀을 두고, 내준 인스턴스는 값 핸들(SpawnHandle)로 추적한다.
    // 인스턴스는 비활성으로 내주며, 호출자가 값을 넣은 뒤 Activate한다. 재사용 초기화는 템플릿의 IPoolable 컴포넌트가 맡는다.
    // 메인 스레드 전용이라 잠금이 없다. Dispose는 동기이며 던지지 않는다. IPoolable 예외만 여기서 잡아 기록하고 그 인스턴스를 파괴한다.
    public sealed class Spawner : IDisposable
    {
        // 보관 루트 이름이다. SpawnerTests가 같은 이름으로 보관 루트를 찾는다.
        const string _storageName = "월드 풀 보관";
        // 규칙별 풀이 처음 잡아 두는 보관 목록 크기다. 보관 최대 수가 더 작으면 그 수를 쓴다.
        const int _initialPoolCapacity = 4;
        // 이보다 작은 제곱 길이의 회전은 0 사원수로 보고 거부한다. Transform에 쓰면 회전이 깨진다.
        const float _minRotationSqrMagnitude = .000001f;

        // 비활성 보관 루트다. 이 아래에서 복제하므로 새 인스턴스의 Awake는 첫 활성화까지 미뤄진다. 종료에서만 파괴한다.
        readonly GameObject _storage;
        // 풀을 쓰는 생성 규칙별 Unity 풀이다. 첫 생성에서 만든다. 생성, 넘친 반납, 비우기는 모두 이 Spawner의 메서드가 맡는다.
        readonly Dictionary<SpawnRule, ObjectPool<SpawnTag>> _pools = new Dictionary<SpawnRule, ObjectPool<SpawnTag>>();
        // 내준 뒤 아직 반납하지 않은 인스턴스다. 종료 때 누수 수를 세고 파괴한다. 상태가 InUse인 표식과 같은 집합이며,
        // 상태를 바꾸는 자리마다 사이에 외부 코드 없이 함께 고친다. 종료만 집합을 먼저 비운 뒤 표식을 파괴한다.
        readonly HashSet<SpawnTag> _inUse = new HashSet<SpawnTag>();
        // IPoolable 실패는 규칙과 단계마다 한 번만 기록한다. 발사마다 도는 경로라 로그가 넘치지 않게 한다.
        readonly Log.Once _hookFailures = new Log.Once();
        // 종료 뒤에는 생성, 활성화, 반납을 막는다.
        bool _disposed;

        // storageParent 아래에 비활성 보관 루트를 만든다. 부모 누락은 내부 구성 결함이라 빌드별 검사로만 알린다.
        public Spawner(Transform storageParent)
        {
            Log.Instance.Assert(storageParent != null, "storageParent: 보관 루트의 부모가 필요합니다.");
            _storage = new GameObject(_storageName);
            _storage.SetActive(false);
            _storage.transform.SetParent(storageParent, false);
        }

        // 규칙의 인스턴스를 비활성 상태로 parent 아래 월드 기준 pose에 놓아 내준다. 풀을 쓰는 규칙은 보관 인스턴스를 다시 쓴다.
        // 부모와 자세는 IPoolable.OnSpawn을 모두 부른 뒤 정한다. 잘못된 입력이나 종료 뒤 호출은 Log.Instance.Fail로 보고하고 빈 핸들을 돌려준다.
        // OnSpawn이 실패하면 그 인스턴스를 파괴하고 빈 핸들을 돌려준다.
        public SpawnHandle Spawn(SpawnRule rule, Pose pose, Transform parent)
        {
            if (!CanSpawn(rule, pose, parent))
                return default;

            SpawnTag tag = Take(rule);
            tag.SetState(SpawnTag.SpawnState.InUse);
            _inUse.Add(tag);
            if (!RunHooks(tag, true))
            {
                _inUse.Remove(tag);
                DestroyTag(tag);
                return default;
            }

            // 훅이 객체를 즉시 파괴했거나 Spawner를 닫았으면 내줄 인스턴스가 없다. 객체 파괴는 외부 파괴로 보고한다.
            // Play Mode의 지연 Destroy는 같은 프레임에 보이지 않으므로 IPoolable 계약이 훅의 파괴를 막는다.
            if (DropIfDestroyed(tag))
                return default;
            if (tag.State != SpawnTag.SpawnState.InUse)
                return default;

            // 훅이 활성화했어도 보관 루트가 비활성이라 Unity 콜백은 아직 돌지 않았으므로, 부모를 옮기기 전에 비활성으로 되돌리면 된다.
            GameObject instance = tag.Instance;
            bool activated = instance.activeSelf;
            if (activated)
                instance.SetActive(false);
            Transform root = instance.transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(pose.position, pose.rotation);
            return new SpawnHandle(tag, tag.Generation);
        }

        // 살아 있는 핸들의 인스턴스를 활성화한다. 지난 핸들, 빈 핸들, 종료 뒤 핸들은 기록 없이 false다.
        // 다른 Spawner의 핸들은 검사 결함이다.
        public bool Activate(SpawnHandle handle)
        {
            if (!TryGetOwned(handle, out SpawnTag tag))
                return false;
            tag.Instance.SetActive(true);
            return true;
        }

        // 살아 있는 핸들의 인스턴스를 반납한다. 훅 안에서 다시 반납하지 못하도록 OnDespawn보다 먼저 사용 종료로 표시한다.
        // 훅이 실패했거나, 풀을 쓰지 않거나, 보관이 가득 찼거나, 종료 뒤면 보관하지 않고 파괴한다.
        // 지난 핸들과 빈 핸들은 기록 없이 false다. 다른 Spawner의 핸들은 검사 결함을 알린 뒤 false다.
        public bool Despawn(SpawnHandle handle)
        {
            if (!TryGetOwned(handle, out SpawnTag tag))
                return false;

            _inUse.Remove(tag);
            tag.SetState(SpawnTag.SpawnState.Pooled);
            GameObject instance = tag.Instance;
            instance.SetActive(false);
            bool clean = RunHooks(tag, false);

            // 훅이 객체를 즉시 파괴했으면 외부 파괴로 보고하고 반납을 끝낸다. 훅 도중 파괴 알림으로 상태가 바뀐 경우도 같다.
            if (DropIfDestroyed(tag))
                return true;
            if (tag.State != SpawnTag.SpawnState.Pooled)
                return true;

            // 훅이 다시 활성화했으면 비활성으로 되돌리고 재사용하지 않는다.
            bool reactivated = instance.activeSelf;
            if (reactivated)
                instance.SetActive(false);
            if (_storage != null)
            {
                instance.transform.SetParent(_storage.transform, false);
                tag.ResetLocalPose();
            }
            tag.AdvanceGeneration();

            // 보관이 가득 차면 Unity 풀이 파괴 콜백인 DestroyTag로 넘긴다. 풀을 쓰지 않는 규칙은 풀이 비어 있어 파괴한다.
            ObjectPool<SpawnTag> pool = tag.Pool;
            if (CanReturnToPool(clean, reactivated, pool))
                pool.Release(tag);
            else
                DestroyTag(tag);
            return true;
        }

        // 한 번만 종료한다. 사용 중 인스턴스 수를 누수 추적에 알리고, 사용 중과 보관 중 인스턴스와 보관 루트를 모두 파괴한다.
        // 사용 중 인스턴스에는 OnDespawn을 부르지 않는다. 파괴 표시를 먼저 해 SpawnTag.OnDestroy가 외부 파괴로 보고하지 않게 한다.
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            var inUse = new SpawnTag[_inUse.Count];
            _inUse.CopyTo(inUse);
            _inUse.Clear();
            int live = 0;
            foreach (SpawnTag tag in inUse)
            {
                if (tag != null)
                    live++;
            }
            LeakTracker.SpawnerDisposedWithLiveInstances(live);

            foreach (SpawnTag tag in inUse)
                DestroyTag(tag);
            // 보관 중인 표식은 Unity 풀이 비우면서 파괴 콜백인 DestroyTag로 넘긴다.
            foreach (ObjectPool<SpawnTag> pool in _pools.Values)
                pool.Clear();
            _pools.Clear();
            UnityObjects.DestroyOwned(_storage);
        }

        // 규칙별 풀의 생성 콜백과 풀을 쓰지 않는 규칙이 새 인스턴스를 만들 때 쓴다. 비활성 보관 루트 아래에서 복제해 Awake를 첫 활성화까지 미룬다.
        // 표식은 한 객체에 하나만 붙으므로 템플릿에 이미 있으면 그것을 쓴다. 표식은 상태를 직렬화하지 않아 복제본에서는 빈 상태다.
        SpawnTag Create(SpawnRule rule, ObjectPool<SpawnTag> pool)
        {
            GameObject instance = Object.Instantiate(rule.Template, _storage.transform, false);
            instance.SetActive(false);
            if (!instance.TryGetComponent(out SpawnTag tag))
                tag = instance.AddComponent<SpawnTag>();
            tag.Initialize(this, rule, pool, instance.GetComponentsInChildren<IPoolable>(true));
            return tag;
        }

        // Spawner가 소유한 인스턴스를 파괴한다. 규칙별 풀의 보관 초과와 비우기 콜백이기도 하다. 파괴 표시를 먼저 해
        // OnDestroy가 외부 파괴로 보지 않게 하고, Play Mode의 지연 Destroy 동안 보이지 않도록 비활성화한다. 이미 파괴된 표식은 DropDestroyed가 맡는다.
        void DestroyTag(SpawnTag tag)
        {
            if (DropIfDestroyed(tag))
                return;
            if (tag.State == SpawnTag.SpawnState.Destroying)
                return;

            tag.SetState(SpawnTag.SpawnState.Destroying);
            GameObject instance = tag.Instance;
            instance.SetActive(false);
            UnityObjects.DestroyOwned(instance);
        }

        // 풀이나 사용 중 목록에서 발견한 파괴된 표식을 버린다. 한 번도 활성화되지 않은 객체는 OnDestroy가 오지 않으므로,
        // 아직 보고되지 않은 파괴만 여기서 누수로 보고한다. 파괴된 객체는 이름을 읽을 수 없어 규칙을 대표로 넘긴다.
        // 표식만 파괴되고 게임 오브젝트가 남았으면 그 객체도 파괴한다.
        void DropDestroyed(SpawnTag tag)
        {
            _inUse.Remove(tag);
            if (tag.State != SpawnTag.SpawnState.Destroying)
            {
                tag.SetState(SpawnTag.SpawnState.Destroying);
                LeakTracker.PooledInstanceDestroyedExternally(tag.Rule);
            }
            GameObject instance = tag.Instance;
            if (instance != null)
                UnityObjects.DestroyOwned(instance);
        }

        // 표식이 이미 파괴됐으면 DropDestroyed로 버리고 true를 돌려준다. 호출자는 true면 그 표식을 더 쓰지 않는다.
        bool DropIfDestroyed(SpawnTag tag)
        {
            if (tag != null)
                return false;
            DropDestroyed(tag);
            return true;
        }

        // SpawnTag.OnDestroy가 씬 정리나 외부 Destroy처럼 Spawner 밖의 파괴를 알린다. 종료 전이면 누수로 보고한다.
        // OnDestroy 안에서는 객체가 아직 살아 있어 인스턴스를 그대로 넘긴다. 보관 풀에 남은 항목은 꺼낼 때 건너뛴다.
        internal void Forget(SpawnTag tag)
        {
            tag.SetState(SpawnTag.SpawnState.Destroying);
            _inUse.Remove(tag);
            if (!_disposed)
                LeakTracker.PooledInstanceDestroyedExternally(tag.Instance);
        }

        // 생성 입력과 상태를 검사한다. 모두 호출자 결함이라 Log.Instance.Fail로 보고한다.
        // 보관 루트가 밖에서 파괴됐으면 새 복제가 활성 씬 루트에서 깨어나므로 거부한다.
        bool CanSpawn(SpawnRule rule, Pose pose, Transform parent)
        {
            if (_disposed)
                return Log.Instance.Fail(new ObjectDisposedException(nameof(Spawner)));
            if (rule == null)
                return Log.Instance.Fail(new ArgumentNullException(nameof(rule)));
            if (rule.Template == null)
                return Log.Instance.Fail(new ArgumentException("생성 규칙에 템플릿이 없습니다.", nameof(rule)), rule);
            if (!IsValidPose(pose))
                return Log.Instance.Fail(new ArgumentOutOfRangeException(nameof(pose), "생성 위치·회전이 유효한 유한 값이 아닙니다."), rule);
            if (parent == null)
                return Log.Instance.Fail(new ArgumentNullException(nameof(parent)), rule);
            if (_storage == null)
                return Log.Instance.Fail(new InvalidOperationException("월드 풀 보관 루트가 Spawner 밖에서 파괴되었습니다."), rule);
            return true;
        }

        // 풀을 쓰고 보관 수가 1 이상인 규칙은 규칙별 풀에서 꺼낸다. 그 밖의 규칙은 매번 새로 만든다.
        // 외부에서 파괴된 표식은 풀에서 바로 뺄 수 없어 꺼낼 때 버리고, 파괴 중인 표식은 건너뛴다.
        // 반복 안의 Get은 보관 수를 하나씩 줄이므로 반복은 반드시 끝난다.
        SpawnTag Take(SpawnRule rule)
        {
            if (!rule.Pooled || rule.MaxInactive < 1)
                return Create(rule, null);
            if (!_pools.TryGetValue(rule, out ObjectPool<SpawnTag> pool))
            {
                pool = CreatePool(rule);
                _pools.Add(rule, pool);
            }
            while (pool.CountInactive > 0)
            {
                SpawnTag tag = pool.Get();
                if (!DropIfDestroyed(tag) && tag.State == SpawnTag.SpawnState.Pooled)
                    return tag;
            }
            return pool.Get();
        }

        // 상태 추적은 Spawner가 하므로 Unity 풀의 중복 반납 검사는 끈다. Unity 풀은 보관 최대 수가 1 미만이면 생성자에서 던지므로
        // Take는 보관 수가 1 이상인 규칙에만 부른다. 생성 콜백이 표식에 풀을 넘기도록 지역 변수를 캡처하며, 꺼낼 때마다 클로저를 만들지 않으려고 분리한다.
        ObjectPool<SpawnTag> CreatePool(SpawnRule rule)
        {
            int maxInactive = Mathf.Max(1, rule.MaxInactive);
            ObjectPool<SpawnTag> pool = null;
            pool = new ObjectPool<SpawnTag>(() => Create(rule, pool), null, null, DestroyTag,
                false, Mathf.Min(_initialPoolCapacity, maxInactive), maxInactive);
            return pool;
        }

        // 살아 있고 이 Spawner가 만든 핸들인지 본다. 지난 핸들과 빈 핸들은 기록 없이 false다.
        // 다른 Spawner의 핸들은 내부 결함이라 검사로 알린 뒤 false다.
        bool TryGetOwned(SpawnHandle handle, out SpawnTag tag)
        {
            tag = handle.Tag;
            if (!handle.IsAlive)
                return false;
            if (ReferenceEquals(tag.Owner, this))
                return true;
            Log.Check(false, "다른 Spawner가 만든 핸들은 이 Spawner로 다룰 수 없습니다.", tag);
            return false;
        }

        // 모은 IPoolable을 차례로 부르고, 하나라도 실패하면 false를 돌려준다. 파괴된 컴포넌트는 건너뛴다.
        // 생성 실패면 어차피 인스턴스를 파괴하므로 첫 실패에서 멈추고, 반납 훅은 나머지 컴포넌트의 정리를 계속한다.
        bool RunHooks(SpawnTag tag, bool spawning)
        {
            bool clean = true;
            foreach (IPoolable poolable in tag.Poolables)
            {
                if (poolable is Object component && component == null)
                    continue;
                try
                {
                    if (spawning)
                        poolable.OnSpawn();
                    else
                        poolable.OnDespawn();
                }
                // 예외 경계: IPoolable 구독자 격리. 실패는 규칙과 단계마다 한 번 기록하고, 호출자는 그 인스턴스를 재사용 없이 파괴한다.
                catch (Exception error)
                {
                    clean = false;
                    ReportHookFailure(tag, error, spawning);
                    if (spawning)
                        break;
                }
            }
            return clean;
        }

        // IPoolable 실패를 규칙 이름과 단계마다 한 번, 원래 예외와 스택 그대로 기록한다. 문맥 객체는 인스턴스가 살아 있을 때만 넘긴다.
        void ReportHookFailure(SpawnTag tag, Exception error, bool spawning)
        {
            SpawnRule rule = tag.Rule;
            string key = (spawning ? "OnSpawn:" : "OnDespawn:") + (rule != null ? rule.name : string.Empty);
            if (!_hookFailures.First(key))
                return;
            GameObject instance = tag.Instance;
            Log.Instance.Exception(error, instance != null ? instance : null);
        }

        // Transform에 쓰기 전에 위치와 회전이 유한하고 회전이 0 사원수가 아닌지 본다. 생성 입력 검사만 쓰므로 밖에 열지 않는다.
        static bool IsValidPose(Pose pose)
        {
            Vector3 p = pose.position;
            Quaternion q = pose.rotation;
            return float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z)
                && float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w)
                && q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w >= _minRotationSqrMagnitude;
        }

        // 훅이 깨끗이 끝났고 다시 켜지지 않았으며, Spawner와 보관 루트가 살아 있고 규칙이 풀을 쓸 때만 보관한다.
        bool CanReturnToPool(bool clean, bool reactivated, ObjectPool<SpawnTag> pool) =>
            clean && !reactivated && !_disposed && _storage != null && pool != null;
    }
}
