#nullable enable annotations
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 StockShopItemEntry(상점 품목 줄)와 상점 판매 목록의 InventoryEntry 칸을 함께 보인다. 두 원작 모두 ItemDisplay의 아이콘, 이름, 수량,
    // 선택 표시를 쓰므로 한 View로 두고, 품목 줄만 가격과 잠김·해금 대기·품절 표시를 더 가진다. 판매 칸에서는 그 참조가 비어 있다.
    // 판매 칸은 InventoryEntry.prefab 중첩 인스턴스다. 그 안의 OriginalInventoryCell은 인벤토리 화면(HudInventoryPanel) 전용이라 제작 도구가 꺼 둔다.
    // 그래서 상점 판매 목록은 끌어 옮기기, 더블클릭 사용, 퀵슬롯 등록 없이 고르기만 한다(S3 7절 판매 셀 재사용 범위, 원작 차이).
    // 클릭은 의도만 상점 View에 넘긴다. 원작 줄의 DOTween Punch 연출은 옮기지 않는다.
    public sealed class OriginalStockShopEntryView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] Image _icon = null!;
        // 이름과 수량은 원작 ItemDisplay처럼 글자 뒤 띠(nameContainer, countGameObject)째 숨긴다.
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] GameObject _nameBox = null!;
        [SerializeField] TMP_Text _count = null!;
        [SerializeField] GameObject _countBox = null!;
        [SerializeField] GameObject _selection = null!;
        // 아래 넷은 원작 StockShopItemEntry에만 있다. 판매 칸에서는 비어 있어 != null로 거른다.
        [SerializeField] TMP_Text? _price;
        [SerializeField] GameObject? _locked;
        [SerializeField] GameObject? _waiting;
        [SerializeField] GameObject? _outOfStock;
        // 판매 칸 InventoryEntry의 버튼이다. 원작처럼 빈 칸은 탐색과 강조 대상이 되지 않게 끈다. 품목 줄에는 없다.
        [SerializeField] Button? _button;

        OriginalStockShopView? _owner;
        bool _sellCell;
        int _index = -1;
        string? _itemId;

        // 원작 StockShopItemEntry.Setup과 Refresh다. 강제 해금 품목은 호출자가 unlocked 참, waiting 거짓으로 넘긴다.
        // 원작처럼 해금됐거나 해금 확인 대기인 줄만 켠다. 그래서 잠김 표시는 원작에서도 보이지 않는다.
        internal void BindShopItem(OriginalStockShopView owner, int index, Sprite? icon, string itemName, int count, string price, bool selected,
            bool unlocked, bool waiting, bool soldOut)
        {
            Bind(owner, false, index, null);
            // 품목 줄도 판매 칸과 같은 원작 ItemDisplay 규칙으로 보인다. 품목 줄은 늘 찬 칸이다.
            OriginalItemDisplay.Show(_icon, _name, _nameBox, _count, _countBox, icon, itemName, count, true);
            _selection.SetActive(selected);
            if (_price != null) _price.text = price;
            if (_locked != null) _locked.SetActive(!unlocked && !waiting);
            if (_waiting != null) _waiting.SetActive(!unlocked && waiting);
            if (_outOfStock != null) _outOfStock.SetActive(soldOut);
            gameObject.SetActive(unlocked || waiting);
        }

        // 원작 InventoryDisplay의 칸 하나다. itemId가 null이면 빈 칸이다. 원작처럼 팔 수 없는 아이템도 보이고 고를 수 있다.
        internal void BindSellCell(OriginalStockShopView owner, string? itemId, Sprite? icon, string itemName, int count, bool selected)
        {
            Bind(owner, true, -1, itemId);
            bool occupied = itemId != null;
            // 판매 칸은 다른 창 칸과 같은 원작 ItemDisplay 규칙을 쓴다.
            OriginalItemDisplay.Show(_icon, _name, _nameBox, _count, _countBox, icon, itemName, count, occupied);
            _selection.SetActive(selected && occupied);
            if (_button != null) _button.interactable = occupied;
            gameObject.SetActive(true);
        }

        // 풀로 돌아간 줄이 앞 줄의 의도를 보내지 않게 연결을 끊는다. 표시는 다음 Bind가 덮거나 줄 모음이 숨긴다.
        internal void ResetBinding()
        {
            _owner = null;
            _index = -1;
            _itemId = null;
        }

        // 원작 StockShopItemEntry.OnPointerClick과 InventoryEntry 왼쪽 클릭이다. 해금 확인과 선택 바꾸기는 화면 컨트롤러가 정한다.
        public void OnPointerClick(PointerEventData data)
        {
            if (_owner == null || data.button != PointerEventData.InputButton.Left) return;
            if (!_sellCell) _owner.ShopEntryClicked(_index);
            else if (_itemId != null) _owner.SellCellClicked(_itemId);
        }

        void Bind(OriginalStockShopView owner, bool sellCell, int index, string? itemId)
        {
            _owner = owner;
            _sellCell = sellCell;
            _index = index;
            _itemId = itemId;
        }
    }
}
