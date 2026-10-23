#nullable enable annotations
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 ItemDisplay와 InventoryDisplay의 표시 규칙이다. 여러 창의 칸 View와 목록 View가 같은 규칙을 쓰도록 한곳에 둔다.
    // 참조는 각 View의 직렬화 필드이고 저작 검사가 연결을 확인한다.
    internal static class OriginalItemDisplay
    {
        // 원작 ItemDisplay의 아이콘, 이름, 수량이다. 템플릿에 남은 슬라이스와 색을 덮어써 원본 아이콘을 제 비율과 색으로 보인다.
        // 글자 뒤 띠가 없는 칸 템플릿도 있어, 띠는 아래 겹침 메서드나 호출자가 정한다.
        internal static void Show(Image icon, TMP_Text nameText, TMP_Text countText, Sprite? sprite, string itemName, int count, bool occupied)
        {
            icon.sprite = sprite;
            icon.type = Image.Type.Simple;
            icon.preserveAspect = true;
            icon.color = Color.white;
            icon.enabled = occupied && sprite != null;
            nameText.text = itemName;
            countText.text = count > 1 ? count.ToString() : "";
        }

        // 원작 ItemDisplay처럼 이름과 수량을 글자 뒤 띠(nameContainer, countGameObject)째 숨긴다. 수량 띠는 둘 이상일 때만 보인다.
        internal static void Show(Image icon, TMP_Text nameText, GameObject nameBox, TMP_Text countText, GameObject countBox, Sprite? sprite,
            string itemName, int count, bool occupied)
        {
            Show(icon, nameText, countText, sprite, itemName, count, occupied);
            nameBox.SetActive(occupied);
            countBox.SetActive(occupied && count > 1);
        }

        // 원작 InventoryDisplay의 capacityTextFormat "({1}/{0})"에 용량과 아이템 수를 넣은 글자다.
        internal static string CapacityText(int itemCount, int capacity) => "(" + itemCount + "/" + capacity + ")";

        // 원작 InventoryDisplay.RefreshGridLayoutPreferredHeight다. 줄 수 × 칸 높이 + 위아래 여백이고 줄 간격은 더하지 않는다.
        // 칸이 없으면 자리표시를 켠다.
        internal static void FitGrid(GridLayoutGroup layout, LayoutElement height, GameObject placeholder, int cellCount)
        {
            int rows = Mathf.CeilToInt(cellCount / (float)Mathf.Max(1, layout.constraintCount));
            height.preferredHeight = rows * layout.cellSize.y + layout.padding.top + layout.padding.bottom;
            placeholder.SetActive(cellCount <= 0);
        }
    }
}
