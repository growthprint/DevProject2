#nullable enable
using System.Collections.Generic;

namespace Game.GAS
{
    // 즉시 효과의 수정자 하나가 Base에 적용되는 순간의 정보다. AttributeSet 훅이 받는다.
    public readonly struct GameplayModifierEvaluation
    {
        internal GameplayModifierEvaluation(GameplayAttribute attribute, AttributeModifierOp op, float magnitude, in GameplayEffectSpec spec,
            int stackCount)
        {
            Attribute = attribute;
            Op = op;
            Magnitude = magnitude;
            Spec = spec;
            StackCount = stackCount;
        }

        public GameplayAttribute Attribute { get; }
        public AttributeModifierOp Op { get; }
        public float Magnitude { get; }
        public GameplayEffectSpec Spec { get; }
        public int StackCount { get; }
        public GameplayEffectDefinition? Effect => Spec.Definition;
        public AbilitySystem? Source => Spec.Source;

        internal GameplayModifierEvaluation WithMagnitude(float magnitude) => new GameplayModifierEvaluation(Attribute, Op, magnitude, Spec, StackCount);
    }

    // UE AttributeSet처럼 어트리뷰트마다 Base와 Current를 갖는다. Current는 Base에 지속 효과 수정자를 적용한 값이다.
    // 자동 clamp는 하지 않는다. 최대치나 사망 바닥 같은 규칙은 게임 쪽 하위 클래스가 훅에서 정한다.
    public abstract class AttributeSet
    {
        readonly GameplayAttribute[] _attributes;
        // 어트리뷰트 번호로 이 세트의 칸을 바로 찾는 표다. 값은 칸 번호 + 1이고 0은 이 세트에 없다는 뜻이다.
        readonly int[] _slots;
        readonly float[] _base;
        readonly float[] _current;
        // 지속 효과가 걸린 어트리뷰트만 집계기를 만든다. 대부분의 스탯은 수정자가 없어 Base를 그대로 쓴다.
        readonly AttributeAggregator?[] _aggregators;
        AbilitySystem? _system;

        protected AttributeSet(IReadOnlyList<GameplayAttribute> attributes)
        {
            _attributes = new GameplayAttribute[attributes.Count];
            int maximumId = 0;
            for (int index = 0; index < attributes.Count; index++)
            {
                _attributes[index] = attributes[index];
                if (attributes[index].Id > maximumId) maximumId = attributes[index].Id;
            }
            _slots = new int[maximumId + 1];
            // 하위 클래스 목록에는 무효 핸들과 중복이 없다. 게임 세트의 목록은 에셋 계약 테스트가 확인한다.
            for (int slot = 0; slot < _attributes.Length; slot++) _slots[_attributes[slot].Id] = slot + 1;
            _base = new float[_attributes.Length];
            _current = new float[_attributes.Length];
            _aggregators = new AttributeAggregator?[_attributes.Length];
        }

        public int Count => _attributes.Length;
        public GameplayAttribute AttributeAt(int slot) => _attributes[slot];
        public bool Has(GameplayAttribute attribute) => attribute.Id < _slots.Length && _slots[attribute.Id] != 0;

        // 세트에 없는 어트리뷰트를 읽으면 0을 돌려준다.
        public float GetBase(GameplayAttribute attribute) => TrySlot(attribute, out int slot) ? _base[slot] : 0f;

        public float GetCurrent(GameplayAttribute attribute) => TrySlot(attribute, out int slot) ? _current[slot] : 0f;

        protected AbilitySystem? AbilitySystem => _system;

        internal void Bind(AbilitySystem system)
        {
            _system = system;
        }

        internal bool TrySlot(GameplayAttribute attribute, out int slot)
        {
            slot = SlotOf(attribute);
            return slot >= 0;
        }

        // 이 세트에 없는 어트리뷰트는 -1이다. 결과를 바로 칸 번호로 쓰는 쓰기 경로는 이 세트에 있는 어트리뷰트만 받는다.
        // 호출 코드의 어트리뷰트 상수와 효과 수정자, 실행 계산 결과는 모두 이 세트에서 고른 것이다.
        int SlotOf(GameplayAttribute attribute) => Has(attribute) ? _slots[attribute.Id] - 1 : -1;

        internal float CurrentAt(int slot) => _current[slot];

