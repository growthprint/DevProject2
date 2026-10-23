#nullable enable
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 돈과 아이템에 걸친 지불 명령이다(원작 EconomyManager.Pay). 계좌 몫은 EconomyManager가, 현금과 비용 아이템은
    // 세션 소지품 도우미가 소유한다. 두 소유자를 모두 확인한 뒤 아이템, 그다음 계좌 순서로 적용한다.
    // 원작 Pay는 돈을 먼저 빼서 아이템이 모자라면 돈만 빠질 수 있다. 이 순서는 그 결함을 막는 원작 차이다.
    internal static partial class GameplayCommands
    {
        // 원작 Cost.Enough(계좌 사용 여부를 고른 IsEnough)다. TryPay와 같은 확인이라 참이면 같은 프레임의 TryPay는 칸 내용물 거절만 남는다.
        // 두 명령 관문 중 하나라도 닫혔으면 거짓이다. 기록 없이 판정만 한다. 매 프레임 불릴 수 있어 배열 계획(TryPlan) 없이 돈을 나누고,
        // 현금 몫은 비용 아이템에 붙은 것처럼 함께 할당 없이 센다.
        // 호출자: 5-C CostTaker, 2-P1 상점 구매, 2-Q3 퀘스트 돈 제출, 3-B1 건설, 3-K1 특성, 4-C1 제조, 4-R 수리.
        internal static bool CanPay(GameSessionManager session, EconomyManager economy, Cost cost, ItemSources sources, bool accountAvailable)
        {
            // 소지품 확인은 읽기만 해서 세션 관문을 보지 않으므로 여기서 본다.
            if (!session.AcceptsCommands) return false;
            if (!CostRules.TrySplit(cost, economy.Money, accountAvailable, out long fromAccount, out int fromCash)) return false;
            return economy.CanSpend(fromAccount)
                && session.Inventory.HasOwned(cost.Items, (GameplayDataSettings.ItemAssets.CashItemTypeID, fromCash), sources);
        }

        // 위 CanPay와 같은 판정을 미리 센 수량 묶음으로 한다. 화면이 행마다 지불 가능을 볼 때 가방 전체를 다시 세지 않으려고 둔다
        // (Docs/runtime-boundaries.md#main-thread-split). 묶음의 출처가 지불 출처이고, 묶음을 센 다시 그리기 안에서만 부른다.
        internal static bool CanPay(GameSessionManager session, EconomyManager economy, Cost cost, OwnedItemCounts owned, bool accountAvailable)
        {
            if (!session.AcceptsCommands) return false;
            if (!CostRules.TrySplit(cost, economy.Money, accountAvailable, out long fromAccount, out int fromCash)) return false;
            return economy.CanSpend(fromAccount) && owned.Has(cost.Items, (GameplayDataSettings.ItemAssets.CashItemTypeID, fromCash));
        }

        // 원작 Cost.Pay다. 계좌를 쓸 수 있으면 계좌에서 먼저 내고 모자란 만큼만 현금 아이템으로 낸다. 현금 몫은 비용 아이템과 합쳐 세고 뺀다.
        // sources는 호출자가 고른 출처라 레이드 중에는 창고가 빠진다(ItemSourceRules.Owned). 어느 확인이든 실패하면 기록 없이 false이고 아무것도 바뀌지 않는다.
        // 호출자는 CanPay와 같다.
        internal static bool TryPay(GameSessionManager session, EconomyManager economy, Cost cost, ItemSources sources, bool accountAvailable)
        {
            // 계획, 계좌 확인, 아이템 소비, 계좌 빼기 순서로 한 번씩만 확인한다. 세션 관문과 아이템 수는 TryConsumeOwned와 그 안의
            // Consume이 보므로 여기서 다시 보지 않는다. 계좌 관문은 런타임이 따로 정하는 조건이라 CanSpend로 먼저 본다.
            // 다른 소지품 명령처럼 월드 첫 초기화가 소유 그래프를 만든 뒤에만 오므로 그래프가 없는 경우는 보지 않는다.
            if (!CostRules.TryPlan(cost, economy.Money, accountAvailable, GameplayDataSettings.ItemAssets.CashItemTypeID, out long fromAccount,
                    out IReadOnlyList<(int id, int amount)> items)) return false;
            if (!economy.CanSpend(fromAccount)) return false;
            // 관문이 닫혔거나, 아이템이 모자라거나, 칸 내용물이 있는 아이템을 다 써야 하면 여기서 거절된다. 계좌는 아직 그대로라 아무것도 바뀌지 않는다.
            if (!session.Inventory.TryConsumeOwned(items, sources)) return false;
            // 같은 호출 안에서 CanSpend를 통과했고 소비 확정 알림의 구독자는 계좌를 바꾸지 않으므로 계좌 몫은 늘 빠진다.
            economy.Spend(fromAccount);
            return true;
        }
    }
}
