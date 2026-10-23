#nullable enable
using System;
using UnityEngine;

namespace Game.GAS
{
    // 효과 하나가 어트리뷰트 하나를 바꾸는 규칙이다. 원작 ModifierDescription의 key, type, value, overrideOrder에 해당한다.
    [Serializable]
    public sealed class GameplayModifierInfo
    {
        [SerializeField] GameplayAttributeName _attribute;
        [SerializeField] AttributeModifierOp _op;
        [SerializeField] bool _overrideOrder;
        [SerializeField] int _order;
        [SerializeField] GameplayEffectMagnitude _magnitude = new GameplayEffectMagnitude();
        [NonSerialized] bool _resolved;
        [NonSerialized] GameplayAttribute _resolvedAttribute;

        public GameplayModifierInfo()
        {
        }

        public GameplayModifierInfo(GameplayAttribute attribute, AttributeModifierOp op, GameplayEffectMagnitude magnitude)
        {
            _attribute = new GameplayAttributeName(attribute.Key);
            _op = op;
            _magnitude = magnitude;
        }

        // 처음 읽을 때 한 번 핸들로 바꾼다. 효과 에셋의 키는 등록된 어트리뷰트라고 본다.
        public GameplayAttribute Attribute
        {
            get
            {
                if (_resolved) return _resolvedAttribute;
                _resolved = true;
                _attribute.TryResolve(out _resolvedAttribute);
                return _resolvedAttribute;
            }
        }

        public AttributeModifierOp Op => _op;
        // 원작 Modifier.Order처럼 덮어쓰기가 없으면 연산 종류의 값이 순서다.
        public int Order => _overrideOrder ? _order : (int)_op;
        public GameplayEffectMagnitude Magnitude => _magnitude;

#if UNITY_EDITOR
        internal void ClearResolved()
        {
            _resolved = false;
            _magnitude.ClearResolved();
        }
#endif
    }
}
