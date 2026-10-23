#nullable enable annotations
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Rendering
{
    // 원본 SodaCraft.EdgeLight 볼륨의 윤곽광 값이다. 원본처럼 _enable과 관계없이 볼륨이 섞은 state 값을
    // 원본과 같은 이름의 전역 셰이더 값으로 넣고, DuckovEdgeLight 셰이더가 읽는다.
    [Serializable, VolumeComponentMenu("Game/Original Edge Light")]
    public sealed class OriginalEdgeLight : VolumeComponent
    {
        public BoolParameter _enable = new(false);
        public Vector2Parameter _direction = new(new Vector2(-1f, 1f));
        public ClampedFloatParameter _edgeLightWidth = new(.001f, 0f, .05f);
        public ClampedFloatParameter _edgeLightFix = new(.001f, 0f, .05f);
        public ClampedFloatParameter _edgeLightClampDistance = new(.001f, .001f, 1f);
        public ColorParameter _edgeLightColor = new(Color.white, true, false, false);
        public ClampedFloatParameter _blendScreenColor = new(1f, 0f, 1f);

        static readonly int _directionId = Shader.PropertyToID("_EdgeLightDirection");
        static readonly int _widthId = Shader.PropertyToID("_EdgeLightWidth");
        static readonly int _fixId = Shader.PropertyToID("_EdgeLightFix");
        static readonly int _clampDistanceId = Shader.PropertyToID("_EdgeLightClampDistance");
        static readonly int _colorId = Shader.PropertyToID("_EdgeLightColor");
        static readonly int _blendScreenColorId = Shader.PropertyToID("_EdgeLightBlendScreenColor");

        public override void Override(VolumeComponent state, float interpFactor)
        {
            base.Override(state, interpFactor);
            var value = (OriginalEdgeLight)state;
            Shader.SetGlobalVector(_directionId, value._direction.value);
            Shader.SetGlobalFloat(_widthId, value._edgeLightWidth.value);
            Shader.SetGlobalFloat(_fixId, value._edgeLightFix.value);
            Shader.SetGlobalFloat(_clampDistanceId, value._edgeLightClampDistance.value);
            Shader.SetGlobalColor(_colorId, value._edgeLightColor.value);
            Shader.SetGlobalFloat(_blendScreenColorId, value._blendScreenColor.value);
        }
    }
}
