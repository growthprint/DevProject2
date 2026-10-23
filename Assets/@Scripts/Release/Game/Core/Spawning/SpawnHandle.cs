#nullable enable annotations
using System;
using UnityEngine;

namespace Game.Core
{
    // Spawner가 내준 인스턴스 한 번의 사용권이다. 표식과 받은 세대만 담는 값이라 할당이 없다.
    // 반납이나 파괴 뒤에는 세대나 상태가 달라져 다시 살아나지 않는다. 기본값은 살아 있지 않은 빈 핸들이다.
    public readonly struct SpawnHandle : IEquatable<SpawnHandle>
    {
        // 인스턴스의 소유 표식이다. 기본 핸들이면 비어 있다.
        readonly SpawnTag _tag;
        // 이 핸들을 만들 때의 표식 세대다.
        readonly int _generation;

        // Spawner만 만든다.
        internal SpawnHandle(SpawnTag tag, int generation)
        {
            _tag = tag;
            _generation = generation;
        }

        // Spawner가 반납이나 활성화할 대상을 찾는 표식이다. 살아 있는지는 IsAlive로 먼저 본다.
        internal SpawnTag Tag => _tag;

        // 표식이 파괴되지 않았고, 세대가 같고, 사용 중일 때만 살아 있다. 표식 비교는 Unity의 파괴 검사를 거친다.
        public bool IsAlive => _tag != null && _tag.Generation == _generation && _tag.State == SpawnTag.SpawnState.InUse;

        // 살아 있는 핸들의 인스턴스다. 살아 있지 않으면 null이다.
        public GameObject? Instance => IsAlive ? _tag.Instance : null;

        // 같은 표식 참조와 같은 세대면 같은 핸들이다. 파괴된 두 표식을 같다고 보는 Unity == 대신 참조로 비교한다.
        public bool Equals(SpawnHandle other) => ReferenceEquals(_tag, other._tag) && _generation == other._generation;

        // 상자에 담긴 비교도 같은 규칙을 따른다.
        public override bool Equals(object? obj) => obj is SpawnHandle other && Equals(other);

        // Equals와 맞도록 표식은 참조 기준 해시를 쓰고 세대와 결합한다. 표식이 비어 있으면 0이다.
        public override int GetHashCode() =>
            HashCode.Combine(ReferenceEquals(_tag, null) ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_tag), _generation);
    }
}
