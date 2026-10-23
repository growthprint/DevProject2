#nullable enable annotations
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 작은 UI 요소와 프레젠터가 함께 쓰는 표시 도우미다. 요소가 앱 UI 컨트롤러에 기대지 않도록 따로 둔다.
    internal static class UiVisibility
    {
        // 표시 여부를 GameObject 활성 상태로 정해, 숨은 Selectable이 탐색과 레이캐스트에서 함께 빠지게 한다.
        internal static void Set(GameObject target, bool visible)
        {
            if (target.activeSelf != visible) target.SetActive(visible);
        }

        // 아이콘 칸은 스프라이트가 없으면 흰 사각형이 보이지 않도록 Image를 끈다.
        internal static void SetIcon(Image image, Sprite? sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite != null;
        }

        // 탭이나 선택지의 선택 표시처럼 같은 위치의 하나만 켜고 나머지를 끈다. 범위 밖 index면 모두 끈다.
        internal static void ShowOnly(GameObject[] targets, int index)
        {
            for (int position = 0; position < targets.Length; position++) targets[position].SetActive(position == index);
        }

        // 하위 Canvas가 있는 화면 뿌리를 켠 채로 두고 그리기, 포인터, 키보드 입력만 켜고 끈다. 뿌리를 끄고 켜면 하위 요소마다
        // OnEnable과 OnDisable이 돌고 레이아웃과 메시를 다시 만들어, 큰 화면은 한 프레임에 수십 ms가 든다. 숨긴 동안에도 계층이 살아 있어
        // 다시 보일 때 바뀐 요소만 다시 그린다. 뿌리에는 저작된 Canvas, GraphicRaycaster, CanvasGroup이 있다.
        internal static void SetCanvasShown(GameObject root, bool shown)
        {
            if (!root.activeSelf) root.SetActive(true);
            root.GetComponent<Canvas>().enabled = shown;
            root.GetComponent<GraphicRaycaster>().enabled = shown;
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.interactable = shown;
            group.blocksRaycasts = shown;
        }

        // 원작 Tabs/ViewArea 안에서 LootView의 형제로 열리는 탭 창(쪽지, 일지, 열쇠 도감)을 보인다. 공용 HUD는 Tabs가 보이는 것을
        // 인벤토리 열림으로 쓰므로, 보이는 동안 Tabs를 보이고 LootView를 꺼서 인벤토리 칸이 함께 그려지지 않게 한다.
        internal static void ShowTabView(GameObject view, GameObject tabs, GameObject lootView)
        {
            lootView.SetActive(false);
            SetCanvasShown(tabs, true);
            view.SetActive(true);
        }

        // 탭 창을 숨기고 Tabs와 LootView를 되돌린다. 공용 HUD 초기화와 화면 닫기는 숨은 창에도 부르므로, 창이 보이고 있었을 때만
        // 되돌려 인벤토리 같은 다른 화면이 쓰는 Tabs와 LootView를 건드리지 않는다.
        internal static void HideTabView(GameObject view, GameObject tabs, GameObject lootView)
        {
            if (!view.activeSelf) return;
            view.SetActive(false);
            lootView.SetActive(true);
            SetCanvasShown(tabs, false);
        }
    }
}
