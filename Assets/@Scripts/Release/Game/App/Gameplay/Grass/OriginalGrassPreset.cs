#nullable enable annotations
using System;
using UnityEngine;

// 장면 전환이 풀 굽기를 부르므로 풀 코드 전체를 게임 어셈블리에 둔다.
namespace Game.App
{
    // 원본 TgsPreset이다. 제작 도구가 원본 값을 그대로 옮기도록 직렬화 필드는 원본 이름 앞에 _를 붙이고 원본 순서를 지킨다.
    // 굽기 값은 계산 셰이더에, 풀잎 모양과 셰이딩 값은 덩어리마다의 MaterialPropertyBlock에 원본 셰이더 이름으로 넣는다.
    public sealed class OriginalGrassPreset : ScriptableObject
    {
        public enum BladeTextureUvLayout
        {
            TriangleSingle,
            TriangleCenter
        }

        // 원본 NoiseSetting이다. 3D 노이즈 텍스처의 빨강 채널 값이 풀잎 높이, 너비, 두께, 색을 각 범위 안에서 정한다.
        [Serializable]
        public struct NoiseSetting
        {
            public int _disable;
            public bool _solo;
            public Texture3D? _texture;
            public float _tiling;
            public float _valueScale;
            public float _valueOffset;
            public Vector2 _height;
            public Vector2 _offset;
            public Vector2 _angle;
            public Vector2 _thickness;
            public Vector2 _skew;
            public Vector2 _colorInfluence;
            public Color _color;

            internal static NoiseSetting GetDefault(bool withDisabled)
            {
                return new NoiseSetting
                {
                    _disable = withDisabled ? 1 : 0,
                    _tiling = 1f,
                    _valueScale = 1f,
                    _color = Color.white
                };
            }
        }

        // 원본 TgsPreset.Settings다. 지형 층이 프리셋 값에 곱하는 배율과 색 보정, 지면 색 섞기 정도를 담는다.
        [Serializable]
        public struct Settings
        {
            public OriginalGrassPreset? _preset;
            [Min(0f)] public float _amount;
            [Min(0f)] public float _height;
            public float _thickness;
            [Min(0f)] public float _angle;
            public float _tiling;
            [Range(-180f, 180f)] public float _slopeBias;
            public Color _tint;
            [Range(-180f, 180f)] public float _hueShift;
            [Range(0f, 3f)] public float _saturation;
            [Range(0f, 1f)] public float _camouflage;

            internal static Settings GetDefault()
            {
                return new Settings
                {
                    _height = 1f,
                    _tiling = 1f,
                    _amount = 1f,
                    _thickness = 1f,
                    _angle = 1f,
                    _tint = Color.white,
                    _saturation = 1f
                };
            }
        }

        // 계산 셰이더 _NoiseParams의 80바이트 원소다. 필드 순서와 형식이 곧 GPU 구조체 배치이므로 바꾸지 않는다.
        struct NoiseSettingGpu
        {
            public int _disabled;
            public float _tiling;
            public float _valueScale;
            public float _valueOffset;
            public Vector2 _height;
            public Vector2 _offset;
            public Vector2 _width;
            public Vector2 _thickness;
            public Vector2 _thicknessApex;
            public Vector2 _colorInfluence;
            public Color _color;

            // 원본처럼 높이와 오프셋에는 높이 배율을, 너비에는 각도 배율을 곱한다.
            // 색은 틴트를 곱한 뒤 HSV로 바꿔 색조를 돌리고 채도를 곱한다.
            internal NoiseSettingGpu(NoiseSetting noise, Settings settings)
            {
                _disabled = noise._disable;
                _tiling = noise._tiling * settings._tiling;
                _valueScale = noise._valueScale;
                _valueOffset = noise._valueOffset;
                _height = noise._height * settings._height;
                _offset = noise._offset * settings._height;
                _width = noise._angle * settings._angle;
                _thickness = noise._thickness * settings._thickness;
                _thicknessApex = noise._skew;
                _colorInfluence = noise._colorInfluence;
                Color.RGBToHSV(noise._color * settings._tint, out float hue, out float saturation, out float value);
                // 도 단위 색조 이동을 HSV 0~1 범위로 바꾼다.
                hue += settings._hueShift * _hueShiftDegreesToUnit;
                hue = Mathf.Repeat(hue, 1f);
                saturation *= settings._saturation;
                _color = Color.HSVToRGB(hue, saturation, value);
            }
        }

