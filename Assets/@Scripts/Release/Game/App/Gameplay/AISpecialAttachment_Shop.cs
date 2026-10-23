#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 AISpecialAttachment_Shop이다. 비밀 상인 캐릭터 루트의 자식 부착물이며, 원작 shop은 부착물 자신이다.
    // 원작 OnInited처럼 상점 물체를 적의 hideIfFoundEnemy로 넘기면, 적이 AI 갱신마다 교전 중이면 끄고 아니면 켠다(원작 AICharacterController.cs:388-391).
    // 제품 F 상호작용은 저작 표에 없는 대상을 월드의 실행 중 목록(SpawnedInteractables)에서 찾는다. 그래서 부착물이 켜져 있는 동안만
    // 상점 상호작용을 그 목록에 두어, 숨은 상점(생성 전, 교전 중, 사망 뒤)은 고를 수 없게 한다. 이 등록은 원작에 없는 제품 규칙이다.
    [DisallowMultipleComponent]
    public sealed class AISpecialAttachment_Shop : MonoBehaviour
    {
        // 원작 shop이다. 제작 도구가 부착물 자신을 넣는다. 켜고 끄는 물체와 아래 등록 콜백을 받는 물체가 같아야 하기 때문이다.
        [SerializeField] GameObject _shop = null!;
        // 같은 물체의 상점 상호작용이다. 원작은 상호작용 끝 UnityEvent가 같은 물체의 StockShop.ShowUI를 불렀다.
        [SerializeField] StockShopInteract _interact = null!;
        // Init이 받은 월드다. Init 전(씬 불러오기, 후보 숨김)에는 비어 있어 켜지고 꺼져도 등록하지 않는다. 일반 C# 객체다.
        GameplayWorldContext? _world;

        // 원작 AISpecialAttachmentBase.Init과 AISpecialAttachment_Shop.OnInited다. 스폰 그룹이 고른 상인을 준비할 때 적이 한 번 부른다.
        internal void Init(OriginalEnemyController enemy, GameplayWorldContext world)
        {
            _world = world;
            enemy.SetHideIfFoundEnemy(_shop);
        }

        // 적이 상점을 다시 보이면 월드 목록에 넣는다. 켜짐과 꺼짐이 번갈아 오므로 같은 대상이 두 번 들어가지 않는다.
        void OnEnable()
        {
            if (_world != null) _world.AddSpawnedInteractable(_interact);
        }

        // 숨김, 사망, 씬 해제로 꺼지거나 파괴될 때 월드 목록에서 뺀다. 넣지 않은 대상을 빼면 아무 일도 없다.
        void OnDisable()
        {
            if (_world != null) _world.RemoveSpawnedInteractable(_interact);
        }
    }
}
