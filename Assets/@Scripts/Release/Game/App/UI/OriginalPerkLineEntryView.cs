#nullable enable annotations
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.UI.PerkLineEntry다. 관계 그래프의 레벨 구분선(PerkLevelLineNode) 하나를 이름 문구로 보인다.
    // 원작처럼 노드 이름을 번역하지 않고 그대로 보인다. 템플릿은 트리 지도 내용(Content) 안에 꺼진 채 저장된다.
    public sealed class OriginalPerkLineEntryView : MonoBehaviour
    {
        [SerializeField] TMP_Text _label = null!;

        // 원작 GetLayoutPosition(구분선 노드의 cachedPosition)이다.
        internal Vector2 LayoutPosition { get; private set; }
        internal RectTransform RectTransform => (RectTransform)transform;

        // 원작 Setup이다.
        internal void Bind(string label, Vector2 layoutPosition)
        {
            _label.text = label;
            LayoutPosition = layoutPosition;
        }
    }
}
