#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 장면 전환이 풀 굽기를 부르므로 풀 코드 전체를 게임 어셈블리에 둔다.
namespace Game.App
{
    // 원본 TgsManager다. 굽기는 펌프 하나(BakePending)가 프레임마다 정해진 수만큼 시작하고, 덩어리마다 LOD 풀잎 수와 가까운 풀 충돌체를
    // 골라 카메라마다 그린다. 원본 Burst 작업과 같은 계산을 평범한 반복문으로 한다. 장면 진입은 검은 화면을 걷기 전에 남은 굽기를 모두 끝낸다.
    // 원작 차이: 원작은 카메라마다 정해진 수만큼 동기로 굽고, 화면이 보인 뒤에도 굽는다.
    public static class OriginalGrassRenderer
    {
        // 원본 TastyGrassShaderGlobalSettings 자산 값이다. 원본 렌더러 기능은 패스 없이 이 값을 정적 필드로 복사하는 일만 한다.
        const int _maxBakesPerFrame = 64;
        // GPU 결과를 기다리는 굽기의 한도다. 읽기 결과는 보통 1~2프레임 뒤에 오므로 세 프레임 몫으로 둔다.
        const int _maxBakesInFlight = _maxBakesPerFrame * 3;
        internal const float _densityScale = 1f;
        const float _lodFalloffExponent = 10f;
        const float _lodScale = 1f;
        const bool _alphaToCoverage = true;
        // 원본 LOD 식의 기준 거리다. 시야각과 LOD 배율이 1이면 이 거리 안의 덩어리는 풀잎을 모두 그린다.
        const float _lodBaseDistance = 32f;
        // 원본 MaxColliderPerInstance다. 풀 셰이더 TGS_MAX_SPHERE_COLLIDERS(_SphereCollider 배열 길이)와 같아야 한다.
        const int _maxColliderPerChunk = 8;
        const string _alphaClipKeyword = "TGS_USE_ALPHACLIP";

        static readonly int _useAlphaToCoverageId = Shader.PropertyToID("_Tgs_UseAlphaToCoverage");
        // 원본 TgsInstance.AllInstances다. 만든 순서가 굽는 순서와 그리는 순서다.
        static readonly List<OriginalGrassChunk> _chunks = new List<OriginalGrassChunk>();
        static readonly List<Vector4> _colliders = new List<Vector4>();
        static readonly Vector4[] _colliderBuffer = new Vector4[_maxColliderPerChunk];
        // 등록된 덩어리 중 굽기가 끝나지 않은 수다. 시작했지만 GPU 결과를 기다리는 덩어리도 센다.
        // 덩어리는 만든 직후에만 굽기 대기이고, 한 번 구우면 다시 대기로 돌아가지 않는다.
        static int _pendingBakes;
        // 아직 굽기를 시작하지 않은 수다. 모두 시작했으면 BakePending이 덩어리 목록을 훑지 않는다.
        static int _waitingBakes;
        // 굽기를 시작해 GPU 결과를 기다리는 덩어리다. BakePending이 끝난 것을 찾아 대기 수에서 뺀다.
        static readonly List<OriginalGrassChunk> _baking = new List<OriginalGrassChunk>();
        // 굽기 한도를 쓴 프레임과 그 프레임에 시작한 수다. 장면 전환과 그리기가 같은 프레임에 불러도 한도를 함께 쓴다.
        static int _budgetFrame = -1;
        static int _startedThisFrame;

        // 장면 전환이 검은 화면을 걷기 전에 이 값이 0이 될 때까지 BakePending을 부른다.
        internal static int PendingBakes => _pendingBakes;

        // 덩어리가 하나라도 있을 때만 그리기 콜백을 건다. 마지막 덩어리가 빠지면 콜백도 뗀다.
        // 굽기는 프레임마다 한 번 부르는 콜백에서, 그리기는 카메라마다 부르는 콜백에서 한다.
        internal static void Add(OriginalGrassChunk chunk)
        {
            if (_chunks.Count == 0)
            {
                RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
                RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            }
            _chunks.Add(chunk);
            _pendingBakes++;
            _waitingBakes++;
        }

