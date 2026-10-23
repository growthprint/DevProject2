#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 자판기(Buildings/SaleMachine/PerkWeaponShop)의 InteractableBase다. 원작은 상호작용 끝 UnityEvent가 같은 물체의 StockShop.ShowUI를 불렀다.
    // 제품은 같은 물체의 상점 인스턴스를 참조로 들고, 상호작용이 끝나면 호스트에 상점 화면을 열게 한다.
    // 원작 씬 값: 대기 시간 0, 이름 키 UI_Trade, 표시 오프셋 (0.26, 1.64, -0.9), 재사용 대기 0.3초. 조율자가 씬에 옮긴다.
    [DisallowMultipleComponent]
    public sealed class StockShopInteract : InteractableBase
    {
        // 같은 물체의 상점 컴포넌트다. 조율자가 씬에서 연결하고 씬 데이터 테스트가 확인한다.
        [SerializeField] StockShop _shop = null!;

        // 상점 열기(IFacilityHost.OpenShop → GameplayScreens → StockShopScreen.Show)가 읽는다.
        internal StockShop Shop => _shop;

        // 원작 상호작용 끝 UnityEvent의 StockShop.ShowUI다. 화면 선택과 하나만 열기는 시설을 가진 호스트가 맡는다.
        internal override void OnInteractFinished(IInteractionHost host) => host.Facilities?.OpenShop(this);
    }
}
