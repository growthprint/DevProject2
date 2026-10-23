#nullable enable annotations
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 InventoryEntry 한 칸이다. 클릭은 선택, 더블클릭과 우클릭은 소비나 장착, 끌어 놓기는 이동이나 퀵슬롯 등록이다.
    // 셀은 소지품을 직접 바꾸지 않고 자기 칸 자리만 인벤토리 패널에 넘긴다. 표시와 끌어 놓기는 장비 칸과 같은 HudItemCell 규칙을 쓴다.
    // 원본 InventoryEntry처럼 포인터가 올라간 칸을 패널에 알려, 인벤토리가 열린 동안 숫자키가 그 칸을 퀵슬롯에 등록한다.
    public sealed class OriginalInventoryCell : HudItemCell, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        // 원본 ItemDisplay의 주황 SelectionIndicator 이미지다. 직렬화 이름은 프리팹이 쓰므로 그대로 두고, 선택한 칸에만 켠다.
        [SerializeField] Image _equipped = null!;
        // 클릭은 OnPointerClick 한 경로로만 받으므로 버튼에는 리스너를 달지 않는다. 빈 칸은 탐색과 강조 대상이 되지 않게 끈다.
        [SerializeField] Button _button = null!;

        // 재사용하는 셀에 칸 하나를 표시한다. 템플릿에 남은 슬라이스 설정을 덮어써 원본 아이콘을 제 비율로 보인다.
        internal void Bind(HudInventoryPanel panel, InventoryCellRef cell, string itemName, Sprite? icon, int count)
        {
            ShowItem(panel, cell, itemName, icon, count);
            _icon.type = Image.Type.Simple; _icon.preserveAspect = true;
            _equipped.enabled = false;
            _button.interactable = !cell.IsEmpty;
        }

        // 패널이 선택 문구를 보이는 동안 선택한 칸에만 원본 주황 테두리를 켠다.
        internal void SetMarked(bool marked) => _equipped.enabled = marked;

        // 클릭을 한 경로로 받아 순서를 정한다. 원본처럼 왼쪽 클릭은 찬 칸을 선택하고, 더블클릭과 우클릭은 활성화를 요청한다.
        // 찬 칸이면 패널이 늘 있으므로 칸이 비었는지만 본다.
        public void OnPointerClick(PointerEventData data)
        {
            if (_cell.IsEmpty) return;
            bool left = data.button == PointerEventData.InputButton.Left;
            if (data.button == PointerEventData.InputButton.Right || (left && data.clickCount >= 2)) _panel.ActivateCell(_cell, this);
            else if (left) _panel.SelectCell(_cell, this);
        }

        // 패널이 숫자키를 받을 때 지금 이 셀이 보이는 칸을 읽는다. 셀은 재사용되므로 들어온 순간의 칸이 아니라 현재 칸을 쓴다.
        internal InventoryCellRef Cell => _cell;

        // 연결이 끊긴 풀 셀은 패널이 없어 알리지 않는다. 나가기는 패널이 이 셀이 기억된 경우에만 지운다.
        public void OnPointerEnter(PointerEventData data)
        {
            if (_panel != null) _panel.Hover(this);
        }

        public void OnPointerExit(PointerEventData data)
        {
            if (_panel != null) _panel.Unhover(this);
        }

        // 이 셀을 퀵슬롯에 놓으면 OriginalQuickSlot이 부른다. 빈 칸은 등록할 아이템이 없어 무시하고, 찬 칸이면 패널이 늘 있다.
        internal void AssignShortcut(int slot)
        {
            if (!_cell.IsEmpty) _panel.RequestShortcut(_cell, this, slot);
        }
    }
}
