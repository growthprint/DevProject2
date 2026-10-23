#nullable enable
using DevProject.Serialization;
using Newtonsoft.Json.Linq;
using static Game.App.SaveJsonFields;

namespace Game.App
{
    // 원본 아이템 소유 그래프를 읽고 쓴다. 현재 소지품, 마지막 사망 기록, 유실물이 같은 형식을 쓴다.
    internal static class ItemGraphSaveJson
    {
        // 아이템 노드는 평평한 목록에 한 번씩만 쓰고, 소유 트리는 루트, 슬롯, 칸이 가리키는 인스턴스 ID로 복원한다.
        // 없는 참조, 중복 소유, 순환은 읽을 때 ItemOwnershipState.TryCreate가 거부한다.
        internal static JObject Write(ItemOwnershipState value) => new JObject
        {
            ["nodes"] = WriteList(value.Items, WriteItemNode),
            ["roots"] = WriteList(value.Roots, WriteItemSlot),
            ["inventories"] = WriteList(value.Inventories, WriteStoredInventory)
        };
        internal static ItemOwnershipState Read(StrictObject value) => value.Create(ItemOwnershipState.TryCreate(
            value.ObjectArray("nodes", ReadItemNode), value.ObjectArray("roots", ReadItemSlot),
            value.ObjectArray("inventories", ReadStoredInventory), out ItemOwnershipState? created), created);

        static JObject WriteItemNode(ItemInstanceState item) => new JObject
        {
            ["instanceId"] = item.InstanceId, ["originalItemId"] = item.OriginalItemId, ["quantity"] = item.Quantity,
            ["absoluteDurability"] = NumberOrNull(item.AbsoluteDurability), ["durabilityLoss"] = NumberOrNull(item.DurabilityLoss),
            ["inspected"] = item.Inspected.HasValue ? new JValue(item.Inspected.Value) : JValue.CreateNull(),
            ["fromInfo"] = NullableString(item.FromInfo),
            ["slots"] = WriteList(item.Slots, WriteItemSlot), ["inventory"] = WriteOptional(item.Inventory, WriteContainer),
            ["variables"] = WriteList(item.Variables, WriteVariable)
        };
        static ItemInstanceState ReadItemNode(StrictObject value) => value.Create(ItemInstanceState.TryCreate(value.Text("instanceId"),
            value.Int32("originalItemId"), value.Int32("quantity"), value.NullableFiniteFloat("absoluteDurability"),
            value.NullableFiniteFloat("durabilityLoss"), value.NullableBoolean("inspected"), value.NullableText("fromInfo"),
            value.ObjectArray("slots", ReadItemSlot), value.NullableObject("inventory", ReadContainer),
            value.ObjectArray("variables", ReadVariable), out ItemInstanceState? created), created);

        static JObject WriteVariable(ItemVariableState value) => new JObject
        {
            ["key"] = value.Key, ["dataType"] = value.DataType, ["dataHex"] = value.DataHex, ["display"] = value.Display
        };
        static ItemVariableState ReadVariable(StrictObject value) => value.Create(ItemVariableState.TryCreate(value.Text("key"),
            value.Int32("dataType"), value.Text("dataHex"), value.Boolean("display"), out ItemVariableState? created), created);

        // 원본 슬롯 키는 철자를 바꾸지 않고 그대로 쓰고, 장착 아이템은 인스턴스 ID로 가리킨다.
        static JObject WriteItemSlot(ItemSlotState value) => new JObject { ["key"] = value.Key, ["instanceId"] = value.InstanceId };
        static ItemSlotState ReadItemSlot(StrictObject value) => value.Create(ItemSlotState.TryCreate(value.Text("key"),
            value.Text("instanceId"), out ItemSlotState? created), created);

        // 창고나 배송 대기함처럼 아이템에 속하지 않는 저장 인벤토리를 원본 키로 쓴다.
        static JObject WriteStoredInventory(StoredInventoryState value) => new JObject
        {
            ["key"] = value.Key, ["inventory"] = WriteContainer(value.Inventory)
        };
        static StoredInventoryState ReadStoredInventory(StrictObject value) => value.Create(StoredInventoryState.TryCreate(
            value.Text("key"), value.Object("inventory", ReadContainer), out StoredInventoryState? created), created);

        // 탄창, 가방, 창고가 모두 같은 칸 형식을 쓴다. 탄약 수는 따로 저장하지 않고 탄창에 든 탄약에서 계산한다.
        static JObject WriteContainer(ItemContainerState value) => new JObject
        {
            ["capacity"] = NumberOrNull(value.Capacity),
            ["entries"] = WriteList(value.Entries, WritePosition),
            ["lockedIndexes"] = WriteInts(value.LockedIndexes)
        };
        static ItemContainerState ReadContainer(StrictObject value) => value.Create(ItemContainerState.TryCreate(
            value.NullableInt32("capacity"), value.ObjectArray("entries", ReadPosition), value.Array("lockedIndexes", _int32Item),
            out ItemContainerState? created), created);

        static JObject WritePosition(ItemPositionState value) => new JObject { ["position"] = value.Position, ["instanceId"] = value.InstanceId };
        static ItemPositionState ReadPosition(StrictObject value) => value.Create(ItemPositionState.TryCreate(value.Int32("position"),
            value.Text("instanceId"), out ItemPositionState? created), created);
    }
}
