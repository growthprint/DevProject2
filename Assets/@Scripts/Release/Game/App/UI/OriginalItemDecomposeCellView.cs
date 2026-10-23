#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 분해 창 목록의 칸 하나다. 원작 InventoryEntry(InventoryEntry.prefab 중첩 인스턴스)의 아이콘, 이름, 수량, 선택 표시와,
    // 원작 EntryFunc_CanOperate가 거짓일 때 켜는 disabledIndicator(분해식이 없는 아이템)를 보인다. 원작 분해 창은 강조 함수를 넘기지 않아 강조 표시는 늘 꺼져 있다.
    // 클릭은 의도만 분해 창 View에 넘긴다. 원작처럼 왼쪽 클릭은 선택이고, 분해 창은 더블클릭을 구독하지 않아 두 번째 클릭은 무시한다.
    // 조작할 수 없는 칸의 클릭도 무시한다. 호버 표시는 공통 칸 기반이 맡는다. 칸 안의 OriginalInventoryCell은 인벤토리 화면 전용이라 제작 도구가 꺼 둔다.
    public sealed class OriginalItemDecomposeCellView : OriginalItemDisplayCellView, IPointerClickHandler
    {
        [SerializeField] Image _icon = null!;
        // 이름과 수량은 원작 ItemDisplay처럼 글자 뒤 띠(nameContainer, countGameObject)째 숨긴다.
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] GameObject _nameBox = null!;
        [SerializeField] TMP_Text _count = null!;
        [SerializeField] GameObject _countBox = null!;
        [SerializeField] GameObject _selection = null!;
        // 원작 disabledIndicator와 칸 버튼이다. 원작처럼 빈 칸 버튼은 탐색 대상이 되지 않게 끈다.
        // 원작 highlightIndicator는 이 창에서 켜지지 않아 제작 도구가 끈 채 저장한다.
        [SerializeField] GameObject _disabled = null!;
        [SerializeField] Button _button = null!;

        // 칸 클릭을 받는 분해 창 View의 대리자다. View가 한 번 만든 것을 넘겨 칸마다 할당하지 않는다.
        Action<string>? _clicked;
        string? _itemId;
        bool _operable;

        // 원작처럼 조작할 수 있는 칸에만 호버 표시를 켠다. 조작할 수 있으면 대리자가 늘 있어 대리자는 보지 않는다.
        private protected override bool Hoverable => _operable;

        // 원작 InventoryEntry.Refresh다. itemId가 null이면 빈 칸이다. 원작 EntryFunc_CanOperate처럼 빈 칸은 늘 조작할 수 있다.
        internal void Bind(Action<string> clicked, string? itemId, Sprite? icon, string itemName, int count, bool selected, bool operable)
        {
            _clicked = clicked;
            _itemId = itemId;
            _operable = operable;
            bool occupied = itemId != null;
            // 아이템 표시는 다른 창 칸과 같은 원작 ItemDisplay 규칙을 쓴다.
            OriginalItemDisplay.Show(_icon, _name, _nameBox, _count, _countBox, icon, itemName, count, occupied);
            _selection.SetActive(selected && occupied);
            _disabled.SetActive(!operable);
            _button.interactable = occupied && operable;
            gameObject.SetActive(true);
        }

        // 풀로 돌아간 칸이나 닫힌 창의 칸이 앞 의도를 보내지 않게 연결을 끊는다. 표시는 다음 Bind가 덮거나 줄 모음이 숨긴다.
        internal void ResetBinding()
        {
            _clicked = null;
            _itemId = null;
            _operable = false;
            HideHover();
        }

        // 원작 InventoryEntry.OnPointerClick이다. 빈 칸, 조작할 수 없는 칸, 더블클릭의 두 번째 클릭은 무시한다.
        // Bind와 ResetBinding이 대리자와 조작 여부를 함께 바꾸므로, 조작할 수 있는 칸이면 대리자가 늘 있다.
        public void OnPointerClick(PointerEventData data)
        {
            if (_itemId == null || !_operable || data.button != PointerEventData.InputButton.Left || data.clickCount >= 2) return;
            _clicked!(_itemId);
        }
    }
}
