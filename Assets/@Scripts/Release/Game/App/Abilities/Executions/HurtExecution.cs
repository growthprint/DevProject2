#nullable enable
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원본 Health.Hurt의 피해 계산을 받은 피해(IncomingDamage)로 넘긴다. 피해량은 SetByCaller(Data.Damage)로 받고,
    // 난이도 배율(Data.DamageFactor)은 이미 곱하지 않은 피해에만 함께 넘긴다. 방어(치명이면 머리, 아니면 몸통)와 물리 원소 계수를 곱하며,
    // 기본값(방어 0, 계수 1)에서는 곱셈이 값을 바꾸지 않는다. 사망 바닥 규칙은 캐릭터 어트리뷰트 세트가 적용한다.
    // AI끼리의 aiCombatFactor 비율(Data.CombatFactor)은 원작처럼 방어 계수와 함께 곱한다.
    [CreateAssetMenu(menuName = "GAS/Executions/Hurt", fileName = "EX_Hurt")]
    internal sealed class HurtExecution : GameplayEffectExecution
    {
        // 원작 DamageInfo.crit > 0을 나타내는 효과 문맥 비트다. 피해를 거는 쪽이 문맥에 싣고, 피해 숫자를 띄우는 큐도 같은 비트를 읽는다.
        // SetByCaller가 아니라 문맥 비트라 SetByCaller 칸과 태그 등록이 필요 없다.
        internal const int CritFlag = 1;
        // 원작 DamageInfo.ignoreArmor를 나타내는 효과 문맥 비트다. 기아 버프가 문맥에 싣고, 이어 거는 피해 효과가 부모 문맥을 그대로 받는다.
        // 그래서 효과 에셋에 칸을 더하지 않고도 버프 피해만 방어를 무시한다.
        internal const int IgnoreArmorFlag = 2;

        public override void Execute(in GameplayEffectExecutionContext context, GameplayEffectExecutionOutput output)
        {
            // 피해량은 거는 코드나 효과 에셋의 SetByCaller 기본값이 늘 주므로 값이 없을 때를 따로 다루지 않는다.
            float damage = context.Spec.GetSetByCaller(CharacterTags.DataDamage);
            // 배율은 이미 곱하지 않은 피해에만 따라오므로 있을 때만 곱한다.
            if (context.Spec.TryGetSetByCaller(CharacterTags.DataDamageFactor, out float factor)) damage *= factor;
            // 원작 Health.cs:369처럼 방어 무시 피해는 방어 단계 전체를 건너뛴다. 난이도 배율, 원소 계수, 최소 1 규칙은 그대로 받는다.
            bool ignoreArmor = context.Spec.Context.HasFlag(IgnoreArmorFlag);
            // 원작 Health.cs:371처럼 치명 피해는 머리 방어로, 그 밖의 피해는 몸통 방어로 막는다.
            float armor = context.TargetValue(context.Spec.Context.HasFlag(CritFlag) ? CharacterAttributes.HeadArmor : CharacterAttributes.BodyArmor);
            // 원작 Health.cs:380-394처럼 관통을 뺀 방어 계수(num5)에 AI끼리의 aiCombatFactor 비율을 곱한다.
            // 관통과 비율은 넘긴 피해에만 있으므로 있을 때만 쓴다. 방어 무시 피해는 둘 다 건너뛰어 계수가 1이다.
            float armorFactor = !ignoreArmor && armor > 0f
                ? DamageRules.ArmorFactor(armor, context.Spec.GetSetByCaller(CharacterTags.DataArmorPiercing))
                : 1f;
            if (!ignoreArmor && context.Spec.TryGetSetByCaller(CharacterTags.DataCombatFactor, out float combatFactor))
                armorFactor *= combatFactor;
            // 방어 계수, 원소 계수, 최소 1 규칙은 음수 회복과 같은 식(DamageRules.HurtDamage)으로 계산한다.
            float element = context.TargetValue(CharacterAttributes.ElementFactorPhysics);
            output.Add(CharacterAttributes.IncomingDamage, AttributeModifierOp.Add, DamageRules.HurtDamage(damage, armorFactor, element));
        }
    }
}
