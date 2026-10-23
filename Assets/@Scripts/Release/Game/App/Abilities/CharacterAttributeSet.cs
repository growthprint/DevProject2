#nullable enable
using System;
using Game.GAS;

namespace Game.App
{
    // 모든 캐릭터(플레이어, 적, 이후 펫과 포탑)가 쓰는 원작 캐릭터 어트리뷰트 세트다. 원작처럼 캐릭터마다 같은 스탯 목록을 갖는다.
    // 자동 clamp는 하지 않는다. 생존 규칙(SurvivalRules, DamageRules)과 결과가 비트 단위로 같아야 하기 때문이다.
    internal sealed class CharacterAttributeSet : AttributeSet
    {
        internal CharacterAttributeSet() : base(CharacterAttributes.All)
        {
        }

        // 원작 SetItem처럼 캐릭터 정의의 스탯으로 Base를 채운다. 어트리뷰트 키가 곧 스탯 키이고, 정의에 없는 키는 대체값을 쓴다.
        // 세트 바깥에서 Base를 바꾸는 공개 경로는 AbilitySystem.SetBaseValue뿐이라 시스템을 받는다.
        internal static void SeedStats(AbilitySystem system, OriginalItemDefinition character)
        {
            foreach ((GameplayAttribute attribute, float fallback) in CharacterAttributes.Stats)
                system.SetBaseValue(attribute, character.Stat(attribute.Key, fallback));
        }

        // 정의를 아직 받지 못한 새 시스템을 대체값으로 채운다. 정의 없이 오는 피해 명령도 방어 0, 원소 계수 1로 계산하게 한다.
        // 새 시스템의 체력은 0이라 사망 규칙대로 State.Dead도 단다. 체력이 바뀌지 않으면 아래 훅이 불리지 않아, 체력 0을 불러와도 태그가 맞는다.
        internal static void SeedDefaults(AbilitySystem system)
        {
            foreach ((GameplayAttribute attribute, float fallback) in CharacterAttributes.Stats)
                system.SetBaseValue(attribute, fallback);
            if (system.GetCurrent(CharacterAttributes.CurrentHealth) <= 0f) system.AddLooseTag(CharacterTags.Dead);
        }

        // 플레이어와 적이 같은 규칙으로 캐릭터 시스템을 만든다. 정의를 받기 전에도 피해 명령을 계산하도록 스탯 대체값으로 채운다.
        internal static AbilitySystem CreateSystem(UnityEngine.Object owner)
        {
            var system = new AbilitySystem(new CharacterAttributeSet(), owner);
            SeedDefaults(system);
            return system;
        }

        // 메타 어트리뷰트를 현재값으로 옮긴다. 받은 피해는 원작 Health.Hurt의 사망 규칙(DamageRules.HealthAfterDamage)을 따르고,
        // State.CannotDie 태그가 있으면 1에서 멈춘다. 회복은 원작 AddHealth처럼 최대 체력에서 멈춘다.
        protected override void PostGameplayEffectExecute(in GameplayModifierEvaluation data)
        {
            if (data.Attribute == CharacterAttributes.IncomingDamage)
            {
                float damage = GetBase(CharacterAttributes.IncomingDamage);
                SetBaseFromHook(CharacterAttributes.IncomingDamage, 0f);
                // 효과 실행은 시스템에 묶인 세트에서만 일어나므로 시스템이 늘 있다.
                bool canDie = !AbilitySystem!.Tags.HasTag(CharacterTags.CannotDie);
                SetBaseFromHook(CharacterAttributes.CurrentHealth,
                    DamageRules.HealthAfterDamage(GetBase(CharacterAttributes.CurrentHealth), damage, canDie));
            }
            else if (data.Attribute == CharacterAttributes.IncomingHeal)
            {
                float heal = GetBase(CharacterAttributes.IncomingHeal);
                SetBaseFromHook(CharacterAttributes.IncomingHeal, 0f);
                SetBaseFromHook(CharacterAttributes.CurrentHealth,
                    Math.Min(GetCurrent(CharacterAttributes.MaxHealth), GetBase(CharacterAttributes.CurrentHealth) + heal));
            }
        }

        // 체력이 0 이하가 되면 State.Dead를 붙이고, 다시 0보다 커지면 뗀다. 전투 월드는 이 태그로 사망을 판정한다.
        protected override void PostAttributeChange(GameplayAttribute attribute, float oldCurrent, float newCurrent)
        {
            if (attribute != CharacterAttributes.CurrentHealth) return;
            // 값 변화는 시스템에 묶인 세트의 작업에서만 일어나므로 시스템이 늘 있다.
            AbilitySystem system = AbilitySystem!;
            bool dead = system.Tags.HasTagExact(CharacterTags.Dead);
            if (newCurrent <= 0f && !dead) system.AddLooseTag(CharacterTags.Dead);
            else if (newCurrent > 0f && dead) system.RemoveLooseTag(CharacterTags.Dead);
        }
    }
}
