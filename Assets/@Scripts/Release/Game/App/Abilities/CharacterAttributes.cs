#nullable enable
using System.Collections.Generic;
using Game.GAS;

namespace Game.App
{
    // 원작 캐릭터 Item(Character_0, 카탈로그 id 0)의 스탯 71개와 현재값 4개, 메타 2개를 GAS 어트리뷰트로 등록한다.
    // 키는 원작 스탯 이름 그대로라 카탈로그 Stat 키와 GE 에셋의 어트리뷰트 이름이 같은 문자열을 쓴다.
    internal static class CharacterAttributes
    {
        // 원작 스탯 키와, 정의에 그 키가 없을 때 쓰는 값이다. 순서는 원작 프리팹의 스탯 순서다. 실제 카탈로그는 71개 키를 모두 갖는다.
        // 대체값은 체력, 에너지, 물, 스태미나, 무게의 최대치와 에너지, 물 소모량, 소지 칸 수, 물 에너지 회복 배율만 0이다.
        // 이동 스탯을 포함한 나머지는 원작 Character_0 값과 같다.
        static readonly (string Key, float Default)[] _statDefaults =
        {
            ("WalkSpeed", 4f), ("RunSpeed", 6f), ("AimTurnSpeed", 1200f), ("TurnSpeed", 200f), ("WalkAcc", 50f), ("RunAcc", 70f),
            ("MaxHealth", 0f), ("MaxEnergy", 0f), ("EnergyCost", 0f), ("MaxWeight", 0f), ("Stamina", 0f), ("StaminaDrainRate", 7f),
            ("StaminaRecoverRate", 20f), ("StaminaRecoverTime", 1.5f), ("HungerDurability", 0f), ("BodyArmor", 0f), ("HeadArmor", 0f),
            ("Attack", 0f), ("MaxWater", 0f), ("WaterCost", 0f), ("GunDamageMultiplier", 1f), ("GunDistanceMultiplier", 1f),
            ("GunShootSpeedMultiplier", 1f), ("ReloadSpeedGain", 0f), ("FoodGain", 0f), ("WaterEnergyRecoverMultiplier", 0f), ("HealGain", 0f),
            ("GunCritRateGain", 0f), ("GunCritDamageGain", 0f), ("GunScatterMultiplier", 1f), ("InventoryCapacity", 0f),
            ("MeleeDamageMultiplier", 1f), ("MeleeCritRateGain", 0f), ("MeleeCritDamageGain", 0f), ("WalkSoundRange", 8f), ("RunSoundRange", 16f),
            ("RecoilControl", 1f), ("GasMask", 0f), ("DashSpeed", 10f), ("DashCanControl", 0f), ("BulletSpeedMultiplier", 1f),
            ("ElementFactor_Physics", 1f), ("ElementFactor_Fire", 1f), ("ElementFactor_Poison", 1f), ("ElementFactor_Electricity", 1f),
            ("ElementFactor_Space", 1f), ("ElementFactor_Ghost", 1f), ("ElementFactor_Ice", 1f), ("Moveability", 1f), ("ViewAngle", 100f),
            ("SenseRange", 1.5f), ("ViewDistance", 37f), ("SoundVisable", 0f), ("HearingAbility", 1f), ("NightVisionAbility", 0f),
            ("NightVisionType", 0f), ("PetCapcity", 1f), ("StormProtection", 0f), ("FireProtection", 0f), ("ElecProtection", 0f),
            ("FishingTime", 1f), ("FishingQualityFactor", 1f), ("WeaponRepairLossFactor", 1f), ("EquipmentRepairLossFactor", 1f),
            ("VisableDistanceFactor", 1f), ("ColdProtection", 0f), ("HeatProtection", 0f), ("HackRangeFactor", 2f), ("HackTimeFactor", 1f),
            ("HackSkillLevel", 0f), ("CanSwim", 0f)
        };

