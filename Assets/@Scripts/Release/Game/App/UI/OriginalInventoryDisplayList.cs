#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 InventoryDisplay 하나의 칸 목록이다. 여러 창의 가방과 창고 목록이 칸 풀, 수용량 글자, 격자 높이 맞춤을 같은 순서로 쓰도록 한곳에 둔다.
    // 참조는 각 View의 직렬화 필드이고 저작 검사가 연결을 확인한다. Begin 뒤 Add로 칸을 꺼내고 End로 닫으며, 셋은 같은 동기 호출 안에서 짝을 이룬다.
    internal sealed class OriginalInventoryDisplayList<TCell> where TCell : Component
    {
        readonly UiTemplateCollection<TCell> _cells;
        readonly RectTransform _grid;
        readonly GameObject _placeholder;
        readonly TMP_Text _capacityText;
        readonly GridLayoutGroup _layout;
        readonly LayoutElement _height;
        // 이번 Begin 뒤에 꺼낸 칸 수다. 격자 높이와 자리표시를 정한다.
        int _count;

        // 원작 InventoryDisplay의 entryPrefab, entriesParent, placeHolder, capacityText, contentLayout, gridLayoutElement다.
        // reset은 풀로 돌아가는 칸의 연결을 끊는다.
        public OriginalInventoryDisplayList(TCell template, Action<TCell> reset, RectTransform grid, GameObject placeholder, TMP_Text capacityText,
            GridLayoutGroup layout, LayoutElement height)
        {
            _cells = new UiTemplateCollection<TCell>(template, reset);
            _grid = grid;
            _placeholder = placeholder;
            _capacityText = capacityText;
            _layout = layout;
            _height = height;
        }

        // 원작 InventoryDisplay.Setup이다. 앞 칸의 연결을 끊고, 수용량 글자는 다른 목록 창과 같은 원작 형식 도우미가 만든다.
        public void Begin(int itemCount, int capacity)
        {
            _count = 0;
            _cells.Begin();
            _capacityText.text = OriginalItemDisplay.CapacityText(itemCount, capacity);
        }

        // 원작 InventoryEntry 하나를 격자에서 꺼낸다. 칸 연결과 표시는 호출자가 맡는다.
        public TCell Add()
        {
            TCell cell = _cells.Acquire(_grid);
            _count++;
            return cell;
        }

        // 원작 RefreshGridLayoutPreferredHeight다. 쓰지 않은 칸을 숨기고 격자 높이와 자리표시는 다른 목록 창과 같은 도우미가 맞춘다.
        public void End()
        {
            _cells.End();
            OriginalItemDisplay.FitGrid(_layout, _height, _placeholder, _count);
        }

        // 창을 닫을 때 모든 칸의 연결을 끊고 숨긴다.
        public void Clear() => _cells.Clear();
    }
}
