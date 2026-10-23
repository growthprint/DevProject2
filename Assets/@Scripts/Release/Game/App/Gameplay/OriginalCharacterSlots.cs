#nullable enable
using System;
using System.Collections.Generic;
using DuckCharacter;

namespace Game.App
{
    // 원작 Character_0의 캐릭터 칸 10개를 원작 순서로 둔다. 칸 키는 OriginalItemDefinition.Slot과 저장 그래프의 슬롯 키와 같다.
    // 정의의 Slot은 그 아이템을 받는 첫 칸이다. 총과 토템은 두 칸이 받고, 토템 두 칸은 원작 forbidItemsWithSameID처럼 같은 종류를 함께 받지 않는다.
    internal static class OriginalCharacterSlots
    {
        internal const string _primary = GameplayWeaponSlots._primary;
        internal const string _secondary = GameplayWeaponSlots._secondary;
        internal const string _melee = GameplayWeaponSlots._melee;
        internal const string _helmat = "Helmat";
        internal const string _armor = "Armor";
        internal const string _faceMask = "FaceMask";
        internal const string _headset = "Headset";
        internal const string _backpack = "Backpack";
        internal const string _totem1 = "Totem1";
        internal const string _totem2 = "Totem2";

        // 인벤토리 화면의 장비 칸 순서이기도 하다. 원작 LootView는 이 순서로 5칸씩 두 줄을 그린다.
        internal static IReadOnlyList<string> All { get; } =
            new[] { _primary, _secondary, _melee, _helmat, _armor, _faceMask, _headset, _backpack, _totem1, _totem2 };

        // 받는 칸 목록은 늘 같으므로 한 번만 만들어 질의마다 할당하지 않는다.
        static readonly string[] _gunSlots = { _primary, _secondary };
        static readonly string[] _meleeSlots = { _melee };
        static readonly string[] _helmatSlots = { _helmat };
        static readonly string[] _armorSlots = { _armor };
        static readonly string[] _faceMaskSlots = { _faceMask };
        static readonly string[] _headsetSlots = { _headset };
        static readonly string[] _backpackSlots = { _backpack };
        static readonly string[] _totemSlots = { _totem1, _totem2 };

        // 이 아이템을 받는 칸을 원작 칸 순서로 돌려준다. 장착할 수 없거나 표에 없는 칸 키면 빈 목록이다.
        internal static IReadOnlyList<string> SlotsFor(OriginalItemDefinition definition) => definition.Slot switch
        {
            _primary => _gunSlots,
            _melee => _meleeSlots,
            _helmat => _helmatSlots,
            _armor => _armorSlots,
            _faceMask => _faceMaskSlots,
            _headset => _headsetSlots,
            _backpack => _backpackSlots,
            _totem1 => _totemSlots,
            _ => Array.Empty<string>()
        };

        // 원작 Slot.CanPlug의 태그 검사에 해당한다. 같은 ID 금지는 다른 칸의 내용을 봐야 하므로 명령 쪽에서 따로 본다.
        internal static bool Accepts(string slot, OriginalItemDefinition definition)
        {
            IReadOnlyList<string> slots = SlotsFor(definition);
            for (int index = 0; index < slots.Count; index++)
                if (slots[index] == slot) return true;
            return false;
        }

        // 같은 종류 금지 칸의 짝 칸이다. 짝이 없는 칸은 null이다.
        internal static string? SameIdPartner(string slot) => slot == _totem1 ? _totem2 : slot == _totem2 ? _totem1 : null;

        // 화면 칸 번호(All 순서)다. 표에 없는 칸 키면 -1이다.
        internal static int IndexOf(string slot)
        {
            IReadOnlyList<string> all = All;
            for (int index = 0; index < all.Count; index++)
                if (all[index] == slot) return index;
            return -1;
        }

        // 원작 칸 이름은 첫 requireTag의 표시 이름이라 태그 이름 키(Tag_*)를 쓴다. 표에 없는 칸 키면 빈 문자열이다.
        internal static string LabelKey(string slot) => slot switch
        {
            _primary or _secondary => "Tag_Gun",
            _melee => "Tag_MeleeWeapon",
            _helmat => "Tag_Helmat",
            _armor => "Tag_Armor",
            _faceMask => "Tag_FaceMask",
            _headset => "Tag_Headset",
            _backpack => "Tag_Backpack",
            _totem1 or _totem2 => "Tag_Totem",
            _ => ""
        };

        // 몸에 외형이 보이는 칸과 그 외형 부위다. 원작 CharacterEquipmentController처럼 머리, 얼굴, 몸, 가방만 모델이 붙는다.
        // 헤드폰과 토템은 이 프로젝트에 외형이 없어 false다.
        internal static bool TryVisualPart(string slot, out EquipPartType part)
        {
            switch (slot)
            {
                case _helmat: part = EquipPartType.Helmet; return true;
                case _faceMask: part = EquipPartType.FaceMask; return true;
                case _armor: part = EquipPartType.Armor; return true;
                case _backpack: part = EquipPartType.Backpack; return true;
                default: part = default; return false;
            }
        }
    }
}
