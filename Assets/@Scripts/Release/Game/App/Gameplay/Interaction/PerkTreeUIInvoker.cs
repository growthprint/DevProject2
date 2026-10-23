#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.PerkTrees.Interactable.PerkTreeUIInvoker다. 기지 스킬 강화 기계(SkillMachine/Interact_Skill)와 특성 건물의 상호작용 대상이고,
    // 상호작용이 끝나면 perkTreeID 트리로 특성 트리 창을 연다. 원작은 OnInteractStart에서 열고 바로 끝내지만, 제품은 대기 0으로 같은 프레임의 끝에서 연다.
    // 트리 ID, 이름 키, 안내 위치, 대기, 묶음 구성원은 씬 제작 도구가 원작 값으로 저장한다.
    [DisallowMultipleComponent]
    public sealed class PerkTreeUIInvoker : InteractableBase
    {
        // 원작 perkTreeID다(예: Skills). 정의에 없는 ID는 화면이 경고를 남기고 열지 않는다.
        [SerializeField] string _perkTreeId = "";

        internal string PerkTreeId => _perkTreeId;

        // 원작 PerkTreeView.Show(PerkTreeManager.GetPerkTree(perkTreeID))다. 시설을 가진 전투 월드 호스트가 기지 화면 묶음으로 연다.
        internal override void OnInteractFinished(IInteractionHost host) => host.Facilities?.OpenPerkTree(this);
    }
}
