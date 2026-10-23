#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원본 아이템 한 개의 저장 상태다. 내구도는 InventoryItem의 0~1 비율과 달리 원본 절대값으로 두고, 모르는 값은 null로 둔다.
    public sealed class ItemInstanceState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        // 슬롯과 변수 목록은 따로 한 번 복사하고, null 목록은 빈 목록으로 둔다.
        public ItemInstanceState(string instanceId, int originalItemId, int quantity, float? absoluteDurability,
            float? durabilityLoss, bool? inspected, string? fromInfo, IReadOnlyList<ItemSlotState> slots,
            ItemContainerState? inventory, IReadOnlyList<ItemVariableState> variables)
        {
            InstanceId = instanceId; OriginalItemId = originalItemId; Quantity = quantity;
            AbsoluteDurability = absoluteDurability; DurabilityLoss = durabilityLoss; Inspected = inspected; FromInfo = fromInfo;
            Slots = ReadOnlyLists.CopyOrEmpty(slots); Inventory = inventory; Variables = ReadOnlyLists.CopyOrEmpty(variables);
        }

        // TryCreate가 쓰는 검사다. 복사는 생성자가 한 번만 하므로 여기서는 판정만 한다.
        // 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(string instanceId, int originalItemId, int quantity, float? absoluteDurability, float? durabilityLoss,
            IReadOnlyList<ItemSlotState>? slots, IReadOnlyList<ItemVariableState>? variables)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return false;
            if (originalItemId < 0) return false;
            if (quantity < 1) return false;
            // 내구도는 0~1 비율이 아닌 원본 절대값이라 상한 없이 음수가 아닌 유한값만 받는다.
            if (absoluteDurability is float durability && !StateValues.IsFiniteNonNegative(durability))
                return false;
            // 내구도 손실은 0 이상 1 이하의 비율이다.
            if (durabilityLoss is float loss && !(float.IsFinite(loss) && loss >= 0f && loss <= 1f))
                return false;
            if (!ReadOnlyLists.AreUnique(slots, state => state.Key)) return false;
            return ReadOnlyLists.AreUnique(variables, state => state.Key);
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(string instanceId, int originalItemId, int quantity, float? absoluteDurability,
            float? durabilityLoss, bool? inspected, string? fromInfo, IReadOnlyList<ItemSlotState> slots,
            ItemContainerState? inventory, IReadOnlyList<ItemVariableState> variables,
            [NotNullWhen(true)] out ItemInstanceState? value)
        {
            value = Check(instanceId, originalItemId, quantity, absoluteDurability, durabilityLoss, slots, variables)
                ? new ItemInstanceState(instanceId, originalItemId, quantity, absoluteDurability, durabilityLoss,
                    inspected, fromInfo, slots, inventory, variables)
                : null;
            return value != null;
        }

        // 발사, 일부 소비, 스택 합치기처럼 수량만 바꾼 노드를 만든다. 생성자를 거치면 이미 복사한 슬롯과 변수 목록을 다시 복사하므로,
        // 다른 필드는 이 값 그대로 두고 목록을 공유한다. 작업 사본은 1 이상 수량만 넘긴다.
        internal ItemInstanceState WithQuantity(int quantity)
        {
            return new ItemInstanceState(this, quantity, Inventory, Slots);
        }

        // 열쇠 같은 필요 아이템을 쓸 때 내구도만 바꾼 노드를 만든다. 드물게 불려 목록을 다시 복사하는 공개 생성자를 쓴다.
        internal ItemInstanceState WithDurability(float durability) =>
            new ItemInstanceState(InstanceId, OriginalItemId, Quantity, durability, DurabilityLoss, Inspected, FromInfo, Slots, Inventory, Variables);

        // 원작 ItemRepairView.Repair는 누적 손실을 올린 뒤 그 손실로 내구도를 다시 정한다. 두 값이 한 노드에서 함께 바뀌어야
        // 손실만 오른 중간 상태가 확정되지 않는다. 값은 호출자(RepairRules.Repaired)가 0~1 손실과 0 이상 내구도로 맞춰 넘긴다.
        internal ItemInstanceState WithDurabilityAndLoss(float durability, float loss) =>
            new ItemInstanceState(InstanceId, OriginalItemId, Quantity, durability, loss, Inspected, FromInfo, Slots, Inventory, Variables);

        // 작업 사본이 인벤토리나 슬롯만 바꾼 노드를 만든다. 넘긴 슬롯 목록만 한 번 복사하고 변수 같은 나머지 불변 목록은 공유한다.
        internal ItemInstanceState With(ItemContainerState? inventory = null, IReadOnlyList<ItemSlotState>? slots = null) =>
            new ItemInstanceState(this, Quantity, inventory ?? Inventory, slots == null ? Slots : ReadOnlyLists.CopyOrEmpty(slots));

        // WithQuantity와 With 전용 생성자다. 바꾸지 않은 불변 목록은 원본 노드와 공유한다.
        ItemInstanceState(ItemInstanceState source, int quantity, ItemContainerState? inventory, IReadOnlyList<ItemSlotState> slots)
        {
            InstanceId = source.InstanceId; OriginalItemId = source.OriginalItemId; Quantity = quantity;
            AbsoluteDurability = source.AbsoluteDurability; DurabilityLoss = source.DurabilityLoss; Inspected = source.Inspected;
            FromInfo = source.FromInfo; Slots = slots; Inventory = inventory; Variables = source.Variables;
        }

        public string InstanceId { get; }
        public int OriginalItemId { get; }
        public int Quantity { get; }
        public float? AbsoluteDurability { get; }
        public float? DurabilityLoss { get; }
        public bool? Inspected { get; }
        public string? FromInfo { get; }
        public IReadOnlyList<ItemSlotState> Slots { get; }
        public ItemContainerState? Inventory { get; }
        public IReadOnlyList<ItemVariableState> Variables { get; }
    }
}
