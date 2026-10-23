#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.App.World;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 ItemExtensions.Drop과 InteractablePickup처럼 가방이 줄어 넘친 아이템을 플레이어 주변 바닥에 떨구고, F 한 번으로 다시 줍게 한다.
    // 전투 월드 컨트롤러가 준비 때 만들어 소유한다. 바닥 아이템은 저장하지 않고, 빌린 지점은 레벨 정리 직후 SceneContext의 World 해제가 치운다.
    internal sealed class DroppedItemSpawner
    {
        // 원작 Throw의 수평 속력 상한과 위로 던지는 속력이다. 물리 대신 같은 높이에 떨어질 때까지 나는 거리로 바꿔 쓴다.
        const float _throwSpeed = 3f;
        const float _throwUpSpeed = 2.5f;
        // 수평 비행 거리는 상한 거리의 0.5~1배에서 고르고, 중력은 0.01 아래로 보지 않아 0으로 나누지 않는다.
        const float _throwDistanceMin = .5f, _throwDistanceMax = 1f;
        const float _minGravity = .01f;
        // 원작 UpdateInventoryCapacity와 PickupItem 실패의 Drop(위치, 강체, Vector3.forward, 360)처럼 사방으로 흔드는 각도다.
        const float _scatterAngle = 360f;
        // 바닥은 떨어질 자리의 발밑 위 1m에서 아래로 3m까지 찾는다. 경사와 낮은 단차를 덮는 범위다.
        const float _groundProbeHeight = 1f;
        const float _groundProbeDistance = 3f;
        // 원작 Rigidbody와 고체 구(r 0.5)가 벽에 막히듯, 허리 아래 높이에서 구를 쓸어 경로 옆 벽까지 잡고 벽 앞 0.2m에 멈춘다.
        // 구가 시작점에서 벽에 겹치면 SphereCast가 그 벽을 못 보므로 반경은 플레이어 CharacterController 반경 0.3m보다 작게 둔다.
        const float _wallProbeHeight = .5f;
        const float _wallProbeRadius = .25f;
        const float _wallMargin = .2f;

        readonly GameplayWorldContext _world;
        // 런타임 지점의 방향은 시체 지점처럼 전투 월드 컨트롤러의 자세를 따른다.
        readonly Transform _owner;
        // 대여와 활성화에 성공한 바닥 아이템이다. 같은 인덱스의 _handles와 _items가 그 아이템의 대여와 아이템 그래프다.
        // Spawn이 셋을 함께 넣고, TryPickUp과 Release가 셋을 함께 뺀다.
        readonly List<InteractablePickup> _runtimePoints = new();
        readonly List<WorldObjectHandle> _handles = new();
        readonly List<ItemOwnershipState> _items = new();
        // 이 월드에서 떨군 순번이다. 지점 ID를 "drop:<순번>"으로 만들어 같은 아이템을 여러 번 떨궈도 겹치지 않게 한다.
        int _dropCount;

        internal DroppedItemSpawner(GameplayWorldContext world, Transform owner)
        {
            _world = world!;
            _owner = owner!;
        }

        // 상호작용 객체가 F 대상 후보로 읽는 바닥 아이템이다. 이미 파괴된 인스턴스는 Unity null이라 판정이 건너뛴다.
        internal IReadOnlyList<InteractablePickup> RuntimePoints => _runtimePoints;

        // 루트가 하나인 아이템 그래프마다 바닥 아이템 지점을 원작 pickupAgentPrefab 규칙으로 빌린다. 이름은 루트 아이템 정의의 이름 키이고,
        // 모양은 템플릿의 OriginalPickupGraphic이 그린다. 규칙 연결은 앱 시작 검증이 확인했고, 대여 실패는 World가 기록한다.
        // 원작 ItemExtensions.Drop(pos, createRigidbody, dropDirection, randomAngle)처럼 direction을 좌우 randomAngle의 절반 안에서 흔든다.
        internal void Spawn(IReadOnlyList<ItemOwnershipState> dropped, Vector3 origin, Vector3 direction, float randomAngle)
        {
            // 원작 Throw처럼 수평 방향만 쓴다. 호출자는 원작 UpdateInventoryCapacity와 PickupItem 실패처럼 Vector3.forward를 넘겨 방향은 늘 있다.
            direction.y = 0f;
            direction.Normalize();
            for (int i = 0; i < dropped.Count; i++)
            {
                ItemOwnershipState items = dropped[i];
                if (!TryRootDefinition(items, out OriginalItemDefinition? found)) continue;
                // 대여와 목록 넣기는 과제 아이템과 같은 Place가 맡는다.
                Place(items, found!, LandingPoint(origin, direction, randomAngle));
            }
        }

        // 소지품에 넣지 못한 아이템을 원작 Drop(위치, 강체, Vector3.forward, 360)처럼 플레이어 위치에서 사방으로 떨군다.
        internal void SpawnAroundPlayer(IReadOnlyList<ItemOwnershipState> dropped) =>
            Spawn(dropped, _world.PlayerPosition, Vector3.forward, _scatterAngle);

        // 원작 SpawnItemForTask의 InstantiateAsync(itemID)와 Drop(pos, createRigidbody: false, Vector3.zero, 0f)이다. 새 아이템 하나를
        // 던지지 않고 그 자리에 바닥 아이템으로 두고 이 객체의 목록에 넣는다. 그래서 줍기(TryPickUp)가 넘친 아이템과 같은 경로로 받는다.
        // 정의가 없거나 대여에 실패하면 기록 없이 null이다(대여 실패는 World가 기록한다). 호출자: QuestTaskSpawner(전투 월드 컨트롤러가 Bind로 넘긴다).
        internal InteractablePickup? SpawnQuestItem(int itemId, Vector3 position)
        {
            if (!_world.Catalog.TryGet(itemId, out OriginalItemDefinition? definition)) return null;
            // 원작 새 아이템은 1개 묶음이다. 그래프 모양은 GameplayInventory 바닥 그래프(루트 하나, 루트 키 _dropRoot)와 같고,
            // ID는 저장 아이템과 같은 영구 ID 규칙으로 만든다. 줍기는 루트 키를 읽지 않는다.
            ItemInstanceState item = definition.CreateItem(PersistentIds.New(), 1);
            var items = new ItemOwnershipState(new[] { item }, new[] { new ItemSlotState(GameplayInventory._dropRoot, item.InstanceId) },
                Array.Empty<StoredInventoryState>());
            return Place(items, definition, position);
        }

        // 원작 LootBoxLoader.CreateCash가 상자에 못 넣은 새 현금을 Drop(pos, createRigidbody: true, dropDirection, randomAngle)으로 던지듯,
        // 새 아이템 한 묶음을 Spawn과 같은 규칙으로 사방에 떨군다. 원작 StackCount처럼 최대 중첩을 넘는 수량은 줄인다. 호출자는 1 이상을 넘기고,
        // 정의가 없으면 기록 없이 남기지 않는다. 그래프 모양과 ID 규칙은 SpawnQuestItem과 같다.
        internal void SpawnNewItem(int itemId, int count, Vector3 origin)
        {
            if (!_world.Catalog.TryGet(itemId, out OriginalItemDefinition? definition)) return;
            ItemInstanceState item = definition.CreateItem(PersistentIds.New(), Math.Min(count, definition.MaxStack));
            var items = new ItemOwnershipState(new[] { item }, new[] { new ItemSlotState(GameplayInventory._dropRoot, item.InstanceId) },
                Array.Empty<StoredInventoryState>());
            Spawn(new[] { items }, origin, Vector3.forward, _scatterAngle);
        }

        // 원작 pickupAgentPrefab 규칙으로 지점을 빌려 아이템 이름과 모양을 넣고, 성공하면 세 목록에 함께 넣는다.
        InteractablePickup? Place(ItemOwnershipState items, OriginalItemDefinition definition, Vector3 position)
        {
            SpawnRule rule = GameplayDataSettings.Prefabs.PickupAgentPrefab!;
            string sourceId = "drop:" + (++_dropCount).ToString(CultureInfo.InvariantCulture);
            WorldObjectHandle? handle = RuntimePointSpawner.Spawn<InteractablePickup>(_world.Objects, rule, "Pickup_" + sourceId,
                new Pose(position, _owner.rotation), (point, area) =>
                {
                    point.Configure(sourceId, area, definition.NameKey);
                    // 원작 InteractablePickup.CreateGraphic처럼 템플릿의 표시 컴포넌트가 아이템 모양을 그린다.
                    OriginalPickupGraphic graphic = point.GetComponentInChildren<OriginalPickupGraphic>(true);
                    graphic.Show(definition);
                }, out InteractablePickup? pickup);
            // 대여에 실패하면 그 아이템은 바닥에 남지 않는다. 대여 실패는 World가, 템플릿 결함은 RuntimePointSpawner가 기록하고, 활성화가 거부되면 기록 없이 반환한다.
            if (handle == null) return null;
            // 원작 Drop처럼 떨군 순간의 하위 씬 묶음 아래에 두어, 그 하위 씬을 떠나면 숨고 돌아오면 다시 보이게 한다. 지울 때는 World 반환이
            // 보관 루트로 되옮기므로 풀과 부딪히지 않는다. 켠 하위 씬이 없으면 World 루트에 그대로 둔다.
            Transform? parent = _world.DropParent();
            if (parent != null) pickup!.transform.SetParent(parent, true);
            _runtimePoints.Add(pickup!);
            _handles.Add(handle);
            _items.Add(items);
            return pickup;
        }

        // 원작 InteractablePickup.OnInteractStart처럼 바닥 아이템을 소지품으로 되돌린다. 세션 명령이 받아들이면 지점을 목록에서 빼고
        // World에 돌려준다. 거절은 rejection으로 알리고 지점은 그대로 둔다. 씬 정리 중이라 세션이 없으면 기록 없이 false다.
        internal bool TryPickUp(InteractablePickup point, out InventoryRejection rejection)
        {
            rejection = InventoryRejection.None;
            int index = IndexOf(point);
            // 바닥 아이템은 이 객체만 빌리고, 씬에 저작한 바닥 아이템은 EditMode 씬 데이터 테스트가 막아 목록에 늘 있다. 그래서 세션만 본다.
            if (!_world.TrySession(out GameSessionManager? session)) return false;
            if (!session!.Inventory.TryPickUp(_world.Catalog, _items[index], out rejection)) return false;
            WorldObjectHandle handle = _handles[index];
            // 풀이 인스턴스를 재사용하므로, 반환 전에 목록에서 빼 다음 대여 때 두 번 들어가지 않게 한다.
            _runtimePoints.RemoveAt(index);
            _handles.RemoveAt(index);
            _items.RemoveAt(index);
            _world.Objects.Despawn(handle);
            return true;
        }

        // 레벨 정리가 부른다. 빌린 지점은 바로 뒤 SceneContext의 World 해제가 반환하거나 파괴하므로 목록만 비운다.
        // 원작처럼 바닥 아이템은 저장하지 않아 씬을 떠나면 사라진다.
        internal void Release()
        {
            _runtimePoints.Clear();
            _handles.Clear();
            _items.Clear();
        }

        // 넘친 아이템 그래프는 루트가 하나이고, 그 루트 아이템의 정의로 이름과 모양을 정한다. 검증된 그래프라 루트 아이템은 늘 있다.
        // 정의가 없는 아이템은 떨구지 않는다.
        bool TryRootDefinition(ItemOwnershipState items, out OriginalItemDefinition? definition)
        {
            definition = null;
            IReadOnlyList<ItemSlotState> roots = items.Roots;
            ItemInstanceState root = items.Find(roots[0].InstanceId)!;
            // 넘친 아이템은 세션 소지품에서 나왔으므로 저장 읽기 검사로 정의가 늘 있다. 누락은 코드 결함뿐이다.
            return _world.Catalog.TryGet(root.OriginalItemId, out definition);
        }

        // 원작 Throw의 포물선이 같은 높이에 내려앉는 거리만큼, 흔든 방향으로 옮긴 뒤 바닥에 붙인다. 벽에 막히면 벽 앞에 놓는다.
        // 바닥을 못 찾으면 원작이 계속 떨어지는 아이템을 던진 자리로 되돌리듯 시작점에 놓는다.
        static Vector3 LandingPoint(Vector3 origin, Vector3 direction, float randomAngle)
        {
            Vector3 heading = Quaternion.Euler(0f, UnityEngine.Random.Range(-randomAngle, randomAngle) * .5f, 0f) * direction;
            float flight = 2f * _throwUpSpeed / Mathf.Max(_minGravity, -Physics.gravity.y);
            float distance = UnityEngine.Random.Range(_throwDistanceMin, _throwDistanceMax) * _throwSpeed * flight;
            GameplayDataSettings.LayersData layers = GameplayDataSettings.Layers;
            if (Physics.SphereCast(origin + Vector3.up * _wallProbeHeight, _wallProbeRadius, heading, out RaycastHit wall, distance + _wallMargin,
                    layers.WallLayerMask, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(0f, wall.distance - _wallMargin);
            Vector3 target = origin + heading * distance;
            return Physics.Raycast(target + Vector3.up * _groundProbeHeight, Vector3.down, out RaycastHit ground, _groundProbeDistance,
                layers.GroundLayerMask, QueryTriggerInteraction.Ignore) ? ground.point : origin;
        }

        // 같은 인스턴스인지 참조로 찾는다. 풀이 인스턴스를 재사용해도 목록에는 지금 대여 중인 지점만 있다.
        int IndexOf(InteractablePickup point)
        {
            for (int i = 0; i < _runtimePoints.Count; i++)
                if (ReferenceEquals(_runtimePoints[i], point)) return i;
            return -1;
        }
    }
}
