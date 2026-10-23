#nullable enable annotations

namespace Game.App
{
    // 인벤토리 화면 셀이 놓인 패널이다. Other는 열린 상자나 창고 쪽이고, Slot은 원작 장비 칸이다.
    // Attachment는 아이템 상세 화면(원작 ItemDetails)의 아이템 칸으로, 총 부착 칸 같은 원작 Slot이다.
    // Pet은 원작 PetProxy 인벤토리인 펫 가방(원작 InventoryDisplay_Pet)의 칸이다.
    internal enum InventoryPanel { Carried, Other, Slot, Attachment, Pet }

    // 셀 선택, 끌기, 놓기 이벤트가 넘기는 셀 자리다. 셀마다 클로저를 만들지 않도록 값 형식으로 넘긴다.
    internal readonly struct InventoryCellRef
    {
        internal InventoryCellRef(InventoryPanel panel, int position, string? id)
        {
            Panel = panel;
            Position = position;
            Id = id;
        }

        internal InventoryPanel Panel { get; }
        // 소지품, 펫 가방, Other 패널은 칸 번호이고, 상자 칸은 상자 칸 번호다. 장비 칸은 OriginalCharacterSlots.All 순서 번호다.
        // 아이템 칸은 상세 화면이 보이는 아이템의 원본 칸 순서 번호다.
        internal int Position { get; }
        // 소유 아이템은 인스턴스 ID, 상자 칸은 칸 번호 문자열이다. 빈 칸은 null이다.
        internal string? Id { get; }
        internal bool IsEmpty => Id == null;
    }
}
