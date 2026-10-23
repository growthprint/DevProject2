using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Game.App
{
    // 지도 블러가 켜진 게임 카메라에만 블러를 건다. Render Graph에서 색상 입력과 출력을 명시해 처리한다.
    public sealed class MapBlurRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] Shader _shader;
        Material _material;
        BlurPass _pass;
        // _shader는 제작 때 Editor의 ComponentAuthoring.ConfigureRendererFeature가 채운다. 제작기의 CreateInstance가
        // 셰이더를 쓰기 전에 Create를 부르면 패스 없이 끝나고, 그동안 큐에 들어간 null 패스는 URP가 AddRenderPasses 직후 지운다.
        public override void Create()
        {
            ReleaseResources();
            if (_shader == null) return;
            _material = CoreUtils.CreateEngineMaterial(_shader);
            _pass = new BlurPass(_material);
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 씬 뷰와 미리보기 카메라는 지도 블러 대상이 아니므로 게임 카메라만 거른다.
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            // 매 프레임 컴포넌트를 찾지 않고, MapBlurCameraState가 켜질 때 등록한 집합에 카메라가 있는지만 본다.
            if (MapBlurCameraState.IsBlurred(renderingData.cameraData.camera)) renderer.EnqueuePass(_pass);
        }
        // 패스는 재질을 쥐므로 재질과 함께 버린다. 셰이더가 빠진 채 다시 만들어도 파괴된 재질을 쥔 패스가 큐에 들어가지 않는다.
        protected override void Dispose(bool disposing) => ReleaseResources();

        void ReleaseResources()
        {
            CoreUtils.Destroy(_material);
            _material = null;
            _pass = null;
        }

        sealed class BlurPass : ScriptableRenderPass
        {
            // MapBlur.shader 패스 순서다. 복사 패스는 축소와 확대에 함께 쓴다.
            const int _copyPass = 0;
            const int _horizontalPass = 1;
            const int _verticalPass = 2;
            // 짧은 변이 1080 픽셀인 화면에서 블러를 계산할 축소 텍스처의 가로세로 배율이다.
            const int _downsample = 4;
            // 원본 ScalableBlur.ScaleWithResolution처럼 짧은 변 1080 픽셀을 기준으로 블러 폭을 맞추고, 그 배율을 0.5~2로 제한한다.
            const float _referenceShortSide = 1080f;
            const float _minResolutionScale = .5f;
            const float _maxResolutionScale = 2f;
            readonly Material _material;
            internal BlurPass(Material material)
            {
                _material = material;
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                TextureHandle color = resources.activeColorTexture;
                // 셰이더 탭 간격은 축소 텍스처의 텍셀 단위라 축소 크기가 화면 위 블러 폭을 정한다. 렌더 배율이 적용된 색 텍스처 대신
                // 원본처럼 카메라 픽셀 크기로 정하고, 짧은 변 기준 배율로 나눠 렌더 배율과 해상도가 달라도 1080p 배율 1과 같은 폭을 낸다.
                Camera camera = frameData.Get<UniversalCameraData>().camera;
                int pixelWidth = camera.pixelWidth;
                int pixelHeight = camera.pixelHeight;
                float resolutionScale = Mathf.Clamp(Mathf.Min(pixelWidth, pixelHeight) / _referenceShortSide, _minResolutionScale, _maxResolutionScale);
                float divisor = _downsample * resolutionScale;
                var small = graph.GetTextureDesc(color);
                small.clearBuffer = false;
                small.width = Mathf.Max(1, Mathf.RoundToInt(pixelWidth / divisor));
                small.height = Mathf.Max(1, Mathf.RoundToInt(pixelHeight / divisor));
                small.name = "MapBlurQuarter";
                TextureHandle down = graph.CreateTexture(small);
                small.name = "MapBlurHorizontal";
                TextureHandle horizontal = graph.CreateTexture(small);
                small.name = "MapBlurVertical";
                TextureHandle vertical = graph.CreateTexture(small);
                // 패스마다 별도 텍스처에 쓴다. 패스는 나중에 실행되므로 공유 재질의 값은 바꾸지 않고 셰이더 패스 번호로만 구분한다.
                // 축소 뒤에는 색 텍스처를 다시 읽지 않으므로, 전체 해상도 텍스처를 새로 만들지 않고 블러 결과를 카메라 색에 바로 덮어쓴다.
                graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(color, down, _material, _copyPass),
                    passName: "Map blur downsample");
                graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(down, horizontal, _material, _horizontalPass),
                    passName: "Map blur horizontal");
                graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(horizontal, vertical, _material, _verticalPass),
                    passName: "Map blur vertical");
                graph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(vertical, color, _material, _copyPass),
                    passName: "Map blur upsample");
            }
        }
    }
}
