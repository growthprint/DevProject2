#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 Interact_CustomFace(프롤로그 거울)다. 끝나면 호스트가 커스터마이징을 연다. 복귀 연출은 커스터마이징 닫힘 알림이 맡는다.
    [DisallowMultipleComponent]
    public sealed class Interact_CustomFace : InteractableBase
    {
        internal override void OnInteractFinished(IInteractionHost host) => host.OpenCustomization(this);

        internal void Configure(string sourceId, Collider? interactCollider, string nameKey, Vector3 markerOffset, float interactTime) =>
            ConfigureInteraction(sourceId, interactCollider, nameKey, markerOffset, interactTime);
    }
}
