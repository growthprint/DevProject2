#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.Utilities.LootBoxLoader다. 같은 상자의 내용을 월드 초기화 때 원작 규칙(생성 확률, 고정 아이템, 개수,
    // 풀이나 태그와 품질 추첨, 현금)으로 정한다. 원작은 UnityEngine.Random을 쓰지만 여기서는 출격과 상자마다 정해진 시드를 쓴다.
    // 그래서 같은 출격을 다시 불러도 같은 칸이 나와 루팅 키가 맞는다. 원작 여섯 씬의 LevelConfig 배율은 모두 1이라 쓰지 않는다.
    [DisallowMultipleComponent]
    public sealed class LootBoxLoader : MonoBehaviour
    {
        // 원작 RandomContainer의 항목 하나다. 태그는 원본 Tag 자산 이름이다.
        [Serializable]
        public struct WeightedTag
        {
            [SerializeField] string _tag;
            [SerializeField] float _weight;
            internal string Tag => _tag;
            internal float Weight => _weight;
            internal WeightedTag(string tag, float weight) { _tag = tag; _weight = weight; }
        }

        [Serializable]
        public struct WeightedInt
        {
            [SerializeField] int _value;
            [SerializeField] float _weight;
            internal int Value => _value;
            internal float Weight => _weight;
            internal WeightedInt(int value, float weight) { _value = value; _weight = weight; }
        }

        [SerializeField] InteractableLootbox _lootBox = null!;
        // 원작 RandomActive가 끄는 상자 물체(복원한 원본 오브젝트)다. 꺼지면 상자 표시와 영역이 함께 사라진다.
        [SerializeField] GameObject _boxObject = null!;
        [SerializeField] float _activeChance = 1f;
        [SerializeField] int _inventorySize = 8;
        [SerializeField] Vector2Int _randomCount = new Vector2Int(1, 1);
        [SerializeField] bool _randomFromPool;
        [SerializeField] WeightedTag[] _tags = Array.Empty<WeightedTag>();
        [SerializeField] string[] _excludeTags = Array.Empty<string>();
        [SerializeField] bool _dontExcludeSpecial;
        [SerializeField] WeightedInt[] _qualities = Array.Empty<WeightedInt>();
        // 원작 randomPool이다. 값이 원본 아이템 ID다.
        [SerializeField] WeightedInt[] _randomPool = Array.Empty<WeightedInt>();
        [SerializeField] float _cashChance;
        [SerializeField] int _minCash, _maxCash;
        [SerializeField] int[] _fixedItems = Array.Empty<int>();
        [SerializeField] float _fixedItemSpawnChance = 1f;

        // 원작 Awake의 RandomActive와 Setup을 한 번에 한다. advancedDebuffMode는 원작 LevelManager.Rule.AdvancedDebuffMode다.
        // 원작처럼 데모 잠금 태그는 데모 빌드가 아니라 제외하지 않는다. 돌려주는 값은 상자에 못 넣어 바닥에 떨굴 현금이고, 없으면 0이다.
        internal int Setup(int seed, OriginalGameplayCatalog catalog, bool advancedDebuffMode)
        {
            var random = new System.Random(seed);
            if (Range(random, 0f, 1f) >= _activeChance)
            {
                _boxObject.SetActive(false);
                _lootBox.gameObject.SetActive(false);
                return 0;
            }
            int count = _randomCount.x <= 0 && _randomCount.y <= 0 ? 0
                : Mathf.RoundToInt(Range(random, _randomCount.x - .5f, _randomCount.y + .5f));
            var items = new List<int>();
            if (Range(random, 0f, 1f) < _fixedItemSpawnChance && _fixedItems.Length > 0) items.AddRange(_fixedItems);
            if (_randomFromPool)
            {
                for (int index = 0; index < count; index++) items.Add(Pick(_randomPool, random));
            }
            else
            {
                var exclude = new HashSet<string>(_excludeTags, StringComparer.Ordinal);
                if (!_dontExcludeSpecial) exclude.Add("Special");
                if (!advancedDebuffMode) exclude.Add("AdvancedDebuffMode");
                for (int index = 0; index < count; index++)
                {
                    string tag = PickTag(random);
                    IReadOnlyList<int> found = catalog.Search(tag, exclude, Pick(_qualities, random));
                    if (found.Count > 0) items.Add(found[random.Next(found.Count)]);
                }
            }
            var types = new List<int>(items.Count + 1);
            var counts = new List<int>(items.Count + 1);
            foreach (int id in items)
            {
                // 원작처럼 0 이하 ID는 건너뛴다. 원본 아이템 ID는 모두 카탈로그에 있다.
                if (id <= 0 || !catalog.TryGet(id, out OriginalItemDefinition? definition)) continue;
                types.Add(id);
                counts.Add(definition.DefaultCount);
            }
            // 원작 Setup처럼 inventorySize가 모자라면 건너뛴 0 이하 ID까지 센 아이템 수로 칸을 늘린다. 건너뛴 자리는 빈 칸으로 남아 현금이 들어간다.
            int capacity = Math.Max(_inventorySize, items.Count);
            // 원작 CreateCash다. 최소가 최대보다 크면 최대를 최소+1로 두고, 끝값은 뽑지 않는다. 원작 GetFirstEmptyPosition은 칸이 0개일 때만
            // 칸 수 이상을 돌려줘 그때만 한 칸 늘린다. 빈 칸이 없으면 AddItem이 실패해 상자 옆 바닥에 떨구므로 그 현금을 호출자에게 돌려준다.
            int droppedCash = 0;
            if (!(Range(random, 0f, 1f) > _cashChance))
            {
                int max = _minCash > _maxCash ? _minCash + 1 : _maxCash;
                int cash = max > _minCash ? random.Next(_minCash, max) : _minCash;
                if (capacity == 0) capacity = 1;
                if (types.Count < capacity)
                {
                    types.Add(GameplayDataSettings.ItemAssets.CashItemTypeID);
                    counts.Add(cash);
                }
                else droppedCash = cash;
            }
            _lootBox.SetContents(types.ToArray(), counts.ToArray(), capacity);
            return droppedCash;
        }

        // 원작 Random.Range(float, float)처럼 [min, max) 안의 값이다.
        static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        // 원작 RandomContainer.GetRandom(lowPercent 0)이다. 가중치 합 안의 값을 뽑아 누적 가중치가 처음 넘는 항목을 고른다.
        static int Pick(WeightedInt[] entries, System.Random random)
        {
            if (entries.Length == 0) return 0;
            float total = 0f;
            foreach (WeightedInt entry in entries) total += entry.Weight;
            float target = Range(random, 0f, total), sum = 0f;
            foreach (WeightedInt entry in entries)
            {
                sum += entry.Weight;
                if (sum >= target) return entry.Value;
            }
            return entries[entries.Length - 1].Value;
        }

        string PickTag(System.Random random)
        {
            if (_tags.Length == 0) return "";
            float total = 0f;
            foreach (WeightedTag entry in _tags) total += entry.Weight;
            float target = Range(random, 0f, total), sum = 0f;
            foreach (WeightedTag entry in _tags)
            {
                sum += entry.Weight;
                if (sum >= target) return entry.Tag;
            }
            return _tags[_tags.Length - 1].Tag;
        }

        // 제작 도구가 원본 LootBoxLoader 블록의 값을 그대로 옮겨 적는다.
        internal void Configure(InteractableLootbox lootBox, GameObject boxObject, float activeChance, int inventorySize, Vector2Int randomCount,
            bool randomFromPool, WeightedTag[] tags, string[] excludeTags, bool dontExcludeSpecial, WeightedInt[] qualities,
            WeightedInt[] randomPool, float cashChance, int minCash, int maxCash, int[] fixedItems, float fixedItemSpawnChance)
        {
            _lootBox = lootBox; _boxObject = boxObject; _activeChance = activeChance; _inventorySize = inventorySize; _randomCount = randomCount;
            _randomFromPool = randomFromPool; _tags = tags; _excludeTags = excludeTags; _dontExcludeSpecial = dontExcludeSpecial;
            _qualities = qualities; _randomPool = randomPool; _cashChance = cashChance; _minCash = minCash; _maxCash = maxCash;
            _fixedItems = fixedItems; _fixedItemSpawnChance = fixedItemSpawnChance;
        }
    }
}
