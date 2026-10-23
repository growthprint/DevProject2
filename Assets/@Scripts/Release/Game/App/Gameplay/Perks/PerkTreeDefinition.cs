#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 관계 그래프의 연결 하나다. From이 부모, To가 자식이며 둘 다 트리 특성 목록의 번호다.
    [Serializable]
    public struct PerkConnection
    {
        [SerializeField] int _from;
        [SerializeField] int _to;

        internal int From => _from;
        internal int To => _to;
    }

    // 원작 PerkLevelLineNode다. 연결이 없는 레벨 구분선이고, 원작처럼 노드 이름을 그대로 표시한다.
    [Serializable]
    public sealed class PerkLevelLine
    {
        [SerializeField] string _displayName = string.Empty;
        [SerializeField] Vector2 _position;

        internal string DisplayName => _displayName;
        internal Vector2 Position => _position;
    }

    // 원작 LevelManager 프리팹의 PerkTree 컴포넌트 하나와 그 관계 그래프다. 가져오기 도구가 만들고 실행 중에는 읽기만 한다.
    public sealed class PerkTreeDefinition : ScriptableObject
    {
        [SerializeField] string _id = string.Empty;
        [SerializeField] bool _horizontal;
        [SerializeField] PerkDefinition[] _perks = Array.Empty<PerkDefinition>();
        // 특성마다 그래프의 첫 노드로 들어오는 연결만 둔다. 원작 부모 확인이 첫 노드만 보기 때문이다.
        [SerializeField] PerkConnection[] _connections = Array.Empty<PerkConnection>();
        [SerializeField] PerkLevelLine[] _levelLines = Array.Empty<PerkLevelLine>();

        // 원작 perkTreeID다. 저장 키와 진입점이 이 ID로 트리를 찾는다.
        internal string Id => _id;
        internal string DisplayNameKey => "PerkTree_" + _id;
        internal bool Horizontal => _horizontal;
        internal IReadOnlyList<PerkDefinition> Perks => _perks;
        internal IReadOnlyList<PerkConnection> Connections => _connections;
        internal IReadOnlyList<PerkLevelLine> LevelLines => _levelLines;

        // 원작 GameObject 이름으로 특성 번호를 찾는다. 트리 하나가 특성 44개 이하라 색인 없이 훑는다.
        internal bool TryGetPerkIndex(string perkName, out int index)
        {
            for (int i = 0; i < _perks.Length; i++)
            {
                if (!string.Equals(_perks[i].Name, perkName, StringComparison.Ordinal)) continue;
                index = i;
                return true;
            }
            index = -1;
            return false;
        }
    }
}
