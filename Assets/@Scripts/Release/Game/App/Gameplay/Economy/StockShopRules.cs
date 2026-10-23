#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 StockShopDatabase.ItemEntry다. 상점 컴포넌트(StockShop._items)에 원작 상인 프로필 순서대로 저작한다.
    // 원작 lockInDemo는 데모 빌드에서만 강제 해금을 끄므로 옮기지 않는다. 같은 상점 안에서 아이템 ID는 겹치지 않는다(씬 데이터 테스트가 확인한다).
    [Serializable]
    public struct StockShopItem
    {
        [SerializeField] int _itemId;
        [SerializeField] int _maxStock;
        [SerializeField] bool _forceUnlock;
        [SerializeField] float _priceFactor;
        [SerializeField] float _possibility;

        public int ItemId => _itemId;
        // 원작 Entry.MaxStock처럼 1보다 작은 최대 재고는 1로 읽는다.
        public int MaxStock => Math.Max(1, _maxStock);
        public bool ForceUnlock => _forceUnlock;
        public float PriceFactor => _priceFactor;
        public float Possibility => _possibility;
    }

    // 원작 StockShop.OverrideSellingPriceEntry다. 그 아이템의 판매 배율을 상점 기본 배율 대신 쓴다.
    [Serializable]
    public struct StockShopSellPrice
    {
        [SerializeField] int _itemId;
        [SerializeField] float _factor;

        public int ItemId => _itemId;
        public float Factor => _factor;
    }

    // 상점 품목 칸을 누른 결과다. 원작 StockShopView는 해금된 품목이면 사고, 해금 확인 대기 품목이면 해금을 확인한다.
    internal enum StockShopPurchase { Rejected, Bought, UnlockConfirmed }

    // 원작 StockShop의 새로고침, 재고 추첨, 가격 규칙이다. 시각, 난수, 아이템 정의를 인자로 받아 상태 없이 판정한다.
    internal static class StockShopRules
    {
        // 원작 GameplayDataSettings.Tags.AdvancedDebuffMode와 Item.CanBeSold가 보는 태그 이름이다.
        const string _advancedDebuffTag = "AdvancedDebuffMode";
        const string _notSellableTag = "NotSellable";
        // 원작 RefreshIfNeeded가 저장 시각을 당기는 가장 오래된 경과(2일)다.
        static readonly TimeSpan _maxRefreshAge = TimeSpan.FromDays(2.0);

        // 원작 RefreshIfNeeded다. 저장 시각이 미래면 지금으로 당기고(timeTravel), 2일보다 오래됐으면 저장 값만 2일 전으로 당긴다.
        // 다시 굴릴지는 2일로 당기기 전 값으로 판정하며 경과가 주기보다 커야 한다. keptBinary는 다시 굴리지 않을 때 남길 저장 시각이다.
        // 저장 시각은 StockShopState 검사를 통과한 DateTime.ToBinary 값이라 변환이 늘 된다.
        internal static bool IsRefreshDue(long lastRefreshBinary, DateTime utcNow, long refreshAfterTicks, out long keptBinary,
            out bool timeTravel)
        {
            DateTime last = DateTime.FromBinary(lastRefreshBinary);
            keptBinary = lastRefreshBinary;
            timeTravel = last > utcNow;
            if (timeTravel)
            {
                last = utcNow;
                keptBinary = utcNow.ToBinary();
            }
            DateTime oldest = utcNow - _maxRefreshAge;
            if (last < oldest) keptBinary = oldest.ToBinary();
            return utcNow - last > TimeSpan.FromTicks(refreshAfterTicks);
        }

        // 원작 NextRefreshETA다. 화면의 새로고침 남은 시간이며, 미래 저장 시각은 지금으로 보고 0 아래는 0이다. 저장 값은 바꾸지 않는다.
        internal static TimeSpan NextRefreshEta(long lastRefreshBinary, DateTime utcNow, long refreshAfterTicks)
        {
            DateTime last = DateTime.FromBinary(lastRefreshBinary);
            if (last > utcNow) last = utcNow;
            TimeSpan left = TimeSpan.FromTicks(refreshAfterTicks) - (utcNow - last);
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }

        // 원작 DoRefreshStock의 품목 하나다. 0과 1 사이 확률만 추첨하고(roll이 확률보다 크면 숨김), 고급 디버프 모드가 꺼졌으면
        // 그 태그 품목을 숨긴다. 숨긴 품목은 재고 0, 보이는 품목은 최대 재고다. 돌려주는 값은 보이는지다.
        internal static bool Restock(StockShopItem item, bool hiddenByMode, Func<float> roll, out int stock)
        {
            if (RolledOut(item.Possibility, roll) || hiddenByMode)
            {
                stock = 0;
                return false;
            }
            stock = item.MaxStock;
            return true;
        }

        // 원작 DoRefreshStock의 확률 추첨이다. 0과 1 사이 확률만 굴리고, 굴린 값이 확률보다 크면 이번 새로고침에서 빠진다.
        // 확률이 범위 밖이면 굴리지 않아 난수 소비 횟수가 원작과 같다.
        static bool RolledOut(float possibility, Func<float> roll) => possibility > 0f && possibility < 1f && roll() > possibility;

        // 원작 SetupSaveData다. 저장된 품목은 모드가 허용하고, 재고가 남았거나 늘 나오는 품목(확률 1 이상)일 때 보인다.
        internal static bool ShowAfterLoad(bool hiddenByMode, int stock, float possibility) => !hiddenByMode && (stock > 0 || possibility >= 1f);

        // 원작 DoRefreshStock과 SetupSaveData의 모드 검사다. 고급 디버프 모드가 꺼졌으면 그 태그 품목을 숨긴다.
        internal static bool IsHiddenByMode(OriginalItemDefinition definition, bool advancedDebuffMode) =>
            !advancedDebuffMode && definition.HasTag(_advancedDebuffTag);

        // 원작 Item.CanBeSold다. 사망 보호(Sticky) 아이템과 NotSellable 태그 아이템은 팔 수 없다. 호출자: 2-P2 판매 칸 표시와 판매 명령.
        internal static bool CanBeSold(OriginalItemDefinition definition) => !definition.Sticky && !definition.HasTag(_notSellableTag);

        // 원작 구매는 새로 만든 아이템 하나를 준다. 겹치는 아이템은 원본 기본 수량 한 묶음이고, 겹치지 않으면 1개다.
        internal static int BuyCount(OriginalItemDefinition definition) => definition.MaxStack > 1 ? Math.Max(1, definition.DefaultCount) : 1;

        // 원작 ConvertPrice(구매)다. 새 아이템의 GetTotalRawValue(내구도 가득, 기본 수량, 꽂힌 것 없음)에 품목 가격 배율을 곱해 내린다.
        internal static long BuyPrice(OriginalItemDefinition definition, float priceFactor) =>
            ApplyFactor((long)definition.Value * BuyCount(definition), priceFactor);

        // 원작 ConvertPrice의 배율 곱이다. 원작처럼 float로 곱하고 내린다.
        internal static long ApplyFactor(long rawValue, float factor) => (long)Math.Floor((float)rawValue * factor);

        // 원작 Item.GetTotalRawValue다. 가격에 남은 내구도 비율을 곱해 내리고, 겹치는 아이템은 수량을 곱한 뒤 칸과 내부 인벤토리의
        // 아이템 값을 더한다. 내구도 기록이 없으면 가득 찬 것이다. 그래프와 아이템 정의는 검증된 세션 값이라 하위 노드와 정의가 늘 있다.
        internal static long TotalRawValue(OriginalGameplayCatalog catalog, ItemOwnershipState graph, ItemInstanceState item)
        {
            OriginalItemDefinition definition = catalog.DefinitionOf(item.OriginalItemId);
            float value = definition.Value;
            if (definition.Durability > 0f) value *= (item.AbsoluteDurability ?? definition.Durability) / definition.Durability;
            long total = (long)Mathf.FloorToInt(value) * (definition.MaxStack > 1 ? item.Quantity : 1);
            IReadOnlyList<ItemSlotState> slots = item.Slots;
            for (int index = 0; index < slots.Count; index++) total += TotalRawValue(catalog, graph, graph.Find(slots[index].InstanceId)!);
            if (item.Inventory != null)
            {
                IReadOnlyList<ItemPositionState> entries = item.Inventory.Entries;
                for (int index = 0; index < entries.Count; index++) total += TotalRawValue(catalog, graph, graph.Find(entries[index].InstanceId)!);
            }
            return total;
        }
    }
}
