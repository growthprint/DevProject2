#nullable enable
using System;
using UnityEngine;

namespace Game.GAS
{
    // 수정자 하나의 크기 정의다. ScalableFloat는 _value 그대로, AttributeBased는 _value × 어트리뷰트 현재값,
    // SetByCaller는 spec의 호출자 값이고 없으면 효과 에셋의 기본값, CustomCalculation은 _value × 계산 에셋 결과다. 층 수는 호출자가 곱한다.
    [Serializable]
    public sealed class GameplayEffectMagnitude
    {
        [SerializeField] GameplayMagnitudeKind _kind;
        [SerializeField] float _value = 1f;
        [SerializeField] GameplayAttributeName _attribute;
        [SerializeField] AttributeCaptureSource _captureFrom = AttributeCaptureSource.Target;
        [SerializeField] GameplayTagName _setByCaller;
        [SerializeField] GameplayModMagnitudeCalculation? _calculation;
        // 이름을 처음 읽을 때 한 번만 핸들로 바꾼다. 못 바꾼 이름도 다시 찾지 않도록 시도 여부를 따로 둔다.
        [NonSerialized] bool _resolved;
        [NonSerialized] GameplayAttribute _resolvedAttribute;
        [NonSerialized] GameplayTag _resolvedTag;

        public GameplayEffectMagnitude()
        {
        }

        GameplayEffectMagnitude(GameplayMagnitudeKind kind, float value, string attribute, AttributeCaptureSource captureFrom, string tag)
        {
            _kind = kind;
            _value = value;
            _attribute = new GameplayAttributeName(attribute);
            _captureFrom = captureFrom;
            _setByCaller = new GameplayTagName(tag);
        }

        public static GameplayEffectMagnitude Scalable(float value) =>
            new GameplayEffectMagnitude(GameplayMagnitudeKind.ScalableFloat, value, "", AttributeCaptureSource.Target, "");

        public static GameplayEffectMagnitude AttributeBased(GameplayAttribute attribute, float coefficient, AttributeCaptureSource from) =>
            new GameplayEffectMagnitude(GameplayMagnitudeKind.AttributeBased, coefficient, attribute.Key, from, "");

        public static GameplayEffectMagnitude SetByCaller(GameplayTag tag) =>
            new GameplayEffectMagnitude(GameplayMagnitudeKind.SetByCaller, 1f, "", AttributeCaptureSource.Target, tag.Name);

        public GameplayMagnitudeKind Kind => _kind;
        public float Value => _value;

        // 시전자, 어트리뷰트 이름, 계산 에셋, SetByCaller 값은 제작 데이터와 호출 코드가 늘 채운다고 본다.
        internal float Evaluate(in GameplayEffectSpec spec, AbilitySystem target)
        {
            Resolve();
            switch (_kind)
            {
                case GameplayMagnitudeKind.ScalableFloat:
                    return _value;
                case GameplayMagnitudeKind.AttributeBased:
                {
                    AbilitySystem? owner = _captureFrom == AttributeCaptureSource.Target ? target : spec.Source;
                    return _value * owner!.Attributes.GetCurrent(_resolvedAttribute);
                }
                case GameplayMagnitudeKind.SetByCaller:
                    // 값이 없을 때의 0은 제작 데이터 결함이고 에셋 계약 테스트가 막아 따로 거르지 않는다.
                    return spec.GetSetByCaller(_resolvedTag);
                case GameplayMagnitudeKind.CustomCalculation:
                    return _value * _calculation!.Calculate(spec, target);
                default:
                    return 0f;
            }
        }

        void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            if (!_attribute.IsEmpty) _attribute.TryResolve(out _resolvedAttribute);
            if (!_setByCaller.IsEmpty) _setByCaller.TryResolve(out _resolvedTag);
        }

#if UNITY_EDITOR
        internal void ClearResolved() => _resolved = false;
#endif
    }
}
