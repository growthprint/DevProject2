#nullable enable annotations
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Rendering
{
    // 원본 SodaCraft.SunFogTD 볼륨의 거리 안개와 화면 공간 태양광 값이다. 원본처럼 _enable과 관계없이 볼륨이 섞은 state 값을
    // 원본과 같은 이름의 전역 셰이더 값으로 넣고, 화면 쿼드 셰이더인 DuckovOverlay가 읽는다.
    [Serializable, VolumeComponentMenu("Game/Original Sun Fog")]
    public sealed class OriginalSunFog : VolumeComponent
    {
        public BoolParameter _enable = new(false);
        public ColorParameter _fogColor = new(new Color(.68718916f, 1.070217f, 1.3615336f, 0f), true, true, false);
        public ColorParameter _sunColor = new(new Color(4.061477f, 2.5092788f, 1.7816858f, 0f), true, true, false);
        public FloatRangeParameter _clipPlanes = new(new Vector2(41f, 72f), .3f, 1000f);
        public Vector2Parameter _sunPoint = new(new Vector2(-2.63f, 1.23f));
        public ClampedFloatParameter _sunSize = new(1.85f, 0f, 10f);
        public ClampedFloatParameter _sunPower = new(1f, .1f, 10f);
        public ClampedFloatParameter _sunAlphaGain = new(.001f, 0f, .25f);

        // 원본 셰이더 전역 이름을 그대로 쓴다. SunFogSunAplhaGain의 오타도 원본 그대로다.
        static readonly int _fogColorId = Shader.PropertyToID("SunFogColor");
        static readonly int _sunColorId = Shader.PropertyToID("SunFogSunColor");
        static readonly int _nearDistanceId = Shader.PropertyToID("SunFogNearDistance");
        static readonly int _farDistanceId = Shader.PropertyToID("SunFogFarDistance");
        static readonly int _sunPointId = Shader.PropertyToID("SunFogSunPoint");
        static readonly int _sunSizeId = Shader.PropertyToID("SunFogSunSize");
        static readonly int _sunPowerId = Shader.PropertyToID("SunFogSunPower");
        static readonly int _sunAlphaGainId = Shader.PropertyToID("SunFogSunAplhaGain");

        public override void Override(VolumeComponent state, float interpFactor)
        {
            base.Override(state, interpFactor);
            var value = (OriginalSunFog)state;
            Shader.SetGlobalColor(_fogColorId, value._fogColor.value);
            Shader.SetGlobalColor(_sunColorId, value._sunColor.value);
            Shader.SetGlobalFloat(_nearDistanceId, value._clipPlanes.value.x);
            Shader.SetGlobalFloat(_farDistanceId, value._clipPlanes.value.y);
            Shader.SetGlobalVector(_sunPointId, value._sunPoint.value);
            Shader.SetGlobalFloat(_sunSizeId, value._sunSize.value);
            Shader.SetGlobalFloat(_sunPowerId, value._sunPower.value);
            Shader.SetGlobalFloat(_sunAlphaGainId, value._sunAlphaGain.value);
        }
    }
}
