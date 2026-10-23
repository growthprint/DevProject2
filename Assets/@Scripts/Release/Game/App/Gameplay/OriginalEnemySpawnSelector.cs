#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 CharacterSpawnerGroupSelector다. 고를 수의 범위를 그룹 수로 자르고, 원작 Random.Range(x, y)처럼 위 끝을 빼고 고른다.
    // 고른 수만큼 그룹을 겹치지 않게 골라 그 그룹의 스포너를 모두 스폰한다. 시드는 출격과 선택기마다 같다.
    public sealed class OriginalEnemySpawnSelector : MonoBehaviour
    {
        // 원작 CharacterSpawnerGroup 하나의 스포너 목록이다. 우두머리는 원작처럼 첫 스포너의 첫 개체이며 그 스포너의 _firstIsLeader가 정한다.
        [Serializable]
        public sealed class SpawnerSet
        {
            [SerializeField] OriginalEnemySpawnGroup[] _spawners = Array.Empty<OriginalEnemySpawnGroup>();
            internal OriginalEnemySpawnGroup[] Spawners => _spawners;
        }

        [SerializeField] string _sourceId = "";
        [SerializeField] SpawnerSet[] _groups = Array.Empty<SpawnerSet>();
        // 원작 spawnGroupCountRange(x, y)다.
        [SerializeField] int _minimum = 1, _maximum = 1;

        // 첫 월드 초기화에서 모든 그룹의 후보를 끈다.
        internal void Hide()
        {
            foreach (SpawnerSet set in _groups)
                foreach (OriginalEnemySpawnGroup spawner in set.Spawners)
                    if (spawner != null) spawner.Hide();
        }

        // 원작 StartSpawn과 RandomSpawn이다. 범위가 같거나 거꾸로면 원작 Random.Range(x, x)처럼 아래 끝을 쓴다.
        internal void Spawn(OriginalEnemyWorld world, GameplayWorldContext shared, DifficultyState? difficulty, OriginalEnemySpawnRoot root)
        {
            // 같은 출격과 선택기면 같은 그룹을 고르도록 적 파사드의 출격 시드를 쓴다.
            var random = new System.Random(world.Seed(_sourceId));
            int maximum = Math.Min(_maximum, _groups.Length), minimum = Math.Min(_minimum, _groups.Length);
            int count = maximum > minimum ? random.Next(minimum, maximum) : minimum;
            var remaining = new List<int>(_groups.Length);
            for (int index = 0; index < _groups.Length; index++) remaining.Add(index);
            for (int picked = 0; picked < count && remaining.Count > 0; picked++)
            {
                int index = random.Next(remaining.Count);
                SpawnerSet set = _groups[remaining[index]];
                remaining.RemoveAt(index);
                foreach (OriginalEnemySpawnGroup spawner in set.Spawners)
                    if (spawner != null) spawner.Spawn(world, shared, difficulty, root);
            }
        }
    }
}
