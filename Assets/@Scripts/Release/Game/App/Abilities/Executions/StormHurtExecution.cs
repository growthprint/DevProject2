#nullable enable
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원작 1111_Buff_Space와 1112_Buff_Space2의 DamageAction이 Health.Hurt로 넘기는 폭풍 피해를 받은 피해(IncomingDamage)로 넘긴다.
    // 피해는 Data.Damage(원작 damageValue 1) × 층 수 × 난이도 배율(Data.DamageFactor, 원작 ignoreDifficulty 0) × 대상 ElementFactor_Space다.
    // 원작 damageInfo.damageValue(0.5와 1)는 DamageAction이 덮어써 쓰이지 않는다. 원작 ignoreArmor 1이라 방어는 곱하지 않고,
    // 원작 Health.Hurt 406~409행처럼 0과 1 사이 결과는 1로 올린다. 사망 바닥 규칙은 캐릭터 어트리뷰트 세트가 적용한다.
    [CreateAssetMenu(menuName = "GAS/Executions/Storm Hurt", fileName = "EX_StormHurt")]
    internal sealed class StormHurtExecution : GameplayEffectExecution
    {
        // 원작 Health.Hurt가 원소 계수를 거친 뒤 0과 1 사이 피해를 올려 맞추는 최소 피해다.
        const float _minimumDamage = 1f;

        public override void Execute(in GameplayEffectExecutionContext context, GameplayEffectExecutionOutput output)
        {
            // 피해량은 효과 에셋의 SetByCaller 기본값이 늘 주므로 값이 없을 때를 따로 다루지 않는다. 원작처럼 층 수를 먼저 곱한다.
            float damage = context.Spec.GetSetByCaller(CharacterTags.DataDamage) * context.StackCount;
            // 이 효과를 거는 곳은 PlayerVitals.TryApplyStormBuff 하나이고 배율을 늘 넘기므로 값이 없을 때를 따로 다루지 않는다.
            damage *= context.Spec.GetSetByCaller(CharacterTags.DataDamageFactor);
            // 원작 원소 계수(공간 1)를 곱한 뒤 대상 계수를 곱하는 순서다. 1을 곱하는 단계는 값을 바꾸지 않아 뺀다.
            float element = context.TargetValue(CharacterAttributes.ElementFactorSpace);
            damage *= element;
            if (RaisesToMinimum(damage, element)) damage = _minimumDamage;
            output.Add(CharacterAttributes.IncomingDamage, AttributeModifierOp.Add, damage);
        }

        // 원작처럼 피해가 0보다 크고 최소 피해보다 작을 때만 올린다. 대상 원소 계수가 0 이하이면 올리지 않는다.
        static bool RaisesToMinimum(float damage, float element) => damage > 0f && damage < _minimumDamage && element > 0f;
    }
}
