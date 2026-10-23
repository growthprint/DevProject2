#nullable enable annotations
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Rendering
{
    // 원본 HorizonBasedAmbientOcclusion.Universal.HBAO 볼륨을 같은 매개변수, 기본값, 열거 값으로 재현한다.
    // 필드 이름은 원본 이름 앞에 '_'를 붙인다. DuckovOpeningContentBuilder가 이 규칙으로 원본 볼륨 값을 옮긴다.
    // 그리기는 OriginalHbaoRendererFeature가 맡는다. 그래픽 옵션이 보통으로 고정이라 프로필 값을 그대로 혼합하며,
    // 게임 코드가 구동하지 않고 렌더 파이프라인만 읽으므로 Game.Rendering에 둔다.
    [Serializable, VolumeComponentMenu("Game/Original HBAO")]
    public sealed class OriginalHbao : VolumeComponent
    {
        public enum Preset { FastestPerformance, FastPerformance, Normal, HighQuality, HighestQuality, Custom }
        public enum Mode { Normal, LitAO }
        public enum RenderingPath { Forward, Deferred }
        public enum Quality { Lowest, Low, Medium, High, Highest }
        public enum Resolution { Full, Half }
        public enum NoiseType { Dither, InterleavedGradientNoise, SpatialDistribution }
        public enum Deinterleaving { Disabled, x4 }
        public enum DebugMode { Disabled, AOOnly, ColorBleedingOnly, SplitWithoutAOAndWithAO, SplitWithAOAndAOOnly, SplitWithoutAOAndAOOnly, ViewNormals }
        public enum BlurType { None, Narrow, Medium, Wide, ExtraWide }
        public enum PerPixelNormals { Reconstruct2Samples, Reconstruct4Samples, Camera }
        public enum VarianceClipping { Disabled, _4Tap, _8Tap }

        // 원본 선언 순서와 기본값 그대로다. 렌더러 기능은 LitAO, 디인터리브, 색 번짐, IGN 노이즈, 8탭 클리핑, 노멀 재구성, 디버그를
        // 그리지 않는다. 이 경로는 범위 밖 원본 볼륨(Thermal, MiniGame)만 쓰므로 값만 데이터로 보존한다.
        // 프로필은 필드 이름 아래 켜짐 여부와 값만 저장하므로 열거 매개변수는 SRP Core의 EnumParameter로 둔다.
        public EnumParameter<Preset> _preset = new(Preset.Normal);
        public EnumParameter<Mode> _mode = new(Mode.LitAO);
        public EnumParameter<RenderingPath> _renderingPath = new(RenderingPath.Forward);
        public EnumParameter<Quality> _quality = new(Quality.Medium);
        public EnumParameter<Deinterleaving> _deinterleaving = new(Deinterleaving.Disabled);
        public EnumParameter<Resolution> _resolution = new(Resolution.Full);
        public EnumParameter<NoiseType> _noiseType = new(NoiseType.Dither);
        public EnumParameter<DebugMode> _debugMode = new(DebugMode.Disabled);
        public ClampedFloatParameter _radius = new(.8f, .25f, 5f);
        public ClampedFloatParameter _maxRadiusPixels = new(128f, 16f, 256f);
        public ClampedFloatParameter _bias = new(.05f, 0f, .5f);
        public ClampedFloatParameter _intensity = new(0f, 0f, 4f);
        public BoolParameter _useMultiBounce = new(false);
        public ClampedFloatParameter _multiBounceInfluence = new(1f, 0f, 1f);
        public ClampedFloatParameter _directLightingStrength = new(.25f, 0f, 1f);
        public ClampedFloatParameter _offscreenSamplesContribution = new(0f, 0f, 1f);
        public FloatParameter _maxDistance = new(150f);
        public FloatParameter _distanceFalloff = new(50f);
        public EnumParameter<PerPixelNormals> _perPixelNormals = new(PerPixelNormals.Camera);
        public ColorParameter _baseColor = new(Color.black);
        public BoolParameter _temporalFilterEnabled = new(false);
        public EnumParameter<VarianceClipping> _varianceClipping = new(VarianceClipping._4Tap);
        public EnumParameter<BlurType> _blurType = new(BlurType.Medium);
        public ClampedFloatParameter _sharpness = new(8f, 0f, 16f);
        public BoolParameter _colorBleedingEnabled = new(false);
        public ClampedFloatParameter _saturation = new(1f, 0f, 4f);
        public ClampedFloatParameter _brightnessMask = new(1f, 0f, 1f);
        public FloatRangeParameter _brightnessMaskRange = new(new Vector2(0f, .5f), 0f, 2f);

        // 원본 HBAOSettings.GlobalVolumePorfile(DuckovGlobalVolumeProfile)의 컴포넌트인지 나타내는 원본 데이터다. 제작 도구가 그 프로필에만 켠다.
        // 그래픽 옵션이 보통(켜짐)으로 고정이라 원본 HBAOSettings.Set이 끌 일이 없어 실행 중에는 읽지 않는다. 그래서 생기는 CS0414만 여기서 끈다.
#pragma warning disable CS0414
        [SerializeField] bool _followsGraphicsOption = false;
#pragma warning restore CS0414

        // 원본 IsActive: 혼합 결과에서 intensity가 override되어 있고 0보다 클 때만 그린다.
        public bool IsActive() => _intensity.overrideState && _intensity.value > 0f;
    }
}
