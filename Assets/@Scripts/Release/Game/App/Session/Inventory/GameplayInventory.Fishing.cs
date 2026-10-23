#nullable enable
using System.Collections.Generic;

namespace Game.App
{
    // 낚시 미끼 소모(SessionInventoryCommands.TryConsumeSlotContent)의 작업 사본 쪽 계산이다. ID 검색이 아니라 아이템 칸 하나의 내용물을 쓴다.
    internal sealed partial class GameplayInventory
    {
        // 원작 Action_FishingV2.SpawnFish와 FishingRod.UseBait의 미끼 쓰기다. owner 아이템의 slot 칸에 꽂힌 아이템이 겹치는 아이템이고 둘 이상이면
        // 하나를 줄이고, 아니면 하위 트리째 칸에서 없앤다. 주인이 없으면 NotOwned, 칸이 비었으면 NotUsable로 기록 없이 거절한다.
        internal InventoryResult ConsumeSlotContent(string owner, string slot, InventoryRules rules)
        {
            if (!TryItem(owner, out ItemInstanceState? holder)) return Unowned();
            string? id = Holder(holder.Slots, slot);
            if (id == null) return InventoryResult.Reject(InventoryRejection.NotUsable);
            ItemInstanceState item = Child(id);
            if (Stackable(item, rules.MaxStack(item)) && item.Quantity > 1)
            {
                SetQuantity(item, item.Quantity - 1);
                return InventoryResult.Ok;
            }
            SetSlot(owner, slot, null);
            TakeTree(id, new List<ItemInstanceState>());
            return InventoryResult.Ok;
        }
    }
}
