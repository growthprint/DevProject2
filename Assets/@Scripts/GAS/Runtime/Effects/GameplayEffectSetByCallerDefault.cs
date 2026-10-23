#nullable enable
using System;
using UnityEngine;

namespace Game.GAS
{
    // 호출자가 값을 주지 않은 SetByCaller를 실행이 읽을 때 효과 에셋이 대신 내주는 값이다.
    // 원작 버프 프리팹처럼 피해량 같은 고정 값을 에셋에 담는다. UE에는 에셋 단위 기본값이 없고, 가장 가까운 것은 읽는 쪽의 DefaultIfNotFound다.
    [Serializable]
    public sealed class GameplayEffectSetByCallerDefault
    {
        [SerializeField] GameplayTagName _tag;
        [SerializeField] float _magnitude;
        [NonSerialized] bool _resolved;
        [NonSerialized] GameplayTag _resolvedTag;

        // 처음 읽을 때 한 번 태그로 바꾼다. 효과 에셋의 이름은 형식에 맞는 SetByCaller 태그라고 본다.
        public GameplayTag Tag
        {
            get
            {
                if (_resolved) return _resolvedTag;
                _resolved = true;
                _tag.TryResolve(out _resolvedTag);
                return _resolvedTag;
            }
        }

        public float Magnitude => _magnitude;

#if UNITY_EDITOR
        internal void ClearResolved() => _resolved = false;
#endif
    }
}
