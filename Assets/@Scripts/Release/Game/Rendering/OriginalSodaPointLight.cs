#nullable enable annotations
using UnityEngine;

namespace Game.Rendering
{
    // 원본 SodaPointLight의 광원 값을 광원 렌더러의 MaterialPropertyBlock에 넣는다. 원본처럼 편집 중에도 값을 반영하고,
    // SodaLight 렌더러 기능이 재정의 재질로 그릴 때 이 블록을 읽는다.
    [ExecuteAlways]
    public sealed class OriginalSodaPointLight : MonoBehaviour
    {
        [SerializeField] Renderer? _lightRenderer;
        [SerializeField, Range(0f, 1f)] float _hardness = .5f;
        [SerializeField, Range(1f, 5f)] float _fallOff = 1f;
        [SerializeField, ColorUsage(false, true)] Color _lightColor = Color.white;
        [SerializeField] bool _enviromentTint;

        static readonly int _lightColorId = Shader.PropertyToID("_LightColor");
        static readonly int _hardnessId = Shader.PropertyToID("_Hardness");
        static readonly int _fallOffId = Shader.PropertyToID("_FallOff");
        static readonly int _enviromentTintId = Shader.PropertyToID("_EnviromentTintOn");
        MaterialPropertyBlock? _block;

        void OnEnable() => Sync();

#if UNITY_EDITOR
        // 에디터 전용 콜백이라 플레이어 빌드에는 넣지 않는다.
        void OnValidate() => Sync();
#endif

        // 제작 때 Editor의 ComponentAuthoring.ConfigureSodaPointLight가 필드를 쓰고 이 메서드를 이름으로 찾아 부른다.
        // 이름을 바꾸면 그쪽도 함께 맞춘다.
        void Sync()
        {
            if (_lightRenderer == null) return;
            _block ??= new MaterialPropertyBlock();
            _block.SetFloat(_hardnessId, _hardness);
            _block.SetFloat(_fallOffId, _fallOff);
            _block.SetColor(_lightColorId, _lightColor);
            _block.SetFloat(_enviromentTintId, _enviromentTint ? 1f : 0f);
            _lightRenderer.SetPropertyBlock(_block);
        }
    }
}
