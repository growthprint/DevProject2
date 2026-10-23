#nullable enable
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 세션 소유자가 원작 키 기록을 넣은 순서로 저장하면서 빠르게 찾도록 순서 목록과 조회용 집합을 함께 둔다.
    // 명령으로 넣는 경로는 늘 둘을 같이 맞춘다. 소유자 안에서만 쓰고 메인 스레드에서만 쓴다.
    internal sealed class OrderedKeySet
    {
        readonly List<string> _order = new List<string>();
        readonly HashSet<string> _set = new HashSet<string>(StringComparer.Ordinal);

        // 서로 다른 키의 수다. 목록이 아니라 집합을 센다.
        internal int Count => _set.Count;

        internal bool Contains(string key) => _set.Contains(key);

        // 처음 보는 키만 끝에 붙인다. 새로 넣었을 때만 참이라, 호출자가 알릴지를 이 값으로 정한다.
        internal bool Add(string key)
        {
            if (!_set.Add(key)) return false;
            _order.Add(key);
            return true;
        }

        internal void Clear()
        {
            _order.Clear();
            _set.Clear();
        }

        // 불러온 목록으로 바꾼다. 기록의 목록은 검증된 불변 값이라 거르지 않고 그 순서 그대로 담는다.
        internal void Load(IReadOnlyList<string> keys)
        {
            Clear();
            foreach (string key in keys)
            {
                _set.Add(key);
                _order.Add(key);
            }
        }

        // 저장 구역은 받은 목록을 복사 없이 보관하므로 저장마다 새 배열로 넘긴다.
        internal string[] ToArray() => _order.ToArray();

        // 원작 List.Sort의 문화권 비교 대신 서수 비교로 순서 목록만 정렬한다. 집합은 순서가 없어 그대로다.
        internal void SortOrdinal() => _order.Sort(StringComparer.Ordinal);
    }
}
