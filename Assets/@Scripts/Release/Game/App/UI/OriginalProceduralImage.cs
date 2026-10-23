#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 절차형 이미지의 모서리 반경, 테두리, 폴오프를 스프라이트와 함께 정점 데이터로 셰이더에 넘긴다.
    // Image를 상속하므로 Image 참조와 기본 uGUI 마스크가 그대로 동작한다.
    [AddComponentMenu("UI/Original Procedural Image")]
    public sealed class OriginalProceduralImage : UnityEngine.UI.Image
    {
        // 반경은 원본 순서대로 좌상, 우상, 우하, 좌하다. _round는 크기에 맞춰 원형으로 그리는 모드다.
        [SerializeField] Vector4 _radii;
        [SerializeField] bool _round;
        [SerializeField] float _borderWidth;
        [SerializeField] float _falloffDistance = 1f;

        // 원본 값과 재질은 Editor의 ComponentAuthoring.Configure가 직렬화 필드에 쓰고, 런타임은 저장된 값만 읽는다.

        // 원본 FixRadius처럼 인접 반경 합이 사각형 변보다 커지지 않게 같은 비율로 축소한다.
        // 크기가 0 이하인 사각형은 OnPopulateMesh가 메시를 비우고 먼저 돌아가므로 여기까지 오지 않는다.
        internal Vector4 GetEffectiveRadii(Rect rect)
        {
            if (_round) return Vector4.one * (Mathf.Min(rect.width, rect.height) * .5f);
            Vector4 result = Vector4.Max(_radii, Vector4.zero);
            float factor = 1f;
            factor = LimitRadius(factor, rect.width, result.x + result.y);
            factor = LimitRadius(factor, rect.width, result.z + result.w);
            factor = LimitRadius(factor, rect.height, result.x + result.w);
            factor = LimitRadius(factor, rect.height, result.y + result.z);
            return result * factor;
        }

        // 영 반경은 그대로 두어 0으로 나누는 계산을 피한다.
        static float LimitRadius(float factor, float length, float sum) =>
            sum > 0f ? Mathf.Min(factor, length / sum) : factor;

        // 스프라이트 UV와 Image 채우기 메시를 보존하고 원본 폴오프만큼 외곽을 확장한다.
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            base.OnPopulateMesh(mesh);
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f)
            {
                mesh.Clear();
                return;
            }
            Vector4 radii = GetEffectiveRadii(rect);
            Vector2 halfSize = rect.size * .5f;
            var vertex = new UIVertex();
            // 모든 정점에 같은 값이므로 중심과 uv3를 반복 앞에서 한 번만 구한다.
            Vector2 center = rect.center;
            Vector4 uv3 = new Vector4(_borderWidth, _falloffDistance, 0f, 0f);
            for (int index = 0; index < mesh.currentVertCount; index++)
            {
                mesh.PopulateUIVertex(ref vertex, index);
                Vector2 local = (Vector2)vertex.position - center;
                local += new Vector2(local.x / rect.width, local.y / rect.height) * _falloffDistance;
                vertex.position = new Vector3(center.x + local.x, center.y + local.y, vertex.position.z);
                vertex.uv1 = new Vector4(local.x, local.y, halfSize.x, halfSize.y);
                vertex.uv2 = radii;
                vertex.uv3 = uv3;
                mesh.SetUIVertex(vertex, index);
            }
        }

        // 활성화, 부모 변경, Canvas 계층 변경 때마다 지금 Canvas의 UV1~3 채널을 다시 켠다.
        // 복제되거나 Canvas에 늦게 붙어도 채널이 켜져 있게 한다.
        protected override void OnEnable()
        {
            base.OnEnable();
            EnableShaderChannels();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            EnableShaderChannels();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            EnableShaderChannels();
        }

        // 형상 데이터는 공유 재질 값이 아니라 정점 UV1~3으로 전달하므로 Canvas가 해당 채널을 넘기게 한다.
        void EnableShaderChannels()
        {
            if (canvas != null)
                canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 |
                    AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
        }
    }
}
