#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.MasterKeys.MasterKeyRegisterViewInvoker다. 원작 기지 Building_MasterKey 건물 안의 상호작용 대상이고,
    // F 상호작용이 끝나면 열쇠 등록 창을 연다. 원작 Awake의 finishWhenTimeOut = true는 제품 지연 상호작용이 시간이 차면 늘 끝내므로 따로 두지 않는다.
    // 영역, 이름 키, 안내 위치, 대기 시간은 건물 프리팹 제작 도구가 ConfigureInteraction으로 원작 값을 저장한다. 이 컴포넌트에는 따로 저장할 값이 없다.
    [DisallowMultipleComponent]
    public sealed class MasterKeyRegisterViewInvoker : InteractableBase
    {
        // 원작 OnInteractFinished의 MasterKeysRegisterView.Show 호출이다. 시설을 가진 전투 월드 호스트가 기지 화면 묶음으로 연다.
        internal override void OnInteractFinished(IInteractionHost host) => host.Facilities?.OpenMasterKeyRegister(this);
    }
}
