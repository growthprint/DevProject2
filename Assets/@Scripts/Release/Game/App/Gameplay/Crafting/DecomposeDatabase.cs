#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 DecomposeDatabase의 분해식 목록이다. 원작은 무효 항목도 담고 사전에서 뒤의 항목이 앞을 덮는다.
    // 여기서는 가져오기 도구가 유효 항목만, 같은 아이템은 뒤의 것만 남겨 아이템마다 한 항목이 되게 한다.
    [CreateAssetMenu(menuName = "Settings/Decompose Database")]
    public sealed class DecomposeDatabase : ScriptableObject
    {
        [SerializeField] DecomposeFormula[] _entries = Array.Empty<DecomposeFormula>();

        // 첫 조회 때 만드는 아이템 ID 색인이다. 분해·등록 창은 칸마다 조회하므로 목록 전체를 다시 훑지 않는다.
        // 다시 가져오면 배열이 새로 생기므로 색인을 만든 배열과 비교해 낡은 색인을 버린다(OriginalQuestCatalog와 같은 규칙).
        [NonSerialized] Dictionary<int, Cost>? _lookup;
        [NonSerialized] DecomposeFormula[]? _lookupSource;

        // 원작 GetFormula와 valid 확인을 합친 것이다. 분해식이 없으면 기본 Cost와 false다.
        public bool TryGet(int itemId, out Cost result)
        {
            if (_lookup == null || !ReferenceEquals(_lookupSource, _entries))
            {
                _lookup = BuildLookup(_entries);
                _lookupSource = _entries;
            }
            return _lookup.TryGetValue(itemId, out result);
        }

#if UNITY_EDITOR
        // 인스펙터가 배열을 제자리에서 고치면 배열 비교로는 알 수 없으므로 색인을 비운다.
        void OnValidate()
        {
            _lookup = null;
            _lookupSource = null;
        }
#endif

        // 같은 아이템이 겹치면 앞 항목을 남긴다. 예전 선형 조회가 첫 항목을 돌려준 것과 같다.
        static Dictionary<int, Cost> BuildLookup(DecomposeFormula[] entries)
        {
            var built = new Dictionary<int, Cost>(entries.Length);
            foreach (DecomposeFormula entry in entries)
                built.TryAdd(entry.Item, entry.Result);
            return built;
        }
    }
}
