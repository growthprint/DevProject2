#nullable enable
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.GAS
{
    // UE AbilitySystemComponent의 동작 부분이다. 캐릭터 하나의 어트리뷰트, 태그, 지속 효과, 어빌리티를 갖는다.
    // 평범한 C# 클래스라 필드 초기화에서 만들 수 있고 스스로 시간을 진행하지 않는다. 소유자가 자기 게이트(일시정지, 대사, 전환) 안에서 Tick을 부른다.
    // 상태는 즉시 바뀌고, 알림은 가장 바깥 작업이 끝날 때 합쳐서 한 번 나간다. 그래서 구독자는 늘 확정된 상태를 본다. 메인 스레드에서만 쓴다.
    public sealed class AbilitySystem
    {
        // 효과가 효과를, 어빌리티가 어빌리티를 부르는 중첩의 한도다. 넘으면 무한 연쇄라 보고하고 거절한다.
        const int _maxDepth = 8;
        // 알림 처리기가 다시 상태를 바꾸면 같은 루프에서 이어 보낸다. 서로 끝없이 바꾸는 결함을 막는 횟수다.
        const int _maxFlushRounds = 16;

        readonly UnityEngine.Object? _owner;
        readonly AttributeSet _attributes;
        // 느슨한 태그, 효과가 준 태그, 실행 중인 어빌리티의 소유 태그를 함께 센다.
        readonly GameplayTagCountMap _tags = new GameplayTagCountMap();
        readonly GameplayTagCountMap _immunities = new GameplayTagCountMap();
        readonly ActiveEffectContainer _effects;
        // 받은 어빌리티 목록과 시작, 종료, 취소 규칙은 효과 목록처럼 전용 컨테이너가 맡는다. 시스템은 해제와 작업 열기를 맡는다.
        readonly GrantedAbilityContainer _abilities;
        // 실행 계산 결과 버퍼다. 중첩 깊이마다 하나씩 두고 재사용해, 실행마다 할당하지 않는다.
        readonly List<GameplayEffectExecutionOutput> _outputs = new List<GameplayEffectExecutionOutput>();
        // 작업 중 바뀐 어트리뷰트 칸과 바뀌기 전 값이다. 같은 칸은 처음 값만 남겨 하나의 알림으로 합친다.
        readonly List<int> _dirtySlots;
        readonly List<int> _dispatchSlots;
        readonly bool[] _slotDirty;
        readonly float[] _slotOld;
        readonly List<GameplayTagChangeData> _dispatchTags = new List<GameplayTagChangeData>();
        readonly Queue<GameplayEffectChangeData> _pendingEffectChanges = new Queue<GameplayEffectChangeData>();
        readonly Queue<GameplayCueParameters> _pendingCues = new Queue<GameplayCueParameters>();
        GameObject? _avatar;
        int _depth;
        bool _flushing;
        bool _released;

        public AbilitySystem(AttributeSet attributes, UnityEngine.Object? owner = null)
        {
            _attributes = attributes;
            _owner = owner;
            _slotDirty = new bool[attributes.Count];
            _slotOld = new float[attributes.Count];
            // 한 칸은 알림 회차마다 한 번만 쌓이므로 어트리뷰트 수가 최대 길이다. 미리 잡아 두어 알림 중에 리스트가 커지며 할당하지 않는다.
            _dirtySlots = new List<int>(attributes.Count);
            _dispatchSlots = new List<int>(attributes.Count);
            _effects = new ActiveEffectContainer(this);
            _abilities = new GrantedAbilityContainer(this);
            attributes.Bind(this);
        }

        // 아래 이벤트는 인스턴스 알림이다. 값을 함께 보내고, 구독자 예외는 ObserverDispatch가 구독자별로 격리한다.
        public event Action<AbilitySystem, AttributeChangeData>? AttributeChanged;
        public event Action<AbilitySystem, GameplayTagChangeData>? TagChanged;
        public event Action<AbilitySystem, GameplayEffectChangeData>? EffectChanged;
        public event Action<AbilitySystem, GameplayCueParameters>? CueNotified;

        public AttributeSet Attributes => _attributes;
        public GameplayTagCountMap Tags => _tags;
        public GameplayTagCountMap Immunities => _immunities;
        public UnityEngine.Object? Owner => _owner;
        public GameObject? Avatar => _avatar;
        public bool IsReleased => _released;
        public int ActiveEffectCount => _effects.Count;
        public ActiveGameplayEffect ActiveEffectAt(int index) => _effects.At(index);
        public int AbilityCount => _abilities.Count;
        public GameplayAbility AbilityAt(int index) => _abilities.At(index);

        public float GetCurrent(GameplayAttribute attribute) => _attributes.GetCurrent(attribute);
        public float GetBase(GameplayAttribute attribute) => _attributes.GetBase(attribute);

        // 효과를 거치지 않고 Base를 정한다(UE SetNumericAttributeBase). 시작값 채우기, 세이브 읽기,
        // 매 프레임 바뀌는 스태미나처럼 효과로 만들 이유가 없는 변화에 쓴다. 해제된 시스템은 무시한다.
        public void SetBaseValue(GameplayAttribute attribute, float value)
        {
            if (!TryBeginOperation()) return;
            using var operation = new OperationScope(this);
            _attributes.SetBase(attribute, value);
        }

        public void AddLooseTag(GameplayTag tag)
        {
            if (!TryBeginOperation()) return;
            using var operation = new OperationScope(this);
            _tags.Add(tag);
        }

        public void RemoveLooseTag(GameplayTag tag)
        {
            if (!TryBeginOperation()) return;
            using var operation = new OperationScope(this);
            _tags.Remove(tag);
        }

        // 효과 없이 주는 면역이다(원작 적 프리셋의 buffResist). 넣은 만큼 빼야 사라진다.
        public void AddImmunityTag(GameplayTag tag)
        {
            if (!_released) _immunities.Add(tag);
        }

        public void RemoveImmunityTag(GameplayTag tag)
        {
            if (!_released) _immunities.Remove(tag);
        }

        // 적용과 같은 규칙으로 결과만 알려 주고 상태는 바꾸지 않는다. 여러 소유자를 먼저 확인하는 명령이 쓴다.
        public EffectApplyResult CanApplyEffect(in GameplayEffectSpec spec)
        {
            if (_released) return EffectApplyResult.Invalid;
            return _effects.Check(spec);
        }

        public EffectApplyResult ApplyEffect(in GameplayEffectSpec spec)
        {
            if (!TryBeginOperation()) return EffectApplyResult.Invalid;
            using var operation = new OperationScope(this);
            return _effects.Apply(spec);
        }

        // 자기 자신이 시전자인 효과를 건다. 시작 효과와 비용, 쿨다운 같은 자기 효과가 쓴다.
        public EffectApplyResult ApplyEffect(GameplayEffectDefinition definition) => ApplyEffect(new GameplayEffectSpec(definition, this));

        public bool HasEffect(GameplayEffectDefinition definition) => _effects.Find(definition) != null;

        // 재사용되는 객체라 같은 프레임 안에서만 읽는다.
        public ActiveGameplayEffect? FindEffect(GameplayEffectDefinition definition) => _effects.Find(definition);

        // stacks가 0 이하이면 효과 전체를, 아니면 그만큼 층만 뺀다(원작 RemoveBuff의 층 단위 제거, UE StacksToRemove).
        public bool RemoveEffect(GameplayEffectDefinition definition, int stacks = -1)
        {
            if (!TryBeginOperation()) return false;
            using var operation = new OperationScope(this);
            return _effects.Remove(definition, stacks);
        }

        // 에셋 태그, 부여 태그, 배타 태그 중 하나가 query(또는 그 자식)인 효과를 모두 뗀다.
        public int RemoveEffectsWithTags(GameplayTagContainer query)
        {
            if (!TryBeginOperation()) return 0;
            using var operation = new OperationScope(this);
            return _effects.RemoveMatching(query, null);
        }

        public int RemoveEffectsWithTag(GameplayTag query)
        {
            if (!TryBeginOperation()) return 0;
            using var operation = new OperationScope(this);
            return _effects.RemoveMatching(query);
        }

        public void RemoveAllEffects()
        {
            if (!TryBeginOperation()) return;
            using var operation = new OperationScope(this);
            _effects.RemoveAll(true);
        }

        // 기본 시계(시계 태그를 비운 효과)를 진행하고, 실행 중인 어빌리티의 OnTick을 부른다.
        // 효과 진행이 회수한 어빌리티는 표시만 하고, OnTick 순회까지 끝난 뒤 한 번에 빼도록 둘을 한 순회 범위로 묶는다.
        public void Tick(float deltaTime)
        {
            if (!TryBeginTick(deltaTime)) return;
            using var iteration = new GrantedAbilityContainer.IterationScope(_abilities);
            _effects.Tick(GameplayTag.None, deltaTime);
            _abilities.Tick(deltaTime);
        }

        // clock 시계에 속한 효과만 진행한다. 서로 다른 게이트에서 흘러야 하는 시간(예: 이동 쿨다운과 생존 버프)을 나눈다.
        public void TickClock(GameplayTag clock, float deltaTime)
        {
            if (!TryBeginTick(deltaTime)) return;
            _effects.Tick(clock, deltaTime);
        }

        // 이미 있으면 false다. 효과가 준 어빌리티와 직접 준 어빌리티를 구분하지 않고 하나만 둔다.
        public bool GiveAbility(GameplayAbilityDefinition definition)
        {
            if (_released) return false;
            return _abilities.Give(definition);
        }

        public bool RemoveAbility(GameplayAbilityDefinition definition) => _abilities.Remove(definition);

        public bool HasAbility(GameplayAbilityDefinition definition) => FindAbility(definition) != null;

        public GameplayAbility? FindAbility(GameplayAbilityDefinition definition) => _abilities.Find(definition);

        public bool IsAbilityActive(GameplayAbilityDefinition definition)
        {
            GameplayAbility? ability = FindAbility(definition);
            return ability != null && ability.IsActive;
        }

        // UE TryActivateAbility 순서: 부여 → 실행 중 → 태그 조건과 차단 → 쿨다운 → 비용 → 어빌리티 고유 조건.
        // 통과하면 취소 태그의 어빌리티를 끝내고, 차단과 소유 태그를 붙인 뒤 OnActivate를 부른다.
        public AbilityActivationResult TryActivateAbility(GameplayAbilityDefinition definition, in GameplayEventData trigger = default)
        {
            if (_released) return AbilityActivationResult.Invalid;
            GameplayAbility? ability = FindAbility(definition);
            if (ability == null) return AbilityActivationResult.NotGranted;
            AbilityActivationResult result = _abilities.CheckActivation(ability, trigger);
            if (result != AbilityActivationResult.Activated) return result;
            if (!BeginOperation()) return AbilityActivationResult.Invalid;
            using var operation = new OperationScope(this);
            _abilities.Activate(definition, ability, trigger);
            return AbilityActivationResult.Activated;
        }

        // 시작하지 않고 결과만 본다. 입력 처리가 시작 가능 여부를 미리 볼 때 쓴다.
        public AbilityActivationResult CanActivateAbility(GameplayAbilityDefinition definition, in GameplayEventData trigger = default)
        {
            // 정의는 호출자가 설정 에셋에서 늘 채워 넘기므로 해제 여부만 본다.
            if (_released) return AbilityActivationResult.Invalid;
            GameplayAbility? ability = FindAbility(definition);
            return ability == null ? AbilityActivationResult.NotGranted : _abilities.CheckActivation(ability, trigger);
        }

        // 정상 종료다. 실행 중이 아니면 아무 일도 하지 않는다.
        public void EndAbility(GameplayAbilityDefinition definition)
        {
            GameplayAbility? ability = FindAbility(definition);
            if (ability != null && ability.IsActive) _abilities.End(ability, false);
        }

        // 취소 종료다. CanBeCanceled와 관계없이 이 어빌리티를 끝낸다.
        public void CancelAbility(GameplayAbilityDefinition definition)
        {
            GameplayAbility? ability = FindAbility(definition);
            if (ability != null && ability.IsActive) _abilities.End(ability, true);
        }

        public int CancelAbilitiesWithTags(GameplayTagContainer tags)
        {
            if (!TryBeginOperation()) return 0;
            using var operation = new OperationScope(this);
            return _abilities.CancelWithTags(tags, null, false);
        }

        public void CancelAllAbilities()
        {
            if (!TryBeginOperation()) return;
            using var operation = new OperationScope(this);
            _abilities.EndActive(false);
        }

        // 비용 효과의 Add 수정자 중 음수 크기가 현재값을 0 아래로 만들면 시작할 수 없다(UE CheckCost).
        // 원작 대시의 "스태미나 < 비용이면 거절"과 같은 판정이다.
        public bool CanAffordCost(GameplayAbilityDefinition definition)
        {
            GameplayEffectDefinition? cost = definition.CostEffect;
            if (cost == null) return true;
            var spec = new GameplayEffectSpec(cost, this);
            for (int index = 0; index < cost.ModifierCount; index++)
            {
                GameplayModifierInfo modifier = cost.ModifierAt(index);
                // 비용 에셋 수정자의 어트리뷰트는 이 세트에 있다고 보고 연산 종류만 거른다.
                if (modifier.Op != AttributeModifierOp.Add) continue;
                float magnitude = modifier.Magnitude.Evaluate(spec, this);
                if (magnitude < 0f && _attributes.GetCurrent(modifier.Attribute) + magnitude < 0f) return false;
            }
            return true;
        }

        public bool CommitCost(GameplayAbilityDefinition definition)
        {
            GameplayEffectDefinition? cost = definition.CostEffect;
            if (cost == null) return true;
            if (!CanAffordCost(definition)) return false;
            ApplyEffect(new GameplayEffectSpec(cost, this));
            return true;
        }

        public void CommitCooldown(GameplayAbilityDefinition definition)
        {
            GameplayEffectDefinition? cooldown = definition.CooldownEffect;
            if (cooldown != null) ApplyEffect(new GameplayEffectSpec(cooldown, this));
        }

        public bool IsOnCooldown(GameplayAbilityDefinition definition)
        {
            GameplayEffectDefinition? cooldown = definition.CooldownEffect;
            return cooldown != null && _tags.HasAny(cooldown.GrantedTags);
        }

        public float GetCooldownRemaining(GameplayAbilityDefinition definition)
        {
            GameplayEffectDefinition? cooldown = definition.CooldownEffect;
            ActiveGameplayEffect? effect = cooldown != null ? _effects.Find(cooldown) : null;
            return effect != null ? effect.TimeRemaining : 0f;
        }

        // 실행 중인 어빌리티에 사건을 전하고 트리거 태그가 맞는 어빌리티를 시작한다. 시작한 수를 돌려준다.
        public int HandleGameplayEvent(in GameplayEventData data)
        {
            // 사건 태그는 호출 코드의 유효한 상수만 온다.
            if (!TryBeginOperation()) return 0;
            // 컨테이너의 순회 범위가 먼저 닫혀 회수 표시를 정리한 뒤 이 작업이 닫혀 알림이 나간다.
            using var operation = new OperationScope(this);
            return _abilities.HandleGameplayEvent(data);
        }

        // 효과 없이 큐만 보낸다(UE ExecuteGameplayCue). 어빌리티가 연출만 낼 때 쓴다.
        public void ExecuteGameplayCue(GameplayTag cueTag, float magnitude = 0f, GameplayEffectContext context = default)
        {
            // 큐 태그는 호출 코드의 유효한 상수만 온다.
            if (!TryBeginOperation()) return;
            using var operation = new OperationScope(this);
            _pendingCues.Enqueue(new GameplayCueParameters(cueTag, GameplayCueEvent.Executed, magnitude, 1, context, null));
        }

        // 씬마다 바뀌는 캐릭터 오브젝트를 연결한다. 아바타가 바뀌면 실행 중인 어빌리티는 옛 아바타의 일이라 모두 취소한다.
        public void SetAvatar(GameObject? avatar)
        {
            if (_released || avatar == _avatar || !BeginOperation()) return;
            using var operation = new OperationScope(this);
            _abilities.EndActive(true);
            _avatar = avatar;
        }

        // 소유자 수명이 끝날 때 한 번 부른다. 어빌리티를 취소하고 효과를 알림 없이 뗀 뒤 구독자를 비운다. 이후 호출은 무시한다.
        public void Release()
        {
            if (_released) return;
            _depth++;
            try
            {
                _abilities.EndActive(true);
                _effects.RemoveAll(false);
            }
            finally
            {
                _depth--;
                _released = true;
                _avatar = null;
                _pendingCues.Clear();
                _pendingEffectChanges.Clear();
                _tags.ClearPendingChanges();
                foreach (int slot in _dirtySlots) _slotDirty[slot] = false;
                _dirtySlots.Clear();
                AttributeChanged = null;
                TagChanged = null;
                EffectChanged = null;
                CueNotified = null;
            }
        }

        // 모든 변경은 이 작업 단위 안에서 일어난다. 중첩이 한도를 넘으면 무한 연쇄라 보고하고 false를 돌려준다.
        internal bool BeginOperation()
        {
            if (_depth >= _maxDepth)
            {
                Log.Instance.Fail(new InvalidOperationException("AbilitySystem: 효과와 어빌리티 중첩이 너무 깊습니다."), _owner);
                return false;
            }
            _depth++;
            return true;
        }

        internal void EndOperation()
        {
            _depth--;
            if (_depth == 0 && !_flushing) Settle();
        }

        // 공개 진입점이 작업을 여는 길이다. 해제된 시스템은 작업을 열지 않아, 해제 뒤 호출은 상태를 바꾸지 않는다.
        bool TryBeginOperation() => !_released && BeginOperation();

        // 연 작업을 using 범위로 닫는다. 자원 해제가 아니라 작업 깊이를 되돌리는 단순 복원이고, 가장 바깥 작업이 닫히면 알림이 나간다.
        // ref struct라 할당이 없다. 시작 검사는 실패 값이 메서드마다 달라 호출 쪽 if가 맡는다. 효과와 어빌리티 컨테이너의 작업도 이 범위로 닫는다.
        internal readonly ref struct OperationScope
        {
            readonly AbilitySystem _system;

            internal OperationScope(AbilitySystem system) => _system = system;

            public void Dispose() => _system.EndOperation();
        }

        internal void RecordAttributeChange(int slot, float oldValue)
        {
            if (_slotDirty[slot]) return;
            _slotDirty[slot] = true;
            _slotOld[slot] = oldValue;
            _dirtySlots.Add(slot);
        }

        internal void EnqueueEffectChange(in GameplayEffectChangeData change) => _pendingEffectChanges.Enqueue(change);

        internal void EnqueueCues(GameplayEffectDefinition definition, GameplayCueEvent cueEvent, float magnitude, int stacks,
            in GameplayEffectContext context)
        {
            GameplayTagContainer cues = definition.Cues;
            for (int index = 0; index < cues.Count; index++)
                _pendingCues.Enqueue(new GameplayCueParameters(cues[index], cueEvent, magnitude, stacks, context, definition));
        }

        internal GameplayEffectExecutionOutput RentOutput()
        {
            // 실행 계산은 늘 작업 안에서 불려 깊이가 1 이상이다. 깊이마다 버퍼 하나를 쓴다.
            int index = _depth - 1;
            while (_outputs.Count <= index) _outputs.Add(new GameplayEffectExecutionOutput());
            return _outputs[index];
        }

        bool TryBeginTick(float deltaTime)
        {
            if (_released) return false;
            // 0이면 일시정지라 조용히 넘긴다. 호출자는 0 이상의 유한값만 넘긴다.
            if (!(deltaTime > 0f)) return false;
            return true;
        }

        // 가장 바깥 작업이 끝나면 효과 조건을 다시 보고 알림을 보낸다. 처리기가 다시 바꾼 내용도 같은 루프에서 이어 보낸다.
        void Settle()
        {
            _flushing = true;
            try
            {
                for (int round = 0; round < _maxFlushRounds; round++)
                {
                    if (_released) return;
                    _depth++;
                    try
                    {
                        _effects.ReevaluateRequirements();
                    }
                    finally
                    {
                        _depth--;
                    }
                    if (!HasPendingNotifications) return;
                    Flush();
                }
            }
            finally
            {
                _flushing = false;
            }
        }

        bool HasPendingNotifications =>
            _dirtySlots.Count > 0 || _tags.PendingChangeCount > 0 || _pendingEffectChanges.Count > 0 || _pendingCues.Count > 0;

        // 어트리뷰트(칸 순서), 태그(순 변화만), 효과 변화, 큐 순서로 보낸다. 보내기 전에 대기열을 비워,
        // 처리기가 일으킨 새 변화는 다음 회차에 따로 나간다.
        void Flush()
        {
            // Mono의 AddRange는 다른 리스트를 받으면 임시 배열을 만들고, Sort는 원소가 둘 이상이면 호출마다 할당한다.
            // 그래서 하나씩 옮기며 칸 번호 순서 자리에 끼워 넣는다. 한 회차에 바뀌는 칸은 보통 몇 개뿐이다.
            _dispatchSlots.Clear();
            foreach (int slot in _dirtySlots)
            {
                int index = _dispatchSlots.Count;
                while (index > 0 && _dispatchSlots[index - 1] > slot) index--;
                _dispatchSlots.Insert(index, slot);
            }
            _dirtySlots.Clear();
            foreach (int slot in _dispatchSlots) _slotDirty[slot] = false;
            foreach (int slot in _dispatchSlots)
            {
                float oldValue = _slotOld[slot];
                float newValue = _attributes.CurrentAt(slot);
                if (oldValue.Equals(newValue)) continue;
                ObserverDispatch.InvokeEachValue(AttributeChanged, this, new AttributeChangeData(_attributes.AttributeAt(slot), oldValue, newValue));
            }

            _dispatchTags.Clear();
            for (int index = 0; index < _tags.PendingChangeCount; index++)
                if (_tags.TryGetPendingChange(index, out GameplayTagChangeData change)) _dispatchTags.Add(change);
            _tags.ClearPendingChanges();
            foreach (GameplayTagChangeData change in _dispatchTags) ObserverDispatch.InvokeEachValue(TagChanged, this, change);

            for (int count = _pendingEffectChanges.Count; count > 0 && _pendingEffectChanges.Count > 0; count--)
                ObserverDispatch.InvokeEachValue(EffectChanged, this, _pendingEffectChanges.Dequeue());

            for (int count = _pendingCues.Count; count > 0 && _pendingCues.Count > 0; count--)
                ObserverDispatch.InvokeEachValue(CueNotified, this, _pendingCues.Dequeue());
        }
    }
}
