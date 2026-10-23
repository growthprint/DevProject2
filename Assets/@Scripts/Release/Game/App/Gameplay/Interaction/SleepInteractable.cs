#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 기지 Base_SceneV2.unity의 Sleep 물체다. 원작은 일반 InteractableBase의 OnInteractFinishedEvent가 ViewsProxy.ShowSleepView를 부른다.
    // F 상호작용이 끝나면 수면 창을 연다. 영역, 이름 키(UI_Sleep), 안내 위치, 대기 시간은 제작 도구(Author Base Camp Sleep Object)가 원작 값으로 저장한다.
    [DisallowMultipleComponent]
    public sealed class SleepInteractable : InteractableBase
    {
        // 원작 ViewsProxy.ShowSleepView 호출이다. 시설을 가진 전투 월드 호스트가 기지 화면 묶음의 수면 화면으로 연다.
        internal override void OnInteractFinished(IInteractionHost host) => host.Facilities?.OpenSleep(this);
    }
}
