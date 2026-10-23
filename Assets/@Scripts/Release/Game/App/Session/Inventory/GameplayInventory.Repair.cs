#nullable enable

namespace Game.App
{
    // 수리 명령(GameplayCommands.TryRepair, TryRepairAll)의 작업 사본 쪽 적용이다. 수리는 칸과 슬롯을 바꾸지 않고 노드의 내구도와
    // 누적 손실만 바꾼다. 판정과 값 계산은 RepairRules가 확정 그래프로 끝냈다.
    internal sealed partial class GameplayInventory
    {
        // 원작 ItemRepairView.Repair의 DurabilityLoss와 Durability 쓰기다. 두 값을 한 노드로 함께 바꾼다. 수리는 드물게 불려
        // 필요 아이템 사용(UseRequired)처럼 전체 사본에 쓴다. 소유하지 않은 ID는 기록 없이 NotOwned로 거절한다.
        internal InventoryResult SetRepaired(string id, float durability, float loss)
        {
            if (!TryItem(id, out ItemInstanceState? item)) return Unowned();
            Reshaped()[id] = item.WithDurabilityAndLoss(durability, loss);
            return InventoryResult.Ok;
        }
    }
}
