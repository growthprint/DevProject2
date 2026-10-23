#nullable enable
using System;
using UnityEngine;

namespace Game.GAS
{
    // UE GameplayEffect 블루프린트에 해당하는 에셋이다. 원작 Buff 프리팹의 지속시간, 최대 층, 배타 태그와 우선순위,
    // Effect의 TickTrigger 주기와 ModifierAction을 한 곳에 담는다. 실행 중에는 읽기만 한다.
    [CreateAssetMenu(menuName = "GAS/Gameplay Effect", fileName = "GE_New")]
    public sealed class GameplayEffectDefinition : ScriptableObject
    {
        [SerializeField] GameplayEffectDurationPolicy _durationPolicy;
        [SerializeField, Min(0f)] float _duration;
        // 0보다 크면 주기 효과다. 원작 TickTrigger처럼 buffer += dt/주기, buffer > 1일 때마다 실행한다.
        [SerializeField, Min(0f)] float _period;
        [SerializeField] bool _executePeriodicOnApplication;
        // 이 효과의 시간을 진행시키는 시계 태그다. 비우면 기본 시계(AbilitySystem.Tick)가 진행한다.
        [SerializeField] GameplayTagName _clock;
        [SerializeField] GameplayModifierInfo[] _modifiers = Array.Empty<GameplayModifierInfo>();
        [SerializeField] GameplayEffectExecution[] _executions = Array.Empty<GameplayEffectExecution>();
        // 호출자가 주지 않은 SetByCaller를 읽을 때 쓰는 에셋 값이다. 호출자 값이 늘 이기고 spec 칸을 차지하지 않는다.
        // UE에는 에셋 단위 기본값이 없어, 가장 가까운 것은 읽는 쪽의 DefaultIfNotFound다.
        [SerializeField] GameplayEffectSetByCallerDefault[] _setByCallerDefaults = Array.Empty<GameplayEffectSetByCallerDefault>();
        [SerializeField] GameplayTagContainer _assetTags = new GameplayTagContainer();
        [SerializeField] GameplayTagContainer _grantedTags = new GameplayTagContainer();
        // 붙어 있는 동안 대상에게 주는 면역이다. 원작 buffResist처럼 그 태그(또는 자식)를 가진 효과를 막는다.
        [SerializeField] GameplayTagContainer _grantedImmunityTags = new GameplayTagContainer();
        [SerializeField] GameplayTagRequirements _applicationRequirements = new GameplayTagRequirements();
        // 충족하지 않는 동안 효과가 꺼진다(수정자, 태그, 주기 실행 없음). 시간은 계속 흐른다.
        [SerializeField] GameplayTagRequirements _ongoingRequirements = new GameplayTagRequirements();
        // Require가 있고 충족되면 효과를 떼어 낸다.
        [SerializeField] GameplayTagRequirements _removalRequirements = new GameplayTagRequirements();
        [SerializeField] GameplayTagContainer _removeEffectsWithTags = new GameplayTagContainer();
        // 원작 maxLayers다. 같은 효과를 다시 걸면 시간을 새로 하고 이 한도 안에서 층을 더한다.
        [SerializeField, Min(1)] int _stackLimit = 1;
        // 원작 exclusiveTag와 exclusiveTagPriority다. 같은 배타 태그의 다른 효과와 함께 붙지 않는다.
        [SerializeField] GameplayTagName _exclusiveTag;
        [SerializeField] int _exclusivePriority;
        [SerializeField] GameplayTagContainer _cues = new GameplayTagContainer();
        // 붙어 있는 동안 대상에게 주는 어빌리티다. 효과가 떨어지면 함께 회수한다.
        [SerializeField] GameplayAbilityDefinition[] _grantedAbilities = Array.Empty<GameplayAbilityDefinition>();
        // 이 효과가 적용되면 같은 시전자로 이어서 거는 효과다(원작 AddBuffAction, UE ConditionalGameplayEffects).
        [SerializeField] GameplayEffectDefinition[] _additionalEffects = Array.Empty<GameplayEffectDefinition>();
        // 켜면 이어 거는 효과가 이 spec의 SetByCaller 값을 받는다. 받은 값은 받는 효과의 에셋 기본값보다 앞선다.
        // UE UAdditionalEffectsGameplayEffectComponent의 bOnApplicationCopyDataFromOriginalSpec에 해당한다.
        [SerializeField] bool _copySetByCallersToAdditionalEffects;

        [NonSerialized] bool _resolved;
        [NonSerialized] GameplayTag _clockTag;
        [NonSerialized] GameplayTag _exclusive;