        internal const int _noiseLayerCount = 4;
        internal const int _noiseSettingGpuStride = 80;
        // 원본 색조 이동 배율(1/360)이다. 원본 리터럴 값을 그대로 쓴다.
        const float _hueShiftDegreesToUnit = 0.0027777778f;

        [SerializeField, Min(0f)] float _density = 60f;
        [SerializeField, Range(0f, 180f)] float _angleLimit = 85f;
        [SerializeField] Vector3 _growDirection = Vector3.up;
        [SerializeField, Range(0f, 1f)] float _growDirectionBySurfaceNormal;
        [SerializeField, Min(0f)] float _minimalHeight = .05f;
        [SerializeField, Min(1f)] int _clumpCount = 2;
        [SerializeField, Range(0f, 1f)] float _clumpPercentage = .9f;
        [SerializeField] bool _clumpLimitNoiseVariation;
        [SerializeField] bool _stemGenerate;
        [SerializeField] Color _stemColor = new Color(.396f, .506f, .133f);
        [SerializeField] float _stemThicknessRatio = .25f;
        [SerializeField] NoiseSetting _noiseLayer0 = new NoiseSetting
        {
            _tiling = 30f,
            _valueScale = 3f,
            _valueOffset = -.25f,
            _height = new Vector2(.15f, .8f),
            _offset = new Vector2(-.35f, -.15f),
            _angle = new Vector2(.02f, .2f),
            _thickness = new Vector2(-.5f, -.5f),
            _skew = new Vector2(-0f, -.25f),
            _colorInfluence = new Vector2(1f, .1f),
            _color = new Color(.3942137f, 43f / 85f, .1320353f)
        };
        [SerializeField] NoiseSetting _noiseLayer1 = NoiseSetting.GetDefault(true);
        [SerializeField] NoiseSetting _noiseLayer2 = NoiseSetting.GetDefault(true);
        [SerializeField] NoiseSetting _noiseLayer3 = NoiseSetting.GetDefault(true);
        [SerializeField, Range(0f, .25f)] float _twirl = .03f;
        [SerializeField] float _flatten;
        [SerializeField, Range(0f, 2f)] float _scruffiness;
        [SerializeField, Range(0f, 1f)] float _limpness;
        [SerializeField] Texture2D? _texture;
        [SerializeField] BladeTextureUvLayout _textureUvLayout;
        [SerializeField, Range(0f, 1f)] float _proceduralShapeBlend;
        [SerializeField, Range(0f, 4f)] float _upperArc = .03f;
        [SerializeField, Range(0f, 4f)] float _lowerArc = 3.5f;
        [SerializeField, Range(0f, .1f)] float _tipRounding;
        [SerializeField, Min(0f)] float _windIntensityScale = 1f;
        [SerializeField] float _windPivotOffset;
        [SerializeField, Range(0f, 1f)] float _smoothness;
        [SerializeField, Range(0f, 1f)] float _occlusionByHeight = 1f;
        [SerializeField, Range(0f, 1f)] float _colliderInfluence = 1f;
        [SerializeField, Min(0f)] float _baseLodFactor = 1f;
        [SerializeField] bool _castShadows = true;

        // 원본처럼 모든 덩어리가 하나의 배열을 돌려 쓴다. SetData가 바로 복사하므로 다음 덩어리가 덮어써도 된다.
        static readonly NoiseSettingGpu[] _noiseLayerBuffers = new NoiseSettingGpu[_noiseLayerCount];

