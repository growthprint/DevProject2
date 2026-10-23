#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 런타임 명령이 바꾸지 않고, 로드한 값을 저장 때 그대로 돌려 쓰는 원본 진행 기록이다. 시설, 경제, 비콘, 제조법, 상점, 암시장을 묶는다.
    // 각 필드의 null은 미포착이고, 빈 목록은 비어 있다고 확인된 값이다.
    public sealed class ProgressionPersistenceState
    {
        // 믿을 수 있는 입력용 생성자다. 생략한 인자는 미포착(null)이다.
        // null이 아닌 목록은 따로 한 번 복사한다. 저장 로드는 모든 인자가 필수인 TryCreate를 쓴다.
        public ProgressionPersistenceState(BuildingPersistenceState? buildings = null, EconomyPersistenceState? economy = null,
            IReadOnlyList<BeaconUnlockState>? unlockedBeacons = null, IReadOnlyList<string>? unlockedFormulas = null,
            IReadOnlyList<StockShopState>? shops = null, BlackMarketState? blackMarket = null)
        {
            Buildings = buildings; Economy = economy; UnlockedBeacons = ReadOnlyLists.CopyOrNull(unlockedBeacons);
            UnlockedFormulas = ReadOnlyLists.CopyOrNull(unlockedFormulas); Shops = ReadOnlyLists.CopyOrNull(shops); BlackMarket = blackMarket;
        }

        // TryCreate가 쓰는 검사다. null 목록은 미포착이라 통과하고, 포착한 목록만 null 항목과 키 중복을 거절한다.
        // 판정만 하고 복사하지 않으며, 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(IReadOnlyList<BeaconUnlockState>? unlockedBeacons, IReadOnlyList<string>? unlockedFormulas,
            IReadOnlyList<StockShopState>? shops)
        {
            if (unlockedBeacons != null && !ReadOnlyLists.AreUnique(unlockedBeacons, beacon => (beacon.BeaconId, beacon.BeaconIndex)))
                return false;
            if (unlockedFormulas != null && !SaveKeys.AreValid(unlockedFormulas))
                return false;
            // 원작은 자판기와 잡화점이 같은 상인(Merchant_Normal)을 쓰므로 상인이 아니라 상점 키로 중복을 막는다.
            if (shops != null && !ReadOnlyLists.AreUnique(shops, shop => shop.ShopKey)) return false;
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(BuildingPersistenceState? buildings, EconomyPersistenceState? economy,
            IReadOnlyList<BeaconUnlockState>? unlockedBeacons, IReadOnlyList<string>? unlockedFormulas,
            IReadOnlyList<StockShopState>? shops, BlackMarketState? blackMarket,
            [NotNullWhen(true)] out ProgressionPersistenceState? value)
        {
            value = Check(unlockedBeacons, unlockedFormulas, shops)
                ? new ProgressionPersistenceState(buildings, economy, unlockedBeacons, unlockedFormulas, shops,
                blackMarket) : null;
            return value != null;
        }

        public BuildingPersistenceState? Buildings { get; }
        public EconomyPersistenceState? Economy { get; }
        public IReadOnlyList<BeaconUnlockState>? UnlockedBeacons { get; }
        public IReadOnlyList<string>? UnlockedFormulas { get; }
        public IReadOnlyList<StockShopState>? Shops { get; }
        public BlackMarketState? BlackMarket { get; }
    }
}
