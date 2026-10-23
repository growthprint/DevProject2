#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Game.App
{
    // CampaignProgress.CheckpointId에 담는 사건 집합과 사건 키의 문자열을 이 타입만 만들고 파싱한다.
    // 집합은 튜토리얼이면 'tutorial|a|b', 진행 중 출격이면 'raid:<출격 ID>|a|b'다. 키 형식은 Docs/game-save.md#campaign에 있다.
    // 이 문자열들은 저장 파일과 씬 RequiredEvent에 남으므로 바꾸지 않는다.
    // 파싱 결과는 CampaignProgress마다 한 번 만들어 매 프레임 판정에서 재사용한다. 처치와 루팅의 Add는 그 결과에 새 키만 끼운 집합을
    // 다음 진행 값에 넘기고, 저장 문자열은 저장이나 비교 때 Format이 만든다. 그래서 사건이 늘어도 집합 전체를 다시 잇거나 읽지 않는다.
    internal static class CampaignEventLog
    {
        // 튜토리얼 사건 집합의 접두사다.
        const string _tutorialPrefix = "tutorial|";
        // 처치 사건 키('dead:<원본>@x,y,z')와 처치 조건 키('dead:<원본>')의 접두사다.
        const string _deathPrefix = "dead:";
        // 상자 칸 루팅 사건 키('loot:<원본>:<칸>')의 접두사다.
        const string _lootPrefix = "loot:";
        // 처치한 적의 시체 루팅 원본 ID('corpse:<원본>')의 접두사다.
        const string _corpsePrefix = "corpse:";

        // 한 진행 값의 파싱 결과다. 집합, 사전, 배열은 만든 뒤 바꾸지 않으므로 다음 진행 값의 결과와 공유할 수 있다.
        internal sealed class Parsed
        {
            // 받은 집합과 사전을 복사하지 않고 그대로 보관한다.
            internal Parsed(string? raidId, string prefix, string[] sorted, HashSet<string> completed,
                Dictionary<string, (float X, float Y, float Z)> deaths, HashSet<string> deathRequirements)
            {
                RaidId = raidId;
                Prefix = prefix;
                Sorted = sorted;
                Completed = completed;
                Deaths = deaths;
                DeathRequirements = deathRequirements;
            }

            // Completed를 Ordinal 순서로 정렬한 배열이다. Add가 새 키를 제자리에 끼우고, Format이 다시 정렬하지 않고 잇는다.
            internal string[] Sorted { get; }

            // 파싱한 범위의 출격 ID이며 튜토리얼은 null이다. Read가 접두사 문자열을 만들지 않고 이 값으로 캐시 재사용을 판정한다.
            internal string? RaidId { get; }
            // 파싱에 쓴 진행 범위 접두사다. Format이 저장 문자열의 머리로 쓴다.
            internal string Prefix { get; }
            // 현재 범위에서 완료된 사건 키 집합이다(Ordinal 비교).
            internal HashSet<string> Completed { get; }
            // 원본 ID별 유효한(유한) 사망 좌표다. 원본마다 하나만 둔다.
            internal Dictionary<string, (float X, float Y, float Z)> Deaths { get; }
            // Deaths의 원본마다 하나씩 둔 처치 조건 키 'dead:<원본>'의 집합이다. '@' 앞까지만 비교하므로
            // 좌표가 붙은 처치 키로 찾아도 문자열을 자르지 않고 같은 원본의 조건을 찾는다.
            internal HashSet<string> DeathRequirements { get; }
        }

        // 처치 조건 키를 첫 '@' 앞까지만 Ordinal로 비교한다. 처치 키는 첫 '@'를 원본과 좌표의 경계로 읽으므로 이 범위가 곧 'dead:<원본>'이다.
        sealed class RequirementComparer : IEqualityComparer<string>
        {
            internal static readonly RequirementComparer _instance = new RequirementComparer();

            public bool Equals(string? x, string? y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                int length = Length(x);
                return length == Length(y) && string.CompareOrdinal(x, 0, y, 0, length) == 0;
            }

            // string.GetHashCode는 '@' 뒤 좌표까지 섞으므로, 비교 범위만 FNV-1a로 섞는다.
            public int GetHashCode(string key) => StableHash.Fnv1a(key, Length(key));

            static int Length(string key)
            {
                int at = key.IndexOf('@');
                return at < 0 ? key.Length : at;
            }
        }

        // 진행 중 출격의 사건 집합 접두사다. 출격 ID가 들어가므로 새 출격은 다른 출격의 상자와 적 완료를 이어받지 않는다.
        internal static string RaidPrefix(string raidId) => "raid:" + raidId + "|";

        // 원본 상자(source)의 칸(index) 루팅 완료 키다.
        internal static string LootKey(string sourceId, int index) => _lootPrefix + sourceId + ":" + index;

        // 처치 완료와 시체 좌표를 함께 담은 사건 키다. 좌표는 문화권과 무관한 R 형식이라 정확히 다시 읽힌다.
        internal static string DeathKey(string sourceId, float x, float y, float z)
        {
            CultureInfo invariant = CultureInfo.InvariantCulture;
            return _deathPrefix + sourceId + "@" + x.ToString("R", invariant) + "," + y.ToString("R", invariant) + "," +
                z.ToString("R", invariant);
        }

        // 문과 대사가 기다리는, 좌표 없는 처치 조건 키다. 좌표를 담은 처치 키가 있으면 완료로 판정한다.
        internal static string DeathRequirement(string sourceId) => _deathPrefix + sourceId;

        // 처치한 적의 시체 루팅 지점 원본 ID다.
        internal static string CorpseSource(string sourceId) => _corpsePrefix + sourceId;

        // 사건 집합은 튜토리얼과 진행 중 출격에만 있다. 루팅과 사건 완료 명령, 월드 표시가 기록할지를 같은 판정으로 정한다.
        internal static bool IsRecording(CampaignState campaign) => campaign.Progress.Stage == CampaignStage.Tutorial || campaign.Raid != null;

        // 현재 범위인 튜토리얼이나 출격에서 사건이 완료됐는지 판정한다.
        // 'dead:<원본>' 키는 '@좌표'가 붙든 안 붙든 그 원본의 유효한 사망 좌표가 기록돼 있으면 완료다.
        internal static bool IsComplete(CampaignState campaign, string id)
        {
            Parsed events = Read(campaign);
            // 매 프레임과 처치마다 불리므로, 처치 키는 '@' 앞까지만 비교하는 조건 집합에서 찾아 문자열을 자르지 않는다.
            return events.Completed.Contains(id) ||
                id.StartsWith(_deathPrefix, StringComparison.Ordinal) && events.DeathRequirements.Contains(id);
        }

        // 기록된 사망 이벤트의 세 좌표가 모두 유한한 실수일 때만 위치를 반환한다.
        internal static bool TryDeathPosition(CampaignState campaign, string sourceId, out float x, out float y, out float z)
        {
            if (Read(campaign).Deaths.TryGetValue(sourceId, out (float X, float Y, float Z) position))
            {
                (x, y, z) = position;
                return true;
            }
            x = y = z = 0;
            return false;
        }

        // 사건 하나를 더한 현재 범위의 사건 집합으로 같은 단계의 진행 값을 만든다. 캐시한 집합은 바꾸지 않는다.
        // 키는 씬 데이터 테스트가 형식을 확인한 저작 키와, LootKey나 DeathKey 같은 이 타입의 키 생성기가 만든 키뿐이다.
        // 튜토리얼 사망은 이 집합을 고치지 않고 튜토리얼 도착 저장 파일로 통째로 되돌린다.
        internal static CampaignProgress Add(CampaignState campaign, string id) => AddRange(campaign, new[] { id });

        // 루팅 한 번에 주운 칸 전체처럼 여러 키를 더한다. 결과는 같은 키를 차례로 Add한 결과와 같다.
        // 키는 Add와 같이 저작 키와 이 타입의 키 생성기가 만든 키뿐이다.
        // 새 키만 정렬된 자리에 끼운 집합을 진행 값에 넘기고, 저장 문자열은 진행 값이 처음 읽힐 때 Format으로 만든다.
        internal static CampaignProgress AddRange(CampaignState campaign, IReadOnlyList<string> ids)
        {
            Parsed events = Read(campaign);
            var completed = new HashSet<string>(events.Completed, StringComparer.Ordinal);
            var added = new List<string>(ids.Count);
            for (int index = 0; index < ids.Count; index++)
            {
                if (completed.Add(ids[index])) added.Add(ids[index]);
            }
            return CampaignProgress.FromEvents(campaign.Progress.Stage, added.Count == 0 ? events : Insert(events, completed, added));
        }

        // 사건 집합의 저장 문자열이다. 늘 범위 접두사 뒤에 Ordinal로 정렬한 키를 '|'로 이은 같은 형식이다.
        internal static string Format(Parsed events) => events.Prefix + string.Join("|", events.Sorted);

        // 새 키를 정렬된 배열에 병합하고, 새 처치 키의 좌표만 읽어 사망 사전에 더한다. 처치 키가 없으면 사전을 공유한다.
        // 이미 좌표가 있는 원본의 처치 키가 또 들어오면, 정렬 순서상 첫 키를 쓰는 파싱 규칙을 지키려고 전체를 다시 만든다.
        static Parsed Insert(Parsed events, HashSet<string> completed, List<string> added)
        {
            added.Sort(StringComparer.Ordinal);
            string[] old = events.Sorted;
            var sorted = new string[old.Length + added.Count];
            for (int index = 0, left = 0, right = 0; index < sorted.Length; index++)
                sorted[index] = right >= added.Count || (left < old.Length && string.CompareOrdinal(old[left], added[right]) < 0)
                    ? old[left++] : added[right++];
            Dictionary<string, (float X, float Y, float Z)> deaths = events.Deaths;
            HashSet<string> requirements = events.DeathRequirements;
            for (int index = 0; index < added.Count; index++)
            {
                if (!TryReadDeath(added[index], out string? source, out (float X, float Y, float Z) position)) continue;
                if (deaths.ContainsKey(source)) return Build(events.RaidId, events.Prefix, sorted, completed);
                if (ReferenceEquals(deaths, events.Deaths))
                {
                    deaths = new Dictionary<string, (float X, float Y, float Z)>(events.Deaths, StringComparer.Ordinal);
                    // 조건 집합은 '@' 앞까지만 비교하는 비교자를 그대로 이어 써야 좌표가 붙은 키로도 찾는다.
                    requirements = new HashSet<string>(events.DeathRequirements, RequirementComparer._instance);
                }
                deaths.Add(source, position);
                requirements.Add(DeathRequirement(source));
            }
            return new Parsed(events.RaidId, events.Prefix, sorted, completed, deaths, requirements);
        }

        // 진행 값의 사건 집합을 현재 범위로 파싱한다. 같은 CampaignProgress에서 출격 ID가 같으면 캐시를 재사용한다.
        static Parsed Read(CampaignState campaign)
        {
            CampaignProgress progress = campaign.Progress;
            // 접두사는 출격 ID로만 정해지므로 캐시 판정은 출격 ID만 비교한다. 튜토리얼은 출격 ID가 null이다.
            // 접두사 문자열은 캐시를 쓸 수 없을 때만 만든다.
            RaidState? raid = campaign.Raid;
            string? raidId = raid?.RaidId;
            Parsed? cached = progress._eventCache;
            if (cached != null && string.Equals(cached.RaidId, raidId, StringComparison.Ordinal)) return cached;
            // Add로 만든 진행 값은 처음부터 자기 집합을 캐시로 들고 있으므로, 여기 오는 것은 저장 파일에서 읽은 값이나
            // 다른 출격 범위로 조회한 값뿐이다. 그때만 저장 문자열을 만들어 읽는다.
            string? checkpoint = progress.CheckpointId;
            string prefix = raid == null ? _tutorialPrefix : RaidPrefix(raid.RaidId);
            string[] sorted = checkpoint != null && checkpoint.StartsWith(prefix, StringComparison.Ordinal)
                ? checkpoint.Substring(prefix.Length).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                : Array.Empty<string>();
            var completed = new HashSet<string>(sorted, StringComparer.Ordinal);
            // 저장 문자열은 정렬된 중복 없는 키지만, 손으로 고친 파일도 같은 결과가 되도록 중복을 빼고 한 번 정렬한다.
            if (completed.Count != sorted.Length)
            {
                sorted = new string[completed.Count];
                completed.CopyTo(sorted);
            }
            Array.Sort(sorted, StringComparer.Ordinal);
            Parsed parsed = Build(raidId, prefix, sorted, completed);
            progress._eventCache = parsed;
            return parsed;
        }

        // 정렬된 키에서 사망 좌표를 읽어 파싱 결과를 만든다. 한 원본에 처치 키가 여럿이면 정렬 순서상 첫 유효 키를 쓴다.
        // IsComplete가 매번 문자열을 자르지 않도록, 유효한 사망 좌표가 있는 원본마다 처치 조건 키를 미리 만든다.
        static Parsed Build(string? raidId, string prefix, string[] sorted, HashSet<string> completed)
        {
            var deaths = new Dictionary<string, (float X, float Y, float Z)>(StringComparer.Ordinal);
            // 좌표가 붙은 처치 키로도 문자열을 자르지 않고 찾도록 '@' 앞까지만 비교한다.
            var requirements = new HashSet<string>(RequirementComparer._instance);
            for (int index = 0; index < sorted.Length; index++)
            {
                if (!TryReadDeath(sorted[index], out string? source, out (float X, float Y, float Z) position) ||
                    deaths.ContainsKey(source)) continue;
                deaths.Add(source, position);
                requirements.Add(DeathRequirement(source));
            }
            return new Parsed(raidId, prefix, sorted, completed, deaths, requirements);
        }

        // 'dead:<원본>@x,y,z' 키에서 원본 ID와 유한한 좌표를 읽는다. 처치 키가 아니거나 좌표가 잘못되면 false다.
        static bool TryReadDeath(string entry, [NotNullWhen(true)] out string? source, out (float X, float Y, float Z) position)
        {
            source = null;
            position = default;
            int at = entry.IndexOf('@');
            if (!entry.StartsWith(_deathPrefix, StringComparison.Ordinal) || at < 0) return false;
            string[] parts = entry.Substring(at + 1).Split(',');
            if (parts.Length != 3 || !TryReadFinite(parts[0], out float x) || !TryReadFinite(parts[1], out float y) ||
                !TryReadFinite(parts[2], out float z)) return false;
            source = entry.Substring(_deathPrefix.Length, at - _deathPrefix.Length);
            position = (x, y, z);
            return true;
        }

        // 사망 좌표 한 성분을 문화권과 무관하게 읽고 NaN과 무한대를 거부한다.
        static bool TryReadFinite(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
