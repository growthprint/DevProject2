#nullable enable annotations
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 플레이어가 죽은 자리의 유실물 묘비다. 원작은 LootBox_Tomb 상자이고, 이 게임은 원작 묘비 Pick All처럼 F 한 번으로 회수한다.
    // SourceId는 그 사망의 출격 토큰을 불변 문화권 문자열로 적은 값이다. 대여마다 DeadBodySpawner가 구성한다.
    [DisallowMultipleComponent]
    public sealed class LostBodyInteractable : InteractableBase, IPoolable
    {
        internal override void OnInteractFinished(IInteractionHost host) => host.RecoverLostBody(this);

        internal void Configure(string sourceId, Collider? interactCollider, string nameKey) =>
            ConfigureInteraction(sourceId, interactCollider, nameKey, Vector3.zero, 0f);

        // 대여 직후에는 할 일이 없다. RuntimePointSpawner가 활성화 전에 구성 콜백으로 Configure를 부른다.
        void IPoolable.OnSpawn() { }

        // 풀 반환 때 출격 토큰과 영역을 비워 다음 대여가 앞 묘비를 물려받지 않게 한다.
        void IPoolable.OnDespawn() => Configure("", null, "");
    }
}
