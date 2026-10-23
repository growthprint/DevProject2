#nullable enable annotations
using System.Collections.Generic;
using System.Globalization;
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원본 ItemDetailsDisplay.Setup이 보이는 값을 소유 그래프와 카탈로그에서 정한다. 표시는 OriginalItemDetailsView가,
    // 아이템 칸 셀의 입력 연결은 HudInventoryPanel이 맡는다. 원작 순서: 공통 정보, 아이템 칸, 탄종, 변수, 상수, 수치, modifier.
    // 수치는 꽂힌 부착물의 modifier를 반영한 값이다(OriginalGameplayCatalog.Effective).
    internal sealed class InventoryItemDetails
    {
        readonly GameplayWorldContext _world;
        // 지금 보이는 소유 아이템과 그 정의다. 상세 화면 칸 셀의 칸 번호를 칸 키로 바꿀 때 쓴다. 상자 칸이면 Owner가 null이다.
        string? _owner;
        OriginalItemDefinition? _definition;

        internal InventoryItemDetails(GameplayWorldContext world) => _world = world;

        // 상세 화면 칸에 끌어 놓을 수 있는 주인 아이템이다. 상자 칸의 아이템은 아직 소유하지 않아 칸을 바꿀 수 없다.
        internal string? Owner => _owner;

        // 상세 화면 칸 번호를 원본 칸 키로 바꾼다. 소유 아이템을 보이는 중이고 번호가 그 정의의 칸 범위 안일 때만 true다.
        internal bool TrySlotKey(int index, out string owner, out string key)
        {
            owner = _owner ?? "";
            key = "";
            if (_owner == null || _definition == null || index < 0 || index >= _definition.ItemSlots.Count) return false;
            key = _definition.ItemSlots[index].Key;
            return true;
        }

        // 상세 화면 칸 번호의 원본 칸 정의다. 끌기 중 꽂을 수 있는 칸 표시가 읽는다.
        internal IReadOnlyList<OriginalItemSlot> ShownSlots =>
            _definition != null ? _definition.ItemSlots : (IReadOnlyList<OriginalItemSlot>)System.Array.Empty<OriginalItemSlot>();

        // 원본 LootView.RefreshDetails의 숨기기다. 인벤토리를 닫을 때는 페이드 없이 끈다.
        internal void Hide(OriginalItemDetailsView view, bool immediate)
        {
            _owner = null;
            _definition = null;
            view.Hide(immediate);
        }

        // 소유 아이템이면 item과 items를, 상자 칸이면 null과 상자 수량을 넘긴다. 정의는 호출자가 찾은 그 아이템의 카탈로그 정의다.
        // "등록됨"이 퀵슬롯 대신 원작 IsRegistered를 쓰게 되어 퀵슬롯 인수를 받지 않는다.
        internal void Show(OriginalItemDetailsView view, HudInventoryPanel panel, ItemOwnershipState? items, ItemInstanceState? item,
            OriginalItemDefinition definition, int lootCount)
        {
            OriginalGameplayCatalog catalog = _world.Catalog;
            _owner = item?.InstanceId;
            _definition = definition;
            int count = item?.Quantity ?? lootCount;
            float weight = items != null && item != null ? OriginalInventoryQueries.TotalWeight(items, catalog, item.InstanceId) : definition.Weight * count;
            // 원작 ItemDetailsDisplay처럼 "등록됨"은 활성 열쇠나 해금된 설계도다. 종류로 정해 상자와 상점 칸도 같은 표시를 받는다.
            bool registered = OriginalInventoryQueries.IsRegistered(GameManager.MasterKeys, GameManager.Crafting, definition.Id);
            view.Begin(definition.Icon, GameLocalization.Text(definition.NameKey), definition.Id, GameLocalization.Text(definition.DescriptionKey),
                string.Format(CultureInfo.InvariantCulture, view.WeightFormat, weight), definition.UseTime > 0, registered);
            // 원본 ItemDetailsDisplay처럼 쌓이는 아이템만 수량을, 내구도를 쓰는 아이템만 내구도를 보인다. 모르는 내구도는 가득 찬 것으로 본다.
            if (definition.MaxStack > 1) view.ShowCount(count);
            if (definition.Durability > 0)
                view.ShowDurability(item?.AbsoluteDurability ?? definition.Durability, definition.Durability, item?.DurabilityLoss ?? 0f);
            // 원본 TagsDisplay처럼 show가 켜진 태그만 원본 순서로 그 태그 색 배경에 보인다. 태그 이름 키 규칙은 GameLocalization이 정한다.
            IReadOnlyList<string> tags = definition.Tags;
            for (int index = 0; index < tags.Count; index++)
                if (catalog.TryGetTag(tags[index], out OriginalTagInfo? tag) && tag.Show)
                    view.AddTag(GameLocalization.Text(GameLocalization.TagNameKey(tag.Name)), tag.Color);
            ShowSlots(view, panel, items, item, definition);
            ShowProperties(view, items, item, definition, catalog);
            view.End();
        }

        // 원본 ItemSlotCollectionDisplay다. 칸마다 칸 이름(첫 requireTag의 Tag_ 키, 없으면 "?")과 꽂힌 아이템을 보인다.
        void ShowSlots(OriginalItemDetailsView view, HudInventoryPanel panel, ItemOwnershipState? items, ItemInstanceState? item,
            OriginalItemDefinition definition)
        {
            IReadOnlyList<OriginalItemSlot> slots = definition.ItemSlots;
            for (int index = 0; index < slots.Count; index++)
            {
                OriginalItemSlot slot = slots[index];
                string label = slot.LabelKey.Length > 0 ? GameLocalization.Text(slot.LabelKey) : "?";
                // 칸 키로 꽂힌 아이템을 찾는 규칙은 개조 화면과 같은 소유 그래프 질의가 정한다.
                string? content = item != null ? OriginalInventoryQueries.SlotHolder(item.Slots, slot.Key) : null;
                string name = "";
                Sprite? icon = null;
                int quantity = 0;
                // 칸 값은 다른 아이템 화면과 같은 공용 칸 값 도우미가 읽는다. 빈 칸이나 상자 칸이면 비운 값이다.
                if (items != null) ItemCellValues.Describe(items, _world.Catalog, content, out icon, out name, out quantity);
                OriginalSlotCell cell = view.AddSlot();
                cell.Bind(panel, new InventoryCellRef(InventoryPanel.Attachment, index, content), label, name, icon, quantity);
                cell.SetSlotIcon(slot.Icon);
            }
        }

        // 원본 propertiesParent 목록이다. 총은 탄종 줄을 먼저 보인다. 원작 BulletTypeDisplay는 장전할 탄종을 보이는데,
        // 이 게임은 그 선택을 따로 두지 않으므로 탄창의 첫 탄종을, 빈 탄창이면 "탄약 미장전"을 보인다.
        void ShowProperties(OriginalItemDetailsView view, ItemOwnershipState? items, ItemInstanceState? item, OriginalItemDefinition definition,
            OriginalGameplayCatalog catalog)
        {
            if (definition.IsGun)
            {
                ItemInstanceState? loaded = null;
                if (items != null && item != null) OriginalInventoryQueries.TryLoadedAmmo(items, item.InstanceId, out loaded);
                // 탄창의 탄은 그래프 노드라 카탈로그 정의가 늘 있다. "탄약 미장전" 키는 탄종 전환 HUD와 같은 키를 쓴다.
                OriginalItemDefinition? ammo = loaded != null ? catalog.DefinitionOf(loaded.OriginalItemId) : null;
                view.ShowBullet(GameLocalization.Text(ammo != null ? ammo.NameKey : BulletTypeSwitch.NotAssignedKey));
                // 원작 총의 변수 중 display가 켜진 것은 현재 장전량(BulletCount)뿐이다.
                int bullets = items != null && item != null ? OriginalInventoryQueries.MagazineCount(items, item.InstanceId) : 0;
                view.AddVariable(GameLocalization.Text("Var_BulletCount"), bullets.ToString(CultureInfo.InvariantCulture));
            }
            IReadOnlyList<OriginalItemConstant> constants = definition.Constants;
            for (int index = 0; index < constants.Count; index++)
            {
                OriginalItemConstant constant = constants[index];
                view.AddVariable(GameLocalization.Text("Var_" + constant.Key), constant.Localized ? GameLocalization.Text(constant.Text) : constant.Text);
            }
            OriginalItemDefinition effective = items != null && item != null ? catalog.Effective(items, item) : definition;
            // 수치와 modifier 줄의 스탯 이름 키 규칙은 특성 설명과 같은 GameLocalization 키 함수가 정한다.
            IReadOnlyList<OriginalItemStat> stats = definition.Stats;
            for (int index = 0; index < stats.Count; index++)
            {
                if (!stats[index].Display) continue;
                string key = stats[index].Key;
                view.AddStat(GameLocalization.Text(GameLocalization.StatNameKey(key)),
                    effective.Stat(key).ToString(catalog.StatInfo(key).Format, CultureInfo.InvariantCulture));
            }
            IReadOnlyList<OriginalItemModifier> modifiers = definition.Modifiers;
            for (int index = 0; index < modifiers.Count; index++)
            {
                OriginalItemModifier modifier = modifiers[index];
                if (!modifier.Display) continue;
                (string format, OriginalStatPolarity polarity) = catalog.StatInfo(modifier.Key);
                view.AddModifier(GameLocalization.Text(GameLocalization.StatNameKey(modifier.Key)), ModifierText(modifier.Type, modifier.Value, format),
                    modifier.Value, polarity);
            }
        }

        // 원본 ModifierDescription.GetDisplayValueString이다. 더하기는 부호와 형식, 퍼센트 더하기는 백분율, 퍼센트 곱하기는 x 백분율이다.
        internal static string ModifierText(AttributeModifierOp type, float value, string format) => type switch
        {
            AttributeModifierOp.Add => (value > 0f ? "+" : "") + value.ToString(format, CultureInfo.InvariantCulture),
            AttributeModifierOp.PercentageAdd =>
                string.Format(CultureInfo.InvariantCulture, "{0}{1:0.##}%", value > 0f ? "+" : "", value * 100f),
            AttributeModifierOp.PercentageMultiply => string.Format(CultureInfo.InvariantCulture, "x{0:0.##}%", 100f + value * 100f),
            _ => "?" + value.ToString(CultureInfo.InvariantCulture)
        };
    }
}
