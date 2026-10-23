#nullable enable annotations
using System;
// 제작 결함 보고(Log)가 Game.Core에 있다.
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 Duckov.UI.ItemCustomizeSelectionView(개조 선택 창)다. 캐릭터 장비 칸(slotDisplay), 가방(inventoryDisplay), 고른 아이템의 상세
    // (OriginalItemDetailsView 재사용), 고른 아이템 이름·아이콘, 개조 가능·불가 표시, "개조 시작" 버튼을 보인다. 값만 받고 의도 이벤트만 낸다.
    // 열고 닫기, 입력 차단, 다음 창 열기는 화면 컨트롤러(CustomizeScreen)가 맡는다. 원작 FadeGroup 전환은 활성 여부로 보인다(StockShopView 선례, 원작 차이).
    public sealed class OriginalItemCustomizeSelectionView : MonoBehaviour, IOriginalItemCustomizeCellOwner
    {
        // 칸 줄 번호다. 원작 slotDisplay(장비 칸), inventoryDisplay(가방) 순서다.
        internal const int _slotList = 0, _bagList = 1;

        // 장비 칸 템플릿과 격자(원작 ItemSlotCollectionDisplay의 entriesParent)다. 템플릿은 꺼진 채 격자 안에 있다.
        [SerializeField] OriginalItemCustomizeCellView _slotTemplate = null!;
        [SerializeField] RectTransform _slotGrid = null!;
        // 가방 칸 템플릿(InventoryEntry.prefab 중첩 인스턴스)과 원작 InventoryDisplay의 entriesParent, placeHolder, capacityText,
        // contentLayout, gridLayoutElement다.
        [SerializeField] OriginalItemCustomizeCellView _cellTemplate = null!;
        [SerializeField] RectTransform _bagGrid = null!;
        [SerializeField] GameObject _bagPlaceholder = null!;
        [SerializeField] TMP_Text _bagCapacity = null!;
        [SerializeField] GridLayoutGroup _bagLayout = null!;
        [SerializeField] LayoutElement _bagHeight = null!;
        // 원작 detailsDisplay다. 원작 itemDetailsFadeGroup 자리는 상세 View가 스스로 켜고 끈다.
        [SerializeField] OriginalItemDetailsView _details = null!;
        // 원작 customizeButtonFadeGroup(개조 시작 버튼)과 placeHolderFadeGroup의 물체다.
        [SerializeField] GameObject _customizeButton = null!;
        [SerializeField] GameObject _placeholder = null!;
        [SerializeField] TMP_Text _selectedName = null!;
        [SerializeField] Image _selectedIcon = null!;
        [SerializeField] GameObject _customizable = null!;
        [SerializeField] GameObject _uncustomizable = null!;
        // 원작 noItemSelectedNameText다. 제작 도구가 원본 값을 옮긴다. 원작은 고른 것이 없을 때 아이콘 색을 투명으로 두어 빈 아이콘은 옮기지 않는다.
        [SerializeField] string _noItemName = "-";

        // 장비 칸 줄은 수용량과 격자 높이가 없는 칸 모음이고, 가방만 원작 InventoryDisplay 목록이다.
        UiTemplateCollection<OriginalItemCustomizeCellView>? _slots;
        OriginalInventoryDisplayList<OriginalItemCustomizeCellView>? _bag;

        // 원작 ItemUIUtilities.Select(칸 클릭)와 개조 시작 버튼이다. 칸 클릭은 아이템 인스턴스 ID를 넘긴다.
        internal event Action<string>? ItemSelected;
        internal event Action? BeginRequested;

        internal bool IsShown => gameObject.activeSelf;
        internal OriginalItemDetailsView Details => _details;

        // 칸 모음과 가방 목록은 처음 쓸 때 만든다. EditMode 테스트와 공용 HUD 초기화가 Awake 없이 부르기 때문이다.
        UiTemplateCollection<OriginalItemCustomizeCellView> Slots =>
            _slots ??= new UiTemplateCollection<OriginalItemCustomizeCellView>(_slotTemplate, static cell => cell.ResetBinding());
        OriginalInventoryDisplayList<OriginalItemCustomizeCellView> Bag =>
            _bag ??= new OriginalInventoryDisplayList<OriginalItemCustomizeCellView>(_cellTemplate, static cell => cell.ResetBinding(), _bagGrid,
                _bagPlaceholder, _bagCapacity, _bagLayout, _bagHeight);

        internal void Show() => gameObject.SetActive(true);

        // 공용 HUD 초기화도 무조건 부르므로 숨어 있어도 된다. 칸 연결을 모두 끊어 닫힌 뒤 눌린 칸이 의도를 보내지 않게 한다.
        internal void Hide()
        {
            Slots.Clear();
            Bag.Clear();
            gameObject.SetActive(false);
        }

        // 원작 slotDisplay.Setup(캐릭터)이다. AddSlot 뒤 EndSlots로 닫는다.
        internal void BeginSlots() => Slots.Begin();

        internal void AddSlot(int index, string label, string? itemId, Sprite? icon, string itemName, int count, bool selected) =>
            Slots.Acquire(_slotGrid).Bind(this, _slotList, index, itemId, icon, itemName, count, selected, label, false);

        internal void EndSlots() => Slots.End();

        // 원작 inventoryDisplay.Setup(캐릭터 가방)이다. 수용량 글자는 목록 도우미가 원작 형식으로 만든다. AddBagCell 뒤 EndBag으로 닫는다.
        internal void BeginBag(int itemCount, int capacity) => Bag.Begin(itemCount, capacity);

        // 원작 InventoryEntry 하나다. 가방 칸은 빈 칸을 고르지 않는다.
        internal void AddBagCell(int position, string? itemId, Sprite? icon, string itemName, int count, bool selected) =>
            Bag.Add().Bind(this, _bagList, position, itemId, icon, itemName, count, selected, null, false);

        // 원작 RefreshGridLayoutPreferredHeight다. 쓰지 않은 칸 숨기기와 격자 높이 맞춤은 목록 도우미가 맡는다.
        internal void EndBag() => Bag.End();

        // 원작 RefreshSelectedItemInfo와 OnItemSelectionChanged의 버튼 전환이다. 고른 것이 없으면 원작처럼 이름은 "-", 아이콘은 투명,
        // 두 표시는 꺼지고 자리표시가 보인다. customizable은 원작 CanCustomize(아이템 칸 1개 이상)다.
        internal void ShowSelected(bool selected, string itemName, Sprite? icon, bool customizable)
        {
            _selectedName.text = selected ? itemName : _noItemName;
            _selectedIcon.sprite = selected ? icon : null;
            _selectedIcon.color = selected ? Color.white : Color.clear;
            _customizable.SetActive(selected && customizable);
            _uncustomizable.SetActive(selected && !customizable);
            bool begin = selected && customizable;
            _customizeButton.SetActive(begin);
            _placeholder.SetActive(!begin);
        }

        // 칸 View가 넘긴 클릭이다. 두 줄 모두 찬 칸만 넘긴다(Bind의 emptyClickable 거짓).
        void IOriginalItemCustomizeCellOwner.CellClicked(int list, int index, string? itemId)
        {
            if (itemId != null) ItemSelected?.Invoke(itemId);
        }

        // 고정 버튼은 제작 도구가 Runtime Only 영속 OnClick으로 이 공개 핸들러에 연결한다(Docs/rules/ui.md).
        // 버튼은 개조할 수 있는 아이템을 고른 동안만 보이므로 그때만 입력이 닿는다.
        public void OnBeginCustomizeClicked()
        {
            Log.Instance.Assert(_customizeButton.activeInHierarchy, "개조 시작 버튼은 개조할 수 있는 아이템을 고른 동안만 보입니다.", this);
            BeginRequested?.Invoke();
        }
    }
}
