#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 CostTakerHUD와 CostTakerHUD_Entry의 표시 규칙이다. 월드의 건설 지점(CostTaker)마다 공용 HUD 항목 하나를 만들고,
    // 지점이 아직 지어지지 않았고 카메라 앞이며 플레이어가 10m 안, 높이 차 2.5m 안일 때만 보인다. 항목은 안내 지점 0.5m 위를 따라간다.
    // 원작처럼 비용이 모자라도 이름과 "( 가진 수 / 필요 수 )"를 보여, 무엇을 가져와야 고칠 수 있는지 알린다. 가진 수는 소유 그래프가 바뀐 프레임에만 센다.
    // 전투 월드 컨트롤러가 HUD를 붙인 뒤 만들고 매 프레임 Tick을 부른다. HUD를 떼면 HUD가 항목 사본을 지운다.
    internal sealed class CostTakerHud
    {
        // 원작 CostTakerHUD_Entry의 HideDistance, HideDistanceYLimit와 UpdatePosition의 위쪽 0.5m다.
        const float _hideDistance = 10f, _hideDistanceYLimit = 2.5f, _markerLift = .5f;

        readonly GameplayWorldContext _world;
        readonly List<CostTaker> _takers = new();
        readonly List<OriginalCostTakerEntryView> _entries = new();
        // 마지막으로 가진 수를 센 소유 그래프다. 그래프는 확정마다 새 인스턴스라 참조가 같으면 수도 같다.
        ItemOwnershipState? _countedGraph;
        bool _counted;

        internal CostTakerHud(GameplayWorldContext world)
        {
            _world = world;
            foreach (InteractableBase interactable in world.Interactables)
            {
                if (!(interactable is CostTaker taker)) continue;
                OriginalCostTakerEntryView entry = world.Hud.CostTakers.Add();
                entry.Setup(GameLocalization.Text(taker.InteractNameKey));
                IReadOnlyList<(int id, int amount)> costs = taker.Costs;
                for (int index = 0; index < costs.Count; index++) entry.AddItem(world.Catalog.DefinitionOf(costs[index].id).Icon, costs[index].amount);
                _takers.Add(taker);
                _entries.Add(entry);
            }
        }

        internal void Tick()
        {
            if (_takers.Count == 0) return;
            OwnedItemCounts? counts = Recount();
            Camera camera = _world.Camera;
            Transform view = camera.transform;
            Vector3 player = _world.Motor.transform.position;
            for (int index = 0; index < _takers.Count; index++)
            {
                CostTaker taker = _takers[index];
                OriginalCostTakerEntryView entry = _entries[index];
                if (counts != null)
                {
                    IReadOnlyList<(int id, int amount)> costs = taker.Costs;
                    for (int row = 0; row < costs.Count; row++) entry.ShowPossess(row, counts.Count(costs[row].id));
                }
                // 원작은 켜진 지점만 등록한다(CostTaker.OnEnable). 제품 지점은 원본 CostTaker 물체의 영역 Collider를 쓰고, 건설 지점이
                // 건설 뒤 그 물체를 끄므로 영역 물체가 꺼졌으면 다 지은 지점이라 숨긴다. 꺼진 하위 씬의 지점도 같은 이유로 숨는다.
                Collider? area = taker.InteractCollider;
                Vector3 marker = taker.MarkerPosition + Vector3.up * _markerLift;
                Vector3 offset = player - taker.transform.position;
                bool shown = taker.isActiveAndEnabled && area != null && area.gameObject.activeInHierarchy
                    && Vector3.Dot(view.forward, marker - view.position) > 0f
                    && Mathf.Abs(offset.y) <= _hideDistanceYLimit && offset.magnitude <= _hideDistance;
                entry.Present(camera.WorldToScreenPoint(marker), shown, Time.unscaledDeltaTime);
            }
        }

        // 소유 그래프가 바뀌었거나 아직 세지 않았으면 지불과 같은 출처(ItemSourceRules.Owned)로 모두 센다. 바뀌지 않았으면 null이다.
        OwnedItemCounts? Recount()
        {
            if (!_world.TrySession(out GameSessionManager? session)) return null;
            ItemOwnershipState? graph = session!.Snapshot().Campaign.Gameplay.Inventory.Items;
            if (_counted && ReferenceEquals(graph, _countedGraph)) return null;
            _counted = true;
            _countedGraph = graph;
            return session.Inventory.CountAllOwned(ItemSourceRules.Owned(session));
        }
    }
}