        // 지형 층이 만든 순서의 반대로 놓으므로 끝에서부터 찾는다. 덩어리가 수천 개라 앞에서부터 찾으면 해제가 느리다.
        // 굽기가 끝나기 전에 빠지는 덩어리는 대기 수에서도 뺀다. 지형 층이 먼저 해제하므로 늦게 온 GPU 결과는 덩어리가 버린다.
        // 시작한 덩어리는 읽기 실패로 굽기 대기로 돌아와도 굽기 중 목록에만 있고 아직 굽지 않은 수에는 없다.
        internal static void Remove(OriginalGrassChunk chunk)
        {
            int index = _chunks.LastIndexOf(chunk);
            if (index < 0)
                return;
            _chunks.RemoveAt(index);
            if (chunk.IsGeometryDirty)
            {
                _pendingBakes--;
                int baking = _baking.LastIndexOf(chunk);
                if (baking >= 0)
                    _baking.RemoveAt(baking);
                else
                    _waitingBakes--;
            }
            if (_chunks.Count == 0)
            {
                RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                OriginalGrassBakeBatch.AbandonAll();
            }
        }

        // 하나뿐인 굽기 펌프다. GPU 결과까지 받은 덩어리를 대기 수에서 빼고, 아직 굽지 않은 덩어리를 만든 순서대로 굽기 시작한다.
        // 장면 전환과 그리기가 함께 부르며, 한 프레임에 시작하는 수와 결과를 기다리는 수는 둘이 함께 쓰는 한도 안에 둔다.
        // 한 번에 시작한 덩어리는 묶음 하나로 GPU 결과를 함께 읽고, 메인 스레드는 GPU를 기다리지 않는다.
        internal static void BakePending(int maxChunks)
        {
            for (int i = _baking.Count - 1; i >= 0; i--)
            {
                OriginalGrassChunk baking = _baking[i];
                // 읽기 실패로 굽기 대기로 돌아온 덩어리는 아래에서 다시 시작한다.
                if (baking.IsWaitingForBake)
                {
                    _baking.RemoveAt(i);
                    _waitingBakes++;
                    continue;
                }
                if (baking.IsGeometryDirty)
                    continue;
                _baking.RemoveAt(i);
                _pendingBakes--;
            }
            if (_pendingBakes == 0)
                OriginalGrassBakeBatch.ReleasePool();
            int frame = Time.frameCount;
            if (frame != _budgetFrame)
            {
                _budgetFrame = frame;
                _startedThisFrame = 0;
            }
            int budget = Mathf.Min(maxChunks, _maxBakesPerFrame - _startedThisFrame);
            OriginalGrassBakeBatch? batch = null;
            for (int i = 0; i < _chunks.Count && budget > 0 && _waitingBakes > 0 && _baking.Count < _maxBakesInFlight; i++)
            {
                OriginalGrassChunk chunk = _chunks[i];
                if (!chunk.IsWaitingForBake)
                    continue;
                if (chunk.HasBakeParameters)
                {
                    // 남은 한도가 이 묶음에 들어올 수 있는 덩어리 수의 상한이다.
                    if (batch == null)
                        batch = new OriginalGrassBakeBatch(Mathf.Min(budget, Mathf.Min(_waitingBakes, _maxBakesInFlight - _baking.Count)));
                    batch.Add(chunk);
                    _baking.Add(chunk);
                }
                else
                {
                    chunk.SkipBake();
                    _pendingBakes--;
                }
                _waitingBakes--;
                budget--;
                _startedThisFrame++;
            }
            if (batch != null)
                batch.Submit();
        }

        // 그리기 쪽 굽기는 프레임마다 한 번만 부른다. 원본은 카메라마다 굽지만, 한도를 프레임 단위로 함께 쓰면 카메라 수와 무관해진다.
        static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras) => BakePending(_maxBakesPerFrame);

