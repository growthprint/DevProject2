#nullable enable annotations
using System;
// 제작 결함 보고(Log)가 Game.Core에 있다.
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.UI.ItemCustomizeView(개조 창)다. 개조 대상 상세(customizingTargetDisplay), 대상의 아이템 칸 고르기와 선택 표시
    // (slotSelectionIndicator), 고른 칸에 꽂을 수 있는 부품 목록(itemDisplayTemplate), 고른 부품 상세(selectedItemDisplay), 장착·해제 버튼을 보인다.
    // 값만 받고 의도 이벤트만 낸다. 명령과 후보 거르기는 화면 컨트롤러(CustomizeScreen)가 맡는다.
    // 대상 상세는 OriginalItemDetailsView를 재사용하고, 그 안의 칸 셀은 인벤토리 화면 전용이라 제작 도구가 꺼 둔다. 같은 자리의 칸 격자를
    // 이 View의 칸 셀로 다시 그려 빈 칸도 고를 수 있게 한다. 원작 FadeGroup 전환은 활성 여부로 보인다(원작 차이).
    public sealed class OriginalItemCustomizeView : MonoBehaviour, IOriginalItemCustomizeCellOwner
    {
        // 칸 줄 번호다. 대상의 아이템 칸, 부품 후보 순서다.
        internal const int _slotList = 0, _itemList = 1;

        [SerializeField] OriginalItemDetailsView _targetDetails = null!;
        [SerializeField] OriginalItemDetailsView _selectedDetails = null!;
        // 대상 아이템 칸 템플릿과 격자다. 템플릿은 꺼진 채 격자 안에 있다.
        [SerializeField] OriginalItemCustomizeCellView _slotTemplate = null!;
        [SerializeField] RectTransform _slotGrid = null!;
        // 원작 slotSelectionIndicator다. 고른 칸 위치로 옮긴다.
        [SerializeField] RectTransform _slotIndicator = null!;
        [SerializeField] GameObject _selectSlotPlaceholder = null!;
        [SerializeField] GameObject _itemsContainer = null!;
        [SerializeField] GameObject _noItemsPlaceholder = null!;
        // 원작 itemDisplayTemplate이다. 원작 PrefabPool처럼 템플릿의 부모 아래에 후보 칸을 만든다.
        [SerializeField] OriginalItemCustomizeCellView _itemTemplate = null!;
        [SerializeField] GameObject _equipButton = null!;
        [SerializeField] GameObject _unequipButton = null!;

        UiTemplateCollection<OriginalItemCustomizeCellView>? _slots, _items;
        // 선택 표시가 따라갈 칸이다. 레이아웃이 칸 위치를 정한 뒤 옮기도록 LateUpdate에서 읽는다.
        Transform? _selectedSlot;

        // 원작 onElementClicked(칸 고르기), ItemUIUtilities.Select(후보 고르기), 장착·해제 버튼이다.
        internal event Action<int>? SlotSelected;
        internal event Action<string>? PartSelected;
        internal event Action? EquipRequested;
        internal event Action? UnequipRequested;

        internal bool IsShown => gameObject.activeSelf;
        internal OriginalItemDetailsView TargetDetails => _targetDetails;
        internal OriginalItemDetailsView SelectedDetails => _selectedDetails;

        // 칸 모음은 처음 쓸 때 만든다. EditMode 테스트와 공용 HUD 초기화가 Awake 없이 부르기 때문이다.
        UiTemplateCollection<OriginalItemCustomizeCellView> Slots =>
            _slots ??= new UiTemplateCollection<OriginalItemCustomizeCellView>(_slotTemplate, static cell => cell.ResetBinding());
        UiTemplateCollection<OriginalItemCustomizeCellView> Items =>
            _items ??= new UiTemplateCollection<OriginalItemCustomizeCellView>(_itemTemplate, static cell => cell.ResetBinding());

        internal void Show() => gameObject.SetActive(true);

        // 공용 HUD 초기화도 무조건 부르므로 숨어 있어도 된다. 칸 연결을 모두 끊어 닫힌 뒤 눌린 칸이 의도를 보내지 않게 한다.
        internal void Hide()
        {
            Slots.Clear();
            Items.Clear();
            _selectedSlot = null;
            gameObject.SetActive(false);
        }

        // 원작 customizingTargetDisplay의 칸 모음이다. AddSlot 뒤 EndSlots로 닫는다. 고른 칸이 없으면 선택 표시를 끈다.
        internal void BeginSlots()
        {
            _selectedSlot = null;
            Slots.Begin();
        }

        internal void AddSlot(int index, string label, string? itemId, Sprite? icon, string itemName, int count, bool selected)
        {
            OriginalItemCustomizeCellView cell = Slots.Acquire(_slotGrid);
            cell.Bind(this, _slotList, index, itemId, icon, itemName, count, selected, label, true);
            if (selected) _selectedSlot = cell.transform;
        }

        internal void EndSlots()
        {
            Slots.End();
            _slotIndicator.gameObject.SetActive(_selectedSlot != null);
        }

        // 원작 RefreshItemListGraphics다. 칸을 고르지 않았으면 칸 고르기 자리표시를, 고른 칸에 후보가 없으면 후보 없음 자리표시를 켠다.
        internal void BeginItems() => Items.Begin();

        internal void AddItem(string itemId, Sprite? icon, string itemName, int count, bool selected) =>
            Items.Acquire(_itemTemplate.transform.parent).Bind(this, _itemList, 0, itemId, icon, itemName, count, selected, null, false);

        internal void EndItems(bool slotSelected, int count)
        {
            Items.End();
            _selectSlotPlaceholder.SetActive(!slotSelected);
            _noItemsPlaceholder.SetActive(slotSelected && count == 0);
            _itemsContainer.SetActive(count > 0);
        }

        // 원작 RefreshDetails의 버튼 전환이다. 고른 부품이 고른 칸에 꽂힌 것이면 해제, 아니면 장착 버튼을 보인다.
        internal void ShowButtons(bool equip, bool unequip)
        {
            _equipButton.SetActive(equip);
            _unequipButton.SetActive(unequip);
        }

        void IOriginalItemCustomizeCellOwner.CellClicked(int list, int index, string? itemId)
        {
            if (list == _slotList) SlotSelected?.Invoke(index);
            else if (itemId != null) PartSelected?.Invoke(itemId);
        }

        // 고정 버튼 두 개는 제작 도구가 Runtime Only 영속 OnClick으로 이 공개 핸들러에 연결한다(Docs/rules/ui.md).
        // 두 버튼은 칸과 부품을 고른 동안만 보이므로 그때만 입력이 닿는다.
        public void OnEquipClicked()
        {
            Log.Instance.Assert(_equipButton.activeInHierarchy, "장착 버튼은 칸과 부품을 고른 동안만 보입니다.", this);
            EquipRequested?.Invoke();
        }

        public void OnUnequipClicked()
        {
            Log.Instance.Assert(_unequipButton.activeInHierarchy, "해제 버튼은 꽂힌 부품을 고른 동안만 보입니다.", this);
            UnequipRequested?.Invoke();
        }

        // 원작 RefreshSelectionIndicator처럼 선택 표시를 고른 칸 위치에 둔다. 칸 격자 레이아웃이 위치를 정한 뒤라 프레임 끝에 맞춘다.
        void LateUpdate()
        {
            if (_selectedSlot != null) _slotIndicator.position = _selectedSlot.position;
        }
    }
}
