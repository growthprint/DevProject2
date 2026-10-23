#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 UI_Spline_Renderer.UISplineRenderer의 특성 트리 연결선 대응이다. 원작 PerkTreeView.AddConnection의 매듭 4개 곡선을
    // 3차 베지어 세 구간으로 나눠 폭 있는 띠로 그린다. 제품에 Unity Splines 패키지가 없어 곡선 계산을 여기서 한다.
    // 원작의 색 그라디언트, 폭 곡선, 시작·끝 그림, 해상도 규칙, 전용 셰이더는 옮기지 않는다. 색은 Graphic 색이고 질감은 원작 m_Texture다(원작 차이).
    // 연결 좌표는 이 그래픽의 로컬 공간이다. 트리 View가 노드 위치를 이 공간으로 바꿔 넘긴다.
    public sealed class OriginalPerkLineGraphic : MaskableGraphic
    {
        // 원작 _width다(특성 트리 두 선 모두 10).
        [SerializeField] float _width = 10f;
        // 베지어 한 구간을 나누는 수다. 원작 해상도 규칙 대신 쓰는 제품 값이다.
        [SerializeField] int _samplesPerCurve = 16;
        // 원작 m_Texture다. 비어 있으면 흰 질감으로 그린다.
        [SerializeField] Texture? _texture;

        // 연결마다 매듭 4개를 차례로 담는다. 접선은 연결마다 하나다(원작은 모든 매듭에 같은 접선을 준다).
        readonly List<Vector3> _knots = new List<Vector3>();
        readonly List<Vector3> _tangents = new List<Vector3>();
        // 메시를 만들 때마다 할당하지 않도록 재사용한다. 메시 갱신은 메인 스레드에서만 일어난다.
        readonly List<Vector3> _points = new List<Vector3>();
        readonly Vector3[] _connection = new Vector3[_knotsPerConnection];

        const int _knotsPerConnection = 4;

        public override Texture mainTexture => _texture != null ? _texture : s_WhiteTexture;

        // 원작 ClearSplines다. 다음 메시 갱신에서 빈 메시가 된다.
        internal void ClearConnections()
        {
            _knots.Clear();
            _tangents.Clear();
            SetVerticesDirty();
        }

        // 원작 AddConnection이다. from은 부모 노드, to는 자식 노드 위치(이 그래픽 로컬)다.
        internal void AddConnection(Vector3 from, Vector3 to, bool horizontal, float tangent)
        {
            Vector3 tangentVector = ConnectionKnots(from, to, horizontal, tangent, _connection);
            for (int index = 0; index < _knotsPerConnection; index++) _knots.Add(_connection[index]);
            _tangents.Add(tangentVector);
            SetVerticesDirty();
        }

        // 원작 PerkTreeView.RefreshConnections의 매듭 계산이다. 접선 T는 가로 트리면 왼쪽, 세로 트리면 위쪽으로 splineTangent 길이다.
        // 매듭: from, from - T, (가로: (from.x, to.y), 세로: (to.x, from.y)) - 2T, to. 모든 매듭의 들어오는 접선은 T, 나가는 접선은 -T다.
        // 돌려준 값은 T다. 테스트가 원작 값과 비교한다.
        internal static Vector3 ConnectionKnots(Vector3 from, Vector3 to, bool horizontal, float tangent, Vector3[] knots)
        {
            Vector3 tangentVector = (horizontal ? Vector3.left : Vector3.up) * tangent;
            knots[0] = from;
            knots[1] = from - tangentVector;
            knots[2] = (horizontal ? new Vector3(from.x, to.y) : new Vector3(to.x, from.y)) - 2f * tangentVector;
            knots[3] = to;
            return tangentVector;
        }

        // Unity Splines의 BezierCurve(a, b)와 같은 구간 점이다. P1 = a + 나가는 접선(-T), P2 = b + 들어오는 접선(T)이다.
        internal static Vector3 Evaluate(Vector3 start, Vector3 end, Vector3 tangentVector, float t)
        {
            Vector3 p1 = start - tangentVector;
            Vector3 p2 = end + tangentVector;
            float u = 1f - t;
            return u * u * u * start + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * end;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (_width <= 0f) return;
            int samples = Mathf.Max(1, _samplesPerCurve);
            for (int connection = 0; connection < _tangents.Count; connection++)
            {
                _points.Clear();
                int first = connection * _knotsPerConnection;
                _points.Add(_knots[first]);
                for (int curve = 0; curve < _knotsPerConnection - 1; curve++)
                    for (int step = 1; step <= samples; step++)
                        _points.Add(Evaluate(_knots[first + curve], _knots[first + curve + 1], _tangents[connection], step / (float)samples));
                AddStrip(mesh);
            }
        }

        // 점 목록을 폭 있는 띠로 만든다. 점마다 앞뒤 점 방향의 법선으로 양쪽 정점을 둔다. 길이가 0인 구간은 앞 방향을 그대로 쓴다.
        // u는 지난 길이 비율, v는 띠의 양쪽(0, 1)이다.
        void AddStrip(VertexHelper mesh)
        {
            float total = 0f;
            for (int index = 1; index < _points.Count; index++) total += Vector3.Distance(_points[index - 1], _points[index]);
            if (total <= 0f) return;
            int start = mesh.currentVertCount;
            float half = _width * .5f;
            float travelled = 0f;
            Vector3 direction = Vector3.up;
            for (int index = 0; index < _points.Count; index++)
            {
                if (index > 0) travelled += Vector3.Distance(_points[index - 1], _points[index]);
                Vector3 span = _points[Mathf.Min(index + 1, _points.Count - 1)] - _points[Mathf.Max(index - 1, 0)];
                span.z = 0f;
                if (span.sqrMagnitude > 0f) direction = span.normalized;
                Vector3 normal = new Vector3(-direction.y, direction.x, 0f) * half;
                float u = travelled / total;
                mesh.AddVert(_points[index] - normal, color, new Vector4(u, 0f));
                mesh.AddVert(_points[index] + normal, color, new Vector4(u, 1f));
                if (index == 0) continue;
                int vertex = start + index * 2;
                mesh.AddTriangle(vertex - 2, vertex - 1, vertex + 1);
                mesh.AddTriangle(vertex - 2, vertex + 1, vertex);
            }
        }
    }
}
