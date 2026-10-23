#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace Game.App
{
    // 원작 CraftingFormulaCollection이다. 제조식을 원작 순서로 담고, 그 순서가 제조 화면 목록 순서다.
    // 원작은 설계도 아이템의 ItemSetting_Formula.formulaID로 제조식을 찾는다. 여기서는 가져오기 도구가 그 연결을 아이템 ID 표로 옮긴다.
    [CreateAssetMenu(menuName = "Settings/Crafting Formula Collection")]
    public sealed class CraftingFormulaCollection : ScriptableObject
    {
        // 설계도 아이템 하나와 그 제조식 ID다. 원작 결함을 그대로 옮기려고(사용자 결정 2026-10-21), 없는 제조식을 가리키는
        // 원작 아이템 6개도 가져오기 도구가 넣는다. 그 아이템은 등록하면 사라지고 아무것도 해금하지 않는다.
        [Serializable]
        internal sealed class FormulaItemEntry
        {
            [SerializeField] int _itemId;
            [SerializeField] string _formulaId = "";

            internal int ItemId => _itemId;
            internal string FormulaId => _formulaId;
        }

        [SerializeField] CraftingFormula[] _formulas = Array.Empty<CraftingFormula>();
        [SerializeField] FormulaItemEntry[] _formulaItems = Array.Empty<FormulaItemEntry>();

        // 첫 조회 때 만드는 제조식 ID 색인과 설계도 아이템 색인이다. 등록 창과 "등록됨" 표시가 칸마다 조회하므로 목록을 다시 훑지 않는다.
        // 다시 가져오면 배열이 새로 생기므로 색인을 만든 배열과 비교해 낡은 색인을 버린다(OriginalQuestCatalog와 같은 규칙).
        [NonSerialized] Dictionary<string, CraftingFormula>? _formulaLookup;
        [NonSerialized] CraftingFormula[]? _formulaLookupSource;
        [NonSerialized] Dictionary<int, string>? _itemLookup;
        [NonSerialized] FormulaItemEntry[]? _itemLookupSource;

        public IReadOnlyList<CraftingFormula> Formulas => _formulas;

        // 원작 TryGetFormula처럼 ID가 같은 첫 제조식을 찾는다. 빈 ID는 원작처럼 찾지 못한 것으로 본다.
        public bool TryGet(string id, [NotNullWhen(true)] out CraftingFormula? formula)
        {
            formula = null;
            if (string.IsNullOrEmpty(id)) return false;
            if (_formulaLookup == null || !ReferenceEquals(_formulaLookupSource, _formulas))
            {
                // 같은 ID가 겹치면 앞 항목을 남긴다. 예전 선형 조회가 첫 항목을 돌려준 것과 같다.
                _formulaLookup = new Dictionary<string, CraftingFormula>(_formulas.Length, StringComparer.Ordinal);
                foreach (CraftingFormula candidate in _formulas) _formulaLookup.TryAdd(candidate.Id, candidate);
                _formulaLookupSource = _formulas;
            }
            return _formulaLookup.TryGetValue(id, out formula);
        }

        // 설계도 등록과 "등록됨" 표시가 아이템 종류로 제조식을 찾는다. 같은 아이템이 겹치면 앞 항목이다.
        public bool TryGetFormulaOfItem(int itemId, out string formulaId)
        {
            if (_itemLookup == null || !ReferenceEquals(_itemLookupSource, _formulaItems))
            {
                _itemLookup = new Dictionary<int, string>(_formulaItems.Length);
                foreach (FormulaItemEntry entry in _formulaItems) _itemLookup.TryAdd(entry.ItemId, entry.FormulaId);
                _itemLookupSource = _formulaItems;
            }
            if (_itemLookup.TryGetValue(itemId, out string? found))
            {
                formulaId = found;
                return true;
            }
            formulaId = "";
            return false;
        }

#if UNITY_EDITOR
        // 인스펙터가 배열을 제자리에서 고치면 배열 비교로는 알 수 없으므로 두 색인을 비운다.
        void OnValidate()
        {
            _formulaLookup = null;
            _formulaLookupSource = null;
            _itemLookup = null;
            _itemLookupSource = null;
        }
#endif
    }
}
