#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.App.World
{
    // 씬의 동적 월드 객체 소유자다. 씬에 이미 있는 캐릭터는 소유하지 않고 등록만 하고, 전투 효과 같은 새 객체는
    // Spawner에서 빌려 Despawn이나 월드 종료까지 소유한다. 호출자는 빌린 객체의 Transform과 활성 상태를 바꿀 수 있지만 파괴하지 않는다.
    // 생성 순서는 비활성 등록(Spawn), 호출자 설정, Activate다.
    public sealed class WorldObjectService : IDisposable
    {
        // 이 월드가 소유하며 Dispose에서 한 번 해제한다.
        readonly Spawner _spawner;
        // 생성 위치를 이 씬의 명시적인 루트 안으로 제한한다.
        readonly Transform _worldRoot;
        // 등록 ID별 핸들이다. 소유 객체의 대여는 핸들이 함께 가져 등록 상태와 반환 상태가 갈라지지 않는다.
        readonly Dictionary<long, WorldObjectHandle> _objects = new Dictionary<long, WorldObjectHandle>();
        // 같은 객체를 다시 등록해도 지난 핸들을 재사용하지 않도록 번호를 계속 올린다.
        long _nextId;
        // 등록 수가 이 값에 닿은 생성에서만 파괴된 등록을 훑어 회수하고, 남은 수의 두 배로 다시 정한다.
        // 발사마다 전체를 훑지 않아 생성 한 번의 평균 비용이 등록 수와 무관하고, 파괴된 등록은 살아 있는 등록 수 남짓까지만 쌓인다.
        int _pruneAt = _minimumPruneAt;
        const int _minimumPruneAt = 64;
        // 종료가 시작되면 새 등록과 생성을 받지 않는다. Dispose가 한 번만 실행되게 하는 표시도 겸한다.
        bool _closing;

        // 생성자에서는 의존성만 연결하고 리소스를 로드하지 않는다.
        public WorldObjectService(Spawner spawner, Transform worldRoot)
        {
            _spawner = spawner;
            _worldRoot = worldRoot;
        }

        // Spawner에서 비활성 인스턴스를 받아 월드 루트 아래 월드 좌표에 두고 등록만 한다. 호출자가 값을 설정한 뒤 Activate로 켠다.
        // 발사처럼 매 프레임 쓰는 경로라 실패는 예외 없이 null이고, 닫힌 월드는 기록하지 않는다. 규칙과 템플릿은 저작 데이터이고 자세는
        // 유한 좌표다. 규칙과 템플릿의 수명은 호출자가 월드 종료까지 보장한다. 재사용 초기화는 템플릿의 IPoolable이 맡는다.
        public WorldObjectHandle? Spawn(SpawnRule rule, Pose worldPose)
        {
            if (!IsOpen) return null;
            // 파괴된 등록은 살아 있지 않게 보일 뿐 동작에 영향이 없으므로, 등록이 늘어난 때에만 몰아서 회수한다.
            if (_objects.Count >= _pruneAt) PruneDestroyed();
            SpawnHandle spawn = _spawner.Spawn(rule, worldPose, _worldRoot);
            // 생성 실패는 Spawner가 기록한다. IPoolable.OnSpawn 중 월드가 닫혔으면 핸들을 내주지 않는다.
            // 그 인스턴스는 Spawner 해제나 루트 파괴가 정리한다.
            if (!spawn.IsAlive || !IsOpen) return null;
            return Register(new WorldObjectHandle(NextId(), spawn, this));
        }

        // Spawn이 돌려준 비활성 소유 객체를 호출자 설정 뒤 켠다. 씬 정리로 닫힌 월드는 기록 없이 false다.
        // 켜는 중(OnEnable)에 등록이 해제되거나 인스턴스가 사라져도 false다.
        public bool Activate(WorldObjectHandle handle)
        {
            // 호출자는 이 월드의 Spawn이 방금 돌려준 소유 핸들만 넘기므로 닫힌 월드만 거른다.
            if (!IsOpen) return false;
            return _spawner.Activate(handle.Spawn) && handle.IsAlive;
        }

        // 이번 등록의 소유 토큰만 Spawner에 반환한다. 실패는 예외 없이 false다. 다른 파괴된 등록은 생성이 몰아서 회수한다.
        // 닫힌 월드, 외부 등록, 지난 토큰은 기록하지 않고, 반환 중 IPoolable 실패는 Spawner가 기록한다.
        public bool Despawn(WorldObjectHandle handle)
        {
            if (_closing) return false;
            return DespawnOwned(handle);
        }

        // 씬에 이미 있는 캐릭터를 파괴 소유권은 넘기지 않고 조회 대상으로 등록한다. 레벨 초기화(SceneContext.InitLevelAsync)만
        // 이 월드와 같은 씬의 캐릭터를 한 번 넘긴다.
        public WorldObjectHandle RegisterExternal(GameObject instance)
        {
            return Register(new WorldObjectHandle(NextId(), instance, this));
        }

        // 한 번만 실행하고 던지지 않는다. 새 생성과 등록을 먼저 막고 등록부를 비워 모든 핸들을 무효로 만든다.
        // 그다음 소유 객체만 Spawner에 반환하고, 외부 객체는 등록만 끊으며, 마지막에 Spawner를 해제한다.
        // 단계마다 Cleanup.Run이 실패를 기록하고 다음 단계를 계속한다. IPoolable.OnDespawn에서 다시 부른 Despawn은 닫힌 월드라 false다.
        public void Dispose()
        {
            if (_closing) return;
            _closing = true;
            var owned = new List<SpawnHandle>(_objects.Count);
            foreach (WorldObjectHandle handle in _objects.Values)
                if (!handle.IsExternal) owned.Add(handle.Spawn);
            _objects.Clear();
            if (_spawner == null) return;
            foreach (SpawnHandle spawn in owned) Cleanup.Run(() => _spawner.Despawn(spawn));
            Cleanup.Run(_spawner.Dispose);
        }

        // 반환 전에 등록부터 지워, OnDisable에서 다시 들어온 Despawn이 아무것도 반환하지 않고 끝나게 한다.
        bool DespawnOwned(WorldObjectHandle handle)
        {
            // 다른 월드나 지난 등록의 토큰으로 현재 대여를 반환하지 않는다.
            if (handle == null || handle.IsExternal || !IsRegistered(handle)) return false;
            _objects.Remove(handle.Id);
            return _spawner.Despawn(handle.Spawn);
        }

        // Unity 객체가 파괴된 등록을 지우고 다음 회수 시점을 남은 등록 수의 두 배로 정한다. 파괴된 소유 객체의 누수 보고는
        // SpawnTag와 Spawner가 맡으므로 여기서는 등록만 지운다. 파괴된 항목이 없으면 할당 없이 끝나며, 대상을 먼저 모은 뒤 지운다.
        void PruneDestroyed()
        {
            List<WorldObjectHandle>? stale = null;
            foreach (WorldObjectHandle handle in _objects.Values)
            {
                // 소유 객체는 Spawner 대여의 생존만 본다. 인스턴스를 꺼내 다시 Unity null 비교를 하지 않는다.
                bool destroyed = handle.IsExternal ? handle.Instance == null : !handle.Spawn.IsAlive;
                if (destroyed) (stale ??= new List<WorldObjectHandle>()).Add(handle);
            }

            // 방금 등록부에서 모은 핸들이라 등록 확인 없이 ID로 지운다. 핸들마다 ID가 달라 같은 항목을 두 번 지우지 않는다.
            if (stale != null)
                foreach (WorldObjectHandle handle in stale)
                    _objects.Remove(handle.Id);
            _pruneAt = Math.Max(_minimumPruneAt, _objects.Count * 2);
        }

        long NextId() => checked(++_nextId);

        // 생존 판정을 이 등록부에 맡기는 핸들을 등록한다. Spawner는 비활성 보관 인스턴스만 내주고 외부 등록은 레벨 초기화가
        // 한 번 하므로 같은 객체가 두 번 들어오지 않는다.
        WorldObjectHandle Register(WorldObjectHandle handle)
        {
            _objects.Add(handle.Id, handle);
            return handle;
        }

        // 번호가 같아도 다른 월드의 핸들이나 지난 핸들은 현재 등록으로 보지 않도록 참조까지 비교한다.
        internal bool IsRegistered(WorldObjectHandle handle) =>
            _objects.TryGetValue(handle.Id, out WorldObjectHandle current) && ReferenceEquals(current, handle);

        // 진입이 예외 없이 쓰는 열림 판정이다. 종료가 시작됐거나 월드 루트가 파괴됐으면 닫힌 것으로 본다. Spawner는 생성자 인수라 늘 있다.
        bool IsOpen => !_closing && _worldRoot != null;
    }
}
