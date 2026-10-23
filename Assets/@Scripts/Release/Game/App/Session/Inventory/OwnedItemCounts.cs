#nullable enable
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 확정 소유 그래프 하나에서 고른 출처의 원본 아이템 수를 한 번에 센 결과다. 화면이 다시 그리기 한 번에 비용 여러 개를 볼 때
    // 행마다 가방 전체를 다시 세지 않도록 SessionInventoryCommands.CountAllOwned가 만든다(Docs/runtime-boundaries.md#main-thread-split).
    // 센 뒤에 그래프가 바뀌면 낡으므로 그 다시 그리기 안에서만 쓴다.
    internal sealed class OwnedItemCounts
    {
        readonly Dictionary<int, long> _counts;
        // 그래프가 아직 없으면(월드 첫 초기화 전) HasOwned처럼 어떤 비용도 넉넉하지 않다.
        readonly bool _hasGraph;

        // 세는 함수만 바꿔 소유 그래프 확인과 같은 합치기 규칙(GameplayInventory.HasIn)을 쓴다. 캡처가 없어 확인마다 할당하지 않는다.
        static readonly Func<OwnedItemCounts, int, ItemSources, long> _count = (owned, id, _) => owned.Count(id);

        internal OwnedItemCounts(Dictionary<int, long> counts, ItemSources sources, bool hasGraph)
        {
            _counts = counts;
            Sources = sources;
            _hasGraph = hasGraph;
        }

        // 센 출처다. 지불 확인은 이 출처를 지불 출처로 쓴다.
        internal ItemSources Sources { get; }

        internal long Count(int originalItemId) => _counts.TryGetValue(originalItemId, out long count) ? count : 0;

        // SessionInventoryCommands.HasOwned(비용 항목과 extra)와 같은 판정을 센 수로 한다.
        internal bool Has(IReadOnlyList<Cost.ItemEntry> costs, (int id, int amount) extra) =>
            _hasGraph && GameplayInventory.HasIn(this, _count, costs, extra, Sources);
    }
}
