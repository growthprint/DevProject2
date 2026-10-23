#nullable enable
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.Economy.StockShop이다. 상점 인스턴스 하나의 품목, 실제 UTC 재고 새로고침, 구매, 판매 가격을 맡는다.
    // 재고와 마지막 새로고침 시각은 EconomyManager의 상점 기록(shops)에 상점 키로 두고, 원작처럼 저장하지 않는 "보임" 표시만 이 컴포넌트가 가진다.
    // 원작은 상인 ID로 재고를 나눠 쓰지만 자판기와 잡화 상점이 같은 상인이라, 저작한 상점 키로 따로 기록한다(사용자 결정 D1).
    // 열기(TryOpen)가 기록을 읽어 맞추고, 그 뒤 구매는 같은 기록일 때만 받는다. 불러오기로 기록이 바뀌면 다시 열어야 한다.
    // 원작 refreshStockOnStart와 표시 알림 키는 범위 안 상점이 모두 기본값이라 옮기지 않는다. 메인 스레드에서만 쓴다.
    [DisallowMultipleComponent]
    public sealed class StockShop : MonoBehaviour
    {
        // 원작 DoRefreshStock의 UnityEngine.Random.Range(0f, 1f) 추첨이다. 캡처 없는 정적 대리자라 새로고침마다 할당하지 않는다.
        static readonly Func<float> _roll = () => UnityEngine.Random.Range(0f, 1f);

        // 저장 기록 키다. 제안 값: 자판기 Base_SaleMachine, 잡화 상점 Base_GeneralStore. 씬 안에서 겹치지 않는다.
        [SerializeField] string _shopKey = "";
        // 원작 merchantID다. 품목은 원작 StockShopDatabase의 이 상인 프로필에서 옮긴다.
        [SerializeField] string _merchantId = "";
        [SerializeField] string _displayNameKey = "";
        // 원작 refreshAfterTimeSpan(TimeSpan 틱)이다. 자판기는 5분(3,000,000,000)이다.
        [SerializeField] long _refreshAfterTicks;
        [SerializeField] bool _accountAvailable;
        [SerializeField] bool _returnCash;
        [SerializeField] float _sellFactor = 0.5f;
        [SerializeField] StockShopSellPrice[] _overrideSellingPrice = Array.Empty<StockShopSellPrice>();
        [SerializeField] StockShopItem[] _items = Array.Empty<StockShopItem>();

        // 마지막으로 읽거나 쓴 상점 기록이다. 경제 소유자의 기록이 이 참조와 다르면 불러오기로 바뀐 것이라 다시 맞춘다.
        StockShopState? _record;
        // _items와 같은 순서의 원작 Entry.Show와 CurrentStock이다. 처음 열기 전에는 null이다.
        bool[]? _show;
        int[]? _stock;
        long _lastRefreshBinary;

        // 열기, 새로고침, 구매로 재고나 보임이 바뀐 뒤 알린다. 계좌 돈과 해금은 EconomyManager.Changed가 알린다. 호출자: 2-P2 상점 화면.
        internal event Action? Changed;

        internal string DisplayNameKey => _displayNameKey;
        // 원작 AccountAvaliable이다. 거짓이면 주운 현금만 쓴다(비밀 상인, 잠긴 잡화 상점, D7).
        internal bool AccountAvailable => _accountAvailable;
        // 원작 returnCash다. 참이면 판매 대금을 현금 아이템으로 준다. 판매는 TrySell이 이 값을 직접 넘기므로 호출자는 5-M 비밀 상인 화면이다.
        internal bool ReturnsCash => _returnCash;
        internal IReadOnlyList<StockShopItem> Items => _items;
        // 원작 Entry.Show와 CurrentStock이다. 열기 전에는 원작 Entry 기본값(보임, 재고 0)이다.
        internal bool IsShown(int index) => _show == null || _show[index];
        internal int StockAt(int index) => _stock != null ? _stock[index] : 0;

        // 원작 Entry의 해금 판정이다. 강제 해금 품목은 경제 소유자의 해금 기록과 상관없이 열려 있고 확인 대기가 되지 않는다.
        // 줄 표시, 상세, 줄 선택, 구매가 같은 판정을 쓴다.
        internal bool IsUnlocked(EconomyManager economy, int index) => _items[index].ForceUnlock || economy.IsUnlocked(_items[index].ItemId);
        internal bool IsWaitingForUnlock(EconomyManager economy, int index) =>
            !_items[index].ForceUnlock && economy.IsWaitingForUnlockConfirm(_items[index].ItemId);

        // 원작 NextRefreshETA다. 화면 카운트다운이 매 프레임 읽어도 저장 값을 바꾸지 않는다.
        internal TimeSpan NextRefreshEta(DateTime utcNow) => StockShopRules.NextRefreshEta(_lastRefreshBinary, utcNow, _refreshAfterTicks);

        // 원작 StockShopItemEntry가 보이는 품목 한 개 값(ConvertPrice)이다. 품목 ID는 저작 데이터라 카탈로그 정의가 늘 있다.
        internal long BuyPrice(OriginalGameplayCatalog catalog, int index) =>
            StockShopRules.BuyPrice(catalog.DefinitionOf(_items[index].ItemId), _items[index].PriceFactor);

        // 원작 ConvertPrice(판매)다. 아이템 트리 전체의 원가에 판매 배율(품목별 덮어쓰기 우선)을 곱해 내린다.
        // itemId는 화면이 같은 확정 그래프에서 고른 아이템이다. 호출자: 2-P2 판매 칸 표시와 판매 명령.
        internal long SellPrice(OriginalGameplayCatalog catalog, ItemOwnershipState graph, string itemId)
        {
            ItemInstanceState item = graph.Find(itemId)!;
            float factor = _sellFactor;
            for (int index = 0; index < _overrideSellingPrice.Length; index++)
                if (_overrideSellingPrice[index].ItemId == item.OriginalItemId)
                {
                    factor = _overrideSellingPrice[index].Factor;
                    break;
                }
            return StockShopRules.ApplyFactor(StockShopRules.TotalRawValue(catalog, graph, item), factor);
        }

        // 원작 EconomyManager.Cash(현금 아이템 451 수)다. 지불과 같은 세션 기본 출처로 센다. 호출자: 2-P2 상점 화면의 현금 표시.
        internal static long CashOwned(GameSessionManager session) =>
            session.Inventory.CountOwned(GameplayDataSettings.ItemAssets.CashItemTypeID, ItemSourceRules.Owned(session));

        // 원작 ShowUI의 RefreshIfNeeded다. 기록을 읽어 맞춘 뒤 새로고침할 때가 됐으면 품목마다 다시 굴리고 지금 시각을 적는다.
        // 미래 시각이나 2일보다 오래된 시각만 당긴 경우도 기록에 쓴다. 경제 관문이 닫혀 써야 할 기록을 못 쓰면 아무것도 바꾸지 않고 false다.
        // advancedDebuffMode는 난이도 소유자의 현재 규칙이다. 호출자: 2-W 상점 열기(2-P2 화면을 띄우기 전).
        internal bool TryOpen(EconomyManager economy, OriginalGameplayCatalog catalog, bool advancedDebuffMode, DateTime utcNow)
        {
            Sync(economy, catalog, advancedDebuffMode);
            bool due = StockShopRules.IsRefreshDue(_lastRefreshBinary, utcNow, _refreshAfterTicks, out long kept, out bool timeTravel);
            // 원작 GameManager.TimeTravelDetected는 로그 한 줄뿐이다. 특성 연구와 같은 문구로 남기려고 시계 역행 공통 기록을 쓴다.
            if (timeTravel) RealTimeRules.ReportTimeTravel("상점 재고 갱신 " + _shopKey, this);
            if (!due && kept == _lastRefreshBinary) return true;
            bool[] show = (bool[])_show!.Clone();
            int[] stock = (int[])_stock!.Clone();
            if (due)
            {
                kept = utcNow.ToBinary();
                for (int index = 0; index < _items.Length; index++)
                    show[index] = StockShopRules.Restock(_items[index], HiddenByMode(catalog, index, advancedDebuffMode), _roll, out stock[index]);
            }
            StockShopState record = Record(kept, stock);
            if (!economy.TrySetShop(record)) return false;
            _show = show;
            _stock = stock;
            _lastRefreshBinary = kept;
            _record = record;
            ObserverDispatch.InvokeEach(Changed);
            return true;
        }

        // 원작 StockShopView의 상호작용 버튼이다. 해금된 품목(강제 해금 포함)이면 하나를 사고, 해금 확인 대기 품목이면 해금을 확인한다.
        // 원작은 해금 확인을 화면이 했지만 화면은 의도만 보내도록 이 명령이 한다. 숨은 품목, 잠긴 품목, 품절, 열린 뒤 기록이 바뀐 경우,
        // 결제 거절은 기록 없이 Rejected이고 아무것도 바뀌지 않는다. dropped는 레이드 구매가 가방에 못 넣어 바닥에 떨굴 아이템이다.
        // index는 화면이 Items에서 고른 줄이다. 호출자: 2-P2 상점 화면, 5-M 비밀 상인.
        internal StockShopPurchase TryBuy(GameSessionManager session, EconomyManager economy, OriginalGameplayCatalog catalog, int index,
            out IReadOnlyList<ItemOwnershipState> dropped)
        {
            dropped = Array.Empty<ItemOwnershipState>();
            if (!OpenedRecordIsCurrent(economy) || !_show![index]) return StockShopPurchase.Rejected;
            StockShopItem item = _items[index];
            // 잠긴 품목 판정은 화면의 줄 표시와 같은 해금 판정을 쓴다.
            if (!IsUnlocked(economy, index))
                return economy.TryConfirmUnlock(item.ItemId) ? StockShopPurchase.UnlockConfirmed : StockShopPurchase.Rejected;
            // 원작 BuyTask는 재고 1 이상만 판다. 화면은 늘 한 개씩 사므로 수량 인자를 두지 않는다.
            int[] stock = (int[])_stock!.Clone();
            if (stock[index] < 1) return StockShopPurchase.Rejected;
            stock[index]--;
            StockShopState restocked = Record(_lastRefreshBinary, stock);
            if (!GameplayCommands.TryBuyShopItem(session, economy, catalog, item, _accountAvailable, restocked, out dropped))
                return StockShopPurchase.Rejected;
            _stock = stock;
            _record = restocked;
            ObserverDispatch.InvokeEach(Changed);
            return StockShopPurchase.Bought;
        }

        // 원작 StockShop.Sell이다. 이 상점의 판매 값(SellPrice)과 현금 반환 여부를 정해 판매 명령에 넘긴다(2-P2 결정).
        // 구매 TryBuy처럼 상점이 명령을 부르고 GameplayCommands는 상점 컴포넌트를 모르게 해, 의존이 상점 → 명령 한 방향이 되게 한다.
        // 판매는 상점 재고를 바꾸지 않아 Changed를 내지 않는다. 고른 아이템이 그사이 사라졌으면 기록 없이 false다.
        // itemId는 화면이 같은 확정 그래프에서 고른 아이템이다. 호출자: 2-P2 상점 화면, 5-M 비밀 상인.
        internal bool TrySell(GameSessionManager session, EconomyManager economy, OriginalGameplayCatalog catalog, string itemId)
        {
            ItemOwnershipState? graph = session.Snapshot().Campaign.Gameplay.Inventory.Items;
            if (graph == null || graph.Find(itemId) == null) return false;
            return GameplayCommands.TrySellShopItem(session, economy, catalog, itemId, SellPrice(catalog, graph, itemId), _returnCash);
        }

        // 원작 Load와 SetupSaveData다. 경제 소유자의 기록이 마지막으로 맞춘 참조와 같으면 그대로 둔다. 다르면 원작 Entry 기본값(보임, 재고 0)에서
        // 시작해 기록에 있는 품목만 재고를 덮고 보임을 다시 정한다. 기록이 없으면 마지막 새로고침은 원작 씬 값처럼 0이라 첫 열기에서 새로고침한다.
        void Sync(EconomyManager economy, OriginalGameplayCatalog catalog, bool advancedDebuffMode)
        {
            economy.TryGetShop(_shopKey, out StockShopState? record);
            if (_show != null && ReferenceEquals(record, _record)) return;
            _record = record;
            _show = new bool[_items.Length];
            _stock = new int[_items.Length];
            _lastRefreshBinary = record != null ? record.LastRefreshBinary : 0;
            for (int index = 0; index < _items.Length; index++)
            {
                _show[index] = true;
                if (record == null || !TryStock(record, _items[index].ItemId, out int saved)) continue;
                _stock[index] = saved;
                _show[index] = StockShopRules.ShowAfterLoad(HiddenByMode(catalog, index, advancedDebuffMode), saved, _items[index].Possibility);
            }
        }

        // 열기(TryOpen)가 맞춘 기록이 경제 소유자의 지금 기록과 같은지다. 열기 전이거나 불러오기로 기록이 바뀌었으면 거짓이라
        // 구매를 받지 않는다. 참이면 _show가 있다.
        bool OpenedRecordIsCurrent(EconomyManager economy) =>
            _show != null && economy.TryGetShop(_shopKey, out StockShopState? current) && ReferenceEquals(current, _record);

        // 품목 ID는 저작 데이터라 카탈로그 정의가 늘 있다.
        bool HiddenByMode(OriginalGameplayCatalog catalog, int index, bool advancedDebuffMode) =>
            StockShopRules.IsHiddenByMode(catalog.DefinitionOf(_items[index].ItemId), advancedDebuffMode);

        // 원작 GenerateSaveData처럼 모든 품목의 재고를 품목 순서로 담는다. 품목 ID가 겹치지 않는 저작 데이터라 믿을 수 있는 입력용 생성자를 쓴다.
        StockShopState Record(long lastRefreshBinary, int[] stock)
        {
            var stocks = new ShopStockState[_items.Length];
            for (int index = 0; index < _items.Length; index++) stocks[index] = new ShopStockState(_items[index].ItemId, stock[index]);
            return new StockShopState(_shopKey, _merchantId, lastRefreshBinary, stocks);
        }

        static bool TryStock(StockShopState record, int itemId, out int stock)
        {
            IReadOnlyList<ShopStockState> stocks = record.Stocks;
            for (int index = 0; index < stocks.Count; index++)
                if (stocks[index].OriginalItemId == itemId)
                {
                    stock = stocks[index].Stock;
                    return true;
                }
            stock = 0;
            return false;
        }
    }
}
