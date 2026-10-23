#nullable enable
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 원작 ModifyCharacterStatsBase처럼 해금된 특성 스탯을 플레이어 Base에 더해 둔 값을 기억한다. 원작은 효과가 아니라 Base 값이라
    // 아바타와 상관없이 남는다. Base 쓰기는 정의 값을 아는 PlayerVitals가 넘긴 setBase가 하고, 메인 스레드에서만 쓴다.
    internal sealed class PlayerPerkStats
    {
        // 키는 스탯 키, 값은 지금 Base에 들어 있는 특성 합이다. 키 정리 버퍼는 사전을 훑는 동안 지우지 않으려고 둔다.
        readonly Dictionary<string, float> _perkStats = new(StringComparer.Ordinal);
        readonly List<string> _stalePerkStats = new();

        // 특성 소유자의 스탯 합을 Base에 맞춘다. 원작처럼 수정자가 아니라 Base = 정의 값 + 특성 합으로 두고, 합이 바뀐 키만 다시 쓰므로
        // 여러 번 불러도 두 번 더하지 않는다. Base가 바뀐 키가 있으면 true다.
        internal bool Sync(IReadOnlyDictionary<string, float> deltas, Action<string, float> setBase)
        {
            bool changed = false;
            // 다른 슬롯을 불러와 사라진 키는 정의 값으로 되돌린다. 사전을 훑는 동안 지우지 않도록 키를 먼저 모은다.
            List<string> stale = _stalePerkStats;
            foreach (KeyValuePair<string, float> applied in _perkStats)
                if (!deltas.ContainsKey(applied.Key)) stale.Add(applied.Key);
            for (int i = 0; i < stale.Count; i++)
            {
                _perkStats.Remove(stale[i]);
                setBase(stale[i], 0f);
                changed = true;
            }
            stale.Clear();
            foreach (KeyValuePair<string, float> delta in deltas)
            {
                if (_perkStats.TryGetValue(delta.Key, out float applied) && applied == delta.Value) continue;
                _perkStats[delta.Key] = delta.Value;
                setBase(delta.Key, delta.Value);
                changed = true;
            }
            return changed;
        }

        // 정의로 채우면 Base가 정의 값으로 돌아가므로 더해 둔 특성 스탯을 다시 더한다. 원작은 레벨마다 새 캐릭터에 OnLevelInitialized로 다시 더한다.
        internal void Reapply(Action<string, float> setBase)
        {
            foreach (KeyValuePair<string, float> applied in _perkStats) setBase(applied.Key, applied.Value);
        }
    }
}
