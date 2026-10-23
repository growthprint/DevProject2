#nullable enable annotations
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 IItemDragSource처럼 소지품 칸과 장비 칸이 함께 쓰는 아이템 표시와 끌어 놓기다. 칸은 자기 자리만 인벤토리 패널에 넘기고
    // 이동 규칙은 화면 소유자가 정한다. 직렬화 필드 이름은 InventoryEntry와 SlotDisplay 프리팹이 쓰므로 바꾸지 않는다.
    public abstract class HudItemCell : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        [SerializeField] private protected Image _icon = null!;
        // 이름과 수량은 각자 배경 띠 컨테이너의 유일한 자식이어야 한다. 빈 칸은 그 컨테이너째 숨긴다.
        [SerializeField] private protected TMP_Text _name = null!;
        [SerializeField] private protected TMP_Text _count = null!;
        // 칸마다 클로저를 만들지 않도록 모든 칸이 같은 패널을 부르고 자기 칸 자리를 값으로 넘긴다.
        // ShowItem은 둘을 함께 넣고 ResetBinding은 함께 비우므로, 찬 칸이면 _panel이 늘 있다.
        private protected HudInventoryPanel? _panel;
        private protected InventoryCellRef _cell;

        // 재사용하는 칸에 칸 하나를 표시한다. 원본 빈 칸처럼 빈 칸은 아이콘, 이름, 수량을 숨기고,
        // 템플릿에 남은 색을 덮어써 원본 아이콘을 제 색으로 보인다.
        private protected void ShowItem(HudInventoryPanel panel, InventoryCellRef cell, string itemName, Sprite? icon, int count)
        {
            _panel = panel;
            _cell = cell;
            bool occupied = !cell.IsEmpty;
            _icon.sprite = icon;
            _icon.enabled = occupied && icon != null;
            _icon.color = Color.white;
            _name.text = itemName;
            _count.text = count > 1 ? count.ToString() : "";
            ShowTextBox(_name, occupied);
            ShowTextBox(_count, occupied && count > 1);
        }

        // 풀로 돌아가거나 HUD 소유자가 바뀐 칸이 앞 칸의 명령을 부르지 않게 연결을 끊는다. 표시는 다음 Bind가 덮거나 셀 풀이 숨긴다.
        internal virtual void ResetBinding()
        {
            _panel = null;
            _cell = default;
        }

        // 원본 ItemDisplay처럼 이름과 수량은 글자 뒤 배경 띠인 부모 컨테이너째 숨긴다.
        static void ShowTextBox(TMP_Text text, bool visible) => text.transform.parent.gameObject.SetActive(visible);

        // 원본 IItemDragSource처럼 왼쪽 버튼으로 찬 칸을 끌 때만 끌기를 시작한다.
        public void OnBeginDrag(PointerEventData data)
        {
            if (!_cell.IsEmpty && data.button == PointerEventData.InputButton.Left) _panel.BeginDrag(this, _cell, _icon.sprite);
        }

        // 비어 있어도 IDragHandler가 있어야 EventSystem이 이 칸을 pointerDrag로 잡아 끌기 시작과 끝, 놓을 곳의 OnDrop을 보낸다.
        public void OnDrag(PointerEventData data) { }

        // 패널은 이 칸이 시작한 끌기일 때만 끝낸다. 놓기 알림이 먼저 오고 이 알림이 뒤따른다.
        public void OnEndDrag(PointerEventData data)
        {
            if (_panel != null && data.button == PointerEventData.InputButton.Left) _panel.EndDrag(this);
        }

        // 빈 칸도 놓기를 받는다. 출발 칸과 같은지와 끌기 중인지는 패널이 판단한다.
        public void OnDrop(PointerEventData data)
        {
            if (_panel != null && data.button == PointerEventData.InputButton.Left) _panel.Drop(_cell);
        }
    }
}
