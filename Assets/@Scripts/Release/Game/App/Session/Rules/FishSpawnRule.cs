#nullable enable
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 원작 FishSpawner의 저작 값이다. FishSpawnTable이 에셋 값으로 한 번 만들고 FishSpawnRule이 읽기만 한다.
    // 태그는 원작 Tag 자산 이름이다. 세션 규칙이 게임플레이 형식에 기대지 않도록 값만 담는다.
    internal sealed class FishSpawnSettings
    {
        internal FishSpawnSettings(IReadOnlyList<(int BaitId, int FishId, float Chance)> specialPairs,
            IReadOnlyList<(string Tag, float Weight)> tags, IReadOnlyList<string> excludeTags,
            IReadOnlyList<(int Quality, float Weight)> qualities, string onlyDay, string onlyNight, string onlySunDay,
            string onlyRainDay, string onlyStorm)
        {
            SpecialPairs = specialPairs;
            Tags = tags;
            ExcludeTags = excludeTags;
            Qualities = qualities;
            OnlyDay = onlyDay;
            OnlyNight = onlyNight;
            OnlySunDay = onlySunDay;
            OnlyRainDay = onlyRainDay;
            OnlyStorm = onlyStorm;
        }

        // 원작 specialPairs, tags, excludeTags, qualities와 시간·날씨 전용 태그 다섯 개(Fish_Only*)다.
        internal IReadOnlyList<(int BaitId, int FishId, float Chance)> SpecialPairs { get; }
        internal IReadOnlyList<(string Tag, float Weight)> Tags { get; }
        internal IReadOnlyList<string> ExcludeTags { get; }
        internal IReadOnlyList<(int Quality, float Weight)> Qualities { get; }
        internal string OnlyDay { get; }
        internal string OnlyNight { get; }
        internal string OnlySunDay { get; }
        internal string OnlyRainDay { get; }
        internal string OnlyStorm { get; }
    }

    // 원작 FishSpawner.Spawn의 물고기 추첨이다. 미끼, 행운(FishingQualityFactor), 밤 여부, 날씨, 난수로 물고기 원본 ID 하나를 정한다.
    // 아이템 찾기는 호출자가 카탈로그 검색(원작 ItemAssetsCollection.Search)을 넘긴다. 상태가 없는 순수 함수이고 던지지도 기록하지도 않는다.
    internal static class FishSpawnRule
    {
        // 원작 Weather 정수다(OriginalWeather와 같은 값). 세션 규칙이 게임플레이 형식에 기대지 않게 정수로 받는다.
        internal const int _sunny = 0;
        internal const int _rainy = 2;
        internal const int _stormyI = 3;
        internal const int _stormyII = 4;
        internal const int _snow = 22;

        // 원작 Spawn이다. 특수 짝이 먼저이고, 미끼가 맞을 때만 난수를 뽑는다. 짝이 없으면 태그와 품질을 가중치로 뽑아 찾고,
        // 행운이 높을수록 품질 추첨의 아래쪽을 잘라 낸다(lowPercent = 1 - 1/행운, 행운은 0.1 이상). 후보가 없으면 false와 -1이다.
        // random01은 0~1 난수(원작 Random.Range(0, 1)), fishTags는 물고기 정의의 태그(없으면 null), search는 태그·제외 태그·품질 검색이다.
        internal static bool TryPick(FishSpawnSettings settings, int baitId, float luck, bool atNight, int weather, Func<float> random01,
            Func<int, IReadOnlyList<string>?> fishTags, Func<string, HashSet<string>, int, IReadOnlyList<int>> search, out int fishId)
        {
            IReadOnlyList<(int BaitId, int FishId, float Chance)> pairs = settings.SpecialPairs;
            for (int index = 0; index < pairs.Count; index++)
            {
                (int bait, int fish, float chance) = pairs[index];
                if (bait == baitId && random01() < chance && IsAllowed(settings, fishTags(fish), atNight, weather))
                {
                    fishId = fish;
                    return true;
                }
            }
            luck = Math.Max(luck, .1f);
            float lowPercent = 1f - 1f / luck;
            string tag = PickTag(settings.Tags, random01);
            int quality = PickQuality(settings.Qualities, lowPercent, random01);
            IReadOnlyList<int> found = search(tag, ExcludeTags(settings, atNight, weather), quality);
            if (found.Count < 1)
            {
                fishId = -1;
                return false;
            }
            fishId = found[Math.Min((int)(random01() * found.Count), found.Count - 1)];
            return true;
        }

        // 원작 CalculateTags다. 기본 제외 태그에 낮 전용이나 밤 전용, 맑음·비·폭풍 전용을 더하고 지금 날씨의 태그만 뺀다.
        // 원작 List.Remove처럼 같은 이름이 두 번 들어 있으면 하나만 빠진다.
        internal static HashSet<string> ExcludeTags(FishSpawnSettings settings, bool atNight, int weather)
        {
            var tags = new List<string>(settings.ExcludeTags) { atNight ? settings.OnlyDay : settings.OnlyNight };
            tags.Add(settings.OnlySunDay);
            tags.Add(settings.OnlyRainDay);
            tags.Add(settings.OnlyStorm);
            switch (weather)
            {
                case _sunny: tags.Remove(settings.OnlySunDay); break;
                case _rainy:
                case _snow: tags.Remove(settings.OnlyRainDay); break;
                case _stormyI:
                case _stormyII: tags.Remove(settings.OnlyStorm); break;
            }
            return new HashSet<string>(tags, StringComparer.Ordinal);
        }

        // 원작 CheckFishDayNightAndWeather다. 특수 짝 물고기의 전용 태그가 지금 시간과 날씨에 맞는지 본다. 정의가 없으면 막지 않는다.
        static bool IsAllowed(FishSpawnSettings settings, IReadOnlyList<string>? tags, bool atNight, int weather)
        {
            if (tags == null) return true;
            if (Has(tags, settings.OnlyNight) && !atNight) return false;
            if (Has(tags, settings.OnlyDay) && atNight) return false;
            if (Has(tags, settings.OnlyRainDay) && weather != _rainy && weather != _snow) return false;
            if (Has(tags, settings.OnlySunDay) && weather != _sunny) return false;
            return !Has(tags, settings.OnlyStorm) || weather == _stormyI || weather == _stormyII;
        }

        static bool Has(IReadOnlyList<string> tags, string tag)
        {
            if (tag.Length == 0) return false;
            for (int index = 0; index < tags.Count; index++)
                if (tags[index] == tag) return true;
            return false;
        }

        // 원작 RandomContainer.GetRandom이다. 가중치 합의 lowPercent 비율부터 합까지 난수를 뽑고, 누적 가중치가 처음 그 값 이상인 항목을 고른다.
        // 항목이 없으면 원작 default(Tag)처럼 빈 이름이고, 빈 이름은 카탈로그 검색이 태그 조건을 건너뛴다.
        static string PickTag(IReadOnlyList<(string Tag, float Weight)> entries, Func<float> random01)
        {
            if (entries.Count < 1) return "";
            float total = 0f;
            for (int index = 0; index < entries.Count; index++) total += entries[index].Weight;
            float roll = total * random01();
            float sum = 0f;
            for (int index = 0; index < entries.Count; index++)
            {
                sum += entries[index].Weight;
                if (sum >= roll) return entries[index].Tag;
            }
            return entries[entries.Count - 1].Tag;
        }

        // 품질 추첨도 같은 GetRandom(lowPercent)이다. 항목이 없으면 원작 default(int)처럼 0이다.
        static int PickQuality(IReadOnlyList<(int Quality, float Weight)> entries, float lowPercent, Func<float> random01)
        {
            if (entries.Count < 1) return 0;
            float total = 0f;
            for (int index = 0; index < entries.Count; index++) total += entries[index].Weight;
            float low = total * lowPercent;
            float roll = low + (total - low) * random01();
            float sum = 0f;
            for (int index = 0; index < entries.Count; index++)
            {
                sum += entries[index].Weight;
                if (sum >= roll) return entries[index].Quality;
            }
            return entries[entries.Count - 1].Quality;
        }
    }
}
