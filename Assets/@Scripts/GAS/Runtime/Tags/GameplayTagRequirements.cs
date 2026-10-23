#nullable enable
using System;
using UnityEngine;

namespace Game.GAS
{
    // UE TagRequirements처럼 Require의 태그가 모두 있고 Ignore의 태그가 하나도 없을 때만 충족된다.
    [Serializable]
    public sealed class GameplayTagRequirements
    {
        [SerializeField] GameplayTagContainer _require = new GameplayTagContainer();
        [SerializeField] GameplayTagContainer _ignore = new GameplayTagContainer();

        public GameplayTagRequirements()
        {
        }

        public GameplayTagRequirements(GameplayTagContainer require, GameplayTagContainer ignore)
        {
            _require = require;
            _ignore = ignore;
        }

        public GameplayTagContainer Require => _require;
        public GameplayTagContainer Ignore => _ignore;
        public bool IsEmpty => _require.IsEmpty && _ignore.IsEmpty;

        public bool IsMetBy(GameplayTagCountMap tags) => tags.HasAll(_require) && !tags.HasAny(_ignore);

#if UNITY_EDITOR
        internal void ClearResolved()
        {
            _require.ClearResolved();
            _ignore.ClearResolved();
        }
#endif
    }
}