        internal void SetBase(GameplayAttribute attribute, float value)
        {
            int slot = SlotOf(attribute);
            CommitBase(slot, value);
        }

        internal void AddModifier(GameplayAttribute attribute, int effectId, int modifierIndex, AttributeModifierOp op, int order, float magnitude)
        {
            int slot = SlotOf(attribute);
            (_aggregators[slot] ??= new AttributeAggregator()).Add(effectId, modifierIndex, op, order, magnitude);
            Recompute(slot);
        }

        internal void SetModifierMagnitude(GameplayAttribute attribute, int effectId, int modifierIndex, float magnitude)
        {
            // 켜진 효과의 수정자만 오므로 Activate가 이 칸의 집계기를 이미 만들었다.
            int slot = SlotOf(attribute);
            AttributeAggregator? aggregator = _aggregators[slot];
            aggregator!.SetMagnitude(effectId, modifierIndex, magnitude);
            Recompute(slot);
        }

        internal void RemoveModifiers(GameplayAttribute attribute, int effectId)
        {
            int slot = SlotOf(attribute);
            AttributeAggregator? aggregator = _aggregators[slot];
            if (aggregator == null || !aggregator.RemoveEffect(effectId)) return;
            Recompute(slot);
        }

        // UE 즉시 효과 순서다: PreGameplayEffectExecute(거부나 크기 변경) → Base 계산 → PreAttributeBaseChange → Current 재계산 →
        // PostGameplayEffectExecute. 메타 어트리뷰트(받은 피해 등)는 하위 클래스가 Post 훅에서 다른 어트리뷰트로 옮긴다.
        internal void ExecuteModifier(in GameplayModifierEvaluation data)
        {
            int slot = SlotOf(data.Attribute);
            float magnitude = data.Magnitude;
            if (!PreGameplayEffectExecute(data, ref magnitude)) return;
            CommitBase(slot, AttributeAggregator.ApplyToBase(_base[slot], data.Op, magnitude));
            PostGameplayEffectExecute(data.WithMagnitude(magnitude));
        }

        // 훅 안에서 다른 어트리뷰트를 정할 때 쓴다(예: 받은 피해를 체력으로 옮기고 메타 값을 0으로 되돌리기).
        protected void SetBaseFromHook(GameplayAttribute attribute, float value) => SetBase(attribute, value);

        // false를 돌려주면 이 수정자를 적용하지 않는다. magnitude를 바꾸면 바뀐 값이 적용된다.
        protected virtual bool PreGameplayEffectExecute(in GameplayModifierEvaluation data, ref float magnitude) => true;

        protected virtual void PostGameplayEffectExecute(in GameplayModifierEvaluation data)
        {
        }

        // Base가 저장되기 직전에 값을 고칠 수 있다(예: 현재 체력을 0과 최대치 사이로).
        protected virtual float PreAttributeBaseChange(GameplayAttribute attribute, float newBase) => newBase;

        // Current가 저장되기 직전에 값을 고칠 수 있다. 수정자 계산 결과에도 적용된다.
        protected virtual float PreAttributeChange(GameplayAttribute attribute, float newCurrent) => newCurrent;

        // Current가 실제로 바뀐 뒤 불린다. 최대치가 줄어 현재값을 맞추는 일 같은 연쇄 규칙을 둔다.
        protected virtual void PostAttributeChange(GameplayAttribute attribute, float oldCurrent, float newCurrent)
        {
        }

        void CommitBase(int slot, float value)
        {
            _base[slot] = PreAttributeBaseChange(_attributes[slot], value);
            Recompute(slot);
        }

        // 수정자가 없으면 Base를 비트 그대로 Current에 둔다. 값이 바뀌었을 때만 시스템에 알림을 맡긴다.
        void Recompute(int slot)
        {
            float old = _current[slot];
            AttributeAggregator? aggregator = _aggregators[slot];
            float value = aggregator == null || aggregator.IsEmpty ? _base[slot] : aggregator.Evaluate(_base[slot]);
            value = PreAttributeChange(_attributes[slot], value);
            _current[slot] = value;
            if (old.Equals(value)) return;
            // 값은 시스템에 묶인 뒤의 작업에서만 바뀌므로 시스템이 늘 있다.
            _system!.RecordAttributeChange(slot, old);
            PostAttributeChange(_attributes[slot], old, value);
        }
    }
}
