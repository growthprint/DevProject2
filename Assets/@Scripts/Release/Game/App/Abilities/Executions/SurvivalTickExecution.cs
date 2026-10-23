#nullable enable
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원본 CharacterMainControl.TickVariables의 1초 틱 한 번이다. 계산은 SurvivalRules.Tick이 하고, 결과를 정확한 값으로 대입한다.
    // 출격 맵 여부는 효과를 거는 쪽이 SetByCaller(Data.RaidMap, 1 또는 0)로 넘긴다.
    [CreateAssetMenu(menuName = "GAS/Executions/Survival Tick", fileName = "EX_SurvivalTick")]
    internal sealed class SurvivalTickExecution : GameplayEffectExecution
    {
        public override void Execute(in GameplayEffectExecutionContext context, GameplayEffectExecutionOutput output)
        {
            // 값이 없으면 비출격 맵으로 본다. 거는 쪽은 늘 1이나 0을 넣는다.
            bool raidMap = context.Spec.TryGetSetByCaller(CharacterTags.DataRaidMap, out float raid) && raid > 0f;
            (float energy, float water) = SurvivalRules.Tick(context.TargetBase(CharacterAttributes.CurrentEnergy),
                context.TargetBase(CharacterAttributes.CurrentWater), context.TargetValue(CharacterAttributes.MaxEnergy),
                context.TargetValue(CharacterAttributes.MaxWater), context.TargetValue(CharacterAttributes.EnergyCost),
                context.TargetValue(CharacterAttributes.WaterCost), context.TargetValue(CharacterAttributes.WaterEnergyRecoverMultiplier), raidMap);
            output.Add(CharacterAttributes.CurrentEnergy, AttributeModifierOp.Override, energy);
            output.Add(CharacterAttributes.CurrentWater, AttributeModifierOp.Override, water);
        }
    }
}
