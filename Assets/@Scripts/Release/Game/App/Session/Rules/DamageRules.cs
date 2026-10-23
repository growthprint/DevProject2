#nullable enable

namespace Game.App
{
    // 원본 Health.Hurt와 난이도 규칙의 피해 계산이다. 상태가 없어 세션 소유자와 월드 코드가 같은 규칙을 함께 쓴다.
    internal static class DamageRules
    {
        // 원본 LevelConfig.isRaidMap이 1인 씬인지 본다. GroundZero와 튜토리얼 Level_Guide는 1이고,
        // 원본 Base.unity와 Prologue_Main.unity는 0이다.
        internal static bool IsRaidMap(GameScene scene) => scene == GameScene.ZeroZone || scene == GameScene.Tutorial;

        // 원본 Health.Hurt의 사망 규칙이다. 체력이 0 이하가 되면 죽을 수 있는 캐릭터는 0이 되고, 사망 판정은 월드 게이트가 한다.
        // 원본 메인 캐릭터는 CanDieIfNotRaidMap이 false라 기지와 프롤로그에서는 1로 남는다. 캐릭터 어트리뷰트 세트가 받은 피해를 옮길 때 쓴다.
        internal static float HealthAfterDamage(float health, float damage, bool canDie)
        {
            float remaining = health - damage;
            if (remaining > 0) return remaining;
            return canDie ? 0f : 1f;
        }

        // 원본 Health.Hurt의 방어 배율이다. 방어가 0 이하면 1이고, 아니면 2/(clamp(방어 - 관통, 0, 999) + 2)다.
        internal static float ArmorFactor(float armor, float piercing) =>
            armor > 0f ? 2f / (System.Math.Max(0f, System.Math.Min(999f, armor - piercing)) + 2f) : 1f;

        // 원작 Health.cs:394-409의 마지막 단계다. 방어 계수와 대상 물리 원소 계수를 차례로 곱하고, 0과 1 사이 결과는 1로 올린다.
        // 피해 실행과 음수 회복이 같은 식을 쓴다. 방어 무시 피해는 방어 계수 1을 넘긴다. 원작 원소 항목은 물리 하나에 배율 1이라 대상 계수만 본다.
        internal static float HurtDamage(float damage, float armorFactor, float element)
        {
            damage *= armorFactor;
            damage *= element;
            return damage > 0f && damage < 1f && element > 0f ? 1f : damage;
        }

        // 원작 Health.cs:358의 치명 추첨이다. roll은 맞는 순간 뽑은 0에서 1 사이 난수이고, 확률보다 작아야 치명이다.
        internal static bool RollsCrit(float rate, float roll) => roll < rate;

        // 원작 HealthSimpleBase.cs:39의 단순 체력 대상 치명 추첨이다. 캐릭터와 달리 확률과 같은 난수도 치명이다.
        internal static bool RollsSimpleCrit(float rate, float roll) => roll <= rate;

        // 원작 ItemAgent_Gun.cs:1170의 AI 총 치명 확률이다. 총 CritRate에 1 + 캐릭터 GunCritRateGain + 탄 CritRateGain을 곱한다.
        internal static float GunCritRate(float weaponRate, float characterGain, float ammoGain) => weaponRate * (1f + characterGain + ammoGain);

        // 원작 ItemAgent_MeleeWeapon.cs:190-191의 근접 치명 값이다. 플레이어와 AI가 같은 식을 쓴다.
        internal static float MeleeCritRate(float weaponRate, float characterGain) => weaponRate * (1f + characterGain);
        internal static float MeleeCritFactor(float weaponFactor, float characterGain) => weaponFactor * (1f + characterGain);

        // 아래 세 배율은 원본 난이도 규칙 값이다. 난이도를 아직 고르지 않았으면 null이고 원본 기본 배율 1을 쓴다.
        // DifficultyState는 Unity 객체가 아닌 C# 클래스라 null 조건 연산자를 쓴다.
        internal static float PlayerDamage(float damage, DifficultyState? difficulty) => damage * PlayerDamageFactor(difficulty);

        // 피해 효과에 SetByCaller로 넘기는 플레이어 피해 배율이다. PlayerDamage와 같은 곱셈이 되도록 값만 따로 꺼낸다.
        internal static float PlayerDamageFactor(DifficultyState? difficulty) => difficulty?.DamageFactorToPlayer ?? 1f;

        internal static float EnemyHealth(float health, DifficultyState? difficulty) => health * (difficulty?.EnemyHealthFactor ?? 1f);

        internal static float Recoil(DifficultyState? difficulty) => difficulty?.RecoilMultiplier ?? 1f;
    }
}