        static readonly int _densityId = Shader.PropertyToID("_Density");
        static readonly int _clumpCountId = Shader.PropertyToID("_ClumpCount");
        static readonly int _bladeLimpnessId = Shader.PropertyToID("_BladeLimpness");
        static readonly int _growDirectionBySurfaceNormalId = Shader.PropertyToID("_GrowDirectionBySurfaceNormal");
        static readonly int _bladeScaleThresholdId = Shader.PropertyToID("_BladeScaleThreshold");
        static readonly int _arcUpId = Shader.PropertyToID("_ArcUp");
        static readonly int _arcDownId = Shader.PropertyToID("_ArcDown");
        static readonly int _tipRoundingId = Shader.PropertyToID("_TipRounding");
        static readonly int _smoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int _scruffinessId = Shader.PropertyToID("_Scruffiness");
        static readonly int _windIntensityScaleId = Shader.PropertyToID("_WindIntensityScale");
        // 노이즈 층 번호 순서다. 계산 셰이더 BakePass의 _NoiseTexture0~3 바인딩과 같은 순서여야 한다.
        static readonly int[] _noiseTextureIds =
        {
            Shader.PropertyToID("_NoiseTexture0"),
            Shader.PropertyToID("_NoiseTexture1"),
            Shader.PropertyToID("_NoiseTexture2"),
            Shader.PropertyToID("_NoiseTexture3")
        };
        static readonly int _growDirectionId = Shader.PropertyToID("_GrowDirection");
        static readonly int _windPivotOffsetByHeightId = Shader.PropertyToID("_WindPivotOffsetByHeight");
        static readonly int _twirlStrengthId = Shader.PropertyToID("_TwirlStrength");
        static readonly int _postVerticalScaleId = Shader.PropertyToID("_PostVerticalScale");
        static readonly int _stemGenerateId = Shader.PropertyToID("_StemGenerate");
        static readonly int _stemColorId = Shader.PropertyToID("_StemColor");
        static readonly int _stemThicknessId = Shader.PropertyToID("_StemThickness");
        static readonly int _colliderInfluenceId = Shader.PropertyToID("_ColliderInfluence");
        static readonly int _growSlopeLimitId = Shader.PropertyToID("_GrowSlopeLimit");
        static readonly int _proceduralShapeBlendId = Shader.PropertyToID("_ProceduralShapeBlend");
        static readonly int _occlusionByHeightId = Shader.PropertyToID("_OcclusionByHeight");
        static readonly int _uvUseCenterTriangleId = Shader.PropertyToID("_UvUseCenterTriangle");
        static readonly int _mainTexId = Shader.PropertyToID("_MainTex");
        static readonly int _clumpPercentageId = Shader.PropertyToID("_ClumpPercentage");
        static readonly int _clumpLimitVariationId = Shader.PropertyToID("_ClumpLimitVariation");

        internal float BaseLodFactor => _baseLodFactor;

        internal bool CastShadows => _castShadows;