        static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            GatherColliders();
            Vector3 cameraPosition = camera.transform.position;
            float fovScale = OriginalGrassChunk.ComputeCameraFovScalingFactor(camera);
            Material? preparedMaterial = null;
            for (int i = 0; i < _chunks.Count; i++)
            {
                OriginalGrassChunk chunk = _chunks[i];
                // 꺼진 하위 씬의 덩어리는 원본에서 목록에 없으므로 LOD와 충돌체 계산 전에 건너뛴다.
                if (!chunk.Shown)
                    continue;
                Bounds aabb = chunk.TightBounds;
                Vector3 min = aabb.min;
                Vector3 max = aabb.max;
                int bladeCount = ComputeLodBladeCount(cameraPosition, min, max, fovScale, chunk);
                if (bladeCount <= 0)
                    continue;

                // 숨김 검사는 원본 순서대로 충돌체를 고른 뒤에 한다.
                int colliderCount = SelectChunkColliders(min, max);
                if (chunk.Hide)
                    continue;
                // 원본은 카메라마다 재질에 알파 설정을 한 번 넣는다. 덩어리마다 지형의 재질을 쓰므로 재질이 달라질 때만 넣는다.
                Material material = chunk.Material;
                if (material != preparedMaterial)
                {
                    PrepareMaterial(material);
                    preparedMaterial = material;
                }
                chunk.Draw(camera, bladeCount * 3, _colliderBuffer, colliderCount);
            }
        }

        // 원본 LOD 식이다. 카메라에서 경계까지 거리와 시야각으로 비율을 구하고, 그 비율만큼 앞쪽 풀잎만 그린다.
        static int ComputeLodBladeCount(Vector3 cameraPosition, Vector3 min, Vector3 max, float fovScale, OriginalGrassChunk chunk)
        {
            float distance = Mathf.Sqrt(DistanceToAabbSqr(cameraPosition, min, max));
            float ratio = Mathf.Clamp01(Mathf.Pow(_lodBaseDistance / distance / fovScale * _lodScale * chunk.LodBias, _lodFalloffExponent));
            return (int)Mathf.Round(ratio * chunk.BladeCount);
        }

        // 원본처럼 덩어리 경계까지 제곱 거리가 충돌체의 w보다 작은 구를 앞에서부터 _maxColliderPerChunk개까지 고르고,
        // 반지름은 w의 제곱근으로 넘긴다. 고른 구는 _colliderBuffer 앞쪽에 채우고 개수를 돌려준다.
        static int SelectChunkColliders(Vector3 min, Vector3 max)
        {
            int colliderCount = 0;
            for (int c = 0; c < _colliders.Count && colliderCount < _maxColliderPerChunk; c++)
            {
                Vector4 collider = _colliders[c];
                var center = new Vector3(collider.x, collider.y, collider.z);
                if (DistanceToAabbSqr(center, min, max) < collider.w)
                {
                    _colliderBuffer[colliderCount] = new Vector4(center.x, center.y, center.z, Mathf.Sqrt(collider.w));
                    colliderCount++;
                }
            }
            return colliderCount;
        }

        // 원본처럼 w에 반지름 제곱과 충돌체 자기 localScale 길이의 곱을 넣는다.
        static void GatherColliders()
        {
            _colliders.Clear();
            List<OriginalGrassCollider> active = OriginalGrassCollider._activeColliders;
            for (int i = 0; i < active.Count; i++)
            {
                OriginalGrassCollider collider = active[i];
                Transform colliderTransform = collider.transform;
                Vector3 position = colliderTransform.position;
                float radius = collider.Radius;
                _colliders.Add(new Vector4(position.x, position.y, position.z, radius * radius * colliderTransform.localScale.magnitude));
            }
        }

        // 원본 전역 설정대로 알파 투 커버리지를 켠다. 그러면 TGS_USE_ALPHACLIP 변형을 끄고 깊이 패스가 AlphaToMask를 쓴다.
        // 지형에 연결한 풀 재질에는 늘 셰이더가 있다.
        static void PrepareMaterial(Material material)
        {
            Shader shader = material.shader;
            var keyword = new LocalKeyword(shader, _alphaClipKeyword);
            if (keyword.isValid)
                material.SetKeyword(keyword, !_alphaToCoverage);
            material.SetInteger(_useAlphaToCoverageId, _alphaToCoverage ? 1 : 0);
        }

        static float DistanceToAabbSqr(Vector3 position, Vector3 min, Vector3 max)
        {
            Vector3 closest = Vector3.Max(min, Vector3.Min(max, position));
            return (position - closest).sqrMagnitude;
        }
    }
}
