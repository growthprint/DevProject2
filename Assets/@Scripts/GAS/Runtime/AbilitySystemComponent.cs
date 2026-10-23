#nullable enable
using System;
using UnityEngine;

namespace Game.GAS
{
    // 프리팹에 붙이는 범용 호스트다. 어트리뷰트 세트는 게임 쪽이 Initialize로 넘기고, 시작 어빌리티와 효과는 인스펙터에서 정한다.
    // 소유자와 아바타가 같은 캐릭터(적, 펫, 포탑)용이다. 수명이 씬보다 긴 플레이어는 상태 소유자가 AbilitySystem을 직접 갖는다.
    [DisallowMultipleComponent]
    public sealed class AbilitySystemComponent : MonoBehaviour
    {
        [SerializeField] GameplayAbilityDefinition[] _startupAbilities = Array.Empty<GameplayAbilityDefinition>();
        [SerializeField] GameplayEffectDefinition[] _startupEffects = Array.Empty<GameplayEffectDefinition>();
        AbilitySystem? _system;

        // Initialize 전에는 null이다. 읽는 쪽은 초기화한 소유자가 정한 순서를 따른다.
        public AbilitySystem? System => _system;

        // 소유자가 한 번만 부른다.
        public AbilitySystem Initialize(AttributeSet attributes)
        {
            var system = new AbilitySystem(attributes, this);
            _system = system;
            system.SetAvatar(gameObject);
            foreach (GameplayAbilityDefinition ability in _startupAbilities)
                if (ability != null) system.GiveAbility(ability);
            foreach (GameplayEffectDefinition effect in _startupEffects)
                if (effect != null) system.ApplyEffect(effect);
            return system;
        }

        // 오브젝트가 사라지면 효과와 어빌리티를 정리하고 구독자를 비운다.
        void OnDestroy() => _system?.Release();
    }
}
