#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 CostTaker다. 원작 ConstructionSite.Awake가 넣은 비용(아이템과 개수)을 낼 수 있을 때만 고를 수 있고(드라이버가 확인),
    // 끝나면 호스트가 비용을 낸 뒤 건설 지점(ConstructionSite)에 알린다. 원작 비용의 돈은 제로존 지점이 모두 0이라 옮기지 않는다.
    [DisallowMultipleComponent]
    public sealed class CostTaker : InteractableBase
    {
        // 원작 Cost.ItemEntry다.
        [Serializable]
        internal struct Entry
        {
            [SerializeField] int _id;
            [SerializeField] int _amount;
            internal Entry(int id, int amount) { _id = id; _amount = amount; }
            internal int Id => _id;
            internal int Amount => _amount;
        }

        [SerializeField] Entry[] _items = Array.Empty<Entry>();
        [SerializeField] ConstructionSite? _site;
        (int id, int amount)[]? _costs;

        // 세션 명령이 받는 비용 목록이다. 직렬화 값은 실행 중 바뀌지 않으므로 처음 읽을 때 한 번 만든다.
        internal IReadOnlyList<(int id, int amount)> Costs
        {
            get
            {
                if (_costs != null) return _costs;
                var costs = new (int id, int amount)[_items.Length];
                for (int index = 0; index < _items.Length; index++) costs[index] = (_items[index].Id, _items[index].Amount);
                return _costs = costs;
            }
        }

        // 원작 OnInteractFinished다. 원작 onPayed가 건설 지점의 OnBuilt를 부른다.
        internal override void OnInteractFinished(IInteractionHost host)
        {
            if (host.TryPay(this) && _site != null) _site.OnBuilt();
        }

        // 제작 도구가 쓰는 구성이다.
        internal void ConfigureCost(Entry[] items, ConstructionSite? site)
        {
            _items = items; _site = site; _costs = null;
        }
    }
}
