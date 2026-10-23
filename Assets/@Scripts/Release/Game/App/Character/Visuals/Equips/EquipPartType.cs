#nullable enable annotations

namespace DuckCharacter
{
    // 직렬화 필드에 정수로 저장되므로 번호를 바꾸지 않는다. EquipCount는 슬롯 개수로, 슬롯 배열 크기와 반복 범위에 쓴다.
    /// <summary>장비 장착 슬롯 종류.</summary>
    public enum EquipPartType
    {
        Helmet = 0,
        FaceMask = 1,
        Armor = 2,
        Backpack = 3,
        LeftHand = 4,
        RightHand = 5,
        MeleeWeapon = 6,
        EquipCount = 7,
    }
}
