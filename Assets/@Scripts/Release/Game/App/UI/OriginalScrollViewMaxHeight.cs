#nullable enable annotations
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 Duckov.UI.ScrollViewMaxHeight다. 스크롤 뷰의 선호 높이를 내용 높이로 두되 최대 높이를 넘지 않게 해, 상세 화면 속성 목록이
    // 내용만큼 자라다가 넘치면 스크롤된다. useTargetParentSize면 최대 높이는 부모 목표 높이에서 형제 높이와 여백을 뺀 값이다.
    // 원본은 내용 크기 변경 알림 컴포넌트도 쓰지만, 매 프레임 높이 비교가 같은 갱신을 하므로 그 알림은 두지 않는다. 필드 이름은 SharedHUD 저작 도구가 쓴다.
    [RequireComponent(typeof(RectTransform))]
    public sealed class OriginalScrollViewMaxHeight : UIBehaviour, ILayoutElement
    {
        [SerializeField] ScrollRect _scrollRect = null!;
        [SerializeField] int _layoutPriority = 1;
        [SerializeField] bool _useTargetParentSize;
        [SerializeField] float _targetParentHeight = 935f;
        [SerializeField] RectTransform[] _siblings = System.Array.Empty<RectTransform>();
        [SerializeField] float _parentLayoutMargin = 16f;
        [SerializeField] float _maxHeight = 100f;

        public float preferredHeight
        {
            get
            {
                float content = _scrollRect.content.sizeDelta.y;
                float limit = _maxHeight;
                if (_useTargetParentSize)
                {
                    float siblings = 0f;
                    foreach (RectTransform sibling in _siblings) siblings += sibling.rect.height;
                    limit = _targetParentHeight - siblings - _parentLayoutMargin;
                }
                return content > limit ? limit : content;
            }
        }

        public float minWidth => -1f;
        public float minHeight => -1f;
        public float preferredWidth => -1f;
        public float flexibleWidth => -1f;
        public float flexibleHeight => -1f;
        // 원작의 Unity ILayoutElement에는 없던 최대 크기다. 제한은 위 preferredHeight가 하므로 둘 다 두지 않는다(-1).
        public float maxWidth => -1f;
        public float maxHeight => -1f;
        public int layoutPriority => _layoutPriority;

        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }

        protected override void OnEnable()
        {
            base.OnEnable();
            SetDirty();
        }

        protected override void OnDisable()
        {
            SetDirty();
            base.OnDisable();
        }

        // 원본처럼 내용 높이가 바뀌어 선호 높이가 지금 높이와 다르면 레이아웃을 다시 계산하게 한다.
        void Update()
        {
            if (preferredHeight != ((RectTransform)transform).rect.height) SetDirty();
        }

        void SetDirty()
        {
            if (IsActive()) LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
    }
}
