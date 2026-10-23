#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Game.GAS
{
    // UE ExecutionCalculation처럼 여러 어트리뷰트를 읽어 결과 수정자를 내는 공유 에셋이다(예: 방어, 원소, 난이도를 거친 피해).
    // 여러 효과와 캐릭터가 같은 에셋을 쓰므로 호출별 상태를 필드에 두지 않는다. 결과는 output에 담고 시스템이 Base에 적용한다.
    public abstract class GameplayEffectExecution : ScriptableObject
    {
        public abstract void Execute(in GameplayEffectExecutionContext context, GameplayEffectExecutionOutput output);
    }

    // 실행 계산이 읽는 값이다. 어트리뷰트는 실행하는 순간의 현재값을 읽는다.
    public readonly struct GameplayEffectExecutionContext
    {
        internal GameplayEffectExecutionContext(AbilitySystem target, in GameplayEffectSpec spec, int stackCount)
        {
            Target = target;
            Spec = spec;
            StackCount = stackCount;
        }

        public AbilitySystem Target { get; }
        public GameplayEffectSpec Spec { get; }
        // 원작 DamageAction처럼 층 수를 곱하는 계산이 읽는다. 즉시 효과는 1이다.
        public int StackCount { get; }
        public AbilitySystem? Source => Spec.Source;
        public GameplayEffectDefinition? Definition => Spec.Definition;
        public GameplayEffectContext EffectContext => Spec.Context;

        public float TargetValue(GameplayAttribute attribute) => Target.Attributes.GetCurrent(attribute);
        public float TargetBase(GameplayAttribute attribute) => Target.Attributes.GetBase(attribute);

        // 시전자가 없거나 시전자 세트에 없는 어트리뷰트면 fallback을 돌려준다. 환경 피해처럼 시전자가 없는 효과가 있다.
        public float SourceValue(GameplayAttribute attribute, float fallback = 0f)
        {
            AbilitySystem? source = Spec.Source;
            return source != null && source.Attributes.Has(attribute) ? source.Attributes.GetCurrent(attribute) : fallback;
        }

        public bool TargetHasTag(GameplayTag tag) => Target.Tags.HasTag(tag);

        public bool SourceHasTag(GameplayTag tag)
        {
            AbilitySystem? source = Spec.Source;
            return source != null && source.Tags.HasTag(tag);
        }
    }

    // 실행 계산의 결과 수정자 목록이다. 시스템이 중첩 깊이마다 하나씩 재사용해 실행마다 할당하지 않는다.
    public sealed class GameplayEffectExecutionOutput
    {
        internal struct Entry
        {
            // 효과 목록이 결과를 Base에 적용할 때만 읽는 값 묶음이라 속성 없이 필드로 둔다.
            internal GameplayAttribute _attribute;
            internal AttributeModifierOp _op;
            internal float _magnitude;
        }

        readonly List<Entry> _entries = new List<Entry>();

        internal GameplayEffectExecutionOutput()
        {
        }

        public void Add(GameplayAttribute attribute, AttributeModifierOp op, float magnitude) =>
            _entries.Add(new Entry { _attribute = attribute, _op = op, _magnitude = magnitude });

        internal int Count => _entries.Count;
        internal Entry At(int index) => _entries[index];
        internal void Clear() => _entries.Clear();
    }
}
