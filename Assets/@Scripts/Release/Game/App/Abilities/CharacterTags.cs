#nullable enable
using Game.GAS;

namespace Game.App
{
    // 게임 코드가 쓰는 GAS 태그 상수다. GE와 GA 에셋은 같은 이름을 문자열로 저장하고, 제작 검사가 레지스트리에 있는지 본다.
    internal static class CharacterTags
    {
        // 체력이 0이 된 캐릭터다. 사망 알림은 이 태그가 붙을 때 한 번 나간다.
        internal static GameplayTag Dead { get; } = GameplayTag.Request("State.Dead");
        // 원작 CanDieIfNotRaidMap이 false인 캐릭터가 비출격 맵에 있을 때다. 치명적인 피해에도 체력 1이 남는다.
        internal static GameplayTag CannotDie { get; } = GameplayTag.Request("State.CannotDie");

        // 원작 CharacterMainControl.currentAction에 해당하는 행동 태그다. 부모 태그 하나로 "행동 중"을 묻는다.
        internal static GameplayTag Action { get; } = GameplayTag.Request("State.Action");
        // 원작 CA_UseItem처럼 사용하는 아이템을 손에 든 동안이다. 아이템 사용 어빌리티가 실행 중에 쥐고, 모터는 그동안 무기 이동 배율을 뺀다.
        internal static GameplayTag ActionUseItem { get; } = GameplayTag.Request("State.Action.UseItem");
        // 원작 ActionPriorities.Fishing 행동 자리다. 낚시 동안 상호작용 객체가 쥐어 다른 행동과 F 상호작용을 막는다.
        internal static GameplayTag ActionFishing { get; } = GameplayTag.Request("State.Action.Fishing");

        // 원작 Buff.BuffExclusiveTags.Starve다. 기아 버프와 그 피해 효과가 함께 다는 에셋 태그라, 이 태그 하나로 둘을 같이 뗀다.
        // 배타 태그로 두지 않는다. 둘이 같은 배타 태그를 가지면 이어 걸린 피해 효과가 부모 버프를 밀어낸다.
        internal static GameplayTag BuffStarve { get; } = GameplayTag.Request("Buff.Starve");

        // 행동 중에 막히는 입력이다. 원작 CA_UseItem이 달리기와 조준, 무기 교체를 막는 규칙을 태그로 둔다.
        internal static GameplayTag BlockSprint { get; } = GameplayTag.Request("Block.Sprint");
        internal static GameplayTag BlockAim { get; } = GameplayTag.Request("Block.ADS");
        internal static GameplayTag BlockWeaponSwitch { get; } = GameplayTag.Request("Block.WeaponSwitch");
        internal static GameplayTag BlockAttack { get; } = GameplayTag.Request("Block.Attack");

        // 효과 시계다. 생존 버프는 생존 게이트(대사 중에도 흐름)에서, 이동 쿨다운은 모터 게이트(포커스를 잃으면 멈춤)에서 흐른다.
        internal static GameplayTag ClockStatus { get; } = GameplayTag.Request("Clock.Status");
        internal static GameplayTag ClockMovement { get; } = GameplayTag.Request("Clock.Movement");

        internal static GameplayTag CueHurt { get; } = GameplayTag.Request("GameplayCue.Character.Hurt");

        // SetByCaller 값의 이름이다. 효과를 거는 코드가 실행 계산에 넘기는 입력이다.
        internal static GameplayTag DataDamage { get; } = GameplayTag.Request("Data.Damage");
        internal static GameplayTag DataDamageFactor { get; } = GameplayTag.Request("Data.DamageFactor");
        internal static GameplayTag DataHeal { get; } = GameplayTag.Request("Data.Heal");
        internal static GameplayTag DataEnergy { get; } = GameplayTag.Request("Data.Energy");
        internal static GameplayTag DataWater { get; } = GameplayTag.Request("Data.Water");
        internal static GameplayTag DataRaidMap { get; } = GameplayTag.Request("Data.RaidMap");
        // 원작 DamageInfo.armorPiercing이다. 폭발(원작 관통 1)처럼 관통이 있는 피해만 넘기고, 없으면 피해 실행이 0으로 본다.
        internal static GameplayTag DataArmorPiercing { get; } = GameplayTag.Request("Data.ArmorPiercing");
        // 원작 Health.Hurt의 aiCombatFactor 비율(공격자 / 피해자)이다. 적이 적에게 준 피해만 넘기고, 없으면 피해 실행이 곱하지 않는다.
        internal static GameplayTag DataCombatFactor { get; } = GameplayTag.Request("Data.CombatFactor");
    }
}
