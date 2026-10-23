#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.GAS
{
    // 큐 태그와 연출 에셋의 표다. 정확히 맞는 태그가 없으면 부모 태그를 차례로 찾는다(UE 큐 태그 대체 규칙).
    [CreateAssetMenu(menuName = "GAS/Gameplay Cue Set", fileName = "GCS_New")]
    public sealed class GameplayCueSet : ScriptableObject
    {
        [Serializable]
        sealed class Entry
        {
            [SerializeField] internal GameplayTagName _tag;
            [SerializeField] internal GameplayCue? _cue;
        }

        [SerializeField] Entry[] _entries = Array.Empty<Entry>();
        // 태그 번호로 찾는 색인이다. 처음 조회할 때 만들고, 에디터에서 표를 고치면 비운다.
        [NonSerialized] Dictionary<int, GameplayCue>? _index;

        public bool TryFind(GameplayTag tag, out GameplayCue? cue)
        {
            Dictionary<int, GameplayCue> index = _index ??= BuildIndex(_entries);
            for (GameplayTag current = tag; current.IsValid; current = current.Parent)
                if (index.TryGetValue(current.Id, out cue)) return true;
            cue = null;
            return false;
        }

        // 세트 에셋의 칸마다 연출과 형식에 맞는 태그가 있고 태그가 겹치지 않는다고 본다. 겹치면 먼저 나온 칸을 쓴다.
        static Dictionary<int, GameplayCue> BuildIndex(Entry[] entries)
        {
            var index = new Dictionary<int, GameplayCue>(entries.Length);
            foreach (Entry entry in entries)
            {
                entry._tag.TryResolve(out GameplayTag tag);
                index.TryAdd(tag.Id, entry._cue!);
            }
            return index;
        }

#if UNITY_EDITOR
        void OnValidate() => _index = null;
#endif
    }
}
