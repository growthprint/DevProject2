#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 씬 게임플레이가 런타임에 붙이는 보조 컴포넌트를 한 번만 확보한다. GetComponent 결과는 Editor에서 가짜 null일 수 있어
    // null 병합 연산자 대신 TryGetComponent로 찾고, 없을 때만 추가한다.
    internal static class GameplayComponents
    {
        internal static T GetOrAdd<T>(GameObject owner) where T : Component
        {
            // 호출자는 모두 살아 있는 컴포넌트의 gameObject나 방금 배치한 인스턴스를 넘기므로 owner는 null이 아니다.
            return owner.TryGetComponent(out T component) ? component : owner.AddComponent<T>();
        }
    }
}
