#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 CostDisplay.Setup을 제조, 레시피 도감, 지도 선택, 특성 트리, 건설 화면이 함께 그린다. 돈 줄은 원작 RefreshMoneyBackground처럼
    // 계좌와 현금을 합쳐 보고, 넉넉한지와 아이템 줄은 지불 명령과 같은 출처로 센다. 카탈로그에 없는 비용 아이템은 아이콘 없이 수만 보인다.
    // 분해 화면은 원작 Setup(cost, count)처럼 배수를 곱한 아이템 줄만 AddItems로 함께 그린다.
    internal static class CostDisplayDrawing
    {
        // 비용 하나만 그리는 화면은 가진 돈과 넉넉한지를 여기서 센다. 지불 확인은 읽기만 해서 세는 순서가 결과를 바꾸지 않는다.
        // 돈 줄, 넉넉함, 아이템 줄이 같은 수를 보므로 가방은 한 번만 센다.
        internal static void Draw(OriginalCostDisplayView display, GameSessionManager session, EconomyManager economy, OriginalGameplayCatalog catalog,
            Cost cost, ItemSources sources)
        {
            OwnedItemCounts owned = session.Inventory.CountAllOwned(sources);
            Draw(display, catalog, cost, owned, OwnedMoney(economy, owned), GameplayCommands.CanPay(session, economy, cost, owned, true));
        }

        // 줄마다 비용을 그리거나 넉넉한지를 다른 표시에도 쓰는 화면은 그 다시 그리기에서 한 번 센 수량 묶음과 두 값을 넘긴다.
        internal static void Draw(OriginalCostDisplayView display, OriginalGameplayCatalog catalog, Cost cost, OwnedItemCounts owned, long money,
            bool enough)
        {
            IReadOnlyList<Cost.ItemEntry> items = cost.Items;
            display.BeginItems(cost.Money, cost.Money <= money, enough, items.Count > 0);
            // 아이템 줄은 분해 화면과 같은 도우미가 그린다. 배수 1은 요구 수량 그대로다.
            AddItems(display, catalog, items, owned, 1);
            display.EndItems();
        }

        // 원작 CostDisplay.Setup(cost, count)의 아이템 줄이다. 요구 수량에 배수를 곱하고, 가진 수는 미리 센 같은 출처의 수량 묶음에서 읽는다.
        // 앞뒤의 BeginItems와 EndItems는 호출자가 부른다. 호출자: Draw와 DecomposeScreen.DrawResult.
        internal static void AddItems(OriginalCostDisplayView display, OriginalGameplayCatalog catalog, IReadOnlyList<Cost.ItemEntry> items,
            OwnedItemCounts owned, int count)
        {
            for (int index = 0; index < items.Count; index++)
            {
                Sprite? icon = catalog.TryGet(items[index].Id, out OriginalItemDefinition? item) ? item!.Icon : null;
                display.AddItem(icon, owned.Count(items[index].Id), (long)items[index].Amount * count);
            }
        }

        // 원작 RefreshMoneyBackground의 돈이다. 계좌 잔액에 같은 출처에서 가진 현금 아이템 수를 더한다.
        internal static long OwnedMoney(EconomyManager economy, OwnedItemCounts owned) =>
            economy.Money + owned.Count(GameplayDataSettings.ItemAssets.CashItemTypeID);
    }
}
