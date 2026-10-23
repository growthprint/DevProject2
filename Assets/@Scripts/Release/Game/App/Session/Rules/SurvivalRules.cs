#nullable enable
using System;

namespace Game.App
{
    // 원본 CharacterMainControl.TickVariables와 UsageUtilities.Use의 생존 수치 계산이다. 상태가 없어 GAS 실행 계산
    // (SurvivalTickExecution, ItemUseExecution)이 캐릭터 어트리뷰트 값을 넘겨 쓰고, 결과 값의 검증과 보관은 PlayerVitals가 맡는다.
    // 피해와 사망 규칙은 DamageRules가 맡는다.
    internal static class SurvivalRules
    {
        // 원본 AddEnergy와 AddWater처럼 0과 최대치 사이로 고정한다. Math.Clamp는 최대치가 음수인 잘못된 정의에서 던지므로 쓰지 않는다.
        internal static float Bounded(float value, float maximum) => Math.Max(0f, Math.Min(maximum, value));

        // 원본 TickVariables의 비출격 맵 회복 한 번이다. 더한 값이 최대치의 25%보다 낮으면 25%, 최대치를 넘으면 최대치다.
        internal static float Recovered(float value, float maximum, float step)
        {
            const float recoverFloorRatio = .25f;
            float next = value + step;
            return next < maximum * recoverFloorRatio ? maximum * recoverFloorRatio : next > maximum ? maximum : next;
        }

        // 원본 CharacterMainControl.TickVariables의 1초 틱 한 번이 만드는 에너지와 수분이다. 기지와 프롤로그에서는
        // 10×WaterEnergyRecoverMultiplier/60씩 회복하되, 최대치의 25%보다 낮으면 25%로 올리고 최대치를 넘지 않는다.
        // 출격 맵에서는 EnergyCost/60과 WaterCost/60씩 줄이고 0 아래로 내리지 않는다. 인수는 원작 캐릭터 스탯의 현재값이다.
        internal static (float Energy, float Water) Tick(float energy, float water, float maxEnergy, float maxWater, float energyCost,
            float waterCost, float recoverMultiplier, bool raidMap)
        {
            // 원본의 분당 수치를 1초 틱 한 번의 값으로 바꾸는 나눗수다.
            const float secondsPerMinute = 60f;
            if (raidMap)
                return (Math.Max(0f, energy - energyCost / secondsPerMinute), Math.Max(0f, water - waterCost / secondsPerMinute));
            float step = 10f * recoverMultiplier / secondsPerMinute;
            return (Recovered(energy, maxEnergy, step), Recovered(water, maxWater, step));
        }

        // 원본 Drug의 양수 회복이다. Health.AddHealth(값 × (1 + HealGain))처럼 최대 체력까지 더한다. HealGain이 0이면
        // 곱하기가 1이라 값이 그대로다. 음수 회복은 피해라 DamageRules와 피해 실행 계산이 맡는다.
        internal static float Healed(float health, float heal, float healGain, float maximum) => Math.Min(maximum, health + heal * (1f + healGain));

        // 원작 UsageUtilities.IsUsable은 사용 행동 하나라도 쓸 수 있으면 참이고, Drug.CheckCanHeal은 체력이 가득이면 회복을 막는다.
        // 이 게임의 소비품 행동은 회복, 에너지와 수분, 이 게임에 없는 상태 이상 제거라서, 회복만 하는 아이템만 가득 찬 체력에서 거절된다.
        internal static bool CanUse(float heal, float energy, float water, float health, float maximum) =>
            heal <= 0f || energy != 0f || water != 0f || health < maximum;

        // 원본 FoodDrink의 에너지와 수분 효과다. 값 × (1 + FoodGain)을 더한 뒤 AddEnergy, AddWater처럼 0과 최대치 사이로 고정한다.
        // 음수 효과도 PlayerState의 비음수 규칙을 지킨다.
        internal static float Fed(float value, float amount, float foodGain, float maximum) => Bounded(value + amount * (1f + foodGain), maximum);
    }
}
