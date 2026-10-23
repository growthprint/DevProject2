#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 문 손잡이(InteractableBase, 끝나면 Door.Switch)다. 연결한 문을 여닫는다.
    [DisallowMultipleComponent]
    public sealed class DoorInteractable : InteractableBase
    {
        [SerializeField] Door _door = null!;

        internal Door Door => _door;

        internal override void OnInteractFinished(IInteractionHost host) => _door.Toggle();

        internal void Configure(string sourceId, Collider? interactCollider, string nameKey, Vector3 markerOffset, float interactTime, Door door)
        {
            ConfigureInteraction(sourceId, interactCollider, nameKey, markerOffset, interactTime);
            _door = door;
        }
    }
}
