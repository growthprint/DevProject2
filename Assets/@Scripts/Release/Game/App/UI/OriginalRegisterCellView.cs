#nullable enable annotations
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 열쇠 등록 창과 설계도 연구 창의 칸 하나다. 목록 칸은 원작 InventoryEntry(InventoryEntry.prefab 중첩 인스턴스), 등록 칸은 원작 SlotDisplay이고
    // 둘 다 원작 ItemDisplay의 아이콘, 이름, 수량, 선택 표시를 쓴다. 목록 칸만 highlightIndicator와 disabledIndicator를, 등록 칸만 칸 이름과 빈 칸 아이콘을
    // 가지며 없는 참조는 비어 있어 != null로 거른다. 원작처럼 왼쪽 클릭은 선택, 더블클릭은 등록 칸으로 옮기기(등록 칸에서는 빼기)이고 조작할 수 없는 칸의
    // 클릭은 무시한다. 저장된 프리팹은 창마다 파생 형식을 가리키므로 공통 몸통만 여기 둔다. 목록 칸 안의 OriginalInventoryCell은 제작 도구가 꺼 둔다.
    public abstract class OriginalRegisterCellView : OriginalItemDisplayCellView, IPointerClickHandler
    {
        // 등록 칸의 목록 번호다. 목록 번호(0부터)와 겹치지 않고 두 등록 창이 같은 값을 쓴다.
        internal const int _slotList = -1;

        [SerializeField] Image _icon = null!;
        // 이름과 수량은 원작 ItemDisplay처럼 글자 뒤 띠(nameContainer, countGameObject)째 숨긴다.
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] GameObject _nameBox = null!;
        [SerializeField] TMP_Text _count = null!;
        [SerializeField] GameObject _countBox = null!;
        [SerializeField] GameObject _selection = null!;
        // 아래 셋은 목록 칸에만 있다. 원작처럼 빈 칸 버튼은 탐색과 강조 대상이 되지 않게 끈다.
        [SerializeField] Button? _button;
        [SerializeField] GameObject? _highlight;
        [SerializeField] GameObject? _disabled;
        // 아래 둘은 등록 칸에만 있다. 원작 SlotDisplay처럼 칸 아이콘은 빈 칸에서만 보인다.
        [SerializeField] TMP_Text? _label;
        [SerializeField] GameObject? _slotIcon;

        IOriginalRegisterCellOwner? _owner;
        int _list = -1;
        string? _itemId;
        bool _operable;

        // 원작처럼 조작할 수 있는 칸에만 호버 표시를 켠다. 조작할 수 있으면 주인이 늘 있어 주인은 보지 않는다.
        private protected override bool Hoverable => _operable;

        // 원작 InventoryEntry.Refresh다. itemId가 null이면 빈 칸이다. 원작 EntryFunc_CanOperate처럼 빈 칸은 늘 조작할 수 있다.
        internal void BindCell(IOriginalRegisterCellOwner owner, int list, string? itemId, Sprite? icon, string itemName, int count, bool selected,
            bool highlight, bool operable)
        {
            Bind(owner, list, itemId, operable);
            bool occupied = itemId != null;
            // 아이템 표시는 다른 창 칸과 같은 원작 ItemDisplay 규칙을 쓴다.
            OriginalItemDisplay.Show(_icon, _name, _nameBox, _count, _countBox, icon, itemName, count, occupied);
            _selection.SetActive(selected && occupied);
            if (_highlight != null) _highlight.SetActive(highlight && occupied);
            if (_disabled != null) _disabled.SetActive(!operable);
            if (_button != null) _button.interactable = occupied && operable;
            gameObject.SetActive(true);
        }

        // 원작 SlotDisplay.Setup과 Refresh다. 등록 칸 이름은 원작 Slot.DisplayName(첫 requireTags의 표시 이름)이다.
        internal void BindSlot(IOriginalRegisterCellOwner owner, string label, string? itemId, Sprite? icon, string itemName, int count, bool selected)
        {
            Bind(owner, _slotList, itemId, true);
            bool occupied = itemId != null;
            // 등록 칸도 목록 칸과 같은 원작 ItemDisplay 규칙을 쓴다.
            OriginalItemDisplay.Show(_icon, _name, _nameBox, _count, _countBox, icon, itemName, count, occupied);
            _selection.SetActive(selected && occupied);
            if (_label != null) _label.text = label;
            if (_slotIcon != null) _slotIcon.SetActive(!occupied);
        }

        // 풀로 돌아간 칸이나 닫힌 창의 칸이 앞 의도를 보내지 않게 연결을 끊는다. 표시는 다음 Bind가 덮거나 줄 모음이 숨긴다.
        internal void ResetBinding()
        {
            _owner = null;
            _list = -1;
            _itemId = null;
            _operable = false;
            HideHover();
        }

        // 원작 InventoryEntry.OnPointerClick, InventoryDisplay와 SlotDisplay의 더블클릭이다. 빈 칸과 조작할 수 없는 칸은 원작처럼 무시한다.
        // Bind와 ResetBinding이 주인과 조작 여부를 함께 바꾸므로, 조작할 수 있는 칸이면 주인이 늘 있다.
        public void OnPointerClick(PointerEventData data)
        {
            if (_itemId == null || !_operable || data.button != PointerEventData.InputButton.Left) return;
            _owner!.CellClicked(_list, _itemId, data.clickCount >= 2);
        }

        void Bind(IOriginalRegisterCellOwner owner, int list, string? itemId, bool operable)
        {
            _owner = owner;
            _list = list;
            _itemId = itemId;
            _operable = operable;
        }
    }
}
