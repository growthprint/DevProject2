#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 비콘의 F 대상(TeleportMarker/Agent_Pickup, UI_ActivateBeacon)이다. 원작은 하위 클래스 없는 InteractableBase의
    // OnInteractFinishedEvent가 TeleportBeacon.ActivateBeacon을 부른다. 제품 상호작용 이벤트에는 그 호출 종류가 없고, 드라이버는
    // InteractableBase만 F로 고르므로 이 하위 클래스가 끝날 때 비콘을 부른다. 호스트 인터페이스는 쓰지 않는다.
    [DisallowMultipleComponent]
    public sealed class TeleportBeaconInteract : InteractableBase
    {
        // 제작 도구가 SerializedObject로 원본 호출 대상 비콘을 적는다. 빠진 참조는 제작 도구와 씬 데이터 테스트가 막는다(C6).
        [SerializeField] TeleportBeacon? _beacon;

        internal override void OnInteractFinished(IInteractionHost host) => _beacon!.ActivateBeacon();
    }
}
