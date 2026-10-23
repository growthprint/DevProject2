#nullable enable annotations
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 CustomFaceUIColorPickerButton을 재구성한 팔레트 격자의 색 버튼이다. 선택된 색이면 테두리를 켠다.
    // 버튼과 테두리 참조는 제작 때 Editor의 ComponentAuthoring이 연결한다.
    [DisallowMultipleComponent]
    public sealed class CustomizationColorButtonUguiView : MonoBehaviour
    {
        [SerializeField] Button _button = null!;
        [SerializeField] GameObject _selectedFrame = null!;

        public Button Button => _button;
        // 칸의 팔레트 색은 SetColor가 버튼 평상시 색에 넣은 값 하나로만 둔다.
        public Color Color => _button.colors.normalColor;

        // 원본처럼 버튼의 평상시, 강조, 선택 색을 모두 팔레트 색으로 맞춰 격자 칸을 칠한다.
        public void SetColor(Color color)
        {
            ColorBlock colors = _button.colors;
            colors.normalColor = color;
            colors.highlightedColor = color;
            colors.selectedColor = color;
            _button.colors = colors;
        }

        // 현재 색이 바뀔 때마다 모든 칸에 불리므로, 선택 상태가 바뀐 칸만 SetActive를 부른다.
        public void SetSelected(bool selected) => UiVisibility.Set(_selectedFrame, selected);
    }
}
