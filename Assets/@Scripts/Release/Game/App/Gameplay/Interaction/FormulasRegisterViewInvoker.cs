#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 FormulasRegisterViewInvoker다. 작업대와 의료소 같은 건물의 "설계도 연구" 상호작용 대상이고, 끝나면 설계도 등록 창을 연다.
    // 원작 Awake의 finishWhenTimeOut = true는 제품 지연 상호작용이 시간이 차면 늘 끝내므로 따로 두지 않는다.
    // 등록 화면 연결이 생겨 창 열기는 아래 OnInteractFinished가 호스트에 맡긴다.
    [DisallowMultipleComponent]
    public sealed class FormulasRegisterViewInvoker : InteractableBase
    {
        // 원작 additionalTags(Tag 자산 목록)를 자산 이름으로 둔다(예: Formula_Blueprint). 등록 칸은 Formula 태그와 이 태그를 모두 요구한다.
        // 건물 프리팹 제작 도구가 원작 값을 저장한다.
        [SerializeField] string[] _additionalTags = Array.Empty<string>();

        // 설계도 등록 창(FormulaRegisterScreen)이 칸 판정과 등록 명령의 장소 태그로 읽는다.
        internal IReadOnlyList<string> AdditionalTags => _additionalTags;

        // 원작 OnInteractFinished의 FormulasRegisterView.Show(additionalTags) 호출이다. 시설을 가진 호스트가 등록 화면 컨트롤러로 연다.
        internal override void OnInteractFinished(IInteractionHost host) => host.Facilities?.OpenFormulaRegister(this);
    }
}
