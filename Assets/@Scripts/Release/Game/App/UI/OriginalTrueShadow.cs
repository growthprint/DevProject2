#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 LeTai TrueShadow를 재현한다. 그림자 정점을 같은 메시 앞쪽에 넣어 한 번에 그리므로 늘 그래픽 뒤에 놓이고 크기 변화도 따라간다.
    // 흐림은 근사다. OriginalProceduralImage는 거리장 폴오프로, 일반 그래픽은 넓힌 사본을 옅게 겹쳐 흉내 내며 원본과 같은지는 확인하지 않았다.
    // 원본의 inset, Normal 외 blendMode, colorBleed는 같은 재질로 한 번에 그려서는 표현할 수 없어 지원하지 않는다.
    // 한 객체에 여럿을 달면 원본처럼 각 그림자는 그래픽 자신만 그림자로 그린다. 나중 컴포넌트의 그림자가 앞 그림자 위, 그래픽 바로 뒤에 놓인다.
    // 그래서 그 객체에서는 다른 메시 효과가 마지막 OriginalTrueShadow보다 앞에 오면 안 된다(EditMode 프리팹 검사가 확인한다).
    [AddComponentMenu("UI/Effects/Original True Shadow")]
    public sealed class OriginalTrueShadow : BaseMeshEffect
    {
        [SerializeField, Min(0f)] float _size = 32f;
        [SerializeField, Range(0f, 1f)] float _spread;
        [SerializeField] float _offsetAngle = 90f;
        [SerializeField, Min(0f)] float _offsetDistance = 12f;
        [SerializeField] Color _color = new(0f, 0f, 0f, .6f);
        [SerializeField] bool _useCasterAlpha = true;
        [SerializeField] bool _ignoreCasterColor = true;

        readonly List<UIVertex> _vertices = new();
        // 메시를 갱신할 때마다 할당하지 않도록 재사용한다. VertexHelper가 내용을 복사해 가므로 재사용해도 안전하다.
        readonly List<UIVertex> _shadows = new();
        // 정점별 그림자 색이다. 겹침 층과 무관하므로 층 루프 전에 한 번만 구한다.
        readonly List<Color32> _shadowColors = new();
        // 일반 그래픽의 흐림을 근사할 때 겹치는 사본 수다.
        const int _softLayers = 4;
        // 흐림 반경이 이보다 작으면 흐림이 거의 없다고 보고 오프셋 사본 한 장만 그린다.
        const float _minimumSoftRadius = .5f;
        // 반폭이 0일 때 0으로 나누지 않도록 둔 분모 하한이다.
        const float _minimumExtent = .0001f;
        // 같은 객체의 그림자 컴포넌트를 순서대로 읽는 재사용 목록이다. 메시 갱신은 메인 스레드에서만 일어난다.
        static readonly List<OriginalTrueShadow> _siblings = new();

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || mesh.currentVertCount == 0) return;
            _vertices.Clear();
            mesh.GetUIVertexStream(_vertices);
            // 앞 그림자들은 그래픽 정점 앞에 층마다 그래픽 정점 수만큼 넣었다. 그 층 수로 그래픽 정점 수를 구해 그래픽만 그림자로 그리고,
            // 새 그림자는 앞 그림자들 뒤, 그래픽 정점 바로 앞에 넣는다. 그림자가 하나면 모든 정점이 그래픽이라 예전과 같다.
            int count = _vertices.Count / (1 + EarlierLayers());
            int casterStart = _vertices.Count - count;
            // 원본 TrueShadow의 각도 규칙이다. 예: 90도와 거리 12는 (0, -12), 45도와 거리 8.58은 (6.07, -6.07)이다.
            float radians = _offsetAngle * Mathf.Deg2Rad;
            Vector3 offset = new(Mathf.Cos(radians) * _offsetDistance, -Mathf.Sin(radians) * _offsetDistance, 0f);
            bool procedural = graphic is OriginalProceduralImage;
            float spread = _spread * _size * .5f;
            float falloff = Mathf.Max(1f, _size * (1f - _spread));
            // 일반 그래픽은 거리장이 없어 중심에서 넓힌 사본 여러 장을 옅게 겹친다. 원본 InteractHUD Marker의 원형 후광
            // size 10, spread 0.4를 보고 맞춘 근사다. 흐림이 거의 없으면 오프셋 사본 한 장만 그린다.
            float soft = SoftRadius;
            int layers = Layers();
            Rect bounds = graphic.rectTransform.rect;
            Vector3 middle = bounds.center;
            _shadows.Clear();
            _shadowColors.Clear();
            for (int index = 0; index < casterStart; index++) _shadows.Add(_vertices[index]);
            for (int index = 0; index < count; index++)
            {
                Color32 casterColor = _vertices[casterStart + index].color;
                Color color = _ignoreCasterColor ? _color : _color * (Color)casterColor;
                if (_useCasterAlpha) color.a = _color.a * casterColor.a / 255f;
                color.a /= layers;
                _shadowColors.Add(color);
            }
            // 거리장 그림자 계산에서 모든 정점에 같은 값이라 루프 밖에서 한 번 구한다.
            Vector2 grownOffset = Vector2.one * spread;
            float halfFalloff = falloff * .5f;
            Vector4 uv2Offset = Vector4.one * spread;
            for (int layer = 1; layer <= layers; layer++)
            {
                float grow = layers == 1 ? spread : spread + soft * layer / layers;
                Vector2 scale = new(bounds.width > 0f ? 1f + 2f * grow / bounds.width : 1f, bounds.height > 0f ? 1f + 2f * grow / bounds.height : 1f);
                for (int index = 0; index < count; index++)
                {
                    UIVertex vertex = _vertices[casterStart + index];
                    vertex.color = _shadowColors[index];
                    if (procedural)
                    {
                        // uv1 = (정점의 사각형 중심 기준 위치, 반폭·반높이), uv2 = 반경, uv3 = (테두리, 폴오프).
                        Vector2 half = new(vertex.uv1.z, vertex.uv1.w);
                        float oldFalloff = Mathf.Max(0f, vertex.uv3.y);
                        Vector2 local = new(vertex.uv1.x, vertex.uv1.y);
                        Vector3 center = vertex.position - (Vector3)local;
                        // 정점을 원래 바깥 경계에 대한 비율로 바꾼 뒤, 넓힌 크기와 새 폴오프의 바깥 경계에 다시 놓는다.
                        Vector2 corner = new(local.x / Mathf.Max(_minimumExtent, half.x + oldFalloff * .5f), local.y / Mathf.Max(_minimumExtent, half.y + oldFalloff * .5f));
                        Vector2 grown = half + grownOffset;
                        Vector2 shadowLocal = new(corner.x * (grown.x + halfFalloff), corner.y * (grown.y + halfFalloff));
                        vertex.position = center + (Vector3)shadowLocal + offset;
                        vertex.uv1 = new Vector4(shadowLocal.x, shadowLocal.y, grown.x, grown.y);
                        vertex.uv2 += uv2Offset;
                        // 링 모양 그래픽의 그림자도 링으로 남도록 바깥으로 넓힌 만큼 테두리 폭을 2×spread 늘린다.
                        // 폭 0은 속이 찬 모양을 뜻하므로 테두리가 없을 때만 0이다.
                        float border = vertex.uv3.x;
                        vertex.uv3 = new Vector4(border > 0f ? border + 2f * spread : 0f, falloff, 0f, 0f);
                    }
                    else
                    {
                        Vector3 local = vertex.position - middle;
                        vertex.position = middle + new Vector3(local.x * scale.x, local.y * scale.y, local.z) + offset;
                    }
                    _shadows.Add(vertex);
                }
            }
            // 그래픽 자신의 정점을 그림자 뒤에 붙여, 그림자가 먼저 그려져 그래픽 뒤에 놓이게 한다.
            for (int index = casterStart; index < _vertices.Count; index++) _shadows.Add(_vertices[index]);
            mesh.Clear();
            mesh.AddUIVertexTriangleStream(_shadows);
        }

        // 일반 그래픽 흐림의 반경이다. 메시를 만들 때와 층 수를 셀 때가 같은 값을 써야 그래픽 정점 수를 맞게 찾는다.
        float SoftRadius => _size * (1f - _spread) * .5f;

        // 거리장 그래픽은 폴오프로 흐리게 해 한 층이고, 일반 그래픽도 흐림이 거의 없으면 한 층이다.
        bool SingleLayer => graphic is OriginalProceduralImage || SoftRadius < _minimumSoftRadius;

        // 이 그림자가 넣는 층 수다. 한 층이 아니면 _softLayers층이다.
        int Layers() => SingleLayer ? 1 : _softLayers;

        // Graphic은 메시 효과를 컴포넌트 순서대로 부르므로, 이 컴포넌트보다 앞의 켜진 그림자들이 넣은 층 수를 더한다.
        int EarlierLayers()
        {
            GetComponents(_siblings);
            int layers = 0;
            for (int index = 0; index < _siblings.Count && !ReferenceEquals(_siblings[index], this); index++)
                if (_siblings[index].IsActive()) layers += _siblings[index].Layers();
            _siblings.Clear();
            return layers;
        }
    }
}
