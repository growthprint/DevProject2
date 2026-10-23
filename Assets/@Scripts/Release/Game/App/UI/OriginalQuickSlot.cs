using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.App
{
    // 퀵슬롯 칸의 드롭 영역이다. 인벤토리 셀을 놓으면 그 셀이 지금 표시하는 아이템의 등록을 요청한다.
    public sealed class OriginalQuickSlot : MonoBehaviour, IDropHandler
    {
        // 칸 번호는 프리팹에 저작한 직렬화 값이다.
        [SerializeField] int _index;
        // 스크롤바처럼 셀이 아닌 드래그 대상을 거른다. UnityEngine.Object에 ?.를 쓰지 않도록 TryGetComponent로 확인한다.
        public void OnDrop(PointerEventData data)
        {
            if (data.pointerDrag != null && data.pointerDrag.TryGetComponent(out OriginalInventoryCell cell))
                cell.AssignShortcut(_index);
        }
    }
}
