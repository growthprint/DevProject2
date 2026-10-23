#nullable enable annotations
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.App
{
    // 원본 그래픽 옵션의 기본 그림자와 FSR 값을 실행 중에 적용하고, 앱이 끝날 때 원래 파이프라인으로 되돌린다.
    internal sealed class GameGraphicsOptions : IAsyncReleasable
    {
        // 현재 품질 계층의 원래 파이프라인 자산이다. 종료 때 복구한다.
        readonly RenderPipelineAsset _previous = QualitySettings.renderPipeline;
        // 공유 프로젝트 자산을 더럽히지 않도록 런타임 복제본에만 값을 쓴다.
        UniversalRenderPipelineAsset? _runtimeAsset;
        // 파이프라인 복원과 복제본 해제를 한 번만 실행한다. 실패는 기록만 하고 던지지 않는다.
        // 해제가 시작되면 Started로 런타임 복제본을 다시 만들지 않는다.
        readonly ReleaseOnce _release;

        public GameGraphicsOptions() => _release = new ReleaseOnce(ReleaseCoreAsync);

        // 원본 ShadowSetting 2단계와 FsrSetting 끄기 수치를 쓴다. 그래픽 값은 옵션 화면과 옵션 파일에 없는 고정값이다.
        // 해제 뒤에만 false이고, EditMode여서 바꿀 것이 없어도 true다.
        public bool Apply()
        {
            // 해제 뒤 호출은 EditMode에서도 예외나 로그 없이 거부한다. 해제 여부는 ReleaseOnce.Started 하나로 본다.
            if (_release.Started) return false;
            // EditMode 테스트가 개발자의 품질 설정을 바꾸지 않게 한다.
            if (!Application.isPlaying) return true;
            if (_runtimeAsset == null)
            {
                // 프로젝트 그래픽 설정이 URP 파이프라인 자산을 쓰므로 형 변환만 한다.
                var source = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
                _runtimeAsset = Object.Instantiate(source);
                _runtimeAsset.name = source.name + " (Runtime Options)";
                _runtimeAsset.hideFlags = HideFlags.DontSave;
                QualitySettings.renderPipeline = _runtimeAsset;
            }
            // 원본 ShadowSetting 2단계의 그림자 거리, 그림자 맵 해상도, 추가 광원 수다.
            _runtimeAsset.shadowDistance = 80f;
            _runtimeAsset.mainLightShadowmapResolution = 2048;
            _runtimeAsset.additionalLightsShadowmapResolution = 2048;
            _runtimeAsset.maxAdditionalLightsCount = 5;
            // 원본 FSR 끄기 상태라 렌더 배율 1과 선형 필터를 쓴다.
            _runtimeAsset.renderScale = 1f;
            _runtimeAsset.upscalingFilter = UpscalingFilterSelection.Linear;
            return true;
        }

        // 원래 렌더 파이프라인을 먼저 되돌린 뒤 복제본을 해제한다. 메인 스레드의 ResourceScope만 부르므로 스레드를 바꾸지 않는다.
        // 래퍼 상태 기계 없이 ReleaseOnce의 공유 완료를 그대로 돌려준다.
        public UniTask ReleaseAsync() => _release.ReleaseAsync();

        // ReleaseOnce가 Started를 먼저 세운 뒤 부르므로, 복원이 실패해도 다시 시도하지 않고 그 뒤의 Apply도 거부된다.
        UniTask ReleaseCoreAsync()
        {
            if (_runtimeAsset != null)
            {
                if (QualitySettings.renderPipeline == _runtimeAsset) QualitySettings.renderPipeline = _previous;
                // 정리가 Play Mode 종료 뒤에 끝날 수 있어, 그때의 모드에 맞게 파괴하는 UnityObjects로 해제한다.
                UnityObjects.DestroyOwned(_runtimeAsset);
                _runtimeAsset = null;
            }
            return UniTask.CompletedTask;
        }
    }
}
