#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 SavesCounter의 처치 수(원작 키 Count/Kills/<key>)를 모든 씬에 걸쳐 소유한다. 적 키별 영구 처치 수다.
    // GameManager 프리팹 자식의 컴포넌트이고 GameManager.KillCounts로 찾는다. 저장은 KillCounts 구역이다. 메인 스레드에서만 쓴다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed class KillCounter : SessionSectionOwner
    {
        // 처음 기록한 순서의 적 키와 키별 수다. 저장은 처음 기록한 순서로 쓴다.
        readonly List<string> _order = new List<string>();
        readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

        // 원작 SavesCounter.GetKillCount다. 기록이 없으면 0이다. 2-Q1의 RequireEnemyKilled 퀘스트 조건이 읽는다.
        internal int GetKillCount(string enemyKey) => _counts.TryGetValue(enemyKey, out int count) ? count : 0;

        // 원작 SavesCounter.AddKillCount다. 이 소유자만 처치 수를 바꾸므로 확인과 적용을 한 멤버로 둔다.
        // 관문이 닫혔거나, 빈 키이거나, int를 넘으면 저장 규칙에 맞지 않아 기록 없이 false다. 호출자: GameplayCommands.TryRecordKill.
        internal bool TryAddKill(string enemyKey)
        {
            if (!AcceptsCommands || string.IsNullOrWhiteSpace(enemyKey)) return false;
            bool known = _counts.TryGetValue(enemyKey, out int count);
            if (count == int.MaxValue) return false;
            if (!known) _order.Add(enemyKey);
            _counts[enemyKey] = count + 1;
            return true;
        }

        // 저장마다 새 배열을 만들어 쓴다.
        private protected override void WriteSection(GameSaveData data)
        {
            var kills = new KillCountState[_order.Count];
            for (int index = 0; index < kills.Length; index++)
                kills[index] = new KillCountState(_order[index], _counts[_order[index]]);
            data.SetKillCounts(kills);
        }

        // 불러온 기록으로 바꾼다. 기록은 코덱이 키 중복과 음수를 거부한 값이다.
        private protected override void ReadSection(GameSaveData data)
        {
            _order.Clear();
            _counts.Clear();
            foreach (KillCountState kill in data.KillCounts)
            {
                _order.Add(kill.EnemyKey);
                _counts.Add(kill.EnemyKey, kill.Count);
            }
        }
    }
}
