#nullable enable
using System.Collections.Generic;

namespace Game.GAS
{
    // 한 캐릭터가 받은 어빌리티 목록과 시작, 종료, 취소 규칙이다. UE AbilitySystemComponent의 ActivatableAbilities에 해당한다.
    // 해제 여부와 공개 진입점의 작업 열기는 AbilitySystem이 맡고, 알림은 작업이 끝날 때 시스템이 모아 보낸다.
    internal sealed class GrantedAbilityContainer
    {
        readonly AbilitySystem _system;
        readonly List<GameplayAbility> _abilities = new List<GameplayAbility>();
        // 실행 중인 어빌리티의 BlockAbilitiesWithTags를 센다. 같은 태그를 여러 어빌리티가 막아도 마지막이 끝날 때 풀린다.
        readonly GameplayTagCountMap _blockedAbilityTags = new GameplayTagCountMap();
        // 어빌리티 목록을 도는 중이면 회수를 표시만 한다. 콜백이 어빌리티를 주거나 회수해도 순회가 깨지지 않는다.
        int _iterating;

        internal GrantedAbilityContainer(AbilitySystem system) => _system = system;

        internal int Count => _abilities.Count;
        internal GameplayAbility At(int index) => _abilities[index];

        internal GameplayAbility? Find(GameplayAbilityDefinition definition)
        {
            for (int index = 0; index < _abilities.Count; index++)
            {
                GameplayAbility ability = _abilities[index];
                if (!ability.PendingRemove && ability.Definition == definition) return ability;
            }
            return null;
        }

        internal bool Give(GameplayAbilityDefinition definition)
        {
            if (Find(definition) != null) return false;
            GameplayAbility instance = definition.CreateInstance(_system);
            _abilities.Add(instance);
            return true;
        }

        internal bool Remove(GameplayAbilityDefinition definition)
        {
            GameplayAbility? ability = Find(definition);
            if (ability == null) return false;
            if (ability.IsActive) End(ability, true);
            ability.PendingRemove = true;
            Sweep();
            return true;
        }

        internal AbilityActivationResult CheckActivation(GameplayAbility ability, in GameplayEventData trigger)
        {
            GameplayAbilityDefinition definition = ability.Definition;
            if (ability.IsActive) return AbilityActivationResult.AlreadyActive;
            if (!definition.ActivationRequirements.IsMetBy(_system.Tags) || IsBlocked(definition)) return AbilityActivationResult.BlockedByTags;
            if (_system.IsOnCooldown(definition)) return AbilityActivationResult.OnCooldown;
            if (!_system.CanAffordCost(definition)) return AbilityActivationResult.CannotAffordCost;
            return ability.CanActivate(trigger) ? AbilityActivationResult.Activated : AbilityActivationResult.Rejected;
        }

        // 시작 검사를 통과한 뒤 AbilitySystem이 연 작업 안에서 불린다. 콜백이 남긴 알림은 그 작업이 닫힐 때 나간다.
        internal void Activate(GameplayAbilityDefinition definition, GameplayAbility ability, in GameplayEventData trigger)
        {
            CancelWithTags(definition.CancelAbilitiesWithTags, ability, false);
            _blockedAbilityTags.AddAll(definition.BlockAbilitiesWithTags);
            _system.Tags.AddAll(definition.ActivationOwnedTags);
            ability.IsActive = true;
            ability.OnActivate(trigger);
        }

        // UE EndAbility 순서: OnEnd를 먼저 부르고, 그다음 소유 태그와 차단을 푼다. 호출자는 실행 중인 어빌리티만 넘긴다.
        // 취소 처리기처럼 OnEnd 안의 게임 로직이 자기를 다시 끝내거나 회수할 수 있다. 그 호출은 UE IsEndAbilityValid처럼 무시해,
        // 종료와 태그 해제가 한 번만 일어나게 한다.
        internal void End(GameplayAbility ability, bool canceled)
        {
            if (ability.IsEnding) return;
            if (!_system.BeginOperation()) return;
            // 작업은 아래 finally가 태그와 차단을 푼 뒤에 닫혀, 알림은 정리된 상태를 본다.
            using var operation = new AbilitySystem.OperationScope(_system);
            ability.IsEnding = true;
            try
            {
                ability.OnEnd(canceled);
            }
            finally
            {
                GameplayAbilityDefinition definition = ability.Definition;
                _system.Tags.RemoveAll(definition.ActivationOwnedTags);
                _blockedAbilityTags.RemoveAll(definition.BlockAbilitiesWithTags);
                ability.IsActive = false;
                ability.IsEnding = false;
            }
        }

