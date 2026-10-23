#nullable enable
using UnityEngine;

namespace Game.GAS
{
    // UE GameplayAbility 블루프린트의 설정 부분이다. 태그 규칙, 비용과 쿨다운 효과, 이벤트 발동 태그를 담고,
    // 실제 동작은 하위 클래스가 만드는 GameplayAbility 인스턴스가 맡는다. 원작 SkillBase의 staminaCost와 coolDownTime도 여기로 온다.
    public abstract class GameplayAbilityDefinition : ScriptableObject
    {
        // 이 어빌리티를 가리키는 태그다. 다른 어빌리티의 취소, 차단 규칙이 이 태그로 찾는다.
        [SerializeField] GameplayTagContainer _abilityTags = new GameplayTagContainer();
        [SerializeField] GameplayTagContainer _cancelAbilitiesWithTags = new GameplayTagContainer();
        [SerializeField] GameplayTagContainer _blockAbilitiesWithTags = new GameplayTagContainer();
        // 실행되는 동안 주인에게 붙는 태그다(예: State.Action.Reload).
        [SerializeField] GameplayTagContainer _activationOwnedTags = new GameplayTagContainer();
        // Require는 주인에게 있어야 하는 태그, Ignore는 있으면 시작할 수 없는 태그다.
        [SerializeField] GameplayTagRequirements _activationRequirements = new GameplayTagRequirements();
        [SerializeField] GameplayTagContainer _triggerEventTags = new GameplayTagContainer();
        // 즉시 효과여야 한다. Add 연산의 음수 크기가 현재값보다 크면 시작할 수 없다.
        [SerializeField] GameplayEffectDefinition? _costEffect;
        // 지속 효과여야 하고, 부여 태그(Cooldown.*)가 붙어 있는 동안 다시 시작할 수 없다.
        [SerializeField] GameplayEffectDefinition? _cooldownEffect;

        public GameplayTagContainer AbilityTags => _abilityTags;
        public GameplayTagContainer CancelAbilitiesWithTags => _cancelAbilitiesWithTags;
        public GameplayTagContainer BlockAbilitiesWithTags => _blockAbilitiesWithTags;
        public GameplayTagContainer ActivationOwnedTags => _activationOwnedTags;
        public GameplayTagRequirements ActivationRequirements => _activationRequirements;
        public GameplayTagContainer TriggerEventTags => _triggerEventTags;
        public GameplayEffectDefinition? CostEffect => _costEffect;
        public GameplayEffectDefinition? CooldownEffect => _cooldownEffect;

        // 어빌리티를 줄 때 한 번 불려 주인마다 인스턴스 하나를 만든다(UE InstancedPerActor).
        protected internal abstract GameplayAbility CreateInstance(AbilitySystem system);

#if UNITY_EDITOR
        // 인스펙터에서 태그를 고치면 이름에서 만든 캐시를 비운다. 하위 클래스가 덮으면 base를 부른다.
        protected virtual void OnValidate()
        {
            _abilityTags.ClearResolved();
            _cancelAbilitiesWithTags.ClearResolved();
            _blockAbilitiesWithTags.ClearResolved();
            _activationOwnedTags.ClearResolved();
            _activationRequirements.ClearResolved();
            _triggerEventTags.ClearResolved();
        }
#endif
    }
}
