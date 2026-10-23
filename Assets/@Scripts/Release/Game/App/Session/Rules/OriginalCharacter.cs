#nullable enable
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원본 플레이어 캐릭터 정의의 스탯과 새 플레이어 생존 값 규칙을 한곳에 둔다. 정의는 호출자가 카탈로그에서 찾아 넘긴다.
    // 새 플레이어 값과 소지 칸 수는 이 접근자로, HUD 최대치와 생존 틱, 피해, 사격 보정은 GAS 어트리뷰트(CharacterAttributes)로 같은 스탯을 읽는다.
    internal static class OriginalCharacter
    {
        // 원본 새 게임 플레이어의 에너지 시작값이다. 사망 복귀에서 사망 직전 값이 없을 때도 이 값을 쓴다.
        internal const float _newGameEnergy = 50f;
        // 원본 새 게임 플레이어의 수분 시작값이다. 사망 복귀에서 사망 직전 값이 없을 때도 이 값을 쓴다.
        internal const float _newGameHydration = 50f;

        // 두 읽기 길이 같은 스탯을 보도록 키는 GAS 어트리뷰트 등록 키를 그대로 쓴다.
        internal static float MaxHealth(OriginalItemDefinition character) => character.Stat(CharacterAttributes.MaxHealth.Key);
        internal static float MaxStamina(OriginalItemDefinition character) => character.Stat(CharacterAttributes.Stamina.Key);
        // 원본 정의의 메인 인벤토리 칸 수다.
        internal static int InventoryCapacity(OriginalItemDefinition character) => (int)character.Stat(CharacterAttributes.InventoryCapacity.Key);
        // 원본 정의의 펫 가방 칸 수다. 새로 만드는 빈 펫 가방이 쓰고, 월드에서는 GAS 현재값으로 다시 맞춘다.
        // 카탈로그 플레이어 정의에는 PetCapcity가 없어, GAS SeedStats와 같은 대체값(원작 Character_0 기본 1)을 써야
        // 용량 동기화가 없는 프롤로그에서도 첫 펫 가방이 0칸이 아니라 원작처럼 1칸이 된다.
        internal static int PetCapacity(OriginalItemDefinition character) =>
            Mathf.RoundToInt(character.Stat(CharacterAttributes.PetCapcity.Key, StatFallback(CharacterAttributes.PetCapcity)));

        // GAS 스탯 대체값 표(CharacterAttributes.Stats) 한 곳에서 키의 대체값을 읽는다. 표에 없는 키는 0이다.
        static float StatFallback(GameplayAttribute attribute)
        {
            foreach ((GameplayAttribute candidate, float fallback) in CharacterAttributes.Stats)
                if (candidate.Key == attribute.Key) return fallback;
            return 0f;
        }

        // 원본 TickVariables는 게임 시간 1초마다 한 번 발동하고 넘친 시간은 버린다. 프롤로그와 전투 월드가 같은 규칙을 쓴다.
        // 누적값이 1초에 이르면 0으로 되돌리고 true를 돌려준다.
        internal static bool AdvanceSurvivalTick(ref float accumulated, float deltaTime)
        {
            accumulated += deltaTime;
            if (accumulated < 1f) return false;
            accumulated = 0f;
            return true;
        }

        // 원본 Movement.GetMoveAnimationValue다. 플레이어 모터와 적이 함께 쓴다. 나누는 걷기와 달리기는 버프가 반영된 지금 속도이고,
        // 지금 걷기와 기본 걷기의 비율을 곱해 느려진 캐릭터는 느린 걸음 동작을 보인다. 원작 적 프리셋은 이동 배율을 기본값에 곱해 그 비율이 1이다.
        internal static float MoveAnimationValue(float speed, bool running, float walkSpeed, float runSpeed, float originWalkSpeed)
        {
            if (walkSpeed <= 0f) return 0f;
            float value = running ? Mathf.InverseLerp(walkSpeed, runSpeed, speed) + 1f : Mathf.Clamp01(speed / walkSpeed);
            return value * walkSpeed / originWalkSpeed;
        }

        // 원본 CharacterWalkSpeed와 CharacterRunSpeed가 곱하는 든 무기의 이동 배율이다. 플레이어 모터와 적이 함께 쓴다.
        // 원본처럼 총과 근접 무기의 MoveSpeedMultiplier만 보고, 맨손이거나 값이 0 이하이면 곱하지 않는다.
        internal static float WeaponMoveSpeedMultiplier(OriginalItemDefinition? weapon)
        {
            if (weapon == null || (!weapon.IsGun && !weapon.IsMelee)) return 1f;
            float multiplier = weapon.Stat("MoveSpeedMultiplier");
            return multiplier > 0f ? multiplier : 1f;
        }

        // 정의의 최대 체력과 스태미나, 주어진 에너지와 수분으로 플레이어를 만든다. 원본 플레이어 정의는 빌드 데이터라 런타임에
        // 검증하지 않고 EditMode 카탈로그 테스트가 확인하며, 호출자는 새 게임 기본값이나 소유자의 생존 값만 넘기므로 검증 없는 생성자를 쓴다.
        internal static PlayerState NewOriginalPlayer(OriginalItemDefinition character, float energy = _newGameEnergy,
            float hydration = _newGameHydration) =>
            new PlayerState(MaxHealth(character), MaxStamina(character), energy, hydration);
    }
}
