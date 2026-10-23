#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 기지 StorageDock 물체의 InteractableBase + ViewsProxy.ShowStorageDock 자리다. F로 고르면 창고 임시 보관함 창을 연다.
    // 원작은 상호작용 시작 이벤트로 열지만, 원작 대기 시간이 0이라 시작 프레임에 끝나는 OnInteractFinished와 같은 프레임이다.
    // 이름 키, 안내 위치, 대기 시간, 영역은 제작 도구가 원작 Base.unity 값으로 저장한다.
    [DisallowMultipleComponent]
    public sealed class StorageDockInteract : InteractableBase
    {
        // 원작 ViewsProxy.ShowStorageDock 호출이다. 시설을 가진 호스트만 열고, 기지가 아니면 보관함 화면이 스스로 열지 않는다.
        internal override void OnInteractFinished(IInteractionHost host) => host.Facilities?.OpenStorageDock(this);
    }
}
