#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 Workbench(InteractableBase)다. 상호작용이 끝나면 개조 선택 창(원작 ItemCustomizeSelectionView.Show)을 연다.
    // 원작 작업대 건물의 Function/Modify는 꺼져 있어 닿을 수 없지만, 사용자 결정(2026-10-19)대로 켜서 쓴다(원작 차이).
    // 이름 키(UI_Interact_Modify), 대기 시간, 영역은 건물 프리팹 제작 도구가 원본 값으로 저장한다.
    [DisallowMultipleComponent]
    public sealed class Workbench : InteractableBase
    {
        // 원작 OnInteractFinished의 ItemCustomizeSelectionView.Show 호출이다. 시설을 가진 호스트가 개조 화면 컨트롤러로 연다.
        internal override void OnInteractFinished(IInteractionHost host) => host.Facilities?.OpenCustomize(this);
    }
}
