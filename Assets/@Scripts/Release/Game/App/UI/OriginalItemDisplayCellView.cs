#nullable enable annotations
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.App
{
    // 원작 ItemDisplay 칸의 호버 표시(hoveringIndicator)를 여러 창의 칸 View가 함께 쓴다. 원작처럼 고를 수 있는 칸에 포인터가 올라가면 켜고,
    // 나가거나 다시 보일 때 끈다. 고를 수 있는지는 칸 종류마다 달라 파생 칸이 Hoverable로 정한다.
    // 파생 칸은 OnEnable을 따로 두지 않는다. 두면 Unity가 파생 쪽만 불러 다시 보일 때의 호버 끄기가 빠진다.
    public abstract class OriginalItemDisplayCellView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        // 원작 hoveringIndicator다. 모두 수리 목록 칸처럼 호버 표시가 없는 칸 템플릿도 있어 비어 있으면 건너뛴다.
        [SerializeField] private protected GameObject? _hovering;

        // 포인터가 올라갔을 때 호버 표시를 켤지다. 클릭을 받는 칸이면 참이다.
        private protected abstract bool Hoverable { get; }

        public void OnPointerEnter(PointerEventData data)
        {
            if (_hovering != null) _hovering.SetActive(Hoverable);
        }

        public void OnPointerExit(PointerEventData data) => HideHover();

        // 원작 OnEnable처럼 다시 보일 때 호버 표시를 끈다.
        void OnEnable() => HideHover();

        // 풀로 돌아가거나 연결을 끊은 칸이 앞 호버 표시를 남기지 않게 끈다.
        private protected void HideHover()
        {
            if (_hovering != null) _hovering.SetActive(false);
        }
    }
}
