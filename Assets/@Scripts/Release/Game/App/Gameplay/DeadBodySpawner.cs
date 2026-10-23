#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.App.World;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원본 DeadBodyManager처럼 저장된 유실물마다 묘비 지점을 두고, 적 처치와 저장된 사망 복원 때 시체 전리품 지점을 둔다.
    // 전투 월드 컨트롤러가 준비 때 만들어 소유한다. 상호작용 객체는 매 프레임 World 전체를 모으지 않고 RuntimePoints만 읽는다.
    // 빌린 지점은 레벨 정리 직후 SceneContext의 World 해제가 반환하거나 파괴하므로 Release는 참조만 놓는다.
    internal sealed class DeadBodySpawner
    {
        readonly GameplayWorldContext _world;
        // 이 월드의 원본 하위 씬 이름이다. 다른 월드의 하위 씬에 속한 유실물은 여기에 두지 않는다.
        readonly string[] _subsceneIds;
        // 런타임 지점의 방향은 전투 월드 컨트롤러의 자세를 따른다.
        readonly Transform _owner;
        // 마지막으로 반영한 유실물 목록 참조다. 상태가 불변이라 참조가 같으면 내용도 같다.
        // 그래서 발사나 장전처럼 소유 그래프만 바뀐 알림은 유실물을 건드리지 않고, 개수가 같은 교체도 놓치지 않는다.
        IReadOnlyList<LostBodyState>? _displayBodies;
        // 유실물 갱신 때 재사용하는 버퍼다. 보이는 출격 토큰과 반환할 토큰을 담으며, 갱신마다 새로 할당하지 않는다.
        readonly HashSet<uint> _visibleBodies = new();
        readonly List<uint> _hiddenBodies = new();
        // 저장된 출격 토큰마다 한 번만 빌린 유실물 묘비 지점이다. 유실물을 회수하면 World에 돌려준다.
        readonly Dictionary<uint, WorldObjectHandle> _lostBodies = new();
        // 대여와 활성화에 성공한 시체 상자와 유실물 묘비다. Spawn 계열이 넣고, Refresh와 Release가 뺀다.
        readonly List<InteractableBase> _runtimeInteractables = new();

        internal DeadBodySpawner(GameplayWorldContext world, string[] subsceneIds, Transform owner)
        {
            _world = world!;
            _subsceneIds = subsceneIds!;
            _owner = owner!;
        }

        // 상호작용 객체가 F 대상 후보로 읽는 런타임 대상이다. 이미 파괴된 인스턴스의 대상은 Unity null이라 판정이 건너뛴다.
        internal IReadOnlyList<InteractableBase> RuntimeInteractables => _runtimeInteractables;

        // 세션 알림이 같은 유실물 목록이면 다시 맞추지 않도록 마지막으로 반영한 목록 참조와 비교한다.
        internal bool Shows(IReadOnlyList<LostBodyState>? bodies) => ReferenceEquals(_displayBodies, bodies);

        // 묘비를 세울 유실물마다 저장 위치에 원본 묘비 지점을 원작 lootBoxPrefab_Tomb 규칙으로 한 번 빌린다.
        // 회수 등으로 보이지 않게 된 묘비만 World에 돌려준다. 반영한 목록 참조를 기억해 Shows가 같은 목록을 알아본다.
        internal void Refresh(IReadOnlyList<LostBodyState>? bodies)
        {
            _displayBodies = bodies;
            _visibleBodies.Clear();
            // 원작처럼 묘비 여부는 기록 때가 아니라 지금 난이도로 정한다. 호출자인 전투 월드 초기화와 세션 알림은
            // GameManager가 있을 때만 이곳에 오므로 난이도 소유자를 바로 읽는다.
            DifficultyState? difficulty = GameManager.Difficulty.Current;
            if (bodies != null)
                foreach (LostBodyState body in bodies)
                {
                    if (!ShouldSpawnTomb(body, _subsceneIds, difficulty)) continue;
                    _visibleBodies.Add(body.RaidId);
                    if (_lostBodies.ContainsKey(body.RaidId)) continue;
                    string sourceId = body.RaidId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    // 묘비 규칙은 앱 수명 게임플레이 데이터이고, GameplayDataSettings 연결은 앱 시작 검증이 확인했다.
                    // 묘비 영역은 RuntimePointSpawner가 템플릿에서 찾은 Interactable Trigger다.
                    WorldObjectHandle? tomb = Spawn<LostBodyInteractable>(GameplayDataSettings.Prefabs.LootBoxPrefab_Tomb!, "LostBody_" + sourceId,
                        new Vector3(body.X, body.Y, body.Z), (point, area) => point.Configure(sourceId, area, "gameplay.recover"));
                    // 대여에 실패하면 World가 원인을 기록했으므로 등록하지 않는다. 유실물 목록이 다음에 바뀔 때 다시 시도한다.
                    if (tomb != null) _lostBodies.Add(body.RaidId, tomb);
                }
            _hiddenBodies.Clear();
            foreach (uint id in _lostBodies.Keys)
                if (!_visibleBodies.Contains(id)) _hiddenBodies.Add(id);
            // 풀이 인스턴스를 재사용하므로, 반환 전에 그 묘비를 런타임 대상 목록에서 빼 두 번 들어가지 않게 한다.
            // 인스턴스가 이미 파괴됐으면 목록의 묘비도 Unity null이라 상호작용 판정이 건너뛴다.
            foreach (uint id in _hiddenBodies)
            {
                WorldObjectHandle tomb = _lostBodies[id];
                GameObject instance = tomb.Instance;
                if (instance != null && instance.TryGetComponent(out LostBodyInteractable point)) _runtimeInteractables.Remove(point);
                _world.Objects.Despawn(tomb);
                _lostBodies.Remove(id);
            }
        }

        // 원작 DeadBodyManager.ShouldSpawnDeadBody처럼 지금 난이도의 SpawnDeadBody가 참이고, 유효한 기록이 이 월드의 하위 씬에
        // 속할 때만 묘비를 세운다. 난이도가 없으면 원작 Ruleset 기본값처럼 세운다. DifficultyState는 Unity 객체가 아니라 ?.를 써도 된다.
        internal static bool ShouldSpawnTomb(LostBodyState body, string[] subsceneIds, DifficultyState? difficulty) =>
            difficulty?.SpawnDeadBody != false && body.Valid && Array.IndexOf(subsceneIds, body.SubSceneId) >= 0;

        // 새 처치와 저장된 사망 복원이 함께 쓴다. 사망 위치에 원작 lootBoxPrefab 규칙으로 원본 시체 지점을 빌려 전리품 지점으로 구성한다.
        // 시체는 씬이 끝날 때까지 World가 소유하고, 저작 지점 목록에는 넣지 않는다.
        internal void SpawnCorpse(OriginalEnemyController enemy, int[] loot, Vector3 position)
        {
            // 전리품 수량은 정의의 기본 수량이다. 정의가 없는 항목은 그릴 수도 주울 수도 없어 뺀다.
            // 같은 카탈로그면 새 처치와 저장 복원이 같은 칸 순서를 만든다.
            int[] lootTypes = new int[loot.Length], lootCounts = new int[loot.Length];
            int kept = 0;
            foreach (int type in loot)
            {
                if (!_world.Catalog.TryGet(type, out OriginalItemDefinition? definition)) continue;
                lootTypes[kept] = type;
                lootCounts[kept] = definition!.DefaultCount;
                kept++;
            }
            if (kept < loot.Length)
            {
                Array.Resize(ref lootTypes, kept);
                Array.Resize(ref lootCounts, kept);
            }
            // 시체 루팅 원본 ID는 CampaignEventLog 규약으로 만들고, 상자 영역은 RuntimePointSpawner가 템플릿에서 찾은 Interactable Trigger다.
            // GameplayDataSettings의 시체 규칙 연결은 앱 시작 검증이 확인했고, 대여 실패는 World가 기록하므로 반환값을 쓰지 않는다.
            Spawn<InteractableLootbox>(GameplayDataSettings.Prefabs.LootBoxPrefab!, "Loot_" + enemy.SourceId, position,
                (point, area) => point.Configure(CampaignEventLog.CorpseSource(enemy.SourceId), area, "gameplay.loot", Vector3.zero, 0f,
                    lootTypes, lootCounts, false));
        }

        // 레벨 정리가 부른다. 빌린 대상은 바로 뒤 SceneContext의 World 해제가 반환하거나 파괴하므로 목록과 표시 기록만 비운다.
        internal void Release()
        {
            _lostBodies.Clear();
            _runtimeInteractables.Clear();
            _displayBodies = null;
        }

        // 대여와 구성 순서는 바닥 아이템과 같은 RuntimePointSpawner가 맡는다. 대상 방향은 전투 월드 컨트롤러의 자세를 따른다.
        WorldObjectHandle? Spawn<T>(SpawnRule rule, string name, Vector3 position, Action<T, Collider?> configure) where T : InteractableBase
        {
            WorldObjectHandle? handle = RuntimePointSpawner.Spawn(_world.Objects, rule, name, new Pose(position, _owner.rotation), configure, out T? spawned);
            if (handle != null) _runtimeInteractables.Add(spawned!);
            return handle;
        }
    }

    // 시체, 유실물, 바닥 아이템이 함께 쓰는 런타임 상호작용 대상 대여다. 생성 규칙으로 비활성 상태로 빌려, 영역을 찾아 구성한 뒤
    // 활성화하고, 성공하면 그 대상을 돌려준다. 메인 스레드에서만 쓴다.
    internal static class RuntimePointSpawner
    {
        // 템플릿 Collider를 모을 때 재사용하는 목록이다. 메인 스레드에서만 쓰고, 찾은 뒤 바로 비워 풀 객체를 잡아 두지 않는다.
        static readonly List<Collider> _colliders = new();

        // 템플릿 루트에는 제작 도구가 붙인 대상 컴포넌트가 있고, 반환 때 그 OnDespawn이 대여별 값을 지운다.
        // 컴포넌트가 없으면 저작 결함이라 먼저 반환하고 Fail로 보고한다. 대여 실패는 World가 기록하고, 활성화가 거부되면 반환하고 null이다.
        // world는 SceneContext가 소유한 이 씬의 World다. 돌려주지 않은 대상은 씬 정리 때 World 해제가 파괴한다.
        internal static WorldObjectHandle? Spawn<T>(WorldObjectService world, SpawnRule rule, string name, Pose pose,
            Action<T, Collider?> configure, out T? spawned) where T : InteractableBase
        {
            spawned = null;
            WorldObjectHandle? handle = world.Spawn(rule, pose);
            if (handle == null) return null;
            GameObject instance = handle.Instance!;
            instance.name = name;
            if (!instance.TryGetComponent(out T interactable))
            {
                world.Despawn(handle);
                Log.Instance.Fail(new InvalidOperationException("런타임 대상 템플릿 루트에 " + typeof(T).Name + "가 없습니다: " + rule.name), rule);
                return null;
            }
            // 원본 전리품 상자의 interactCollider처럼 템플릿의 Interactable 레이어 Trigger를 대상 영역으로 쓴다. F 탐색 구가 이 Collider로
            // 대상을 찾으므로, 템플릿에 없으면 그 대상은 F로 찾을 수 없다.
            Collider? area = FindArea(instance);
            configure(interactable, area);
            if (world.Activate(handle))
            {
                spawned = interactable;
                return handle;
            }
            world.Despawn(handle);
            return null;
        }

        // 비활성 대여본의 자식까지 보고 첫 Interactable 레이어 Trigger를 돌려준다. 없으면 null이다.
        static Collider? FindArea(GameObject instance)
        {
            instance.GetComponentsInChildren(true, _colliders);
            Collider? area = null;
            for (int i = 0; i < _colliders.Count; i++)
            {
                Collider collider = _colliders[i];
                if (collider.isTrigger && collider.gameObject.layer == GameLayers.Interactable) { area = collider; break; }
            }
            _colliders.Clear();
            return area;
        }
    }
}
