#nullable enable annotations
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.App
{
    // 원본 DragHandler를 재구성한 화면 전체 드래그 영역이다. 드래그로 캐릭터를 돌리는 처리는 CustomizationScreen이 맡는다.
    [DisallowMultipleComponent]
    public sealed class CustomizationDragArea : MonoBehaviour, IDragHandler
    {
        public event Action<PointerEventData>? Dragged;

        public void OnDrag(PointerEventData eventData) => Dragged?.Invoke(eventData);
    }
}
