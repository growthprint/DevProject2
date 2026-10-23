#nullable enable annotations
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 LongPressButton이다. 누르고 있는 동안 채움 이미지를 늘리고, 누른 시간이 다 차면 한 번 알린다.
    // 원작처럼 시간 배율과 상관없는 시간으로 재며, 손을 떼거나 포인터가 벗어나면 진행을 버린다. 건설 목록의 종류 회수 버튼이 쓴다.
    public sealed class OriginalLongPressButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        // 원작 fill과 pressTime이다. 건설 목록 회수 버튼은 1초다.
        [SerializeField] Image _fill = null!;
        [SerializeField] float _pressTime = 1f;

        float _pressStartedAt;
        bool _pressed;

        // 원작 onPressFullfilled다. 무엇을 회수할지는 버튼을 가진 줄 View가 정한다.
        internal event Action? Fulfilled;

        float Progress => _pressed ? (Time.unscaledTime - _pressStartedAt) / _pressTime : 0f;

        void Update()
        {
            _fill.fillAmount = Progress;
            if (!_pressed || Progress < 1f) return;
            _pressed = false;
            Fulfilled?.Invoke();
        }

        // 꺼질 때 진행을 버려, 다시 켜졌을 때 남은 시간으로 회수되지 않게 한다.
        void OnDisable() => _pressed = false;

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            _pressStartedAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData) => _pressed = false;

        public void OnPointerExit(PointerEventData eventData) => _pressed = false;
    }
}
