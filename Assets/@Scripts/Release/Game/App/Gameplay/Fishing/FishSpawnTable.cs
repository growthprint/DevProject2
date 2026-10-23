#nullable enable
using System;
using UnityEngine;

namespace Game.App
{
    // 원작 FishSpawner.SpecialPair다. 그 미끼(baitID)로 낚으면 chance 확률로 그 물고기(fishID)가 먼저 걸린다.
    [Serializable]
    internal sealed class FishSpecialPair
    {
        [SerializeField] int _baitId;
        [SerializeField] int _fishId;
        [SerializeField] float _chance;

        internal int BaitId => _baitId;
        internal int FishId => _fishId;
        internal float Chance => _chance;
    }

    // 원작 RandomContainer<Tag>의 항목이다. 태그는 원작 Tag 자산 이름이다.
    [Serializable]
    internal sealed class FishTagWeight
    {
        [SerializeField] string _tag = "";
        [SerializeField] float _weight;

        internal string Tag => _tag;
        internal float Weight => _weight;
    }

    // 원작 RandomContainer<int> 품질 항목이다.
    [Serializable]
    internal sealed class FishQualityWeight
    {
        [SerializeField] int _quality;
        [SerializeField] float _weight;

        internal int Quality => _quality;
        internal float Weight => _weight;
    }

    // 원작 낚시터 루트의 FishSpawner 값과, 낚시터 상호작용을 켜는 낚싯대 아이템 목록이다. 가져오기 도구(Game/Foundation/Import Original Fishing Data)가
    // 원작 씬의 FishSpawner마다 하나씩 SerializedObject로 채운다. 낚싯대 목록은 원작 손 모델(ItemAgent)에 FishingRod가 붙은 아이템이다.
    // 런타임은 읽기만 한다. 규칙 값은 Settings로 한 번 만들어 FishSpawnRule에 넘긴다.
    internal sealed class FishSpawnTable : ScriptableObject
    {
        [SerializeField] FishSpecialPair[] _specialPairs = Array.Empty<FishSpecialPair>();
        [SerializeField] FishTagWeight[] _tags = Array.Empty<FishTagWeight>();
        [SerializeField] string[] _excludeTags = Array.Empty<string>();
        [SerializeField] FishQualityWeight[] _qualities = Array.Empty<FishQualityWeight>();
        // 원작 Fish_OnlyDay, Fish_OnlyNight, Fish_OnlySunDay, Fish_OnlyRainDay, Fish_OnlyStorm 필드가 가리키는 Tag 자산 이름이다.
        [SerializeField] string _onlyDay = "";
        [SerializeField] string _onlyNight = "";
        [SerializeField] string _onlySunDay = "";
        [SerializeField] string _onlyRainDay = "";
        [SerializeField] string _onlyStorm = "";
        // 원작 Action_FishingV2.SyncInteractable이 손에 든 ItemAgent의 FishingRod로 판정하는 아이템이다(원작 343, 1095, 1096).
        [SerializeField] int[] _rodItemIds = Array.Empty<int>();
        [NonSerialized] FishSpawnSettings? _settings;

        // 손에 든 무기 칸 아이템이 낚싯대인지다. 낚시터가 바뀐 손 아이템마다 다시 본다.
        internal bool IsRod(int originalItemId) => Array.IndexOf(_rodItemIds, originalItemId) >= 0;

        // 규칙 값은 처음 읽을 때 한 번 만든다. 에셋은 실행 중 바뀌지 않고, Editor에서 바뀌면 OnValidate가 비운다.
        internal FishSpawnSettings Settings => _settings ??= Build();

#if UNITY_EDITOR
        // 가져오기 도구나 인스펙터가 값을 바꾸면 만든 규칙 값을 비워 다음 읽기에서 다시 만든다. Editor 콜백이라 UNITY_EDITOR로 감싼다.
        void OnValidate() => _settings = null;
#endif

        FishSpawnSettings Build()
        {
            var pairs = new (int BaitId, int FishId, float Chance)[_specialPairs.Length];
            for (int index = 0; index < pairs.Length; index++)
                pairs[index] = (_specialPairs[index].BaitId, _specialPairs[index].FishId, _specialPairs[index].Chance);
            var tags = new (string Tag, float Weight)[_tags.Length];
            for (int index = 0; index < tags.Length; index++) tags[index] = (_tags[index].Tag, _tags[index].Weight);
            var qualities = new (int Quality, float Weight)[_qualities.Length];
            for (int index = 0; index < qualities.Length; index++) qualities[index] = (_qualities[index].Quality, _qualities[index].Weight);
            return new FishSpawnSettings(pairs, tags, (string[])_excludeTags.Clone(), qualities, _onlyDay, _onlyNight, _onlySunDay,
                _onlyRainDay, _onlyStorm);
        }
    }
}
