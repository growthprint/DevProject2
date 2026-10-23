#nullable enable annotations
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 InteractablePickup이다. 바닥에 떨군 아이템 그래프 하나를 F 한 번으로 줍는다. 지연 없는 상호작용이다.
    // 아이템 그래프와 대여 손잡이는 DroppedItemSpawner가 가지며, 모양은 템플릿의 OriginalPickupGraphic이 그린다.
    [DisallowMultipleComponent]
    public sealed class InteractablePickup : InteractableBase, IPoolable
    {
        internal override void OnInteractFinished(IInteractionHost host) => host.PickUp(this);

        internal void Configure(string sourceId, Collider? interactCollider, string nameKey) =>
            ConfigureInteraction(sourceId, interactCollider, nameKey, Vector3.zero, 0f);

        // 대여 직후에는 할 일이 없다. RuntimePointSpawner가 활성화 전에 구성 콜백으로 Configure를 부른다.
        void IPoolable.OnSpawn() { }

        // 풀 반환 때 구분자와 영역을 비워 다음 대여가 앞 아이템의 값을 물려받지 않게 한다.
        void IPoolable.OnDespawn() => Configure("", null, "");
    }
}
