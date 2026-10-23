#nullable enable
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 원작 StockShop.BuyTask의 결제, 배달, 재고 차감과 StockShop.Sell의 제거, 대금 지급이다. 계좌 돈과 상점 재고 기록은 EconomyManager가,
    // 현금과 사고판 아이템은 세션 소지품이 소유한다. 품목이 보이는지, 해금됐는지, 재고가 남았는지는 상점 인스턴스 상태를 가진 StockShop.TryBuy가
    // 구매 명령 전에 본다. 판매는 StockShop.TrySell이 그 상점의 판매 값과 현금 반환 여부를 정해 부른다.
    // 두 명령 모두 상점 컴포넌트(StockShop)를 받지 않고 값만 받는다. 상점 → 명령 한 방향 의존이다(2-P2 결정).
    internal static partial class GameplayCommands
    {
        // 원작이 상점에서 산 아이템의 FromInfoKey에 넣는 출처 키다. 아이템 저장 값(fromInfo)이라 바꾸지 않는다.
        internal const string _tradeSource = "UI_Trade";

        // 원작처럼 결제 뒤 새 아이템 하나를 보낸다. 값은 StockShopRules.BuyPrice이고, 계좌는 상점이 허용할 때만 쓴다(원작 accountAvaliable).
        // 출처와 배달은 원작 LevelConfig.SaveCharacter처럼 씬이 정한다. 기지는 창고 현금도 쓰고 가방, 창고, 보관함 순으로 받으며,
        // 레이드(비밀 상인)는 가방과 펫 가방 현금만 쓰고 가방에 못 넣은 것을 dropped로 돌려줘 호출자가 바닥에 떨군다.
        // restocked는 호출자가 재고 하나를 뺀 상점 기록이고 결제와 배달이 끝난 뒤 쓴다. 결제가 거절되면 기록 없이 false이고 아무것도 바뀌지 않는다.
        // 호출자: StockShop.TryBuy(2-P2 상점 화면, 5-M 비밀 상인).
        internal static bool TryBuyShopItem(GameSessionManager session, EconomyManager economy, OriginalGameplayCatalog catalog,
            StockShopItem item, bool accountAvailable, StockShopState restocked, out IReadOnlyList<ItemOwnershipState> dropped)
        {
            dropped = Array.Empty<ItemOwnershipState>();
            // 상점 품목 ID는 원작 상인 프로필에서 옮긴 저작 데이터라 카탈로그 정의가 늘 있다(씬 데이터 테스트가 확인한다).
            OriginalItemDefinition definition = catalog.DefinitionOf(item.ItemId);
            var price = new Cost(StockShopRules.BuyPrice(definition, item.PriceFactor), Array.Empty<Cost.ItemEntry>());
            GameScene scene = session.Snapshot().SceneId;
            // TryPay가 두 관문과 계좌·현금을 모두 확인한 뒤 아이템, 그다음 계좌 순서로 낸다. 거절이면 아무것도 바뀌지 않았다.
            if (!TryPay(session, economy, price, ItemSourceRules.Owned(scene), accountAvailable)) return false;
            ItemDelivery delivery = scene == GameScene.BaseCamp ? ItemDelivery.PlayerThenStorage : ItemDelivery.PlayerThenDrop;
            // 같은 호출의 지불이 세션 관문을 확인했으므로 배달은 늘 성공한다. 구조 결함으로 거절되면 발견한 소지품 명령이 이미 보고했다.
            // 원작 BuyTask는 산 아이템의 FromInfoKey를 UI_Trade로 정한다(StockShop.cs:408).
            if (!session.Inventory.TryDeliver(catalog, new[] { (item.ItemId, StockShopRules.BuyCount(definition)) }, delivery, _tradeSource,
                    out dropped))
                return false;
            // 같은 호출의 지불이 경제 관문을 확인했으므로 재고 기록은 늘 바뀐다.
            economy.TrySetShop(restocked);
            return true;
        }

        // 원작 StockShop.Sell이다. 고른 아이템을 하위 트리(부착물, 내용물)째 소지품에서 빼고 판매 값 price를 준다.
        // price와 returnsCash(원작 returnCash)는 StockShop.TrySell이 같은 확정 그래프로 정한 그 상점의 값이다.
        // 현금을 돌려주면 현금 아이템을 가방, 창고, 보관함 순으로 보내고(원작 Cost.Return), 아니면 계좌에 더한다.
        // 출처는 구매 지불과 같은 씬 기본 출처라 레이드 중에는 창고 아이템을 팔지 않는다. 고른 아이템은 그 출처 가방의 맨 위 칸에 있어야 한다.
        // 판매 금지 아이템, 없는 아이템, 닫힌 관문, 넘치는 대금은 기록 없이 false이고 아무것도 바뀌지 않는다.
        // 원작 판매 알림(OnItemSoldByPlayer)은 분석과 외부 장치 동기화만 받아 옮기지 않는다. 호출자: StockShop.TrySell(2-P2 상점 화면, 5-M 비밀 상인).
        internal static bool TrySellShopItem(GameSessionManager session, EconomyManager economy, OriginalGameplayCatalog catalog,
            string itemId, long price, bool returnsCash)
        {
            GameSessionSnapshot snapshot = session.Snapshot();
            // 상점 화면은 월드 첫 초기화가 소유 그래프를 만든 뒤에만 열린다. 화면이 고른 ID가 그사이 사라졌으면 거절한다.
            ItemOwnershipState graph = snapshot.Campaign.Gameplay.Inventory.Items!;
            ItemInstanceState? item = graph.Find(itemId);
            if (item == null) return false;
            // 소유 그래프의 아이템은 로드 검사가 카탈로그 정의를 확인한 값이라 정의가 늘 있다.
            if (!StockShopRules.CanBeSold(catalog.DefinitionOf(item.OriginalItemId))) return false;
            // 원작은 아이템을 먼저 지우고 대금을 준다. 지운 뒤 대금이 거절되지 않도록 받을 곳을 먼저 본다.
            // 현금 배달은 세션 관문만 보고 넘친 몫은 보관함이 받으므로 현금 수량 범위만 본다. 값은 저작 가격에 배율을 곱한 것이라 음수가 아니다.
            if (returnsCash ? price > int.MaxValue : !economy.CanAdd(price)) return false;
            // 관문이 닫혔거나 고른 아이템이 출처 가방의 맨 위 칸에 없으면 여기서 거절된다. 대금은 아직 주지 않았다.
            if (!session.Inventory.TryRemoveOwned(itemId, ItemSourceRules.Owned(snapshot.SceneId), out _)) return false;
            // 같은 호출의 제거가 세션 관문을 확인했으므로 현금 배달은 늘 성공한다. 구조 결함으로 거절되면 발견한 소지품 명령이 이미 보고했다.
            if (returnsCash)
                // 원작 Sell의 현금은 Cost.Return으로만 만들고 FromInfoKey를 넣지 않는다(StockShop.cs:440).
                return session.Inventory.TryDeliver(catalog, new[] { (GameplayDataSettings.ItemAssets.CashItemTypeID, (int)price) },
                    ItemDelivery.PlayerThenStorage, null, out _);
            // 같은 호출 안에서 CanAdd를 통과했고 제거 확정 알림의 구독자는 계좌를 바꾸지 않으므로 대금은 늘 더해진다.
            economy.Add(price);
            return true;
        }
    }
}
