#nullable enable annotations
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 CustomFaceUIColorPicker를 재구성한 색 선택 행이다. 제목, 현재 색 막대, 펼침 버튼, 팔레트 격자로 이뤄진다.
    // 격자 칸은 원본처럼 비활성 템플릿을 복제해 만들고 화면이 살아 있는 동안 재사용한다. 색 선택 명령과 구독은 CustomizationScreen이 맡는다.
    [DisallowMultipleComponent]
    public sealed class CustomizationColorPickerUguiView : MonoBehaviour
    {
        // 제목 참조는 제작 때 Editor의 ComponentAuthoring이 이름으로 채우고 코드에서는 읽지 않는다. 그래서 생기는 CS0414만 여기서 끈다.
#pragma warning disable CS0414
        [SerializeField] TMP_Text _label = null!;
#pragma warning restore CS0414
        [SerializeField] Button _toggle = null!;
        [SerializeField] Graphic _display = null!;
        [SerializeField] RectTransform _grid = null!;
        [SerializeField] CustomizationColorButtonUguiView _template = null!;

        readonly List<CustomizationColorButtonUguiView> _buttons = new();
        // 격자를 처음 펼칠 때 칸을 만들 팔레트다. 화면을 열 때 행 일곱 개의 칸 수백 개를 한꺼번에 복제하지 않으려고 칸 만들기를 미룬다
        // (Docs/runtime-boundaries.md#main-thread-split). 팔레트가 바뀌면 다음 펼침이 칸을 다시 맞춘다.
        readonly List<Color> _palette = new();
        bool _built;

        public Button Toggle => _toggle;
        // 격자를 한 번 펼친 뒤에야 칸이 있다. 펼치기 전에는 빈 목록이다.
        public IReadOnlyList<CustomizationColorButtonUguiView> Buttons => _buttons;
        public bool IsGridOpen => _grid.gameObject.activeSelf;

        // 현재 색 막대인 펼침 버튼은 명령이 바뀌지 않으므로 프리팹에 저장한 OnClick이 OnToggleClicked를 부른다.
        // 명령은 화면이 BindToggle로 넘긴다. 팔레트 칸은 이 View가 만들 때 구독하고, 누른 칸 번호를 BindSelect의 명령으로 넘긴다.
        System.Action? _onToggle;
        System.Action<int>? _onSelect;

        // 화면이 펼침 명령을 연결한다. null을 넘기면 연결을 끊는다.
        internal void BindToggle(System.Action? toggle) => _onToggle = toggle;

        // 화면이 팔레트 칸 선택 명령을 연결한다. 칸 번호는 팔레트 번호다. null을 넘기면 연결을 끊는다.
        internal void BindSelect(System.Action<int>? select) => _onSelect = select;

        // 프리팹에 저장한 OnClick만 부르는 진입점이다.
        public void OnToggleClicked() => _onToggle?.Invoke();

        // 마지막으로 표시한 현재 색의 RGB다. 외형의 다른 값만 바뀐 갱신에서 팔레트 칸 전체의 선택 표시를 다시 돌지 않으려고 둔다.
        bool _hasCurrent;
        Color _current;

        // 팔레트를 받아 두고, 격자가 이미 펼쳐져 있을 때만 바로 칸을 맞춘다.
        // 칸 색이 바뀌면 선택 표시도 달라지므로 기억한 현재 색을 버려 다음 SetCurrent가 다시 칠하게 한다.
        public void SetPalette(IReadOnlyList<Color> colors)
        {
            _hasCurrent = false;
            _palette.Clear();
            for (int index = 0; index < colors.Count; index++) _palette.Add(colors[index]);
            _built = false;
            if (IsGridOpen) BuildCells();
        }

        // 팔레트 수에 맞게 격자 칸을 만들거나 숨기고 색을 칠한다. 원본처럼 알파는 1로 고정한다. 새 칸은 만들 때 한 번만 구독한다.
        // 펼치기 전에 받은 현재 색이 있으면 그 색으로 선택 테두리를 칠한다.
        void BuildCells()
        {
            _built = true;
            _template.gameObject.SetActive(false);
            while (_buttons.Count < _palette.Count)
            {
                int paletteIndex = _buttons.Count;
                CustomizationColorButtonUguiView button = Instantiate(_template, _grid, false);
                button.name = "Color" + paletteIndex;
                button.Button.onClick.AddListener(() => _onSelect?.Invoke(paletteIndex));
                _buttons.Add(button);
            }
            for (int index = 0; index < _buttons.Count; index++)
            {
                bool used = index < _palette.Count;
                _buttons[index].gameObject.SetActive(used);
                if (!used) continue;
                Color color = _palette[index];
                _buttons[index].SetColor(new Color(color.r, color.g, color.b, 1f));
                if (_hasCurrent) _buttons[index].SetSelected(ColorMatchesRgb(_buttons[index].Color, _current));
            }
        }

        // 원본 SetColor처럼 현재 색 막대와 선택 테두리를 갱신한다. 표시는 알파를 무시하므로 RGB가 정확히 같으면 건너뛴다.
        // 칸을 아직 만들지 않았으면 테두리는 BuildCells가 칠한다.
        public void SetCurrent(Color color)
        {
            if (_hasCurrent && _current.r == color.r && _current.g == color.g && _current.b == color.b) return;
            _hasCurrent = true;
            _current = color;
            _display.color = new Color(color.r, color.g, color.b, 1f);
            if (!_built) return;
            foreach (CustomizationColorButtonUguiView button in _buttons)
                button.SetSelected(ColorMatchesRgb(button.Color, color));
        }

        const float _rgbTolerance = .00001f;

        // 팔레트 색과 현재 색은 알파를 무시하고 RGB만 비교한다.
        internal static bool ColorMatchesRgb(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < _rgbTolerance && Mathf.Abs(a.g - b.g) < _rgbTolerance && Mathf.Abs(a.b - b.b) < _rgbTolerance;

        // 색을 고르거나 행을 다시 채울 때 닫힌 격자에도 닫기를 요청하므로, 상태가 바뀔 때만 SetActive를 부른다.
        // 처음 펼치거나 팔레트가 바뀐 뒤 펼치면 그때 칸을 맞춘다.
        public void SetGridOpen(bool open)
        {
            if (open && !_built) BuildCells();
            UiVisibility.Set(_grid.gameObject, open);
        }
    }
}
