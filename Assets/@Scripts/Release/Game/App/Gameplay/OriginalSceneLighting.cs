#nullable enable annotations
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.App
{
    // 원작 MultiSceneCore가 켠 하위 씬을 SetActiveScene하면 그 씬의 RenderSettings가 걸리듯, 하위 씬 루트가 켜질 때
    // 그 씬의 태양광, 환경광 모드, 반사 설정을 RenderSettings에 적용한다. 값은 제작 때 Editor의 ComponentAuthoring이 쓴다.
    public sealed class OriginalSceneLighting : MonoBehaviour
    {
        // 원본 m_Sun이 없는 하위 씬(기지)은 비워 두고, 그때는 씬의 태양을 그대로 둔다.
        [SerializeField] Light? _sun;
        [SerializeField] AmbientMode _ambientMode;
        // 원본 하위 씬 RenderSettings의 반사 모드, Custom 큐브맵, 반사 세기다. 반사를 저작하지 않은 사용처는 _applyReflection이 거짓이라 건드리지 않는다.
        [SerializeField] bool _applyReflection;
        [SerializeField] DefaultReflectionMode _reflectionMode;
        [SerializeField] Cubemap? _customReflection;
        [SerializeField] float _reflectionIntensity = 1f;
        void OnEnable()
        {
            if (_sun != null) RenderSettings.sun = _sun;
            RenderSettings.ambientMode = _ambientMode;
            if (!_applyReflection) return;
            RenderSettings.defaultReflectionMode = _reflectionMode;
            RenderSettings.customReflectionTexture = _customReflection;
            RenderSettings.reflectionIntensity = _reflectionIntensity;
        }
    }
}
