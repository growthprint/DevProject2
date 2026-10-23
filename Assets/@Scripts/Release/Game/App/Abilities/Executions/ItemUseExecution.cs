#nullable enable
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원본 UsageUtilities.Use의 소비품 효과다. 아이템의 회복, 에너지, 수분 값과 플레이어 피해 배율을 SetByCaller로 받는다.
    // 양수 회복은 최대 체력까지 더하고, 음수 회복(원본 Drug.Heal → Health.Hurt)은 피해 실행과 같은 방어와 최소 1 규칙을 거친 받은 피해로 넘긴다.
    [CreateAssetMenu(menuName = "GAS/Executions/Item Use", fileName = "EX_ItemUse")]
    internal sealed class ItemUseExecution : GameplayEffectExecution
    {
        public override void Execute(in GameplayEffectExecutionContext context, GameplayEffectExecutionOutput output)
        {
            // 거는 쪽(PlayerVitals)이 네 값을 늘 함께 넣으므로 값이 없을 때를 따로 다루지 않는다.
            GameplayEffectSpec spec = context.Spec;
            float heal = spec.GetSetByCaller(CharacterTags.DataHeal);
            float energy = spec.GetSetByCaller(CharacterTags.DataEnergy);
            float water = spec.GetSetByCaller(CharacterTags.DataWater);
            float factor = spec.GetSetByCaller(CharacterTags.DataDamageFactor);
            if (heal >= 0f)
                output.Add(CharacterAttributes.CurrentHealth, AttributeModifierOp.Override, SurvivalRules.Healed(
                    context.TargetBase(CharacterAttributes.CurrentHealth), heal, context.TargetValue(CharacterAttributes.HealGain),
                    context.TargetValue(CharacterAttributes.MaxHealth)));
            // 원작 Drug.cs:101-107은 기본값 DamageInfo로 Health.Hurt를 부른다. 그래서 난이도 배율 뒤에 관통 0의 몸통 방어 계수,
            // 물리 원소 계수, 최소 1 규칙을 받는다. 치명 확률 0이고 공격자가 없어 머리 방어와 AI끼리의 비율은 쓰지 않는다.
            else output.Add(CharacterAttributes.IncomingDamage, AttributeModifierOp.Add, DamageRules.HurtDamage(-heal * factor,
                DamageRules.ArmorFactor(context.TargetValue(CharacterAttributes.BodyArmor), 0f),
                context.TargetValue(CharacterAttributes.ElementFactorPhysics)));
            float foodGain = context.TargetValue(CharacterAttributes.FoodGain);
            output.Add(CharacterAttributes.CurrentEnergy, AttributeModifierOp.Override, SurvivalRules.Fed(
                context.TargetBase(CharacterAttributes.CurrentEnergy), energy, foodGain, context.TargetValue(CharacterAttributes.MaxEnergy)));
            output.Add(CharacterAttributes.CurrentWater, AttributeModifierOp.Override, SurvivalRules.Fed(
                context.TargetBase(CharacterAttributes.CurrentWater), water, foodGain, context.TargetValue(CharacterAttributes.MaxWater)));
        }
    }
}
