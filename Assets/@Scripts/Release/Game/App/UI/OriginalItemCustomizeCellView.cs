#nullable enable annotations
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 개조 두 창이 함께 쓰는 칸 하나다. 원작 SlotDisplay(장비 칸, 개조 대상의 아이템 칸)와 InventoryEntry·ItemDisplay(가방 칸, 부품 후보)가
    // 같은 원작 ItemDisplay 표시(아이콘, 이름, 수량, 선택)를 쓰므로 한 View로 둔다. 칸 이름과 빈 칸 아이콘은 칸 템플릿에만 있고, 없는 참조는 비어 있다.
    // 클릭은 의도만 주인 창 View에 넘긴다. 원작처럼 왼쪽 클릭이 선택이다. 아이템 칸은 빈 칸도 고를 수 있고(원작 onElementClicked),
    // 가방 칸과 후보 칸은 빈 칸을 무시한다. 안의 OriginalSlotCell·OriginalInventoryCell은 인벤토리 화면 전용이라 제작 도구가 꺼 둔다.
    public sealed class OriginalItemCustomizeCellView : OriginalItemDisplayCellView, IPointerClickHandler
    {
        [SerializeField] Image _icon = null!;
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] TMP_Text _count = null!;
        // 원작 ItemDisplay의 nameContainer, countGameObject, selectionIndicator다. 칸 템플릿에 없으면 비어 있다.
        // 호버 표시(hoveringIndicator)는 공통 칸 기반이 맡는다.
        [SerializeField] GameObject? _nameBox;
        [SerializeField] GameObject? _countBox;
        [SerializeField] GameObject? _selection;
        // 원작처럼 빈 가방 칸 버튼은 탐색과 강조 대상이 되지 않게 끈다. 가방 칸에만 있다.
        [SerializeField] Button? _button;
        // 칸 템플릿에만 있다. 원작 SlotDisplay처럼 칸 아이콘은 빈 칸에서만 보인다.
        [SerializeField] TMP_Text? _label;
        [SerializeField] GameObject? _slotIcon;

        IOriginalItemCustomizeCellOwner? _owner;
        int _list = -1, _index = -1;
        string? _itemId;
        bool _emptyClickable;

        // 고를 수 있는 칸(찬 칸이거나 빈 칸을 고를 수 있는 칸)이다. 호버 표시와 클릭이 같은 판정을 쓴다.
        private protected override bool Hoverable => _itemId != null || _emptyClickable;

        // 칸을 묶는다. itemId가 null이면 빈 칸이다. label은 칸 템플릿만 쓰며 null이면 바꾸지 않는다.
        internal void Bind(IOriginalItemCustomizeCellOwner owner, int list, int index, string? itemId, Sprite? icon, string itemName, int count,
            bool selected, string? label, bool emptyClickable)
        {
            _owner = owner;
            _list = list;
            _index = index;
            _itemId = itemId;
            _emptyClickable = emptyClickable;
            bool occupied = itemId != null;
            // 아이템 표시는 다른 창 칸과 같은 원작 ItemDisplay 규칙을 쓴다. 칸 템플릿에 따라 띠가 없어 띠만 여기서 정한다.
            OriginalItemDisplay.Show(_icon, _name, _count, icon, itemName, count, occupied);
            if (_nameBox != null) _nameBox.SetActive(occupied);
            if (_countBox != null) _countBox.SetActive(occupied && count > 1);
            if (_selection != null) _selection.SetActive(selected);
            if (_button != null) _button.interactable = occupied || emptyClickable;
            if (_label != null && label != null) _label.text = label;
            if (_slotIcon != null) _slotIcon.SetActive(!occupied);
            gameObject.SetActive(true);
        }

        // 풀로 돌아간 칸이나 닫힌 창의 칸이 앞 의도를 보내지 않게 연결을 끊는다. 표시는 다음 Bind가 덮거나 줄 모음이 숨긴다.
        internal void ResetBinding()
        {
            _owner = null;
            _list = -1;
            _index = -1;
            _itemId = null;
            _emptyClickable = false;
            HideHover();
        }

        // Bind와 ResetBinding이 주인, 아이템, 빈 칸 클릭 여부를 함께 바꾸므로, 고를 수 있는 칸이면 주인이 늘 있다.
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || !Hoverable) return;
            _owner!.CellClicked(_list, _index, _itemId);
        }
    }

    // 칸 클릭을 받는 개조 창 View다. list는 그 창의 줄 번호, index는 칸 번호, itemId는 칸의 아이템(빈 칸이면 null)이다.
    internal interface IOriginalItemCustomizeCellOwner
    {
        void CellClicked(int list, int index, string? itemId);
    }
}
