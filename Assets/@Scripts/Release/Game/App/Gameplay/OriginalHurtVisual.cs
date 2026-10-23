#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원본 HurtVisual처럼 피격 밝기를 1에서 시작해 초당 8씩 0까지 줄인다.
    // 공유 재질을 복제하지 않도록 MaterialPropertyBlock으로 렌더러마다 값을 넣는다.
    internal sealed class OriginalHurtVisual : MonoBehaviour
    {
        static readonly int _hurtValueId = Shader.PropertyToID("_HurtValue");
        // 피격 밝기를 적용할 렌더러 목록이다. 새 배열을 만들지 않도록 같은 목록을 다시 채우고,
        // 입자와 궤적 렌더러는 채울 때 한 번만 걸러 Apply가 매 프레임 타입을 다시 보지 않게 한다.
        readonly List<Renderer> _renderers = new();
        // 피격 밝기를 받지 않는 입자와 궤적 렌더러를 고른다. 캡처 없는 정적 대리자라 호출마다 할당하지 않는다.
        static readonly System.Predicate<Renderer> _isEffectRenderer = renderer => renderer is ParticleSystemRenderer || renderer is TrailRenderer;
        // 네이티브 객체는 MonoBehaviour 생성자에서 만들면 안 되므로 처음 적용할 때 만든다.
        MaterialPropertyBlock? _block;
        float _value;
        // 원본 HurtVisual.SetRenderers처럼 외형이나 무기 표시가 바뀐 뒤에만 목록을 다시 채운다. 첫 피격에도 채우도록 켜 둔 채 시작한다.
        bool _renderersChanged = true;
        // 외형 적용과 무기 교체가 부른다. 다음 피격 때 바뀐 계층에서 렌더러를 다시 모은다.
        internal void MarkRenderersChanged() => _renderersChanged = true;
        internal void Flash()
        {
            if (_renderersChanged)
            {
                _renderersChanged = false;
                GetComponentsInChildren(true, _renderers);
                _renderers.RemoveAll(_isEffectRenderer);
            }
            _value = 1; Apply();
        }
        void Update()
        {
            if(_value<=0)return;
            _value=Mathf.Max(0,_value-Time.unscaledDeltaTime*8);Apply();
        }
        void Apply()
        {
            _block ??= new MaterialPropertyBlock();
            foreach(Renderer renderer in _renderers)
            {
                // 입자와 궤적 렌더러는 Flash가 목록에서 걸렀으므로 파괴된 렌더러만 건너뛴다.
                // 바뀐 외형에서 빠진 파츠도 여기서 걸러져, 목록을 다시 채우기 전까지 남은 렌더러만 칠한다.
                if(renderer==null)continue;
                // 파츠 틴트와 같은 렌더러 전체 블록을 읽고 피격 밝기만 덮어써서 틴트 색을 지우지 않는다.
                renderer.GetPropertyBlock(_block);_block.SetFloat(_hurtValueId,_value);renderer.SetPropertyBlock(_block);
            }
        }
        // 풀로 돌아가 꺼진 모델이나 사망 모델에 피격 밝기가 남지 않게 0으로 되돌린다.
        void OnDisable(){_value=0;Apply();}
    }
}
