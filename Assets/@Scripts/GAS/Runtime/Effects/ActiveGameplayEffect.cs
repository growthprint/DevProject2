#nullable enable
using System;
using System.Collections.Generic;

namespace Game.GAS
{
    // 대상에게 붙어 있는 지속 효과 하나다. 원작 Buff 인스턴스의 층(CurrentLayers)과 남은 시간에 해당한다.
    // 시스템이 객체를 재사용하므로 밖에서 참조를 오래 들고 있지 않는다. 값은 읽기만 한다.
    public sealed class ActiveGameplayEffect
    {
        // 같은 어셈블리의 효과 목록이 직접 읽고 쓰는 상태라 internal 필드로 두고, 밖에는 아래 읽기 속성만 연다.
        internal int _id;
        internal GameplayEffectSpec _specValue;
        internal int _stacks;
        internal float _remaining;
        internal float _periodBuffer;
        internal bool _inhibited;
        internal bool _pendingRemove;
        // 적용할 때 한 번 계산한 수정자 크기다. 층 수를 곱하기 전 값이라 층이 바뀌어도 다시 계산하지 않는다.
        internal float[] _magnitudes = Array.Empty<float>();
        // 이 효과가 새로 준 어빌리티만 기록해, 떨어질 때 원래 있던 어빌리티는 회수하지 않는다.
        internal readonly List<GameplayAbilityDefinition> _grantedAbilities = new List<GameplayAbilityDefinition>();

        internal ActiveGameplayEffect()
        {
        }

        public GameplayEffectDefinition Definition => _specValue.Definition!;
        public GameplayEffectSpec Spec => _specValue;
        public int StackCount => _stacks;
        public float Duration => Definition.Duration;
        public float TimeRemaining => _remaining;
        // 원작 Buff.CurrentLifeTime에 해당한다. 시간 제한이 없는 효과는 0이다.
        public float Elapsed => Definition.DurationPolicy == GameplayEffectDurationPolicy.HasDuration ? Definition.Duration - _remaining : 0f;
        public bool IsInhibited => _inhibited;

        internal void Reset(int id, in GameplayEffectSpec spec)
        {
            _id = id;
            _specValue = spec;
            _stacks = 1;
            GameplayEffectDefinition definition = spec.Definition!;
            _remaining = definition.DurationPolicy == GameplayEffectDurationPolicy.HasDuration ? definition.Duration : 0f;
            _periodBuffer = 0f;
            _inhibited = false;
            _pendingRemove = false;
            if (_magnitudes.Length < definition.ModifierCount) _magnitudes = new float[definition.ModifierCount];
            _grantedAbilities.Clear();
        }
    }

    // 지속 효과가 붙거나 층이 바뀌거나 떨어졌다는 알림 값이다.
    public readonly struct GameplayEffectChangeData
    {
        public GameplayEffectChangeData(GameplayEffectDefinition definition, int stackCount, GameplayEffectChangeKind kind)
        {
            Definition = definition;
            StackCount = stackCount;
            Kind = kind;
        }

        public GameplayEffectDefinition Definition { get; }
        public int StackCount { get; }
        public GameplayEffectChangeKind Kind { get; }
    }
}
