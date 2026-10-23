#nullable enable
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 원작 ItemRepairView의 수리(OnRepairButtonClicked → Repair)와 모두 수리(RepairItems)다. 돈은 EconomyManager와 세션 현금이,
    // 내구도와 누적 손실은 세션 소유 그래프가 소유한다. 판정과 가격은 화면과 같은 RepairRules로 정하고, 수리 전용 소유자는 두지 않는다.
    // 확인 뒤 돈을 내고 고친 값을 확정한다. 지불은 현금 아이템만 쓰고 내구도 아이템을 건드리지 않으므로, 같은 호출 안의 확정은 실패하지 않는다.
    // accountAvailable은 원작 LevelConfig.SaveCharacter라 기지에서만 참이다. 현금 출처는 씬 기본 출처(ItemSourceRules.Owned)다.
    internal static partial class GameplayCommands
    {
        // 원작 Repair다. 고른 아이템은 원작 창이 보이는 장비 칸이나 가방 맨 위 칸에 있어야 하고 원작 CanRepair를 지나야 한다.
        // 관문이 닫혔거나, 고를 수 없는 아이템이거나, 고칠 수 없거나, 돈이 모자라면 기록 없이 false이고 아무것도 바뀌지 않는다.
        // 호출자: RepairScreen의 수리 단추.
        internal static bool TryRepair(GameSessionManager session, EconomyManager economy, OriginalGameplayCatalog catalog, string itemInstanceId,
            bool accountAvailable)
        {
            if (!session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = session.Snapshot();
            // 수리 화면은 월드 첫 초기화가 소유 그래프를 만든 뒤에만 열린다. 화면이 고른 ID가 그사이 사라졌으면 TryShown이 거절한다.
            ItemOwnershipState items = snapshot.Campaign.Gameplay.Inventory.Items!;
            if (!RepairRules.TryShown(items, itemInstanceId, out ItemInstanceState? item)) return false;
            // 소유 그래프의 아이템은 로드 검사가 카탈로그 정의를 확인한 값이라 정의가 늘 있다.
            OriginalItemDefinition definition = catalog.DefinitionOf(item.OriginalItemId);
            float maximum = definition.Durability, current = RepairRules.Current(definition, item), loss = RepairRules.Loss(item);
            if (!RepairRules.CanRepair(maximum, current, loss, RepairRules.IsRepairable(definition))) return false;
            RepairQuote quote = RepairRules.Quote(maximum, current, loss, RepairRules.LossRatio(definition), definition.Value);
            // TryPay가 두 관문과 계좌, 현금을 모두 확인한 뒤 낸다. 거절이면 아무것도 바뀌지 않는다. 가격 0도 원작처럼 지불 성공이다.
            if (!TryPay(session, economy, RepairPrice(quote.Price), ItemSourceRules.Owned(snapshot.SceneId), accountAvailable)) return false;
            (float durability, float repairedLoss) = RepairRules.Repaired(maximum, loss, quote);
            // 같은 호출의 지불이 세션 관문을 확인했고 현금 소비는 이 아이템을 지우지 않으므로 확정은 늘 성공한다.
            return session.Inventory.TrySetRepaired(new[] { (item.InstanceId, durability, repairedLoss) });
        }

        // 원작 RepairAllPanel.OnButtonClicked → RepairItems다. 장비 칸 전체 가격을 한 번 내고, 원작 결함까지 그대로 옮겨 'Repairable' 태그와
        // 파손을 보지 않고 장비 칸 아이템을 모두 고친다(RepairRules.RepairAll). 원작 단추는 목록이 비면 꺼지므로 빈 목록은 거절한다.
        // 관문이 닫혔거나 돈이 모자라도 기록 없이 false이고 아무것도 바뀌지 않는다. 호출자: RepairScreen의 모두 수리 단추.
        internal static bool TryRepairAll(GameSessionManager session, EconomyManager economy, OriginalGameplayCatalog catalog, bool accountAvailable)
        {
            if (!session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = session.Snapshot();
            // 수리 화면은 월드 첫 초기화가 소유 그래프를 만든 뒤에만 열린다.
            ItemOwnershipState items = snapshot.Campaign.Gameplay.Inventory.Items!;
            var listed = new List<ItemInstanceState>();
            var changes = new List<(string id, float durability, float loss)>();
            long price = RepairRules.RepairAll(items, catalog, listed, changes);
            if (listed.Count == 0) return false;
            if (!TryPay(session, economy, RepairPrice(price), ItemSourceRules.Owned(snapshot.SceneId), accountAvailable)) return false;
            // 목록 아이템은 늘 값이 바뀌어 changes가 비지 않는다. 같은 호출의 지불이 관문을 확인했으므로 확정은 늘 성공한다.
            return session.Inventory.TrySetRepaired(changes);
        }

        // 원작 new Cost(price)처럼 돈만 있는 비용이다.
        static Cost RepairPrice(long money) => new Cost(money, Array.Empty<Cost.ItemEntry>());
    }
}
