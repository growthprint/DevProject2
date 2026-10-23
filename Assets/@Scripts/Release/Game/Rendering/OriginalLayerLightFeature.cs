#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering
{
    // 원본의 SodaLight와 EdgeLight 렌더러 기능인 URP 14 RenderObjects를 Render Graph로 재현한다. 원본과 같은 이벤트,
    // 레이어, 투명 큐에서 그 레이어를 재정의 재질로 그린다. URP 17 RenderObjects는 GBuffer와 깊이 입력을 선언하지 않아 여기서 직접 연결한다.
    public sealed class OriginalLayerLightFeature : ScriptableRendererFeature
    {
        [SerializeField] Material? _overrideMaterial;
        [SerializeField] LayerMask _layerMask;
        [SerializeField] RenderPassEvent _passEvent = RenderPassEvent.BeforeRenderingDeferredLights;
        // SodaLight처럼 GBuffer를 읽는 기능에서 켠다. 켜면 GBuffer가 없는 Forward 카메라에서는 그리지 않는다.
        [SerializeField] bool _requiresGBuffer;
        LayerLightPass? _pass;

        // 패스 설정은 매 프레임이 아니라 패스를 만들 때 한 번 넣는다. URP는 직렬화 값이 바뀔 때마다 Create를 다시 부르고,
        // 제작 때 Editor의 ComponentAuthoring.ConfigureRendererFeature도 필드를 쓴 뒤 Create를 부르므로 새 패스가 새 값을 받는다.
        public override void Create()
        {
            _pass = new LayerLightPass(name, _overrideMaterial, _layerMask, _passEvent, _requiresGBuffer);
        }

        // 설정은 Create에서 끝났으므로 여기서는 검사 뒤 패스만 넣는다.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            renderer.EnqueuePass(_pass);
        }

        sealed class LayerLightPass : ScriptableRenderPass
        {
            static readonly List<ShaderTagId> _shaderTags = new()
                { new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly") };
            static readonly int _depthId = Shader.PropertyToID("_OriginalLightDepth");
            static readonly int _gBuffer0Id = Shader.PropertyToID("_OriginalLightGBuffer0");
            // 원본 SodaPointLight가 GBuffer1.x로 빛을 줄인다. URP 메탈릭 경로에서 이 값은 금속도가 아니라 반사율이다.
            static readonly int _gBuffer1Id = Shader.PropertyToID("_OriginalLightGBuffer1");
            static readonly int _gBuffer2Id = Shader.PropertyToID("_OriginalLightGBuffer2");
            // 같은 재질을 일반 투명 패스에서 그릴 때와 구분하도록 이 패스 동안만 1로 둔다.
            static readonly int _fromFeatureId = Shader.PropertyToID("_OriginalLightFromFeature");
            // 값이 바뀌면 Create가 패스를 새로 만들므로 한 패스의 설정은 생성 뒤 바뀌지 않는다.
            readonly string _passName;
            readonly Material? _material;
            readonly LayerMask _layerMask;
            readonly bool _requiresGBuffer;

            sealed class PassData
            {
                public RendererListHandle _list;
                public TextureHandle _depth;
                public TextureHandle _gBuffer0;
                public TextureHandle _gBuffer1;
                public TextureHandle _gBuffer2;
                public bool _hasGBuffer;
            }

            // Create는 제작 도구가 재질을 연결하기 전에도 불리므로 null을 받는다.
            internal LayerLightPass(string passName, Material? material, LayerMask layerMask, RenderPassEvent passEvent, bool requiresGBuffer)
            {
                _passName = passName;
                _material = material;
                _layerMask = layerMask;
                _requiresGBuffer = requiresGBuffer;
                renderPassEvent = passEvent;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var rendering = frameData.Get<UniversalRenderingData>();
                var camera = frameData.Get<UniversalCameraData>();
                var lights = frameData.Get<UniversalLightData>();
                TextureHandle[]? gBuffer = resources.gBuffer;
                // GBuffer 검사 결과는 그리기 여부와 GBuffer 연결 여부에 함께 쓰므로 한 번만 구한다.
                bool hasGBuffer = HasLightingGBuffer(gBuffer);
                if (_requiresGBuffer && !hasGBuffer) return;
                if (!resources.activeColorTexture.IsValid() || !resources.activeDepthTexture.IsValid()) return;

                DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(_shaderTags, rendering, camera, lights, SortingCriteria.CommonTransparent);
                drawing.overrideMaterial = _material;
                drawing.overrideMaterialPassIndex = 0;
                var filtering = new FilteringSettings(RenderQueueRange.transparent, _layerMask);
                RendererListHandle list = graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));

                using var builder = graph.AddRasterRenderPass<PassData>(_passName, out PassData data);
                data._list = list;
                builder.UseRendererList(list);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                data._depth = resources.activeDepthTexture;
                builder.UseTexture(data._depth, AccessFlags.Read);
                data._hasGBuffer = hasGBuffer;
                if (hasGBuffer)
                {
                    data._gBuffer0 = gBuffer![0];
                    data._gBuffer1 = gBuffer[1];
                    data._gBuffer2 = gBuffer[2];
                    builder.UseTexture(data._gBuffer0, AccessFlags.Read);
                    builder.UseTexture(data._gBuffer1, AccessFlags.Read);
                    builder.UseTexture(data._gBuffer2, AccessFlags.Read);
                }
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
                {
                    // 깊이 스텐실 텍스처라 깊이 요소를 지정해서 연결한다.
                    context.cmd.SetGlobalTexture(_depthId, pass._depth, RenderTextureSubElement.Depth);
                    if (pass._hasGBuffer)
                    {
                        context.cmd.SetGlobalTexture(_gBuffer0Id, pass._gBuffer0);
                        context.cmd.SetGlobalTexture(_gBuffer1Id, pass._gBuffer1);
                        context.cmd.SetGlobalTexture(_gBuffer2Id, pass._gBuffer2);
                    }
                    context.cmd.SetGlobalFloat(_fromFeatureId, 1f);
                    context.cmd.DrawRendererList(pass._list);
                    context.cmd.SetGlobalFloat(_fromFeatureId, 0f);
                });
            }

            // 원본 SodaPointLight 수식은 알베도, 반사율, 노멀인 GBuffer 0~2를 모두 읽으므로 셋 다 유효해야 한다.
            static bool HasLightingGBuffer(TextureHandle[]? gBuffer) =>
                gBuffer != null && gBuffer.Length > 2 && gBuffer[0].IsValid() && gBuffer[1].IsValid() && gBuffer[2].IsValid();
        }
    }
}
