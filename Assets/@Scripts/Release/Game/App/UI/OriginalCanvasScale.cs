#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 CanvasScalerController의 배율 규칙을 제품 캔버스 안에 중첩된 공용 패널에 적용한다.
    // 배율은 화면 픽셀을 기준 해상도로 나눈 가로와 세로 값 중 작은 쪽이다. 예: 1920×1080 화면, 대사 기준 2520×1080이면 0.762이고 원본 대사 상자 폭은 762px이다.
    // 중첩 캔버스에서는 CanvasScaler가 동작하지 않아 부모를 덮는 크기와 로컬 배율을 런타임에만 맞추므로 프리팹 값은 바뀌지 않는다.
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class OriginalCanvasScale : MonoBehaviour
    {
        [SerializeField] Vector2 _referenceResolution = new(2520f, 1080f);

        // OnEnable이 늘 채운다. 이 컴포넌트는 제품 캔버스 안에 중첩된 패널에만 붙고 SharedHudTests가 그 자리를 확인한다.
        Canvas _root = null!;
        float _appliedScale;
        Vector2 _appliedParent;

        void OnEnable()
        {
            _root = GetComponentInParent<Canvas>(true).rootCanvas;
            _appliedScale = 0f;
            Apply();
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            // 이 컴포넌트는 제품 캔버스 안에 중첩된 패널에만 붙이고 그 패널이 켜질 때만 돌므로 루트 캔버스와 RectTransform 부모가 늘 있다.
            var parent = (RectTransform)transform.parent;
            Vector2 pixels = _root.pixelRect.size;
            float target = Mathf.Min(pixels.x / _referenceResolution.x, pixels.y / _referenceResolution.y);
            // 부모 한 단위가 차지하는 픽셀 = 루트 scaleFactor × 루트 대비 부모 누적 배율.
            float parentPixels = _root.scaleFactor * parent.lossyScale.x / _root.transform.lossyScale.x;
            if (target <= 0f || parentPixels <= 0f) return;
            float scale = target / parentPixels;
            Vector2 parentSize = parent.rect.size;
            if (Mathf.Approximately(scale, _appliedScale) && parentSize == _appliedParent) return;
            _appliedScale = scale;
            _appliedParent = parentSize;
            var rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = parentSize / scale;
            rect.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