        public GameplayEffectDurationPolicy DurationPolicy => _durationPolicy;
        public float Duration => _duration;
        public float Period => _period;
        public bool IsPeriodic => _period > 0f && _durationPolicy != GameplayEffectDurationPolicy.Instant;
        public bool ExecutePeriodicOnApplication => _executePeriodicOnApplication;
        public int ModifierCount => _modifiers.Length;
        public GameplayModifierInfo ModifierAt(int index) => _modifiers[index];
        public int ExecutionCount => _executions.Length;
        public GameplayEffectExecution? ExecutionAt(int index) => _executions[index];
        // 제작 검사와 에셋 계약 테스트가 에셋에 담긴 기본값을 그대로 확인하는 통로다.
        public int SetByCallerDefaultCount => _setByCallerDefaults.Length;
        public GameplayEffectSetByCallerDefault SetByCallerDefaultAt(int index) => _setByCallerDefaults[index];
        public GameplayTagContainer AssetTags => _assetTags;
        public GameplayTagContainer GrantedTags => _grantedTags;
        public GameplayTagContainer GrantedImmunityTags => _grantedImmunityTags;
        public GameplayTagRequirements ApplicationRequirements => _applicationRequirements;
        public GameplayTagRequirements OngoingRequirements => _ongoingRequirements;
        public GameplayTagRequirements RemovalRequirements => _removalRequirements;
        public GameplayTagContainer RemoveEffectsWithTags => _removeEffectsWithTags;
        public int StackLimit => Mathf.Max(1, _stackLimit);
        public int ExclusivePriority => _exclusivePriority;
        public GameplayTagContainer Cues => _cues;
        public int GrantedAbilityCount => _grantedAbilities.Length;
        public GameplayAbilityDefinition? GrantedAbilityAt(int index) => _grantedAbilities[index];
        public int AdditionalEffectCount => _additionalEffects.Length;
        public GameplayEffectDefinition? AdditionalEffectAt(int index) => _additionalEffects[index];
        // 이어 거는 효과의 spec을 만드는 쪽이 값을 넘길지 정할 때 읽는다.
        public bool CopySetByCallersToAdditionalEffects => _copySetByCallersToAdditionalEffects;

        public GameplayTag Clock
        {
            get
            {
                Resolve();
                return _clockTag;
            }
        }

        public GameplayTag ExclusiveTag
        {
            get
            {
                Resolve();
                return _exclusive;
            }
        }

        // 면역 검사에 쓰는 효과의 정체성 태그(에셋 태그와 배타 태그) 중 하나라도 query이거나 그 자식인지 본다.
        // 배타 태그가 없으면 None이라 MatchesTag가 false다.
        internal bool IdentityMatches(GameplayTag query) => _assetTags.HasTag(query) || ExclusiveTag.MatchesTag(query);

        // RemoveEffectsWithTags가 쓰는 판정이다. 에셋 태그, 부여 태그, 배타 태그를 모두 본다.
        internal bool OwnedMatches(GameplayTag query) => IdentityMatches(query) || _grantedTags.HasTag(query);

        // 유지 조건이 있고 지금 태그가 그 조건을 채우지 못하면 꺼진 채로 붙는다. 조건이 비면 늘 켜진다.
        internal bool IsInhibitedBy(GameplayTagCountMap tags) => !_ongoingRequirements.IsEmpty && !_ongoingRequirements.IsMetBy(tags);

        // 제거 조건은 Require가 있을 때만 본다. Require가 비면 조건이 거의 늘 채워져 효과가 붙자마자 떨어진다.
        internal bool IsRemovedBy(GameplayTagCountMap tags) => !_removalRequirements.Require.IsEmpty && _removalRequirements.IsMetBy(tags);

        // 피격과 주기 실행마다 불리므로 할당 없이 앞 칸부터 찾는다. 같은 태그가 둘이면 앞 칸이 이긴다.
        // 정확 일치 비교라 무효 태그 칸이나 무효 질의는 맞지 않는다. 직렬화된 칸은 Unity가 늘 인스턴스로 채운다.
        internal bool TryGetSetByCallerDefault(GameplayTag tag, out float magnitude)
        {
            for (int index = 0; index < _setByCallerDefaults.Length; index++)
            {
                GameplayEffectSetByCallerDefault entry = _setByCallerDefaults[index];
                if (!entry.Tag.MatchesTagExact(tag)) continue;
                magnitude = entry.Magnitude;
                return true;
            }
            magnitude = 0f;
            return false;
        }

        void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            if (!_clock.IsEmpty) _clock.TryResolve(out _clockTag);
            if (!_exclusiveTag.IsEmpty) _exclusiveTag.TryResolve(out _exclusive);
        }

#if UNITY_EDITOR
        // 인스펙터나 제작 도구가 값을 고치면 이름에서 만든 캐시를 모두 비워 다음 읽기 때 다시 만든다.
        // 두 배열의 칸은 Unity 직렬화가 늘 인스턴스로 채운다.
        void OnValidate()
        {
            _resolved = false;
            foreach (GameplayModifierInfo modifier in _modifiers) modifier.ClearResolved();
            // 인스펙터에서 고친 기본값 태그 이름이 다음 조회에 바로 쓰이도록 그 캐시도 비운다.
            foreach (GameplayEffectSetByCallerDefault entry in _setByCallerDefaults) entry.ClearResolved();
            _assetTags.ClearResolved();
            _grantedTags.ClearResolved();
            _grantedImmunityTags.ClearResolved();
            _applicationRequirements.ClearResolved();
            _ongoingRequirements.ClearResolved();
            _removalRequirements.ClearResolved();
            _removeEffectsWithTags.ClearResolved();
            _cues.ClearResolved();
        }
#endif
    }
}
