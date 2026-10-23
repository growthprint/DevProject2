#nullable enable annotations
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.App
{
    // 원본 SodaCraft LightControl 볼륨을 URP에서 재현한다. 볼륨으로 섞인 환경광, 안개색, 태양광 값을 렌더 상태에 적용한다.
    [Serializable, VolumeComponentMenu("Game/Original Light Control")]
    public sealed class OriginalLightControl : VolumeComponent
    {
        public BoolParameter _enable = new(false);
        public ColorParameter _skyColor = new(Color.black, true, true, false);
        public ColorParameter _equatorColor = new(Color.black, true, true, false);
        public ColorParameter _groundColor = new(Color.black, true, true, false);
        public ColorParameter _sunColor = new(Color.white, true, true, false);
        public ColorParameter _fowColor = new(Color.white, true, true, false);
        public ColorParameter _sodaLightTint = new(Color.white, true, true, false);
        public MinFloatParameter _sunIntensity = new(1f, 0f);
        // 원본은 이 값을 태양의 UmbraSoftShadows.profile.contactStrength에 쓴다(SodaCraft/LightControl.cs:77-85).
        // 이 프로젝트에는 Umbra가 없어 값만 보존하며, 의미가 다른 URP shadowStrength로 대신 적용하지 않는다.
        public ClampedFloatParameter _sunShadowHardness = new(.96f, 0f, 1f);
        public Vector3Parameter _sunRotation = new(new Vector3(59f, 168f, 0f));
        static readonly int _fogColorId = Shader.PropertyToID("_SodaUnknowColor");
        static readonly int _lightTintId = Shader.PropertyToID("SodaPointLight_EnviromentTint");

        // 원본 LightControl.Override(:51-87)처럼 enable과 무관하게 지금까지 섞인 state를 전역 렌더 상태에 바로 쓰고,
        // 씬 전환 때 되돌리지 않는다. 하위 씬마다 OriginalSceneLighting이 RenderSettings.sun을 바꾸므로 원본과 달리 태양을 캐시하지 않는다.
        public override void Override(VolumeComponent state, float interpFactor)
        {
            base.Override(state, interpFactor);
            // Edit Mode에서도 기본 프로필 계산과 씬 뷰 렌더 때 볼륨이 평가된다. 그때 열린 씬의 환경광과 태양을 덮어쓰면
            // 씬 빌더가 그 값을 저장하므로 플레이 중에만 적용한다.
            if (!Application.isPlaying) return;
            var value = (OriginalLightControl)state;
            RenderSettings.ambientSkyColor = value._skyColor.value;
            RenderSettings.ambientEquatorColor = value._equatorColor.value;
            RenderSettings.ambientGroundColor = value._groundColor.value;
            Shader.SetGlobalColor(_fogColorId, value._fowColor.value);
            Shader.SetGlobalColor(_lightTintId, value._sodaLightTint.value);
            Light sun = RenderSettings.sun;
            if (sun == null) return;
            sun.color = value._sunColor.value;
            sun.intensity = value._sunIntensity.value;
            sun.transform.rotation = Quaternion.Euler(value._sunRotation.value);
        }
    }
}
