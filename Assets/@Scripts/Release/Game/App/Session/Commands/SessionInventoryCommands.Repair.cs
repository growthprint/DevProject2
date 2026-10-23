#nullable enable
using System.Collections.Generic;

namespace Game.App
{
    // 수리 명령의 소지품 쪽 확정이다. 돈은 GameplayCommands.TryRepair와 TryRepairAll이 먼저 냈고, 이 명령은 고친 노드만 한 번에 확정한다.
    internal sealed partial class SessionInventoryCommands
    {
        // 원작 ItemRepairView.Repair의 아이템 쪽 적용이다. 목록의 모든 아이템이 바뀌거나, 하나라도 거절되면 아무것도 바뀌지 않는다.
        // 관문이 닫혔거나 소유하지 않은 ID면 기록 없이 false다. 값은 RepairRules.Repaired가 저장 상태 범위로 맞춘 것이다.
        // 호출자: GameplayCommands.TryRepair, TryRepairAll.
        internal bool TrySetRepaired(IReadOnlyList<(string id, float durability, float loss)> repairs)
        {
            if (!_session.AcceptsCommands) return false;
            return ChangeInventory(repairs, static (inventory, items) =>
            {
                for (int index = 0; index < items.Count; index++)
                {
                    InventoryResult result = inventory.SetRepaired(items[index].id, items[index].durability, items[index].loss);
                    if (!result.IsOk) return result;
                }
                return InventoryResult.Ok;
            }, out _);
        }
    }
}
