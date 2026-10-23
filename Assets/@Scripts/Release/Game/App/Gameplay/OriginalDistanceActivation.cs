#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 SetActiveByPlayerDistance다. 등록한 적을 플레이어와의 거리로 켜고 꺼서, 먼 적의 AI와 애니메이션이 돌지 않게 한다.
    // 원작의 하위 씬별 목록 대신 목록 하나에 소속 하위 씬 이름을 함께 둔다. 이유는 tasks/2026-10-22-set-active-by-player-distance.md에 있다.
    internal sealed class OriginalDistanceActivation
    {
        // 원작 LevelManager 프리팹에 저작된 거리다. 코드 기본값 100이 아니라 이 프리팹 값이 쓰인다.
        const float _distance = 50f;
        const float _sqrDistance = _distance * _distance;

        // 적 파사드가 레벨마다 하나 가져 정적 상태를 남기지 않는다. 준비된 산 적만 들어오고, 죽거나 파괴될 때 빠진다.
        // subscene은 스폰 루트의 원본 하위 씬 이름이며 비어 있으면 메인 씬 적이다. byDistance가 거짓인 적은 거리로 끄지 않는다.
        readonly List<(GameObject target, string subscene, bool byDistance)> _targets = new();

        internal void Register(GameObject target, string subscene = "", bool byDistance = true) => _targets.Add((target, subscene, byDistance));

        // 죽을 때 뺀 적이 파괴될 때 다시 빼도 되도록, 없는 대상이면 아무것도 하지 않는다.
        internal void Unregister(GameObject target)
        {
            for (int index = 0; index < _targets.Count; index++)
                if (_targets[index].target == target) { _targets.RemoveAt(index); return; }
        }

        // 원작처럼 3차원 거리가 엄격히 미만이면 켠다. 상태가 바뀔 때만 SetActive를 불러 같은 값을 다시 쓰지 않는다.
        // 원작은 꺼진 하위 씬의 캐릭터를 씬째 내리므로, 소속 하위 씬이 지금 하위 씬이 아닌 적은 거리와 관계없이 끈다.
        internal void Tick(Vector3 player, string? activeSubscene = null)
        {
            foreach ((GameObject target, string subscene, bool byDistance) in _targets)
            {
                bool on = (subscene.Length == 0 || subscene == activeSubscene)
                    && (!byDistance || (player - target.transform.position).sqrMagnitude < _sqrDistance);
                if (target.activeSelf != on) target.SetActive(on);
            }
        }
    }
}