        internal int CancelWithTags(GameplayTagContainer tags, GameplayAbility? except, bool force)
        {
            if (tags.IsEmpty) return 0;
            int canceled = 0;
            using var iteration = new IterationScope(this);
            int count = _abilities.Count;
            for (int index = 0; index < count; index++)
            {
                GameplayAbility ability = _abilities[index];
                if (ability == except || !ability.AcceptsCancel(force)) continue;
                if (!ability.Definition.AbilityTags.HasAny(tags)) continue;
                End(ability, true);
                canceled++;
            }
            return canceled;
        }

        // force면 CanBeCanceled와 관계없이 모두 끝낸다(아바타 교체, 해제). 아니면 취소 가능한 것만 끝낸다.
        internal void EndActive(bool force)
        {
            using var iteration = new IterationScope(this);
            int count = _abilities.Count;
            for (int index = 0; index < count; index++)
            {
                GameplayAbility ability = _abilities[index];
                if (ability.AcceptsCancel(force)) End(ability, true);
            }
        }

        // 먼저 실행 중인 어빌리티에 사건을 전하고, 그다음 트리거 태그가 맞는 어빌리티를 시작한다.
        // 순서를 이렇게 해 방금 시작한 어빌리티가 같은 사건을 두 번 받지 않게 한다. AbilitySystem이 연 작업 안에서 불린다.
        internal int HandleGameplayEvent(in GameplayEventData data)
        {
            int activated = 0;
            using var iteration = new IterationScope(this);
            int count = _abilities.Count;
            for (int index = 0; index < count; index++)
            {
                GameplayAbility ability = _abilities[index];
                if (ability.IsLive) ability.OnGameplayEvent(data);
            }
            for (int index = 0; index < count; index++)
            {
                GameplayAbility ability = _abilities[index];
                if (ability.IsActive || ability.PendingRemove || !Triggers(ability.Definition, data.EventTag)) continue;
                if (_system.TryActivateAbility(ability.Definition, data) == AbilityActivationResult.Activated) activated++;
            }
            return activated;
        }

        // 실행 중인 어빌리티마다 따로 작업을 열어 OnTick을 부른다. 순회 범위는 효과 진행까지 묶는 AbilitySystem.Tick이 연다.
        internal void Tick(float deltaTime)
        {
            int count = _abilities.Count;
            for (int index = 0; index < count && !_system.IsReleased; index++)
            {
                GameplayAbility ability = _abilities[index];
                if (!ability.IsLive || !_system.BeginOperation()) continue;
                using var operation = new AbilitySystem.OperationScope(_system);
                ability.OnTick(deltaTime);
            }
        }

        // 실행 중인 어빌리티의 차단 태그가 이 어빌리티 태그나 그 부모와 직접 맞으면 막힌다.
        bool IsBlocked(GameplayAbilityDefinition definition) => _blockedAbilityTags.HasAnyTagOrParentExplicit(definition.AbilityTags);

        static bool Triggers(GameplayAbilityDefinition definition, GameplayTag eventTag)
        {
            GameplayTagContainer triggers = definition.TriggerEventTags;
            for (int index = 0; index < triggers.Count; index++)
                if (eventTag.MatchesTag(triggers[index])) return true;
            return false;
        }

        // 순회 중이 아닐 때 회수 표시된 어빌리티를 목록에서 뺀다.
        void Sweep()
        {
            if (_iterating > 0) return;
            for (int index = _abilities.Count - 1; index >= 0; index--)
                if (_abilities[index].PendingRemove) _abilities.RemoveAt(index);
        }

        // 어빌리티 목록 순회를 using 범위로 묶는다. 닫힐 때 순회 표시를 내리고, 가장 바깥 순회면 회수 표시된 어빌리티를 뺀다.
        internal readonly ref struct IterationScope
        {
            readonly GrantedAbilityContainer _container;

            internal IterationScope(GrantedAbilityContainer container)
            {
                _container = container;
                container._iterating++;
            }

            public void Dispose()
            {
                _container._iterating--;
                _container.Sweep();
            }
        }
    }
}
