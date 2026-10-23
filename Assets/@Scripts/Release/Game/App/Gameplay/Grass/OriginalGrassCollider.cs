#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;

// 장면 전환이 풀 굽기를 부르므로 풀 코드 전체를 게임 어셈블리에 둔다.
namespace Game.App
{
    // 원본 TgsCollider다. 켜져 있는 동안 목록에 올라, 풀 렌더러가 덩어리마다 가까운 구를 골라 풀잎을 밀어내게 한다.
    // 원본 캐릭터 프리팹은 GrassCollider 자식에 반지름 0.35로 붙인다.
    public sealed class OriginalGrassCollider : MonoBehaviour
    {
        [SerializeField, Min(0f)] float _radius = 1f;

        internal static readonly List<OriginalGrassCollider> _activeColliders = new List<OriginalGrassCollider>(16);

        internal float Radius => _radius;

        void OnEnable() => _activeColliders.Add(this);

        void OnDisable() => _activeColliders.Remove(this);
    }
}
