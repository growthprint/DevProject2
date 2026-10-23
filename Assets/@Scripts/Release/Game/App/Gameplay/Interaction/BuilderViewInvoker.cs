#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 BuilderViewInvoker다. 기지 건설 입구(BuildingArea/Invoker/BuildingAreaInvoker, _1)의 상호작용 대상이고,
    // F 상호작용이 끝나면 연결한 건설 영역으로 건설 창을 연다. 영역, 이름 키(UI_Build), 안내 위치, 대기 시간은 씬 제작 도구가 원작 값으로 저장한다.
    [DisallowMultipleComponent]
    public sealed class BuilderViewInvoker : InteractableBase
    {
        // 원작 buildingArea다. 저작 연결이라 비어 있는 경우는 씬 검사가 막는다.
        [SerializeField] BuildingArea _buildingArea;

        internal BuildingArea BuildingArea => _buildingArea;

        // 원작 OnInteractFinished의 BuilderView.Show 호출이다. 시설을 가진 전투 월드 호스트가 기지 화면 묶음으로 연다.
        internal override void OnInteractFinished(IInteractionHost host) => host.Facilities?.OpenBuilder(this);
    }
}
