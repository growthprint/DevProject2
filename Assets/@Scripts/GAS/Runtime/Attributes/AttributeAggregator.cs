#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Game.GAS
{
    // 어트리뷰트 하나에 걸린 지속 효과 수정자 목록과 현재값 계산이다. 계산은 원작 ItemStatsSystem.Stat.Recalculate를 그대로 따른다.
    // 수정자는 (순서, 효과 번호, 수정자 번호)로 정렬해 두므로, 같은 순서끼리는 효과를 적용한 순서를 지킨다.
    // 게임의 아이템 수치(총 부착물 modifier)도 같은 원작 공식을 쓰도록 추가와 계산을 공개한다.
    public sealed class AttributeAggregator
    {
        struct Entry
        {
            // 이 클래스 안에서만 쓰는 값 묶음이라 속성 없이 필드로 둔다. 크기를 고칠 때는 꺼내 고친 뒤 다시 넣는다.
            internal int _effectId;
            internal int _modifierIndex;
            internal AttributeModifierOp _op;
            internal int _order;
            internal float _magnitude;
        }

        readonly List<Entry> _entries = new List<Entry>();

        internal bool IsEmpty => _entries.Count == 0;

        // 아이템 수치 계산도 쓰므로 공개한다. 효과 번호 자리에는 수정자 출처의 순번을 넘긴다.
        public void Add(int effectId, int modifierIndex, AttributeModifierOp op, int order, float magnitude)
        {
            var entry = new Entry { _effectId = effectId, _modifierIndex = modifierIndex, _op = op, _order = order, _magnitude = magnitude };
            int index = _entries.Count;
            while (index > 0 && Compare(_entries[index - 1], entry) > 0) index--;
            _entries.Insert(index, entry);
        }

        // 층이 바뀌면 크기만 고친다. 순서는 그대로라 다시 정렬하지 않는다.
        internal void SetMagnitude(int effectId, int modifierIndex, float magnitude)
        {
            for (int index = 0; index < _entries.Count; index++)
            {
                Entry entry = _entries[index];
                if (entry._effectId != effectId || entry._modifierIndex != modifierIndex) continue;
                entry._magnitude = magnitude;
                _entries[index] = entry;
                return;
            }
        }

        // 람다 캡처로 호출마다 대리자가 생기지 않게 직접 순회한다.
        internal bool RemoveEffect(int effectId)
        {
            bool removed = false;
            for (int index = _entries.Count - 1; index >= 0; index--)
            {
                if (_entries[index]._effectId != effectId) continue;
                _entries.RemoveAt(index);
                removed = true;
            }
            return removed;
        }

        // 원작 공식 (Base + ΣAdd) × max(0, 1+ΣPctAdd) × Π max(0, 1+PctMul)이다. 같은 순서의 연속된 PercentageAdd만 묶는다.
        // 수정자가 없으면 base를 비트 그대로 돌려준다. float 연산 순서를 원작과 맞추려고 지역 변수와 계산 순서를 바꾸지 않는다.
        // 아이템 수치 계산도 쓰므로 공개한다.
        public float Evaluate(float baseValue)
        {
            float result = baseValue;
            float percentageAdd = 0f;
            bool percentageAdding = false;
            int lastOrder = int.MinValue;
            for (int index = 0; index < _entries.Count; index++)
            {
                Entry entry = _entries[index];
                if (percentageAdding && (entry._order != lastOrder || entry._op != AttributeModifierOp.PercentageAdd))
                {
                    result *= Mathf.Max(0f, 1f + percentageAdd);
                    percentageAdd = 0f;
                    percentageAdding = false;
                }
                lastOrder = entry._order;
                switch (entry._op)
                {
                    case AttributeModifierOp.Add:
                        result += entry._magnitude;
                        break;
                    case AttributeModifierOp.PercentageAdd:
                        percentageAdding = true;
                        percentageAdd += entry._magnitude;
                        break;
                    case AttributeModifierOp.PercentageMultiply:
                        result *= Mathf.Max(0f, 1f + entry._magnitude);
                        break;
                    case AttributeModifierOp.Override:
                        result = entry._magnitude;
                        break;
                }
            }
            if (percentageAdding) result *= Mathf.Max(0f, 1f + percentageAdd);
            return result;
        }

        // 즉시 효과가 Base에 한 번 적용하는 계산이다. 퍼센트 연산은 지속 효과와 같은 max(0, 1+값) 규칙을 쓴다.
        internal static float ApplyToBase(float baseValue, AttributeModifierOp op, float magnitude) => op switch
        {
            AttributeModifierOp.Add => baseValue + magnitude,
            AttributeModifierOp.PercentageAdd => baseValue * Mathf.Max(0f, 1f + magnitude),
            AttributeModifierOp.PercentageMultiply => baseValue * Mathf.Max(0f, 1f + magnitude),
            AttributeModifierOp.Override => magnitude,
            _ => baseValue
        };

        static int Compare(Entry left, Entry right)
        {
            if (left._order != right._order) return left._order < right._order ? -1 : 1;
            if (left._effectId != right._effectId) return left._effectId < right._effectId ? -1 : 1;
            return left._modifierIndex.CompareTo(right._modifierIndex);
        }
    }
}