        // 게임 코드가 직접 읽는 스탯이다. 나머지 원작 스탯도 모두 등록되어 GE 에셋에서 키 이름으로 쓸 수 있다.
        // 원작에서 Stamina는 최대 스태미나다.
        internal static GameplayAttribute WalkSpeed { get; } = GameplayAttribute.Register("WalkSpeed");
        internal static GameplayAttribute RunSpeed { get; } = GameplayAttribute.Register("RunSpeed");
        internal static GameplayAttribute AimTurnSpeed { get; } = GameplayAttribute.Register("AimTurnSpeed");
        internal static GameplayAttribute TurnSpeed { get; } = GameplayAttribute.Register("TurnSpeed");
        internal static GameplayAttribute WalkAcc { get; } = GameplayAttribute.Register("WalkAcc");
        internal static GameplayAttribute RunAcc { get; } = GameplayAttribute.Register("RunAcc");
        internal static GameplayAttribute MaxHealth { get; } = GameplayAttribute.Register("MaxHealth");
        internal static GameplayAttribute MaxEnergy { get; } = GameplayAttribute.Register("MaxEnergy");
        internal static GameplayAttribute EnergyCost { get; } = GameplayAttribute.Register("EnergyCost");
        internal static GameplayAttribute MaxWeight { get; } = GameplayAttribute.Register("MaxWeight");
        internal static GameplayAttribute Stamina { get; } = GameplayAttribute.Register("Stamina");
        internal static GameplayAttribute StaminaDrainRate { get; } = GameplayAttribute.Register("StaminaDrainRate");
        internal static GameplayAttribute StaminaRecoverRate { get; } = GameplayAttribute.Register("StaminaRecoverRate");
        internal static GameplayAttribute StaminaRecoverTime { get; } = GameplayAttribute.Register("StaminaRecoverTime");
        internal static GameplayAttribute BodyArmor { get; } = GameplayAttribute.Register("BodyArmor");
        // 원작 Health.HeadArmor다. 피해 실행이 치명 피해의 방어값으로 읽는다.
        internal static GameplayAttribute HeadArmor { get; } = GameplayAttribute.Register("HeadArmor");
        internal static GameplayAttribute MaxWater { get; } = GameplayAttribute.Register("MaxWater");
        internal static GameplayAttribute WaterCost { get; } = GameplayAttribute.Register("WaterCost");
        internal static GameplayAttribute GunDamageMultiplier { get; } = GameplayAttribute.Register("GunDamageMultiplier");
        internal static GameplayAttribute GunDistanceMultiplier { get; } = GameplayAttribute.Register("GunDistanceMultiplier");
        internal static GameplayAttribute ReloadSpeedGain { get; } = GameplayAttribute.Register("ReloadSpeedGain");
        internal static GameplayAttribute FoodGain { get; } = GameplayAttribute.Register("FoodGain");
        internal static GameplayAttribute WaterEnergyRecoverMultiplier { get; } = GameplayAttribute.Register("WaterEnergyRecoverMultiplier");
        internal static GameplayAttribute HealGain { get; } = GameplayAttribute.Register("HealGain");
        // 원작 ItemAgent_Gun과 ItemAgent_MeleeWeapon의 치명 확률 증가와 근접 치명 배율 증가다. 치명 추첨이 읽는다.
        internal static GameplayAttribute GunCritRateGain { get; } = GameplayAttribute.Register("GunCritRateGain");
        internal static GameplayAttribute GunCritDamageGain { get; } = GameplayAttribute.Register("GunCritDamageGain");
        internal static GameplayAttribute GunScatterMultiplier { get; } = GameplayAttribute.Register("GunScatterMultiplier");
        internal static GameplayAttribute InventoryCapacity { get; } = GameplayAttribute.Register("InventoryCapacity");
        internal static GameplayAttribute MeleeDamageMultiplier { get; } = GameplayAttribute.Register("MeleeDamageMultiplier");
        internal static GameplayAttribute MeleeCritRateGain { get; } = GameplayAttribute.Register("MeleeCritRateGain");
        internal static GameplayAttribute MeleeCritDamageGain { get; } = GameplayAttribute.Register("MeleeCritDamageGain");
        // 원작 CharacterSoundMaker의 걷기와 달리기 발소리 반경이다. 발소리를 AI에게 알릴 때 읽는다.
        internal static GameplayAttribute WalkSoundRange { get; } = GameplayAttribute.Register("WalkSoundRange");
        internal static GameplayAttribute RunSoundRange { get; } = GameplayAttribute.Register("RunSoundRange");
        internal static GameplayAttribute RecoilControl { get; } = GameplayAttribute.Register("RecoilControl");
        internal static GameplayAttribute DashSpeed { get; } = GameplayAttribute.Register("DashSpeed");
        // 원작 프리셋이 정하는 GasMask와 DashCanControl이다. 적 준비가 프리셋 값을 이 핸들로 넣는다.
        // GasMask는 원작에서도 독가스 구역만 읽고, DashCanControl은 대시 중 동작 규칙이 읽는다.
        internal static GameplayAttribute GasMask { get; } = GameplayAttribute.Register("GasMask");
        internal static GameplayAttribute DashCanControl { get; } = GameplayAttribute.Register("DashCanControl");
        internal static GameplayAttribute BulletSpeedMultiplier { get; } = GameplayAttribute.Register("BulletSpeedMultiplier");
        internal static GameplayAttribute ElementFactorPhysics { get; } = GameplayAttribute.Register("ElementFactor_Physics");
        internal static GameplayAttribute Moveability { get; } = GameplayAttribute.Register("Moveability");
        // 원작 CharacterMainControl.PetCapcity가 읽는 펫 가방 칸 수다. 카탈로그 스탯과 GE 에셋이 같은 키를 쓰도록 원작 철자를 그대로 둔다.
        internal static GameplayAttribute PetCapcity { get; } = GameplayAttribute.Register("PetCapcity");
        // 원작 CharacterMainControl.StormProtection과 Health.ElementFactor(공간)다. 폭풍 버프 면제 판정(PlayerVitals.TryApplyStormBuff)과
        // 폭풍 피해 실행(StormHurtExecution)이 읽는다.
        internal static GameplayAttribute StormProtection { get; } = GameplayAttribute.Register("StormProtection");
        internal static GameplayAttribute ElementFactorSpace { get; } = GameplayAttribute.Register("ElementFactor_Space");
        // 원작 캐릭터 스탯 FishingTime과 FishingQualityFactor다. 낚시 행동이 고리 시간과 물고기 품질 추첨에 읽는다.
        internal static GameplayAttribute FishingTime { get; } = GameplayAttribute.Register("FishingTime");
        internal static GameplayAttribute FishingQualityFactor { get; } = GameplayAttribute.Register("FishingQualityFactor");

