#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 RandomCharacterSpawner다. 스폰 루트가 부르면 원본 지점을 마릿수만큼 고르고, 지점마다 저작한 후보 중 하나를 켜고 준비한다.
    // 최소 거리와 확률은 원작처럼 스폰 루트가 가진다. 같은 출격이면 같은 마릿수, 지점, 프리셋 추첨 결과다.
    public sealed class OriginalEnemySpawnGroup : MonoBehaviour
    {
        [SerializeField] string _sourceId = "";
        [SerializeField] OriginalEnemyController[] _candidates = Array.Empty<OriginalEnemyController>();
        // 원작 randomPresetInfos가 둘 이상이면 지점마다 프리셋 순서대로 후보를 하나씩 둔다. 후보 순번은 지점 순번 × 이 값 + 프리셋 순번이다.
        // 가중치는 원작 GetAPresetByWeight 값이다. 프리셋이 하나인 스포너는 1과 빈 가중치라 프리셋을 추첨하지 않는다.
        [SerializeField] int _presetsPerPoint = 1;
        [SerializeField] float[] _presetWeights = Array.Empty<float>();
        // 원작 spawnCountRange다. 원작 enemySpawnCountFactor는 늘 1이라 저작 값 그대로 쓴다.
        [SerializeField] int _minimum, _maximum;
        // 원작 firstIsLeader, OnStartCreateEvent의 NotificationProxy 알림 키, spawnTimeSpace다.
        [SerializeField] bool _firstIsLeader;
        [SerializeField] string _notifyKey = "";
        [SerializeField] float _spawnInterval = .1f;
        // 원작 masterGroup이다. 우두머리가 있는 원작 CharacterSpawnerGroup의 스포너는 모두 그 그룹의 첫 스포너를 가리키고,
        // 그 첫 스포너가 그룹의 우두머리와 구성원을 가진다. 원작은 실행 중에 넣지만 제품은 제작 도구가 저작한다. 비어 있으면 따라가기가 없다.
        [SerializeField] OriginalEnemySpawnGroup? _master;
        OriginalEnemyController? _leader;
        readonly List<OriginalEnemyController> _members = new List<OriginalEnemyController>();

        // 원작 CharacterSpawnerGroup.LeaderAI다. 구성원이 매 프레임 읽는다(5-W2b). 원작 Update처럼 우두머리가 죽으면
        // 살아 있는 구성원 중 마지막으로 들어온 적이 이어받는다. 원작은 죽은 캐릭터가 사라져 목록에서 빠지고, 제품은 Dead로 거른다.
        internal OriginalEnemyController? Leader
        {
            get
            {
                if (_leader != null && !_leader.Dead) return _leader;
                _leader = null;
                for (int index = _members.Count - 1; index >= 0; index--)
                {
                    OriginalEnemyController member = _members[index];
                    if (member != null && !member.Dead) { _leader = member; break; }
                }
                return _leader;
            }
        }

        // 원작 AddCharacterSpawned다. 우두머리로 정한 적이나 아직 우두머리가 없을 때 들어온 첫 적이 우두머리가 된다.
        void Join(OriginalEnemyController enemy, bool isLeader)
        {
            if (isLeader || _leader == null) _leader = enemy;
            _members.Add(enemy);
            enemy.FollowGroup(this);
        }

        // 첫 월드 초기화가 적 준비보다 먼저 부른다. 꺼진 후보는 준비되지 않아 월드 적 준비가 건너뛴다.
        internal void Hide()
        {
            foreach (OriginalEnemyController value in _candidates)
                if (value != null) value.gameObject.SetActive(false);
        }

        // 원작 CreateAsync와 Points.GetRandomPoints다. 알림은 마릿수를 정하기 전에 낸다. 남은 지점을 무작위로 하나씩 꺼내
        // 플레이어와 거리가 최소 거리를 엄격히 넘을 때만 쓰므로, 지점이 모자라면 마릿수가 준다. 지점을 모두 고른 뒤 원작 CreateAt처럼
        // 지점마다 가중치로 프리셋을 뽑고, 고른 순서마다 원작 개체 간격만큼 생성을 늦춘다.
        internal void Spawn(OriginalEnemyWorld world, GameplayWorldContext shared, DifficultyState? difficulty, OriginalEnemySpawnRoot root)
        {
            if (_notifyKey.Length > 0) shared.Notify(_notifyKey);
            // 같은 출격과 스포너면 같은 마릿수와 추첨 순서가 나오도록 적 파사드의 출격 시드를 쓴다.
            var random = new System.Random(world.Seed(_sourceId));
            int count = _maximum >= _minimum ? random.Next(_minimum, _maximum + 1) : _minimum;
            Vector3 player = world.PlayerPosition;
            float minimum = root.MinimumDistance, maximum = root.MaximumDistance;
            // 후보 대신 지점을 고른다. 프리셋이 하나인 스포너는 지점마다 후보가 하나라 고른 지점이 곧 후보다.
            int points = _candidates.Length / _presetsPerPoint;
            var remaining = new List<int>(points);
            for (int point = 0; point < points; point++)
                if (PointAvailable(point)) remaining.Add(point);
            var chosen = new List<int>(Math.Min(count, remaining.Count));
            while (chosen.Count < count && remaining.Count > 0)
            {
                int index = random.Next(remaining.Count);
                int point = remaining[index];
                remaining.RemoveAt(index);
                // 원작 GetRandomPoints처럼 최소 거리를 엄격히 넘는 지점만 쓰고, 최소 거리가 최대 거리보다 작지 않으면 아무 지점도 쓰지 않는다.
                // 한 지점의 후보는 모두 같은 원본 지점에 저작되므로 첫 후보 위치로 잰다.
                if (BeyondMinimum(_candidates[point * _presetsPerPoint].transform.position, player, minimum, maximum)) chosen.Add(point);
            }
            for (int spawned = 0; spawned < chosen.Count; spawned++)
            {
                // 원작 CreateAt마다 부르는 GetAPresetByWeight다. 같은 시드 난수를 이어 쓰고, 프리셋이 하나인 스포너는 고를 것이 없어 뽑지 않는다.
                int preset = _presetsPerPoint > 1 ? DrawPreset(_presetWeights, random.NextDouble()) : 0;
                OriginalEnemyController candidate = _candidates[chosen[spawned] * _presetsPerPoint + preset];
                candidate.gameObject.SetActive(true);
                // 원작 CharacterRandomPreset처럼 준비 뒤 상점 같은 특수 부착물도 Init하도록 월드 문맥을 넘긴다.
                // 원작 스포너가 캐릭터를 자기 하위 씬에 묶듯, 꺼진 하위 씬의 적이 켜지지 않도록 스폰 루트의 하위 씬 이름도 넘긴다.
                candidate.Prepare(world, shared, difficulty, spawned * _spawnInterval, root.SubsceneId);
                // 원작처럼 우두머리 그룹이 있을 때만 등록한다. 첫 개체는 firstIsLeader면 우두머리다.
                if (_master != null) _master.Join(candidate, spawned == 0 && _firstIsLeader);
            }
        }

        // 지점의 대체 후보가 모두 있고 하나도 준비되지 않았을 때만 그 지점을 쓴다. 하나라도 준비됐으면 그 지점에는 이미 개체가 있다.
        bool PointAvailable(int point)
        {
            for (int offset = 0; offset < _presetsPerPoint; offset++)
            {
                OriginalEnemyController value = _candidates[point * _presetsPerPoint + offset];
                if (value == null || value.Prepared) return false;
            }
            return true;
        }

        // 원작 GetAPresetByWeight다. [0, 1) 난수 roll을 가중치 합만큼 늘리고, 누적 가중치가 그 값을 처음 넘는 프리셋을 고른다.
        // 합이 0 이하이거나 끝까지 고르지 못하면 원작처럼 마지막 프리셋이다.
        internal static int DrawPreset(float[] weights, double roll)
        {
            double total = 0;
            foreach (float weight in weights) total += weight;
            if (total <= 0) return weights.Length - 1;
            double pick = roll * total, sum = 0;
            for (int index = 0; index < weights.Length; index++)
            {
                sum += weights[index];
                if (pick < sum) return index;
            }
            return weights.Length - 1;
        }

        // 후보와 플레이어의 거리가 최소 거리를 엄격히 넘고, 최소 거리가 최대 거리보다 작은지다. 원작 Points.GetRandomPoints 조건 순서다.
        static bool BeyondMinimum(Vector3 position, Vector3 player, float minimum, float maximum) =>
            Vector3.Distance(position, player) > minimum && minimum < maximum;
    }
}
