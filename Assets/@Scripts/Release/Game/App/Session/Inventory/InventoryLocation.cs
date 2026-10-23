#nullable enable

namespace Game.App
{
    // 인벤토리 명령의 목적지 종류다. 소지품, 창고, 펫 가방은 칸 번호로, 장비는 원작 캐릭터 칸 키로 가리킨다.
    // ItemSlot은 총 부착 칸 같은 다른 아이템의 칸(원작 Slot)이고, 주인 아이템 ID와 칸 키로 가리킨다. Pet은 원작 PetProxy의 가방이다.
    internal enum InventoryArea { Carried, Storage, Equipment, ItemSlot, Pet }

    // 끌어 놓기와 더블클릭이 고른 목적지다. 화면 셀 정보를 세션 명령이 읽는 값으로 한 번만 바꿔 담는다.
    internal readonly struct InventoryLocation
    {
        // 원작 AddAndMerge처럼 같은 종류 스택에 먼저 합치고, 남으면 첫 빈 칸에 넣으라는 칸 번호다.
        internal const int _anyPosition = -1;

        InventoryLocation(InventoryArea area, int position, string? slot, string? owner)
        {
            Area = area;
            Position = position;
            Slot = slot;
            Owner = owner;
        }

        internal InventoryArea Area { get; }
        // 소지품, 창고, 펫 가방의 칸 번호다. 장비 목적지에서는 쓰지 않는다.
        internal int Position { get; }
        // 장비 목적지의 원작 칸 키(OriginalCharacterSlots)나 아이템 칸 키다. 다른 목적지에서는 null이다.
        internal string? Slot { get; }
        // 아이템 칸 목적지의 주인 아이템 인스턴스 ID다. 다른 목적지에서는 null이다.
        internal string? Owner { get; }

        internal static InventoryLocation Carried(int position) => new InventoryLocation(InventoryArea.Carried, position, null, null);
        internal static InventoryLocation Storage(int position) => new InventoryLocation(InventoryArea.Storage, position, null, null);
        // 펫 가방 칸이다. 소지품과 같이 칸 번호나 _anyPosition을 받는다.
        internal static InventoryLocation Pet(int position) => new InventoryLocation(InventoryArea.Pet, position, null, null);
        internal static InventoryLocation Equipment(string slot) => new InventoryLocation(InventoryArea.Equipment, _anyPosition, slot, null);
        internal static InventoryLocation ItemSlot(string owner, string slot) =>
            new InventoryLocation(InventoryArea.ItemSlot, _anyPosition, slot, owner);
    }
}
