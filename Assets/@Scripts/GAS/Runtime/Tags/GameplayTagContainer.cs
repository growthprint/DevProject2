#nullable enable
using System;
using UnityEngine;

namespace Game.GAS
{
    // 에셋에 저장하는 태그 목록이다. 이름은 처음 읽을 때 한 번 태그로 바꿔 두고, 이후 조회는 할당 없이 배열만 읽는다.
    [Serializable]
    public sealed class GameplayTagContainer
    {
        [SerializeField] GameplayTagName[] _tags = Array.Empty<GameplayTagName>();
        [NonSerialized] GameplayTag[]? _resolved;

        public GameplayTagContainer()
        {
        }

        // 코드에서 목록을 만들 때 쓴다. 저장 형식과 같게 이름으로 보관한다.
        public GameplayTagContainer(params GameplayTag[] tags)
        {
            _tags = new GameplayTagName[tags.Length];
            for (int index = 0; index < tags.Length; index++) _tags[index] = new GameplayTagName(tags[index].Name);
        }

        public int Count => Resolved.Length;
        public bool IsEmpty => Resolved.Length == 0;
        public GameplayTag this[int index] => Resolved[index];

        // UE HasTag처럼 목록의 태그가 query이거나 그 자식이면 true다.
        public bool HasTag(GameplayTag query)
        {
            foreach (GameplayTag tag in Resolved)
                if (tag.MatchesTag(query)) return true;
            return false;
        }

        public bool HasTagExact(GameplayTag query)
        {
            foreach (GameplayTag tag in Resolved)
                if (tag.MatchesTagExact(query)) return true;
            return false;
        }

        public bool HasAny(GameplayTagContainer other)
        {
            GameplayTag[] tags = other.Resolved;
            foreach (GameplayTag tag in tags)
                if (HasTag(tag)) return true;
            return false;
        }

        GameplayTag[] Resolved => _resolved ??= Resolve(_tags);

        // 인스펙터에서 비워 둔 칸은 조용히 건너뛴다. 에셋에 적은 이름은 형식에 맞는다고 보고, 틀린 이름도 건너뛴다.
        // 목록 배열은 필드 초기값과 생성자, 직렬화가 늘 채운다.
        static GameplayTag[] Resolve(GameplayTagName[] names)
        {
            if (names.Length == 0) return Array.Empty<GameplayTag>();
            var tags = new GameplayTag[names.Length];
            int count = 0;
            foreach (GameplayTagName name in names)
            {
                if (name.IsEmpty) continue;
                if (name.TryResolve(out GameplayTag tag)) tags[count++] = tag;
            }
            if (count == tags.Length) return tags;
            Array.Resize(ref tags, count);
            return tags;
        }

#if UNITY_EDITOR
        // 에디터에서 목록을 고치면 소유 에셋의 OnValidate가 불러 다음 조회 때 다시 읽게 한다.
        internal void ClearResolved() => _resolved = null;
#endif
    }
}
