#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering
{
    // 원본 HBAORendererFeature(URP 14 CommandBuffer)의 Normal 모드를 URP 17 Render Graph로 재현한다.
    // 순서는 AO, 양방향 블러, 시간 필터, 합성이며 투명 직전에 그린다. 원본의 카메라 색 복사는 곱 혼합 합성으로 결과가 같아 생략한다.
    // OriginalHbao 볼륨과 한 어셈블리에 둔다.
    public sealed class OriginalHbaoRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] Shader? _shader;
        Material? _material;
        HbaoPass? _pass;

        public override void Create()
        {
            ReleaseResources();
            if (_shader == null) return;
            _material = CoreUtils.CreateEngineMaterial(_shader);
            _pass = new HbaoPass(_material);
        }

        // 원본처럼 후처리가 켜진 카메라에서, 혼합된 볼륨이 활성(IsActive)일 때만 넣는다.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 원본 FrameCleanup은 HBAO가 꺼진 프레임에도 돈다. 여기서는 꺼지면 패스를 넣지 않으므로 활성 검사 전에 매 카메라 해제한다.
            // 그러지 않으면 시간 필터를 쓴 메인 메뉴 뒤 AO를 끈 씬에서 카메라별 전체 해상도 기록이 렌더러 해제 때까지 남는다.
            // 셰이더는 렌더러 자산에 저작되고 RuntimeAuthoringContractTests가 연결을 확인하므로 패스가 없는 경우를 검사하지 않는다.
            _pass!.ReleaseStaleHistories();
            if (!renderingData.cameraData.postProcessEnabled) return;
            OriginalHbao? hbao = VolumeManager.instance.stack.GetComponent<OriginalHbao>();
            if (hbao == null || !hbao.IsActive()) return;
            _pass.Setup(hbao, renderingData.cameraData.cameraType == CameraType.SceneView);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing) => ReleaseResources();

        void ReleaseResources()
        {
            _pass?.Dispose();
            _pass = null;
            CoreUtils.Destroy(_material);
            _material = null;
        }

        sealed class HbaoPass : ScriptableRenderPass, IDisposable
        {
            // DuckovHbao.shader 패스 순서다.
            const int _aoPass = 0;
            const int _blurPass = 1;
            const int _temporalPass = 2;
            const int _compositePass = 3;
            // 원본 HBAORenderPass의 시간 필터 노이즈 회전과 오프셋 수열이다.
            static readonly float[] _temporalRotations = { 60f, 300f, 180f, 240f, 120f, 0f };
            static readonly float[] _temporalOffsets = { 0f, .5f, .25f, .75f };
            // 원본 UpdateMaterialProperties의 최대 반경 픽셀 식이 쓰는 기준 화면 넓이(1080p)와 최솟값이다.
            const float _referencePixelCount = 1920f * 1080f;
            const float _minRadiusPixels = 16f;
            static readonly int _mainTexId = Shader.PropertyToID("_MainTex");
            static readonly int _hbaoTexId = Shader.PropertyToID("_HBAOTex");
            static readonly int _noiseTexId = Shader.PropertyToID("_NoiseTex");
            static readonly int _motionVectorTextureId = Shader.PropertyToID("_MotionVectorTexture");
            static readonly int _cameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");
            static readonly int _cameraNormalsTextureId = Shader.PropertyToID("_CameraNormalsTexture");
            static readonly int _inputTexelSizeId = Shader.PropertyToID("_Input_TexelSize");
            static readonly int _aoTexelSizeId = Shader.PropertyToID("_AO_TexelSize");
            static readonly int _targetScaleId = Shader.PropertyToID("_TargetScale");
            static readonly int _uvToViewId = Shader.PropertyToID("_UVToView");
            static readonly int _radiusId = Shader.PropertyToID("_Radius");
            static readonly int _maxRadiusPixelsId = Shader.PropertyToID("_MaxRadiusPixels");
            static readonly int _negInvRadius2Id = Shader.PropertyToID("_NegInvRadius2");
            static readonly int _angleBiasId = Shader.PropertyToID("_AngleBias");
            static readonly int _aoMultiplierId = Shader.PropertyToID("_AOmultiplier");
            static readonly int _intensityId = Shader.PropertyToID("_Intensity");
            static readonly int _offscreenSamplesContribId = Shader.PropertyToID("_OffscreenSamplesContrib");
            static readonly int _maxDistanceId = Shader.PropertyToID("_MaxDistance");
            static readonly int _distanceFalloffId = Shader.PropertyToID("_DistanceFalloff");
            static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
            static readonly int _blurSharpnessId = Shader.PropertyToID("_BlurSharpness");
            static readonly int _blurDeltaUVId = Shader.PropertyToID("_BlurDeltaUV");
            static readonly int _temporalParamsId = Shader.PropertyToID("_TemporalParams");
            static readonly int _historyScaleId = Shader.PropertyToID("_HistoryBuffer_RTHandleScale");
            static readonly int _worldToViewId = Shader.PropertyToID("_HbaoWorldToView");

            readonly Material _material;
            readonly MaterialPropertyBlock _properties = new();
            readonly LocalKeyword[] _qualityKeywords;
            readonly LocalKeyword[] _offscreenKeywords;
            readonly LocalKeyword[] _blurKeywords;
            readonly LocalKeyword[] _varianceKeywords;
            // 시간 필터 기록은 카메라별 수명 관리가 따로 있어 원본 CameraHistoryBuffers 역할을 하는 객체에 맡긴다.
            readonly HbaoHistoryBuffers _historyBuffers = new();
            Texture2D? _noise;
            OriginalHbao? _hbao;
            bool _sceneView;

            internal HbaoPass(Material material)
            {
                _material = material;
                Shader shader = material.shader;
                _qualityKeywords = Keywords(shader, "QUALITY_LOWEST", "QUALITY_LOW", "QUALITY_MEDIUM", "QUALITY_HIGH", "QUALITY_HIGHEST");
                _offscreenKeywords = Keywords(shader, "OFFSCREEN_SAMPLES_CONTRIBUTION");
                _blurKeywords = Keywords(shader, "BLUR_RADIUS_2", "BLUR_RADIUS_3", "BLUR_RADIUS_4", "BLUR_RADIUS_5");
                _varianceKeywords = Keywords(shader, "VARIANCE_CLIPPING_4TAP");
                profilingSampler = new ProfilingSampler("HBAO");
                requiresIntermediateTexture = true;
            }

            static LocalKeyword[] Keywords(Shader shader, params string[] names)
            {
                var keywords = new LocalKeyword[names.Length];
                for (int index = 0; index < names.Length; index++) keywords[index] = new LocalKeyword(shader, names[index]);
                return keywords;
            }

            // 원본 Setup처럼 디버그가 꺼진 Normal 모드는 투명 직전에 그린다. 깊이와 카메라 노멀을 요청하고, 시간 필터를 쓰면 모션 벡터도 요청한다.
            internal void Setup(OriginalHbao hbao, bool sceneView)
            {
                _hbao = hbao;
                _sceneView = sceneView;
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                ScriptableRenderPassInput input = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
                if (UsesTemporalFilter(hbao, sceneView)) input |= ScriptableRenderPassInput.Motion;
                ConfigureInput(input);
            }

            // 원본은 씬 뷰에서 시간 필터를 쓰지 않는다.
            static bool UsesTemporalFilter(OriginalHbao hbao, bool sceneView) => hbao._temporalFilterEnabled.value && !sceneView;

            // 패스가 만든 GPU 자원이므로 렌더러 기능이 패스를 버릴 때 카메라별 기록부터 함께 놓는다.
            public void Dispose()
            {
                _historyBuffers.Dispose();
                CoreUtils.Destroy(_noise);
                _noise = null;
            }

            // 키워드 묶음에서 _keyword 번째만 켜고, -1이면 모두 끈다. 화면 밖 기여 묶음(_extraKeywords)은 AO 패스만 쓴다.
            // 텍스처 칸은 셰이더 이름별로 두고, 각 단계는 자기가 읽는 칸만 채운다. 빈 칸은 그리기 때 넘기지 않는다.
            sealed class PassData
            {
                public Material? _material;
                public MaterialPropertyBlock? _properties;
                public LocalKeyword[]? _keywords;
                public int _keyword;
                public LocalKeyword[]? _extraKeywords;
                public int _extraKeyword;
                public int _pass;
                public Vector2 _blurDelta;
                public TextureHandle _depth;
                public TextureHandle _normals;
                public TextureHandle _motion;
                public TextureHandle _mainTex;
                public TextureHandle _hbaoTex;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                OriginalHbao hbao = _hbao;
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer) return;
                TextureHandle depth = resources.cameraDepthTexture;
                TextureHandle normals = resources.cameraNormalsTexture;
                if (!depth.IsValid() || !normals.IsValid()) return;
                // 노이즈 텍스처는 처음 그릴 때 한 번 만들고 패스를 버릴 때까지 다시 쓴다.
                if (_noise == null) _noise = HbaoNoise.Create();

                // 원본 FetchRenderParameters처럼 반 해상도는 홀수 크기를 올림해 절반으로 만든다. 디인터리브는 구현하지 않는다.
                RenderTextureDescriptor target = cameraData.cameraTargetDescriptor;
                int width = target.width;
                int height = target.height;
                bool half = hbao._resolution.value == OriginalHbao.Resolution.Half;
                int aoWidth = half ? (width + width % 2) / 2 : width;
                int aoHeight = half ? (height + height % 2) / 2 : height;

                // 모션 벡터가 있어야 지난 프레임 위치를 찾으므로, 시간 필터를 쓰고 모션 벡터가 있을 때만 기록을 받는다.
                HbaoHistoryBuffers.History? history = null;
                if (UsesTemporalFilter(hbao, _sceneView) && resources.motionVectorColor.IsValid())
                    history = _historyBuffers.Acquire(cameraData.camera, aoWidth, aoHeight);
                // 시간 필터 노이즈 값은 이번 프레임 기록 수로 정하므로, 시간 필터 단계가 기록 수를 올리기 전에 넣는다.
                SetCameraProperties(hbao, cameraData, width, height, aoWidth, aoHeight, history);

                // 원본은 AO 대상을 (0,0,0,1)로 지운 뒤 그린다. 최대 거리 밖 픽셀은 지운 값(차폐 없음)으로 남는다.
                var desc = new TextureDesc(aoWidth, aoHeight)
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    clearBuffer = true,
                    clearColor = new Color(0f, 0f, 0f, 1f),
                    name = "HBAO",
                };
                // 원본 HBAORenderPass처럼 단계마다 기록 메서드를 두고, 각 단계의 결과를 다음 단계의 AO 입력으로 넘긴다.
                TextureHandle ao = RecordAo(graph, hbao, desc, depth, normals);
                if (hbao._blurType.value != OriginalHbao.BlurType.None) ao = RecordBlur(graph, hbao, desc, ao);
                if (history != null) ao = RecordTemporalFilter(graph, hbao, history, ao, resources.motionVectorColor);
                RecordComposite(graph, ao, resources.activeColorTexture);
            }

            // 원본 AO 단계다. 깊이와 카메라 노멀로 차폐를 계산해 지운 AO 대상에 쓴다.
            TextureHandle RecordAo(RenderGraph graph, OriginalHbao hbao, TextureDesc desc, TextureHandle depth, TextureHandle normals)
            {
                TextureHandle ao = graph.CreateTexture(desc);
                int quality = Mathf.Clamp((int)hbao._quality.value, 0, _qualityKeywords.Length - 1);
                using IRasterRenderGraphBuilder builder = BeginPass(graph, "HBAO AO", _aoPass, ao, AccessFlags.Write, _qualityKeywords, quality,
                    out PassData data);
                data._extraKeywords = _offscreenKeywords;
                data._extraKeyword = hbao._offscreenSamplesContribution.value > 0f ? 0 : -1;
                data._depth = depth;
                data._normals = normals;
                builder.UseTexture(depth);
                builder.UseTexture(normals);
                return ao;
            }

            // 원본 Blur처럼 가로, 세로 순으로 양방향 블러를 건다. BlurType.None이면 부르지 않는다.
            TextureHandle RecordBlur(RenderGraph graph, OriginalHbao hbao, TextureDesc desc, TextureHandle ao)
            {
                desc.clearBuffer = false;
                desc.name = "HBAO Blur Horizontal";
                TextureHandle horizontal = graph.CreateTexture(desc);
                desc.name = "HBAO Blur Vertical";
                TextureHandle vertical = graph.CreateTexture(desc);
                int radius = Mathf.Clamp((int)hbao._blurType.value - 1, 0, _blurKeywords.Length - 1);
                RecordBlurAxis(graph, "HBAO Blur Horizontal", radius, new Vector2(1f / desc.width, 0f), source: ao, destination: horizontal);
                RecordBlurAxis(graph, "HBAO Blur Vertical", radius, new Vector2(0f, 1f / desc.height), source: horizontal, destination: vertical);
                return vertical;
            }

            // 블러 방향은 두 블러 패스가 서로 다른 유일한 값이라 재질이 아니라 패스별 속성 블록으로 넘긴다.
            void RecordBlurAxis(RenderGraph graph, string name, int radius, Vector2 blurDelta, TextureHandle source, TextureHandle destination)
            {
                using IRasterRenderGraphBuilder builder = BeginPass(graph, name, _blurPass, destination, AccessFlags.Write, _blurKeywords, radius,
                    out PassData data);
                data._blurDelta = blurDelta;
                data._mainTex = source;
                builder.UseTexture(source);
            }

            // 원본 TemporalFilter처럼 지난 프레임 기록과 이번 결과를 섞어 이번 기록에 쓰고, 합성은 이번 기록을 읽는다.
            // 범위 안 원본 볼륨은 8탭 분산 클리핑을 쓰지 않으므로 8탭도 4탭으로 처리한다.
            TextureHandle RecordTemporalFilter(RenderGraph graph, OriginalHbao hbao, HbaoHistoryBuffers.History history, TextureHandle ao,
                TextureHandle motion)
            {
                var previousParams = new ImportResourceParams { clearOnFirstUse = history._frameCount == 0, clearColor = Color.white, discardOnLastUse = false };
                TextureHandle previous = graph.ImportTexture(history._previous!, previousParams);
                TextureHandle current = graph.ImportTexture(history._current!);
                int variance = hbao._varianceClipping.value == OriginalHbao.VarianceClipping.Disabled ? -1 : 0;
                using IRasterRenderGraphBuilder builder = BeginPass(graph, "HBAO Temporal Filter", _temporalPass, current, AccessFlags.Write,
                    _varianceKeywords, variance, out PassData data);
                data._mainTex = previous;
                data._hbaoTex = ao;
                data._motion = motion;
                builder.UseTexture(previous);
                builder.UseTexture(ao);
                builder.UseTexture(motion);
                history._frameCount++;
                history._lastRenderedFrame = Time.frameCount;
                return current;
            }

            // 원본 Composite 단계를 카메라 색 위에 곱 혼합으로 그린다. 혼합에 지금 색이 필요해 읽기와 쓰기로 붙인다.
            void RecordComposite(RenderGraph graph, TextureHandle ao, TextureHandle color)
            {
                using IRasterRenderGraphBuilder builder = BeginPass(graph, "HBAO Composite", _compositePass, color, AccessFlags.ReadWrite, null, -1,
                    out PassData data);
                data._hbaoTex = ao;
                builder.UseTexture(ao);
            }

            // 대상 하나에 전체 화면을 그리는 패스를 연다. 렌더 그래프가 PassData를 풀에서 재사용하므로 단계별 칸을 모두 비운다.
            // 호출한 쪽은 자기 단계가 읽는 텍스처만 이름 있는 칸에 넣고 입력으로 선언한 뒤 빌더를 닫는다.
            IRasterRenderGraphBuilder BeginPass(RenderGraph graph, string name, int pass, TextureHandle destination, AccessFlags access,
                LocalKeyword[]? keywords, int keyword, out PassData data)
            {
                IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass<PassData>(name, out data, profilingSampler);
                data._material = _material;
                data._properties = _properties;
                data._pass = pass;
                data._keywords = keywords;
                data._keyword = keyword;
                data._extraKeywords = null;
                data._extraKeyword = -1;
                data._blurDelta = Vector2.zero;
                data._depth = TextureHandle.nullHandle;
                data._normals = TextureHandle.nullHandle;
                data._motion = TextureHandle.nullHandle;
                data._mainTex = TextureHandle.nullHandle;
                data._hbaoTex = TextureHandle.nullHandle;
                builder.SetRenderAttachment(destination, 0, access);
                // 공유 재질의 로컬 키워드를 명령 버퍼로 바꾸므로, 키워드를 쓰는 패스만 전역 상태 변경을 허용한다.
                if (keywords != null) builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData passData, RasterGraphContext context) => Draw(passData, context));
                return builder;
            }

            static void SetKeywords(RasterCommandBuffer cmd, Material material, LocalKeyword[]? keywords, int enabled)
            {
                if (keywords == null) return;
                for (int index = 0; index < keywords.Length; index++) cmd.SetKeyword(material, keywords[index], index == enabled);
            }

            // 카메라마다 같은 값은 SetCameraProperties가 재질에 넣었으므로, 여기서는 이 패스가 읽는 텍스처와 블러 방향만 넘긴다.
            // 속성 블록은 그리기 명령에 넣을 때 복사되므로 패스마다 비우고 다시 채워도 앞 패스 값이 바뀌지 않는다.
            static void Draw(PassData pass, RasterGraphContext context)
            {
                Material material = pass._material!;
                MaterialPropertyBlock properties = pass._properties!;
                SetKeywords(context.cmd, material, pass._keywords, pass._keyword);
                SetKeywords(context.cmd, material, pass._extraKeywords, pass._extraKeyword);

                properties.Clear();
                if (pass._pass == _blurPass) properties.SetVector(_blurDeltaUVId, pass._blurDelta);
                SetTexture(properties, _cameraDepthTextureId, pass._depth);
                SetTexture(properties, _cameraNormalsTextureId, pass._normals);
                SetTexture(properties, _mainTexId, pass._mainTex);
                SetTexture(properties, _hbaoTexId, pass._hbaoTex);
                SetTexture(properties, _motionVectorTextureId, pass._motion);
                context.cmd.DrawProcedural(Matrix4x4.identity, material, pass._pass, MeshTopology.Triangles, 3, 1, properties);
            }

            static void SetTexture(MaterialPropertyBlock properties, int id, TextureHandle texture)
            {
                if (texture.IsValid()) properties.SetTexture(id, (RTHandle)texture);
            }

            // 원본 UpdateMaterialProperties처럼 카메라마다 한 번 공통 값을 재질에 넣는다. URP는 카메라 하나의 기록, 실행, 제출을
            // 끝낸 뒤 다음 카메라를 기록하므로 여러 카메라가 이 재질을 함께 써도 값이 섞이지 않는다. 시점은 하나이고 동적 해상도는 쓰지 않는다.
            void SetCameraProperties(OriginalHbao hbao, UniversalCameraData cameraData, int width, int height, int aoWidth, int aoHeight,
                HbaoHistoryBuffers.History? history)
            {
                Material material = _material;
                Matrix4x4 projection = cameraData.GetProjectionMatrix(0);
                float radius = hbao._radius.value;
                float bias = hbao._bias.value;
                material.SetTexture(_noiseTexId, _noise);
                material.SetVector(_inputTexelSizeId, new Vector4(1f / width, 1f / height, width, height));
                material.SetVector(_aoTexelSizeId, new Vector4(1f / aoWidth, 1f / aoHeight, aoWidth, aoHeight));
                material.SetVector(_targetScaleId, hbao._resolution.value == OriginalHbao.Resolution.Half
                    ? new Vector4((width + .5f) / width, (height + .5f) / height, 1f, 1f)
                    : Vector4.one);
                material.SetVector(_uvToViewId, new Vector4(2f / projection.m00, -2f / projection.m11, -1f / projection.m00, 1f / projection.m11));
                material.SetFloat(_radiusId, radius * .5f * (height / (2f / projection.m11)));
                // 화면 넓이가 1080p와 달라도 같은 화면 비율의 반경이 되도록 넓이 비의 제곱근을 곱한다.
                material.SetFloat(_maxRadiusPixelsId,
                    Mathf.Max(_minRadiusPixels, hbao._maxRadiusPixels.value * Mathf.Sqrt(width * height / _referencePixelCount)));
                material.SetFloat(_negInvRadius2Id, -1f / (radius * radius));
                material.SetFloat(_angleBiasId, bias);
                material.SetFloat(_aoMultiplierId, 2f * (1f / (1f - bias)));
                // 프로젝트는 선형 색 공간이므로 원본의 감마 보정(×0.4545)은 적용되지 않는다.
                material.SetFloat(_intensityId, hbao._intensity.value);
                material.SetFloat(_offscreenSamplesContribId, hbao._offscreenSamplesContribution.value);
                material.SetFloat(_maxDistanceId, hbao._maxDistance.value);
                material.SetFloat(_distanceFalloffId, hbao._distanceFalloff.value);
                material.SetVector(_baseColorId, hbao._baseColor.value);
                material.SetFloat(_blurSharpnessId, hbao._sharpness.value);
                // 시간 필터 기록은 AO 크기 그대로 할당하므로 RTHandle 배율은 1이다.
                material.SetVector(_historyScaleId, Vector4.one);
                material.SetMatrix(_worldToViewId, cameraData.GetViewMatrix(0));
                // 기록 수로 회전과 오프셋 수열을 각자 길이만큼 돌아가며 써서 프레임마다 노이즈를 바꾼다.
                material.SetVector(_temporalParamsId, history != null
                    ? new Vector2(_temporalRotations[history._frameCount % _temporalRotations.Length] / 360f,
                        _temporalOffsets[history._frameCount % _temporalOffsets.Length])
                    : Vector2.zero);
            }

            // 렌더러 기능은 패스만 쥐므로 매 카메라의 기록 정리를 패스를 거쳐 기록 객체에 넘긴다.
            internal void ReleaseStaleHistories() => _historyBuffers.ReleaseStale();
        }

        // 원본 CameraHistoryBuffers처럼 시간 필터 기록을 카메라별로 두고, 한 프레임 넘게 그리지 않은 카메라는 해제한다.
        sealed class HbaoHistoryBuffers : IDisposable
        {
            // 카메라 키는 참조로만 비교한다. Unity Equals는 파괴된 카메라 키를 엔진에 묻고, 씬을 읽는 동안 그 질문이 로딩 잠금을 기다린다.
            readonly Dictionary<Camera, History> _histories = new(CameraReferenceComparer._instance);
            // 열거 중인 사전에서 바로 지울 수 없어 해제할 카메라를 모아 두는 재사용 목록이다. 쓰고 나면 늘 비운다.
            readonly List<Camera> _staleCameras = new();

            internal sealed class History
            {
                public RTHandle? _previous;
                public RTHandle? _current;
                public int _frameCount;
                public int _lastRenderedFrame;

                public void Release()
                {
                    _previous?.Release();
                    _current?.Release();
                    _previous = null;
                    _current = null;
                }
            }

            internal History Acquire(Camera camera, int width, int height)
            {
                if (!_histories.TryGetValue(camera, out History history))
                {
                    history = new History();
                    _histories.Add(camera, history);
                }
                // 기록은 고정 크기로 할당해 RTHandle.referenceSize가 할당한 너비와 높이 그대로이므로 그 값과 비교한다.
                if (history._current == null || history._current.referenceSize != new Vector2Int(width, height))
                {
                    history.Release();
                    history._previous = Allocate(width, height, 0);
                    history._current = Allocate(width, height, 1);
                    history._frameCount = 0;
                }
                else
                {
                    (history._previous, history._current) = (history._current, history._previous);
                }
                return history;
            }

            // 원본 HistoryBufferAllocator처럼 RGBA16F, 점 필터, 반복 래핑으로 만든다.
            static RTHandle Allocate(int width, int height, int frame) => RTHandles.Alloc(width, height, GraphicsFormat.R16G16B16A16_SFloat,
                filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Repeat, name: "HBAO_HistoryBuffer_" + frame);

            // 원본 FrameCleanup처럼 한 프레임 넘게 시간 필터를 쓰지 않은 카메라의 기록을 해제한다. 파괴된 카메라도 포함한다.
            // HBAO 활성 여부와 무관하게 AddRenderPasses가 카메라마다 부르므로, 해제 대상 목록은 재사용해 매 프레임 할당하지 않는다.
            internal void ReleaseStale()
            {
                if (_histories.Count == 0) return;
                int frame = Time.frameCount;
                foreach (KeyValuePair<Camera, History> entry in _histories)
                {
                    // 파괴된 카메라는 더 그리지 않으므로 한 프레임 뒤 나이 검사로 함께 걸린다. 키 참조는 남아 있어 그 키로 제거한다.
                    // 파괴 검사로 Unity null 비교를 하지 않는다. 씬을 읽는 동안 그 비교가 로딩 잠금을 기다리기 때문이다.
                    if (frame - entry.Value._lastRenderedFrame > 1) _staleCameras.Add(entry.Key);
                }
                foreach (Camera camera in _staleCameras)
                {
                    // 같은 키를 두 번 찾지 않도록 꺼내면서 지운다. 키는 방금 사전에서 모았으므로 늘 있다.
                    if (_histories.Remove(camera, out History history))
                        history.Release();
                }
                _staleCameras.Clear();
            }

            public void Dispose()
            {
                foreach (History history in _histories.Values) history.Release();
                _histories.Clear();
            }

            // 사전이 카메라 키를 찾고 지울 때 Unity Equals 대신 참조와 참조 해시만 쓰게 한다. 파괴된 키도 엔진에 묻지 않는다.
            sealed class CameraReferenceComparer : IEqualityComparer<Camera>
            {
                internal static readonly CameraReferenceComparer _instance = new();
                public bool Equals(Camera? x, Camera? y) => ReferenceEquals(x, y);
                public int GetHashCode(Camera camera) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(camera);
            }
        }

        // 원본 CreateNoiseTexture의 Dither 노이즈다. 4×4 RGHalf에 점 필터와 반복 래핑을 쓰고, MersenneTwister 수열을 (i, j) 순서로 채운다.
        static class HbaoNoise
        {
            // 노이즈 텍스처의 한 변 크기다. DuckovHbao.shader의 HBAO_NOISE_UV_SCALE은 이 값의 역수(0.25)라 함께 맞춰야 한다.
            const int _noiseSize = 4;
            // 원본 HBAORenderPass가 Dither 노이즈에 쓰는 MersenneTwister 수열이다.
            static readonly float[] _mersenneTwister =
            {
                .556725f, .00552f, .708315f, .583199f, .236644f, .99238f, .981091f, .119804f, .510866f, .560499f,
                .961497f, .557862f, .539955f, .332871f, .417807f, .920779f, .730747f, .07669f, .008562f, .660104f,
                .428921f, .511342f, .587871f, .906406f, .43798f, .620309f, .062196f, .119485f, .235646f, .795892f,
                .044437f, .617311f,
            };

            internal static Texture2D Create()
            {
                TextureFormat format = SystemInfo.SupportsTextureFormat(TextureFormat.RGHalf) ? TextureFormat.RGHalf : TextureFormat.RGB24;
                var noise = new Texture2D(_noiseSize, _noiseSize, format, false, true)
                {
                    name = "HBAO Noise",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat,
                    hideFlags = HideFlags.DontSave,
                };
                int index = 0;
                for (int i = 0; i < _noiseSize; i++)
                {
                    for (int j = 0; j < _noiseSize; j++)
                    {
                        float r = _mersenneTwister[index++];
                        float g = _mersenneTwister[index++];
                        noise.SetPixel(i, j, new Color(r, g, 0f));
                    }
                }
                noise.Apply();
                return noise;
            }
        }
    }
}