        // 저장과 HUD가 읽는 현재값이다. 원작 Health.CurrentHealth, currentStamina, Variables의 CurrentEnergy와 CurrentWater에 해당한다.
        internal static GameplayAttribute CurrentHealth { get; } = GameplayAttribute.Register("CurrentHealth");
        internal static GameplayAttribute CurrentStamina { get; } = GameplayAttribute.Register("CurrentStamina");
        internal static GameplayAttribute CurrentEnergy { get; } = GameplayAttribute.Register("CurrentEnergy");
        internal static GameplayAttribute CurrentWater { get; } = GameplayAttribute.Register("CurrentWater");

        // 메타 어트리뷰트다. 효과가 값을 넣으면 세트 훅이 체력으로 옮기고 0으로 되돌린다(UE Damage, Healing 패턴).
        internal static GameplayAttribute IncomingDamage { get; } = GameplayAttribute.Register("IncomingDamage");
        internal static GameplayAttribute IncomingHeal { get; } = GameplayAttribute.Register("IncomingHeal");

        // 카탈로그 정의에서 Base를 채우는 원작 스탯 목록이다. 위 속성이 먼저 등록된 뒤 만들어지도록 맨 아래에 둔다.
        internal static IReadOnlyList<(GameplayAttribute Attribute, float Default)> Stats { get; } = BuildStats();

        // 캐릭터 세트가 갖는 어트리뷰트 전체다. 원작 스탯 71개 다음에 현재값 4개와 메타 2개가 온다.
        internal static IReadOnlyList<GameplayAttribute> All { get; } = BuildAll();

        // 대체값과 함께 둔 핸들의 Key가 카탈로그 스탯 키다.
        static (GameplayAttribute, float)[] BuildStats()
        {
            var stats = new (GameplayAttribute, float)[_statDefaults.Length];
            for (int index = 0; index < _statDefaults.Length; index++)
                stats[index] = (GameplayAttribute.Register(_statDefaults[index].Key), _statDefaults[index].Default);
            return stats;
        }

        static GameplayAttribute[] BuildAll()
        {
            var all = new List<GameplayAttribute>(_statDefaults.Length + 6);
            // Stats 순서 그대로 넣어 세트 칸 순서가 원작 스탯 순서를 따른다.
            foreach ((GameplayAttribute attribute, float _) in Stats) all.Add(attribute);
            all.Add(CurrentHealth);
            all.Add(CurrentStamina);
            all.Add(CurrentEnergy);
            all.Add(CurrentWater);
            all.Add(IncomingDamage);
            all.Add(IncomingHeal);
            return all.ToArray();
        }
    }
}
