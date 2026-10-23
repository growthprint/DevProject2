#nullable enable annotations
using System.Collections;
using UnityEngine;

namespace Game.App
{
    // 원본 ScrollViewBorderFrame을 재현한다. 스크롤할 콘텐츠가 남은 방향의 테두리만 보여 주며 입력과 레이아웃은 건드리지 않는다.
    [DisallowMultipleComponent]
    public sealed class OriginalScrollViewBorderFrame : MonoBehaviour
    {
        // 원본 ScrollRect와 네 방향 테두리 이미지다. Editor의 ComponentAuthoring.Configure가 원본 값으로 채운다.
        // _scroll과 그 viewport, content는 제작 도구가 늘 연결하는 참조라 null을 검사하지 않는다.
        [SerializeField] UnityEngine.UI.ScrollRect _scroll = null!;
        [SerializeField] UnityEngine.UI.Graphic? _upGraphic;
        [SerializeField] UnityEngine.UI.Graphic? _downGraphic;
        [SerializeField] UnityEngine.UI.Graphic? _leftGraphic;
        [SerializeField] UnityEngine.UI.Graphic? _rightGraphic;
        // 원본 값이다. 넘친 거리에 _extendOffset을 더한 값이 _extendThreshold에 이르면 테두리가 _maxAlpha까지 진해진다.
        // Refresh가 _extendThreshold로 바로 나누므로, 0보다 큰지는 EditMode 저작 검사가 제품 프리팹과 씬에서 확인한다.
        [SerializeField, Range(0f, 1f)] float _maxAlpha = 1f;
        [SerializeField] float _extendThreshold = 10f;
        [SerializeField] float _extendOffset;

        // 원본처럼 켜질 때 스크롤 이벤트를 구독하고, 레이아웃이 자리 잡은 세 프레임 뒤에 다시 계산한다.
        void OnEnable()
        {
            HideFrames();
            _scroll.onValueChanged.AddListener(OnScrolled);
            StartCoroutine(RefreshAfterLayout());
        }

        // 원본처럼 Start에서도 한 번 계산해 첫 표시를 맞춘다.
        void Start() => Refresh();

        // 원본의 세 번 yield와 같다. 화면이 닫히면 OnDisable이 대기를 멈춘다.
        IEnumerator RefreshAfterLayout()
        {
            yield return null;
            yield return null;
            yield return null;
            Refresh();
        }

        // 화면 뿌리를 끄지 않고 Canvas로 숨기는 화면이 다시 보일 때 부른다. 켜질 때와 같이 테두리를 숨기고
        // 레이아웃이 자리 잡은 세 프레임 뒤에 다시 계산한다. 꺼져 있으면 다음 OnEnable이 같은 일을 하므로 건너뛴다.
        // Edit Mode에서는 OnEnable처럼 코루틴이 돌지 않으므로 아무것도 하지 않는다.
        internal void RefreshWhenShown()
        {
            if (!isActiveAndEnabled || !Application.isPlaying) return;
            StopAllCoroutines();
            HideFrames();
            StartCoroutine(RefreshAfterLayout());
        }

        // 화면을 다시 열 때 구독이나 대기 중인 계산이 겹치지 않도록 모두 풀고 테두리를 숨긴다.
        void OnDisable()
        {
            if (_scroll != null) _scroll.onValueChanged.RemoveListener(OnScrolled);
            StopAllCoroutines();
            HideFrames();
        }

        // 스크롤 위치 값은 쓰지 않고, 실제 콘텐츠 경계로 다시 계산한다.
        void OnScrolled(Vector2 position) => Refresh();

        // 방향마다 viewport 밖으로 넘친 콘텐츠 거리로 테두리 알파를 정한다. 원본 계산 그대로이며, 넘친 콘텐츠가 없으면 투명하다.
        public void Refresh()
        {
            Rect rect = _scroll.viewport.rect;
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_scroll.viewport, _scroll.content);
            SetAlpha(_upGraphic, Mathf.Lerp(0f, _maxAlpha, (bounds.max.y - rect.yMax + _extendOffset) / _extendThreshold));
            SetAlpha(_downGraphic, Mathf.Lerp(0f, _maxAlpha, (rect.yMin - bounds.min.y + _extendOffset) / _extendThreshold));
            SetAlpha(_leftGraphic, Mathf.Lerp(0f, _maxAlpha, (rect.xMin - bounds.min.x + _extendOffset) / _extendThreshold));
            SetAlpha(_rightGraphic, Mathf.Lerp(0f, _maxAlpha, (bounds.max.x - rect.xMax + _extendOffset) / _extendThreshold));
        }

        // 켜질 때와 꺼질 때 네 방향 테두리를 모두 숨겨 남은 강조가 보이지 않게 한다.
        void HideFrames()
        {
            SetAlpha(_upGraphic, 0f);
            SetAlpha(_downGraphic, 0f);
            SetAlpha(_leftGraphic, 0f);
            SetAlpha(_rightGraphic, 0f);
        }

        // 색은 두고 알파만 바꾼다. 알파가 같으면 메시를 다시 만들지 않도록 건너뛴다.
        static void SetAlpha(UnityEngine.UI.Graphic? graphic, float alpha)
        {
            if (graphic == null || graphic.color.a == alpha) return;
            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }
}
