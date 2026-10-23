#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 기지 Buildings/SkillMachine/Interact_Skill/Endowment 물체의 InteractableBase + ViewsProxy.ShowEndowmentView 자리다.
    // F로 고르면 재능 선택 창을 연다. 원작은 스킬 강화 기계(Interact_Skill)의 상호작용 묶음 구성원이라 자기 영역 Collider가 꺼져 있고,
    // 묶음 대표는 그 기계의 특성 진입점이다. 이름 키 Endowment_Selection, 대기 0.2초, 안내 위치는 제작 도구가 원작 Base_SceneV2.unity 값으로 저장한다.
    [DisallowMultipleComponent]
    public sealed class EndowmentSelectionInteract : InteractableBase
    {
        // 원작 상호작용 끝의 ViewsProxy.ShowEndowmentView 호출이다. 시설을 가진 전투 월드 호스트가 기지 화면 묶음으로 연다.
        internal override void OnInteractFinished(IInteractionHost host) => host.Facilities?.OpenEndowmentSelection(this);
    }
}
