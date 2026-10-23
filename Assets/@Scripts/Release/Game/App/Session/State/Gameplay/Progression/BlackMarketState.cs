#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 암시장의 초기화 여부와 갱신 기회를 결과 목록과 함께 보존한다.
    public sealed class BlackMarketState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        // 수요와 공급 목록은 따로 한 번 복사하고, null 목록은 빈 목록으로 둔다.
        public BlackMarketState(bool valid, long lastRefreshBinary, int refreshChance,
            IReadOnlyList<MarketEntryState> demands, IReadOnlyList<MarketEntryState> supplies)
        {
            Valid = valid; LastRefreshBinary = lastRefreshBinary; RefreshChance = refreshChance;
            Demands = ReadOnlyLists.CopyOrEmpty(demands); Supplies = ReadOnlyLists.CopyOrEmpty(supplies);
        }

        // TryCreate가 쓰는 검사다. 복사는 생성자가 한 번만 하므로 여기서는 판정만 한다.
        // 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(long lastRefreshBinary, int refreshChance, IReadOnlyList<MarketEntryState>? demands,
            IReadOnlyList<MarketEntryState>? supplies)
        {
            if (!StockShopState.IsRefreshBinary(lastRefreshBinary))
                return false;
            if (refreshChance < 0) return false;
            if (!ReadOnlyLists.AreUnique(demands, state => state.OriginalItemId)) return false;
            return ReadOnlyLists.AreUnique(supplies, state => state.OriginalItemId);
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(bool valid, long lastRefreshBinary, int refreshChance,
            IReadOnlyList<MarketEntryState> demands, IReadOnlyList<MarketEntryState> supplies,
            [NotNullWhen(true)] out BlackMarketState? value)
        {
            value = Check(lastRefreshBinary, refreshChance, demands, supplies)
                ? new BlackMarketState(valid, lastRefreshBinary, refreshChance, demands, supplies) : null;
            return value != null;
        }
        public bool Valid { get; }
        public long LastRefreshBinary { get; }
        public int RefreshChance { get; }
        public IReadOnlyList<MarketEntryState> Demands { get; }
        public IReadOnlyList<MarketEntryState> Supplies { get; }
    }
}
