#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 MultiSceneTeleporter다. 끝나면 목적지 하위 씬을 켜고 플레이어를 목적지 좌표로 옮긴다.
    [DisallowMultipleComponent]
    public sealed class MultiSceneTeleporter : InteractableBase
    {
        [SerializeField] Vector3 _destination;
        // 원본의 하위 씬 전환은 목적지 좌표와 따로 적는다. 음수면 하위 씬을 바꾸지 않는다.
        [SerializeField] int _destinationSubscene = -1;
        // 원작 SimpleTeleporter의 transitionTime(효과 시간, 0.28초)과 delay(효과 뒤 이동까지, 0.3초)다. 원작 MultiSceneTeleporter인
        // 동굴·기지 출입은 효과가 없어 둘 다 0이고, 호스트가 바로 옮긴다. 기본값 0은 효과를 저작하지 않은 다른 순간이동 대상이 지연을 얻지 않게 한다.
        // 제작 도구가 SerializedObject로 원작 SimpleTeleporter 쌍에만 값을 적는다.
        [SerializeField] float _effectTime;
        [SerializeField] float _delay;

        internal Vector3 Destination => _destination;
        internal int DestinationSubscene => _destinationSubscene;
        internal float EffectTime => _effectTime;
        internal float Delay => _delay;

        internal override void OnInteractFinished(IInteractionHost host) => host.Teleport(this);

        // 제작 도구가 쓰는 구성이다. 목적지 하위 씬은 하위 씬 연결을 마친 뒤 ConfigureDestinationSubscene으로 따로 쓴다.
        internal void Configure(string sourceId, Collider? interactCollider, string nameKey, Vector3 markerOffset, float interactTime,
            Vector3 destination)
        {
            ConfigureInteraction(sourceId, interactCollider, nameKey, markerOffset, interactTime);
            _destination = destination;
        }

        internal void ConfigureDestinationSubscene(int index) => _destinationSubscene = index;
    }
}
