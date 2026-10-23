#nullable enable annotations
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 InteractablePickup의 graphicRoot 아래 표시다. 원작 ItemGraphicInfo.CreateAGraphic처럼 아이템 모델이 있으면 모델을,
    // 없으면 아이콘 스프라이트를 보인다. 바닥 아이템 템플릿에 붙고, 풀 반환 때 이번 대여에서 만든 모델을 지운다.
    [DisallowMultipleComponent]
    public sealed class OriginalPickupGraphic : MonoBehaviour, IPoolable
    {
        // 모델이 없는 아이템의 아이콘을 그린다. 원작 ItemGraphicInfo.fallbackSprite 자리다.
        // 아래 세 참조는 템플릿에서 연결하고 EditMode 저작 테스트가 비어 있지 않음을 보므로 Show는 검사하지 않는다.
        [SerializeField] SpriteRenderer _sprite = null!;
        // 원작 ItemGraphic_Sprite 프리팹 루트다(그림자 쿼드와 아이콘). 원작은 모델이 있는 아이템에 이 프리팹을 만들지 않으므로
        // 모델을 보일 때는 루트째 숨긴다.
        [SerializeField] GameObject _spriteGraphic = null!;
        // 아이템 모델을 붙일 부모다. 원작 InteractablePickup.graphicRoot 자리다.
        [SerializeField] Transform _modelRoot = null!;
        // 이번 대여에서 만든 아이템 모델이다. 반환 때 파괴해 다음 대여가 앞 아이템의 모델을 보이지 않게 한다.
        GameObject? _model;

        // 바닥 아이템을 빌린 뒤 활성화 전에 한 번 부른다. 원작처럼 모델을 graphicRoot 원점에 단위 크기로 붙이고 아이콘 표시를 통째로 숨기며,
        // 모델이 없으면 아이콘을 보인다. 풀은 OnDespawn이 앞 모델을 지운 인스턴스만 다시 빌려주므로 여기서 지우지 않는다.
        internal void Show(OriginalItemDefinition definition)
        {
            GameObject? visual = definition.Visual;
            if (visual != null)
            {
                GameObject model = Instantiate(visual, _modelRoot, false);
                model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                model.transform.localScale = Vector3.one;
                _model = model;
                _spriteGraphic.SetActive(false);
                return;
            }
            _spriteGraphic.SetActive(true);
            _sprite.sprite = definition.Icon;
            _sprite.enabled = true;
        }

        // 대여 직후에는 할 일이 없다. 바닥 아이템 생성이 활성화 전에 Show로 이번 아이템의 모양을 정한다.
        void IPoolable.OnSpawn() { }

        // 풀 반환 때 모델과 아이콘을 비워 다음 대여가 앞 아이템의 모양을 물려받지 않게 한다. 초기화 전에 와도 안전하다.
        void IPoolable.OnDespawn()
        {
            ClearModel();
            if (_sprite != null) _sprite.sprite = null;
        }

        // 만든 모델을 한 번만 파괴한다. 이미 파괴된 모델은 DestroyOwned가 건너뛴다.
        void ClearModel()
        {
            GameObject? model = _model;
            _model = null;
            UnityObjects.DestroyOwned(model);
        }
    }
}
