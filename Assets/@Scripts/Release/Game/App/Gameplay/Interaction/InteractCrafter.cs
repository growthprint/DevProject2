#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 InteractCrafter다. 작업대, 인쇄기, 의료소 같은 제조 장소의 상호작용 대상이고, 끝나면 그 장소 태그로 제조 창을 연다.
    // 원작 Awake의 finishWhenTimeOut = true는 제품 지연 상호작용이 시간이 차면 늘 끝내므로 따로 두지 않는다.
    // 제조 화면 연결이 생겨 창 열기는 아래 OnInteractFinished가 호스트에 맡긴다.
    [DisallowMultipleComponent]
    public sealed class InteractCrafter : InteractableBase
    {
        // 원작 requireTag다(예: WorkBenchAdvanced). 비었거나 공백이면 원작 FilterCraft처럼 해금한 제조식을 모두 보인다.
        // 건물 프리팹 제작 도구가 원작 값을 저장한다.
        [SerializeField] string _requireTag = "";

        // 제조 창(CraftScreen)이 목록 거르기와 제조 명령의 장소 태그로 읽는다.
        internal string RequireTag => _requireTag;

        // 원작 OnInteractFinished의 CraftView.SetupAndOpenView(FilterCraft) 호출이다. 원작 base 호출은 빈 동작이라 옮길 것이 없다.
        // 시설을 가진 호스트가 제조 화면 컨트롤러로 열고, 목록은 이 장소 태그로 거른다.
        internal override void OnInteractFinished(IInteractionHost host) => host.Facilities?.OpenCraft(this);
    }
}
