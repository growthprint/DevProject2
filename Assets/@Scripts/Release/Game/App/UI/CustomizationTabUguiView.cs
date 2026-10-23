#nullable enable annotations
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 CustomFaceTabs를 재구성한 왼쪽 부위 탭이다. 누르면 이 탭의 패널만 보이고 배경이 선택 색으로 바뀐다.
    [DisallowMultipleComponent]
    public sealed class CustomizationTabUguiView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] Graphic _background = null!;
        [SerializeField] Color _normalColor = new(0f, 0f, 0f, 0f);
        [SerializeField] Color _selectedColor = new(0.069419734f, 0.35869557f, 0.3773585f, 1f);
        [SerializeField] GameObject[] _panels = Array.Empty<GameObject>();

        // 원본처럼 포인터 클릭으로만 탭을 고른다. 패널 전환은 화면이 열려 있는 동안 구독한 쪽이 처리한다.
        public event Action<CustomizationTabUguiView>? Clicked;
        public GameObject[] Panels => _panels;

        public void SetSelected(bool selected) => _background.color = selected ? _selectedColor : _normalColor;

        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke(this);
    }
}
