#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace Game.App
{
    // 원작 QuestRelation 그래프의 노드 하나다. 대리 노드는 들어오는 연결을 받지 않고 선행 판정의 노드로도 치지 않는다.
    [Serializable]
    public sealed class QuestRelationNodeData
    {
#pragma warning disable CS0414
        // 원본 그래프 기록이다. 실행은 정의의 선행 ID만 쓰고, 가져오기 대조와 테스트는 직렬화 값으로 읽는다.
        [SerializeField] int _questId = 0;
        [SerializeField] bool _proxy = false;
#pragma warning restore CS0414
    }

    // 원작 QuestRelation 그래프의 연결 하나다. 출발과 도착은 노드 목록의 번호다.
    [Serializable]
    public sealed class QuestRelationLinkData
    {
#pragma warning disable CS0414
        // 노드와 같은 이유로 실행 코드가 읽지 않는 원본 기록이라 경고를 이 두 필드에서만 끈다.
        [SerializeField] int _source = 0;
        [SerializeField] int _target = 0;
#pragma warning restore CS0414
    }

    // 원작 QuestCollection 315개와 관계 그래프를 순수 데이터로 담는다. 프리팹 참조가 없어 씬과 관계없이 QuestManager가 읽는다.
    // 내용은 Editor의 OriginalQuestImporter가 원작 추출본에서 다시 만든다.
    public sealed class OriginalQuestCatalog : ScriptableObject
    {
        // 원작 QuestCollection 순서다. 창구 목록이 원작과 같은 순서로 보이도록 정렬하지 않는다.
        [SerializeField] OriginalQuestDefinition[] _quests = Array.Empty<OriginalQuestDefinition>();
        [SerializeField] QuestRelationNodeData[] _relationNodes = Array.Empty<QuestRelationNodeData>();
        [SerializeField] QuestRelationLinkData[] _relationLinks = Array.Empty<QuestRelationLinkData>();

        // 첫 조회 때 만드는 ID 색인이다. 다시 가져오면 배열이 새로 생기므로 색인을 만든 배열과 비교해 낡은 색인을 버린다.
        [NonSerialized] Dictionary<int, OriginalQuestDefinition>? _lookup;
        [NonSerialized] OriginalQuestDefinition[]? _lookupSource;

        public IReadOnlyList<OriginalQuestDefinition> Quests => _quests;

        // 퀘스트 ID 조회의 유일한 경로다. 던지지도 기록하지도 않으며, 정의 없는 저장 퀘스트의 보고는 호출자가 맡는다.
        public bool TryGet(int id, [NotNullWhen(true)] out OriginalQuestDefinition? definition)
        {
            if (_lookup == null || !ReferenceEquals(_lookupSource, _quests))
            {
                _lookup = BuildLookup(_quests);
                _lookupSource = _quests;
            }
            return _lookup.TryGetValue(id, out definition);
        }

#if UNITY_EDITOR
        // 인스펙터가 배열을 제자리에서 고치면 배열 비교로는 알 수 없으므로 색인을 비운다.
        void OnValidate()
        {
            _lookup = null;
            _lookupSource = null;
        }
#endif

        // 중복 ID는 가져오기 도구가 쓰기 전에 거부한다. 그래도 겹치면 던지지 않고 앞 항목을 남긴다.
        static Dictionary<int, OriginalQuestDefinition> BuildLookup(OriginalQuestDefinition[] quests)
        {
            var built = new Dictionary<int, OriginalQuestDefinition>(quests.Length);
            foreach (OriginalQuestDefinition quest in quests)
                built.TryAdd(quest.Id, quest);
            return built;
        }
    }
}
