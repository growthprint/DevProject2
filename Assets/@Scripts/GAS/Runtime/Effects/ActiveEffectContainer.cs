#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.GAS
{
    // 한 캐릭터의 지속 효과 목록과 적용 규칙이다. 원작 CharacterMainControl.AddBuff와 CharacterBuffManager의 규칙을 따른다.
    // 모든 변경은 AbilitySystem의 작업 안에서 불리고, 알림은 작업이 끝날 때 시스템이 모아 보낸다.
    internal sealed class ActiveEffectContainer
    {
        // 억제와 제거 조건을 다시 볼 때 서로 연쇄하는 태그 변화가 끝없이 돌지 않게 막는 횟수다.
        const int _maxReevaluateRounds = 8;

        readonly AbilitySystem _system;
        readonly List<ActiveGameplayEffect> _active = new List<ActiveGameplayEffect>();
        readonly Stack<ActiveGameplayEffect> _pool = new Stack<ActiveGameplayEffect>();
        // 효과마다 늘어나는 번호다. 집계기가 같은 순서의 수정자를 적용한 순서대로 두는 기준이다.
        int _nextId = 1;
        // 목록을 도는 중이면 제거를 표시만 하고, 순회가 끝난 뒤 한 번에 뺀다.
        int _iterating;

        internal ActiveEffectContainer(AbilitySystem system) => _system = system;

        internal int Count => _active.Count;
        internal ActiveGameplayEffect At(int index) => _active[index];

        internal ActiveGameplayEffect? Find(GameplayEffectDefinition definition)
        {
            foreach (ActiveGameplayEffect effect in _active)
                if (!effect._pendingRemove && effect.Definition == definition) return effect;
            return null;
        }

        // 확인과 적용이 같은 판정을 쓰도록 원작 AddBuff 순서를 한 곳에 둔다: 면역 → 적용 조건 → 즉시 → 같은 효과 → 배타 태그.
        // target은 Stacked면 같은 효과, 배타 판정이면 같은 배타 태그의 기존 효과이고 나머지는 null이다. 적용 조건은 UE처럼 면역 다음에 본다.
        EffectApplyResult Decide(GameplayEffectDefinition definition, out ActiveGameplayEffect? target)
        {
            target = null;
            if (IsImmune(definition)) return EffectApplyResult.Immune;
            if (!definition.ApplicationRequirements.IsMetBy(_system.Tags)) return EffectApplyResult.RequirementsNotMet;
            if (definition.DurationPolicy == GameplayEffectDurationPolicy.Instant) return EffectApplyResult.Executed;
            target = Find(definition);
            if (target != null) return EffectApplyResult.Stacked;
            target = FindExclusive(definition.ExclusiveTag);
            return target == null ? EffectApplyResult.Added : ExclusiveOutcome(target, definition);
        }

        // 적용과 같은 판정만 하고 상태는 바꾸지 않는다. GameplayCommands처럼 여러 소유자를 먼저 확인하는 호출자가 쓴다.
        internal EffectApplyResult Check(in GameplayEffectSpec spec) => Decide(spec.Definition!, out _);

        // 확인과 같은 판정 결과로 갈라, 미리 확인한 결과와 실제 적용 결과가 늘 같다. 거절은 상태를 바꾸지 않고 결과만 돌려준다.
        internal EffectApplyResult Apply(in GameplayEffectSpec spec)
        {
            GameplayEffectDefinition definition = spec.Definition!;
            EffectApplyResult outcome = Decide(definition, out ActiveGameplayEffect? target);
            if (outcome == EffectApplyResult.Executed)
            {
                Execute(spec, 1, null);
                RemoveMatching(definition.RemoveEffectsWithTags, null);
                ApplyAdditional(spec);
                return outcome;
            }

            if (outcome == EffectApplyResult.Stacked)
            {
                ActiveGameplayEffect existing = target!;
                // 원작 NotifyIncomingBuffWithSameID: 시간은 늘 새로 하고, 층은 한도보다 적을 때만 더한다.
                existing._remaining = definition.DurationPolicy == GameplayEffectDurationPolicy.HasDuration ? definition.Duration : 0f;
                if (existing._stacks < definition.StackLimit)
                {
                    existing._stacks++;
                    if (!existing._inhibited) UpdateMagnitudes(existing);
                    _system.EnqueueEffectChange(new GameplayEffectChangeData(definition, existing._stacks, GameplayEffectChangeKind.StackChanged));
                }
                ApplyAdditional(spec);
                return outcome;
            }

            if (outcome == EffectApplyResult.KeptExisting)
            {
                ActiveGameplayEffect kept = target!;
                kept._specValue = kept._specValue.WithSource(spec.Source);
                return outcome;
            }
            if (outcome != EffectApplyResult.Added) return outcome;
            // 배타 태그 판정에서 새 효과가 이기면 같은 태그의 기존 효과를 먼저 뗀다.
            if (target != null) Remove(target, -1);

            ActiveGameplayEffect effect = _pool.Count > 0 ? _pool.Pop() : new ActiveGameplayEffect();
            effect.Reset(_nextId++, spec);
            for (int index = 0; index < definition.ModifierCount; index++)
                effect._magnitudes[index] = definition.ModifierAt(index).Magnitude.Evaluate(spec, _system);
            effect._inhibited = definition.IsInhibitedBy(_system.Tags);
            _active.Add(effect);
            if (!effect._inhibited) Activate(effect);
            _system.EnqueueEffectChange(new GameplayEffectChangeData(definition, effect._stacks, GameplayEffectChangeKind.Added));
            RemoveMatching(definition.RemoveEffectsWithTags, effect);
            if (ExecutesOnApplication(definition, effect)) Execute(effect._specValue, effect._stacks, effect);
            ApplyAdditional(spec);
            return EffectApplyResult.Added;
        }

        // 주기 효과는 켜져 있고 붙자마자 떨어지지 않았을 때만 붙는 순간 한 번 실행한다(UE bExecutePeriodicEffectOnApplication).
        // 붙는 중의 콜백이 효과 객체를 풀로 돌릴 수 있어 정의는 적용 spec의 것을 받는다.
        static bool ExecutesOnApplication(GameplayEffectDefinition definition, ActiveGameplayEffect effect) =>
            definition.IsPeriodic && definition.ExecutePeriodicOnApplication && !effect._inhibited && !effect._pendingRemove;

        // stacks가 0 이하이거나 남은 층 이상이면 효과 전체를, 아니면 그만큼 층만 뺀다(원작 RemoveBuff의 층 단위 제거).
        internal bool Remove(GameplayEffectDefinition definition, int stacks)
        {
            ActiveGameplayEffect? effect = Find(definition);
            if (effect == null) return false;
            Remove(effect, stacks);
            return true;
        }

        // 즉시 효과마다 불리고 질의는 대개 비어 있어, 빈 질의는 순회와 정리 없이 바로 0을 돌려준다.
        internal int RemoveMatching(GameplayTagContainer query, ActiveGameplayEffect? except) =>
            query.IsEmpty ? 0 : RemoveWhere(query, except, static (definition, tags) => OwnsAny(definition, tags));

        // None 질의는 어떤 효과의 태그와도 맞지 않아 0을 돌려준다.
        internal int RemoveMatching(GameplayTag query) =>
            RemoveWhere(query, null, static (definition, tag) => definition.OwnedMatches(tag));

        // 두 질의 형식이 같은 제거 루프를 쓴다. 술어는 정적 람다라 처음 한 번만 만들어져 호출마다 할당하지 않는다.
        // 순회 중 제거는 표시만 하고 끝난 뒤 한 번에 뺀다.
        int RemoveWhere<TQuery>(TQuery query, ActiveGameplayEffect? except, Func<GameplayEffectDefinition, TQuery, bool> matches)
        {
            int removed = 0;
            using (new IterationScope(this))
            {
                for (int index = 0; index < _active.Count; index++)
                {
                    ActiveGameplayEffect effect = _active[index];
                    if (effect._pendingRemove || effect == except || !matches(effect.Definition, query)) continue;
                    Remove(effect, -1);
                    removed++;
                }
            }
            Sweep();
            return removed;
        }

        // notify가 false면 큐와 변화 알림 없이 조용히 뗀다. 시스템 해제가 쓴다.
        internal void RemoveAll(bool notify)
        {
            using (new IterationScope(this))
            {
                for (int index = 0; index < _active.Count; index++)
                {
                    ActiveGameplayEffect effect = _active[index];
                    if (effect._pendingRemove) continue;
                    if (notify) Remove(effect, -1);
                    else
                    {
                        // 어빌리티 종료 콜백이 같은 효과를 다시 떼도 찾지 못하도록 끄기 전에 제거 표시를 한다.
                        effect._pendingRemove = true;
                        if (!effect._inhibited) Deactivate(effect, false);
                    }
                }
            }
            Sweep();
        }

        // clock 시계에 속한 효과만 진행한다. 효과마다 따로 작업을 열어, 앞 효과의 알림이 다음 효과 전에 나간다.
        // 이번 진행 중에 새로 붙은 효과는 다음 진행부터 시간이 흐른다. 해제 여부는 알림까지 나간 뒤 작업 범위 밖에서 본다.
        internal void Tick(GameplayTag clock, float deltaTime)
        {
            int count = _active.Count;
            using (new IterationScope(this))
            {
                for (int index = 0; index < count; index++)
                {
                    ActiveGameplayEffect effect = _active[index];
                    if (effect._pendingRemove || effect.Definition.Clock != clock) continue;
                    if (!_system.BeginOperation()) break;
                    using (new AbilitySystem.OperationScope(_system))
                    {
                        Advance(effect, deltaTime);
                    }
                    if (_system.IsReleased) break;
                }
            }
            Sweep();
        }

        // 태그가 바뀐 작업이 끝날 때마다 불린다. 제거 조건이 맞으면 떼고, 유지 조건이 바뀌면 끄거나 다시 켠다.
        // 끄고 켤 때 주기 누적을 0으로 되돌린다(원작에서 기아 중 사망하면 누적을 비우는 것과 같다).
        internal void ReevaluateRequirements()
        {
            for (int round = 0; round < _maxReevaluateRounds; round++)
            {
                bool changed = false;
                using (new IterationScope(this))
                {
                    for (int index = 0; index < _active.Count; index++)
                    {
                        ActiveGameplayEffect effect = _active[index];
                        if (effect._pendingRemove) continue;
                        GameplayEffectDefinition definition = effect.Definition;
                        if (definition.IsRemovedBy(_system.Tags))
                        {
                            Remove(effect, -1);
                            changed = true;
                            continue;
                        }
                        GameplayTagRequirements ongoing = definition.OngoingRequirements;
                        if (ongoing.IsEmpty) continue;
                        bool met = ongoing.IsMetBy(_system.Tags);
                        if (met == !effect._inhibited) continue;
                        effect._periodBuffer = 0f;
                        if (met)
                        {
                            effect._inhibited = false;
                            Activate(effect);
                        }
                        else
                        {
                            // 끄는 중에 어빌리티 종료 콜백이 이 효과를 떼면 Remove가 한 번 더 끄지 않도록 먼저 억제로 표시한다.
                            effect._inhibited = true;
                            Deactivate(effect, true);
                        }
                        changed = true;
                    }
                }
                Sweep();
                if (!changed) return;
            }
        }

        // 주기 실행을 먼저 하고 만료를 본다. 주기는 원작 TickTrigger 그대로 buffer += dt/주기, buffer > 1(엄격)일 때마다 실행한다.
        // 지속시간은 원작 대시 쿨다운과 같은 카운트다운 max(0, 남은 시간 - dt)이고 0 이하면 만료다.
        void Advance(ActiveGameplayEffect effect, float deltaTime)
        {
            GameplayEffectDefinition definition = effect.Definition;
            if (definition.IsPeriodic)
            {
                if (effect._inhibited) effect._periodBuffer = 0f;
                else
                {
                    effect._periodBuffer += deltaTime / definition.Period;
                    while (effect._periodBuffer > 1f)
                    {
                        effect._periodBuffer -= 1f;
                        Execute(effect._specValue, effect._stacks, effect);
                        // 실행이 이 효과를 뗄 수 있어 실행마다 본다. 억제와 시스템 해제는 작업 밖에서만 바뀐다.
                        if (effect._pendingRemove) break;
                    }
                }
            }
            if (effect._pendingRemove || definition.DurationPolicy != GameplayEffectDurationPolicy.HasDuration) return;
            effect._remaining = Mathf.Max(0f, effect._remaining - deltaTime);
            if (effect._remaining <= 0f) Remove(effect, -1);
        }

        // UE 실행 순서: 정의의 수정자 크기를 모두 먼저 계산하고 Base에 적용한 뒤, 실행 계산을 순서대로 돌려 그 결과를 적용한다.
        // 주기 실행은 크기를 실행할 때마다 다시 읽는다(원작 DamageAction의 최대 체력 비율처럼). Override를 뺀 크기에는 층 수를 곱한다.
        void Execute(in GameplayEffectSpec spec, int stacks, ActiveGameplayEffect? source)
        {
            GameplayEffectDefinition definition = spec.Definition!;
            GameplayEffectExecutionOutput output = _system.RentOutput();
            float cueMagnitude = 0f;
            bool hasCueMagnitude = false;
            output.Clear();
            for (int index = 0; index < definition.ModifierCount; index++)
            {
                GameplayModifierInfo modifier = definition.ModifierAt(index);
                GameplayAttribute attribute = modifier.Attribute;
                float raw = modifier.Magnitude.Evaluate(spec, _system);
                output.Add(attribute, modifier.Op, modifier.Op == AttributeModifierOp.Override ? raw : raw * stacks);
            }
            ApplyOutput(output, spec, stacks, ref cueMagnitude, ref hasCueMagnitude);
            for (int index = 0; index < definition.ExecutionCount; index++)
            {
                GameplayEffectExecution? execution = definition.ExecutionAt(index);
                output.Clear();
                // 효과 에셋의 실행 계산 칸은 비어 있지 않다.
                execution!.Execute(new GameplayEffectExecutionContext(_system, spec, stacks), output);
                ApplyOutput(output, spec, stacks, ref cueMagnitude, ref hasCueMagnitude);
            }
            output.Clear();
            _system.EnqueueCues(definition, GameplayCueEvent.Executed, cueMagnitude, source != null ? source._stacks : stacks, spec.Context);
        }

        void ApplyOutput(GameplayEffectExecutionOutput output, in GameplayEffectSpec spec, int stacks, ref float cueMagnitude,
            ref bool hasCueMagnitude)
        {
            AttributeSet attributes = _system.Attributes;
            for (int index = 0; index < output.Count; index++)
            {
                GameplayEffectExecutionOutput.Entry entry = output.At(index);
                attributes.ExecuteModifier(new GameplayModifierEvaluation(entry._attribute, entry._op, entry._magnitude, spec, stacks));
                if (hasCueMagnitude) continue;
                hasCueMagnitude = true;
                cueMagnitude = entry._magnitude;
            }
        }

        // 효과를 켠다: 주기 효과가 아니면 수정자를 집계기에 넣고, 태그와 면역과 어빌리티를 주고, Added 큐를 보낸다.
        void Activate(ActiveGameplayEffect effect)
        {
            GameplayEffectDefinition definition = effect.Definition;
            if (!definition.IsPeriodic)
            {
                AttributeSet attributes = _system.Attributes;
                for (int index = 0; index < definition.ModifierCount; index++)
                {
                    GameplayModifierInfo modifier = definition.ModifierAt(index);
                    attributes.AddModifier(modifier.Attribute, effect._id, index, modifier.Op, modifier.Order, Scaled(effect, index, modifier.Op));
                }
            }
            _system.Tags.AddAll(definition.GrantedTags);
            _system.Immunities.AddAll(definition.GrantedImmunityTags);
            for (int index = 0; index < definition.GrantedAbilityCount; index++)
            {
                GameplayAbilityDefinition? ability = definition.GrantedAbilityAt(index);
                if (ability != null && _system.GiveAbility(ability)) effect._grantedAbilities.Add(ability);
            }
            _system.EnqueueCues(definition, GameplayCueEvent.Added, 0f, effect._stacks, effect._specValue.Context);
        }

        void Deactivate(ActiveGameplayEffect effect, bool notify)
        {
            GameplayEffectDefinition definition = effect.Definition;
            if (!definition.IsPeriodic)
            {
                AttributeSet attributes = _system.Attributes;
                for (int index = 0; index < definition.ModifierCount; index++)
                {
                    GameplayAttribute attribute = definition.ModifierAt(index).Attribute;
                    // 무효 핸들은 세트의 칸 표에 없어 Has가 false다.
                    if (attributes.Has(attribute)) attributes.RemoveModifiers(attribute, effect._id);
                }
            }
            _system.Tags.RemoveAll(definition.GrantedTags);
            _system.Immunities.RemoveAll(definition.GrantedImmunityTags);
            foreach (GameplayAbilityDefinition ability in effect._grantedAbilities) _system.RemoveAbility(ability);
            effect._grantedAbilities.Clear();
            if (notify) _system.EnqueueCues(definition, GameplayCueEvent.Removed, 0f, effect._stacks, effect._specValue.Context);
        }

        void Remove(ActiveGameplayEffect effect, int stacks)
        {
            if (effect._pendingRemove) return;
            GameplayEffectDefinition definition = effect.Definition;
            if (stacks > 0 && stacks < effect._stacks)
            {
                effect._stacks -= stacks;
                if (!effect._inhibited) UpdateMagnitudes(effect);
                _system.EnqueueEffectChange(new GameplayEffectChangeData(definition, effect._stacks, GameplayEffectChangeKind.StackChanged));
                return;
            }
            // UE처럼 끄기 전에 제거 표시를 해, 어빌리티 종료 콜백이 같은 효과를 다시 떼도 찾지 못하고 두 번 끄지 않는다.
            // 끄는 동안 순회 표시를 올려, 콜백 안의 다른 제거가 이 객체를 풀로 돌려 재사용하지 못하게 한다.
            // 콜백이 같은 효과를 다시 걸면 새 효과는 붙지만, 줄 어빌리티가 아직 회수 전이라 받지 못한다. 이 경우는 지원 범위 밖이다.
            effect._pendingRemove = true;
            using (new IterationScope(this))
            {
                if (!effect._inhibited) Deactivate(effect, true);
            }
            _system.EnqueueEffectChange(new GameplayEffectChangeData(definition, 0, GameplayEffectChangeKind.Removed));
            Sweep();
        }

        void UpdateMagnitudes(ActiveGameplayEffect effect)
        {
            GameplayEffectDefinition definition = effect.Definition;
            if (definition.IsPeriodic) return;
            AttributeSet attributes = _system.Attributes;
            for (int index = 0; index < definition.ModifierCount; index++)
            {
                GameplayModifierInfo modifier = definition.ModifierAt(index);
                attributes.SetModifierMagnitude(modifier.Attribute, effect._id, index, Scaled(effect, index, modifier.Op));
            }
        }

        // 원작 ModifierAction처럼 수정자 크기에 층 수를 곱한다. Override는 값 대입이라 곱하지 않는다.
        static float Scaled(ActiveGameplayEffect effect, int index, AttributeModifierOp op) =>
            op == AttributeModifierOp.Override ? effect._magnitudes[index] : effect._magnitudes[index] * effect._stacks;

        void ApplyAdditional(in GameplayEffectSpec spec)
        {
            GameplayEffectDefinition definition = spec.Definition!;
            for (int index = 0; index < definition.AdditionalEffectCount; index++)
            {
                GameplayEffectDefinition? additional = definition.AdditionalEffectAt(index);
                // UE AdditionalEffects 컴포넌트처럼 시전자와 문맥은 늘 잇고, 부모 정의가 켠 경우에만 SetByCaller 값도 넘긴다.
                // 효과 에셋의 추가 효과 칸은 비어 있지 않고 자기 자신을 가리키지 않는다.
                _system.ApplyEffect(spec.ForAdditional(additional!, definition.CopySetByCallersToAdditionalEffects));
            }
        }

        // 원작 AddBuff: 기존 우선순위가 높으면 거부, 같고 둘 다 시간제한이며 기존 경과 시간 > 새 총시간이면 기존 유지, 아니면 교체.
        // 원작 코드가 남은 시간이 아니라 경과 시간과 비교하므로 그대로 따른다.
        static EffectApplyResult ExclusiveOutcome(ActiveGameplayEffect other, GameplayEffectDefinition incoming)
        {
            GameplayEffectDefinition existing = other.Definition;
            if (existing.ExclusivePriority > incoming.ExclusivePriority) return EffectApplyResult.ExclusiveRejected;
            if (existing.ExclusivePriority == incoming.ExclusivePriority
                && existing.DurationPolicy == GameplayEffectDurationPolicy.HasDuration
                && incoming.DurationPolicy == GameplayEffectDurationPolicy.HasDuration
                && other.Elapsed > incoming.Duration)
                return EffectApplyResult.KeptExisting;
            return EffectApplyResult.Added;
        }

        ActiveGameplayEffect? FindExclusive(GameplayTag tag)
        {
            if (!tag.IsValid) return null;
            foreach (ActiveGameplayEffect effect in _active)
                if (!effect._pendingRemove && effect.Definition.ExclusiveTag == tag) return effect;
            return null;
        }

        // 원작 buffResist처럼 에셋 태그나 배타 태그, 또는 그 부모가 면역에 직접 있으면 막는다.
        // 배타 태그가 없으면 None이라 부모 순회가 바로 끝나 막지 않는다.
        bool IsImmune(GameplayEffectDefinition definition)
        {
            GameplayTagCountMap immunities = _system.Immunities;
            return immunities.HasAnyTagOrParentExplicit(definition.AssetTags) || immunities.HasTagOrParentExplicit(definition.ExclusiveTag);
        }

        static bool OwnsAny(GameplayEffectDefinition definition, GameplayTagContainer query)
        {
            for (int index = 0; index < query.Count; index++)
                if (definition.OwnedMatches(query[index])) return true;
            return false;
        }

        // 순회 중이 아닐 때 제거 표시된 효과를 목록에서 빼고 재사용 풀로 돌린다. 남은 효과의 순서는 지킨다.
        void Sweep()
        {
            if (_iterating > 0) return;
            int write = 0;
            for (int read = 0; read < _active.Count; read++)
            {
                ActiveGameplayEffect effect = _active[read];
                if (effect._pendingRemove)
                {
                    effect._specValue = default;
                    _pool.Push(effect);
                    continue;
                }
                _active[write++] = effect;
            }
            if (write < _active.Count) _active.RemoveRange(write, _active.Count - write);
        }

        // 효과 목록 순회 표시를 using 범위로 올리고 내린다. 정리는 호출자가 범위 밖에서 Sweep으로 따로 한다.
        readonly ref struct IterationScope
        {
            readonly ActiveEffectContainer _container;

            internal IterationScope(ActiveEffectContainer container)
            {
                _container = container;
                container._iterating++;
            }

            public void Dispose() => _container._iterating--;
        }
    }
}
