#nullable enable
using System;
using System.Collections.Generic;

namespace Game.GAS
{
    // 태그 하나가 생기거나 사라졌다는 알림 값이다. 부모 태그의 변화도 따로 한 번씩 나간다.
    public readonly struct GameplayTagChangeData
    {
        public GameplayTagChangeData(GameplayTag tag, bool present)
        {
            Tag = tag;
            Present = present;
        }

        public GameplayTag Tag { get; }
        public bool Present { get; }
    }

    // 캐릭터가 지금 가진 태그의 개수 표다. 같은 태그를 효과 둘이 주면 둘 다 빠져야 사라진다.
    // 자식이 있으면 부모도 있는 것으로 세어(A.B가 있으면 A도 있음) 부모 조회가 배열 읽기 한 번이다.
    public sealed class GameplayTagCountMap
    {
        // 태그 번호로 바로 읽는 배열이다. 직접 넣은 개수와 자식까지 합친 개수를 따로 센다. 새 태그가 생기면 늘린다.
        int[] _explicit = Array.Empty<int>();
        int[] _inclusive = Array.Empty<int>();
        // 알림을 모아 보내는 동안 바뀐 태그와 바뀌기 전 있었는지다. 같은 작업 안에서 생겼다 사라진 태그는 알리지 않는다.
        readonly List<int> _changed = new List<int>();
        bool[] _changeRecorded = Array.Empty<bool>();
        bool[] _wasPresent = Array.Empty<bool>();

        public bool HasTag(GameplayTag tag) => tag.Id < _inclusive.Length && _inclusive[tag.Id] > 0;

        public bool HasTagExact(GameplayTag tag) => tag.Id < _explicit.Length && _explicit[tag.Id] > 0;

        // 직접 넣은 개수다. 자식이 준 개수는 세지 않는다.
        public int GetCount(GameplayTag tag) => tag.Id < _explicit.Length ? _explicit[tag.Id] : 0;

        public bool HasAny(GameplayTagContainer tags)
        {
            for (int index = 0; index < tags.Count; index++)
                if (HasTag(tags[index])) return true;
            return false;
        }

        public bool HasAll(GameplayTagContainer tags)
        {
            for (int index = 0; index < tags.Count; index++)
                if (!HasTag(tags[index])) return false;
            return true;
        }

        // 면역과 차단 검사용이다. 효과 태그가 A.B.C일 때 A.B.C, A.B, A 중 하나라도 직접 들어 있으면 막힌다.
        internal bool HasTagOrParentExplicit(GameplayTag tag)
        {
            for (GameplayTag current = tag; current.IsValid; current = current.Parent)
                if (current.Id < _explicit.Length && _explicit[current.Id] > 0) return true;
            return false;
        }

        // 면역과 차단 검사용이다. 목록의 태그 하나라도 그 자신이나 부모가 직접 들어 있으면 막힌다.
        internal bool HasAnyTagOrParentExplicit(GameplayTagContainer tags)
        {
            for (int index = 0; index < tags.Count; index++)
                if (HasTagOrParentExplicit(tags[index])) return true;
            return false;
        }

        internal void Add(GameplayTag tag)
        {
            Ensure(tag.Id);
            _explicit[tag.Id]++;
            for (GameplayTag current = tag; current.IsValid; current = current.Parent)
            {
                if (_inclusive[current.Id] == 0) Record(current.Id, false);
                _inclusive[current.Id]++;
            }
        }

        internal void Remove(GameplayTag tag)
        {
            // 소유자는 넣은 유효 태그만 넣은 만큼 빼므로 개수가 음수로 내려가지 않는다.
            _explicit[tag.Id]--;
            for (GameplayTag current = tag; current.IsValid; current = current.Parent)
            {
                if (_inclusive[current.Id] == 1) Record(current.Id, true);
                _inclusive[current.Id]--;
            }
        }

        internal void AddAll(GameplayTagContainer tags)
        {
            for (int index = 0; index < tags.Count; index++) Add(tags[index]);
        }

        internal void RemoveAll(GameplayTagContainer tags)
        {
            for (int index = 0; index < tags.Count; index++) Remove(tags[index]);
        }

        internal int PendingChangeCount => _changed.Count;

        // 기록된 태그 중 지금 있음 여부가 처음과 달라진 것만 알림 값으로 돌려준다.
        internal bool TryGetPendingChange(int index, out GameplayTagChangeData change)
        {
            int id = _changed[index];
            bool present = _inclusive[id] > 0;
            change = new GameplayTagChangeData(new GameplayTag(id), present);
            return present != _wasPresent[id];
        }

        internal void ClearPendingChanges()
        {
            foreach (int id in _changed) _changeRecorded[id] = false;
            _changed.Clear();
        }

        void Record(int id, bool wasPresent)
        {
            if (_changeRecorded[id]) return;
            _changeRecorded[id] = true;
            _wasPresent[id] = wasPresent;
            _changed.Add(id);
        }

        // 태그 번호는 늘 태그 표 크기보다 작다. 표 전체 크기까지 한 번에 늘려, 새 태그가 생길 때마다 배열을 다시 만들지 않게 한다.
        void Ensure(int id)
        {
            if (id < _explicit.Length) return;
            int size = GameplayTag.Count;
            Array.Resize(ref _explicit, size);
            Array.Resize(ref _inclusive, size);
            Array.Resize(ref _changeRecorded, size);
            Array.Resize(ref _wasPresent, size);
        }
    }
}
