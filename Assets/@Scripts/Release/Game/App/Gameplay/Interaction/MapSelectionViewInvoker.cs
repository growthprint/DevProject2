#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 기지 순간이동기(Building_TeleportMachine)의 상호작용 대상이다. 원작은 InteractableBase의 끝 UnityEvent가
    // ViewsProxy.ShowMapSelectionView를 부르고, 제품은 이 형식이 끝나면 호스트에 지도 선택 창 열기를 맡긴다.
    // 이름 키(UI_Interact_TeleportMachine), 영역, 안내 위치, 대기는 건물 프리팹 제작 도구가 원작 값으로 저장한다.
    [DisallowMultipleComponent]
    public sealed class MapSelectionViewInvoker : InteractableBase
    {
        // 원작 MapSelectionView.Show 자리다. 시설을 가진 전투 월드 호스트가 기지 화면 묶음으로 연다.
        internal override void OnInteractFinished(IInteractionHost host) => host.Facilities?.OpenMapSelection(this);
    }
}