        // 원본 ApplyToComputeShader다. 밀도에는 전역 밀도 배율과 층의 양 배율을 곱하고, 경사 한계는 각도의 코사인으로 넘긴다.
        // 노이즈 텍스처는 BakePass에만 묶고, 빠진 칸은 원본처럼 흰 텍스처로 채운다.
        internal void ApplyToComputeShader(ComputeShader compute, Settings settings, int bakePassKernel)
        {
            compute.SetFloat(_densityId, _density * OriginalGrassRenderer._densityScale * settings._amount);
            compute.SetInt(_clumpCountId, _clumpCount);
            compute.SetFloat(_clumpPercentageId, _clumpPercentage);
            compute.SetFloat(_clumpLimitVariationId, _clumpLimitNoiseVariation ? 1f : 0f);
            compute.SetFloat(_scruffinessId, _scruffiness);
            compute.SetFloat(_bladeLimpnessId, _limpness);
            compute.SetFloat(_twirlStrengthId, _twirl);
            compute.SetFloat(_postVerticalScaleId, _flatten);
            compute.SetFloat(_growDirectionBySurfaceNormalId, _growDirectionBySurfaceNormal);
            compute.SetFloat(_growSlopeLimitId, Mathf.Cos(Mathf.Clamp(_angleLimit + settings._slopeBias, 0f, 180f) * (MathF.PI / 180f)));
            compute.SetVector(_growDirectionId, _growDirection);
            compute.SetInt(_stemGenerateId, _stemGenerate ? 1 : 0);
            compute.SetVector(_stemColorId, _stemColor);
            compute.SetFloat(_stemThicknessId, _stemThicknessRatio);
            compute.SetFloat(_bladeScaleThresholdId, _minimalHeight * settings._height);
            // 층 0부터 3까지 원본 순서로 묶는다.
            for (int i = 0; i < _noiseLayerCount; i++)
                compute.SetTexture(bakePassKernel, _noiseTextureIds[i], NoiseTextureOrWhite(GetNoiseLayer(i)._texture));
        }

        // 원본 ApplyLayerSettingsToBuffer다. 솔로로 고른 층이 하나라도 있으면 나머지 층은 꺼진 기본값으로 보낸다.
        // 버퍼 원소 순서가 계산 셰이더의 층 번호이므로 0부터 차례로 채운다.
        internal void ApplyLayerSettingsToBuffer(ComputeBuffer buffer, Settings settings)
        {
            bool anySolo = _noiseLayer0._solo || _noiseLayer1._solo || _noiseLayer2._solo || _noiseLayer3._solo;
            for (int i = 0; i < _noiseLayerCount; i++)
                _noiseLayerBuffers[i] = new NoiseSettingGpu(HandleNoiseSettingSolo(GetNoiseLayer(i), anySolo), settings);
            buffer.SetData(_noiseLayerBuffers);
        }

        // 직렬화 필드 이름이 원본 필드 이름이라 배열로 묶지 않고 층 번호로 네 필드 중 하나를 고른다.
        NoiseSetting GetNoiseLayer(int index) => index switch
        {
            0 => _noiseLayer0,
            1 => _noiseLayer1,
            2 => _noiseLayer2,
            _ => _noiseLayer3
        };

        // 원본 ApplyToMaterialPropertyBlock이다. 덩어리마다 처음 그릴 때 한 번 넣는 풀잎 모양과 셰이딩 값이다.
        internal void ApplyToMaterialPropertyBlock(MaterialPropertyBlock block)
        {
            block.SetFloat(_arcUpId, _upperArc);
            block.SetFloat(_arcDownId, _lowerArc);
            block.SetFloat(_tipRoundingId, _tipRounding);
            block.SetFloat(_proceduralShapeBlendId, _proceduralShapeBlend);
            block.SetFloat(_smoothnessId, _smoothness);
            block.SetFloat(_occlusionByHeightId, _occlusionByHeight);
            block.SetFloat(_windPivotOffsetByHeightId, _windPivotOffset);
            block.SetFloat(_windIntensityScaleId, _windIntensityScale);
            block.SetFloat(_colliderInfluenceId, _colliderInfluence);
            block.SetFloat(_uvUseCenterTriangleId, _textureUvLayout == BladeTextureUvLayout.TriangleCenter ? 1f : 0f);
            block.SetTexture(_mainTexId, _texture != null ? _texture : Texture2D.whiteTexture);
        }

        static NoiseSetting HandleNoiseSettingSolo(NoiseSetting noise, bool anySolo)
            => anySolo && !noise._solo ? NoiseSetting.GetDefault(true) : noise;

        static Texture NoiseTextureOrWhite(Texture3D? texture)
            => texture != null ? (Texture)texture : Texture2D.whiteTexture;
    }
}
