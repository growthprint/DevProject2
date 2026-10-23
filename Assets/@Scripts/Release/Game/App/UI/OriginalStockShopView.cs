#nullable enable annotations
using System;
using System.Globalization;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 Duckov.Economy.UI.StockShopView다. 상인 이름, 재고 새로고침 남은 시간, 상점 품목 줄, 판매 목록 세 개(가방, 펫 가방, 창고),
    // 고른 것의 상세(OriginalItemDetailsView 재사용), 재고, 가격, 상호작용 버튼을 보인다. 값만 받고 의도 이벤트만 낸다.
    // 상점과 경제 소유자, 저장 상태는 읽지 않는다. 열고 닫기, 입력 차단, 명령은 화면 컨트롤러(StockShopScreen)가 맡는다.
    // 원작 FadeGroup 전환과 구매 뒤 클릭 막기(clickBlockerFadeGroup)는 옮기지 않고 활성 여부로 보인다(NoteIndexView 선례, 원작 차이).
    public sealed class OriginalStockShopView : MonoBehaviour
    {
        // 원작 StockShopView 직렬화 값의 번역 키다. 제작 도구가 원본 CSV 값을 번역 테이블에 둔다.
        const string _stockKey = "UI_Stock", _buyKey = "UI_Purchase", _soldOutKey = "UI_SoldOut", _sellKey = "UI_Sell",
            _unlockKey = "UI_Unlock", _lockedKey = "UI_Locked";
        // 판매 목록 번호다. 원작 playerInventoryDisplay, petInventoryDisplay, playerStorageDisplay 순서다.
        internal const int _bagList = 0, _petList = 1, _storageList = 2, _sellListCount = 3;

        [SerializeField] TMP_Text _merchantName = null!;
        [SerializeField] TMP_Text _countdown = null!;
        // 원작 entryTemplate(상점 품목 줄)이다. 꺼진 채 저장하고 같은 부모 아래에 복제해 쓴다.
        [SerializeField] OriginalStockShopEntryView _entryTemplate = null!;
        // 판매 칸 템플릿이다. InventoryEntry.prefab 중첩 인스턴스이고 꺼진 채 가방 목록 격자 안에 있다.
        [SerializeField] OriginalStockShopEntryView _sellTemplate = null!;
        // 아래 배열은 판매 목록 번호 순서다. 원작 InventoryDisplay 루트, entriesParent, placeHolder, capacityText,
        // contentLayout, gridLayoutElement다.
        [SerializeField] GameObject[] _sellLists = Array.Empty<GameObject>();
        [SerializeField] RectTransform[] _sellGrids = Array.Empty<RectTransform>();
        [SerializeField] GameObject[] _sellPlaceholders = Array.Empty<GameObject>();
        [SerializeField] TMP_Text[] _sellCapacities = Array.Empty<TMP_Text>();
        [SerializeField] GridLayoutGroup[] _sellLayouts = Array.Empty<GridLayoutGroup>();
        [SerializeField] LayoutElement[] _sellHeights = Array.Empty<LayoutElement>();
        // 원작 details(ItemDetailsDisplay)다. 아래 재고, 버튼, 가격 표시는 원작처럼 이 상세 안에 있어 상세와 함께 숨는다.
        [SerializeField] OriginalItemDetailsView _details = null!;
        [SerializeField] TMP_Text _stock = null!;
        [SerializeField] string _stockFormat = "{text} : {current} / {max}";
        [SerializeField] Button _interaction = null!;
        [SerializeField] Image _interactionImage = null!;
        [SerializeField] TMP_Text _interactionText = null!;
        [SerializeField] Color _interactableColor = Color.white;
        [SerializeField] Color _notInteractableColor = Color.gray;
        [SerializeField] GameObject _cashOnly = null!;
        [SerializeField] GameObject _cannotSell = null!;
        [SerializeField] GameObject _priceDisplay = null!;
        [SerializeField] TMP_Text _priceText = null!;
        [SerializeField] GameObject _lockDisplay = null!;

        // 줄 모음과 판매 목록은 처음 쓸 때 만든다. EditMode 테스트와 공용 HUD 초기화가 Awake 없이 부르기 때문이다.
        UiTemplateCollection<OriginalStockShopEntryView>? _entries;
        OriginalInventoryDisplayList<OriginalStockShopEntryView>[]? _sellDisplays;
        DateTime _nextRefreshUtc;
        long _shownSeconds = -1;

        // 원작 줄 클릭(StockShopItemEntry.OnPointerClick)과 판매 칸 클릭(ItemUIUtilities.Select)이다. 품목은 상점 품목 번호, 판매 칸은 아이템 인스턴스 ID다.
        internal event Action<int>? ShopEntrySelected;
        internal event Action<string>? SellItemSelected;
        internal event Action? InteractRequested;
        internal event Action? CloseRequested;

        internal bool IsShown => gameObject.activeSelf;
        internal OriginalItemDetailsView Details => _details;

        UiTemplateCollection<OriginalStockShopEntryView> Entries =>
            _entries ??= new UiTemplateCollection<OriginalStockShopEntryView>(_entryTemplate, static entry => entry.ResetBinding());

        // 판매 목록마다 같은 판매 칸 템플릿과 그 목록 번호의 직렬화 참조로 만든다.
        OriginalInventoryDisplayList<OriginalStockShopEntryView> SellLists(int list)
        {
            if (_sellDisplays == null)
            {
                _sellDisplays = new OriginalInventoryDisplayList<OriginalStockShopEntryView>[_sellListCount];
                for (int index = 0; index < _sellListCount; index++)
                    _sellDisplays[index] = new OriginalInventoryDisplayList<OriginalStockShopEntryView>(_sellTemplate,
                        static entry => entry.ResetBinding(), _sellGrids[index], _sellPlaceholders[index], _sellCapacities[index], _sellLayouts[index],
                        _sellHeights[index]);
            }
            return _sellDisplays[list];
        }

        // 원작 SetupAndShow다. 남은 시간은 nextRefreshUtc로 이 View의 Update가 글자만 바꾼다.
        internal void Show(string merchantName, DateTime nextRefreshUtc)
        {
            _merchantName.text = merchantName;
            _nextRefreshUtc = nextRefreshUtc;
            _shownSeconds = -1;
            gameObject.SetActive(true);
            RefreshCountdown();
        }

        // 공용 HUD 초기화도 무조건 부르므로 숨어 있어도 된다. 줄 연결을 모두 끊어 닫힌 뒤 눌린 줄이 의도를 보내지 않게 한다.
        internal void Hide()
        {
            Entries.Clear();
            for (int list = 0; list < _sellListCount; list++) SellLists(list).Clear();
            gameObject.SetActive(false);
        }

        internal void BeginShopItems() => Entries.Begin();

        internal void AddShopItem(int index, Sprite? icon, string itemName, int count, long price, bool selected, bool unlocked, bool waiting, bool soldOut) =>
            Entries.Acquire(_entryTemplate.transform.parent)
                .BindShopItem(this, index, icon, itemName, count, Money(price), selected, unlocked, waiting, soldOut);

        internal void EndShopItems() => Entries.End();

        // 원작 InventoryDisplay.Setup이다. shown이 거짓이면 원작처럼 그 목록 전체를 숨긴다(펫 가방 없음, 창고 없음).
        // 칸 풀, 수용량 글자, 격자 높이는 다른 목록 창과 같은 목록 도우미가 맡는다. AddSellCell 뒤 EndSellList로 닫는다.
        internal void BeginSellList(int list, bool shown, int itemCount, int capacity)
        {
            _sellLists[list].SetActive(shown);
            SellLists(list).Begin(itemCount, capacity);
        }

        // 원작 InventoryEntry 하나다. itemId가 null이면 빈 칸이다.
        internal void AddSellCell(int list, string? itemId, Sprite? icon, string itemName, int count, bool selected) =>
            SellLists(list).Add().BindSellCell(this, itemId, icon, itemName, count, selected);

        // 원작 RefreshGridLayoutPreferredHeight다. 쓰지 않은 칸 숨기기와 격자 높이 맞춤은 목록 도우미가 맡는다.
        internal void EndSellList(int list) => SellLists(list).End();

        // 원작 RefreshStockText와 RefreshInteractionButton의 상점 줄 경우다. enough는 원작 new Cost(price).Enough처럼 계좌 돈만 센 값이다.
        internal void ShowShopItemState(int stock, int maxStock, bool unlocked, bool waiting, long price, bool enough, bool cashOnly)
        {
            _stock.gameObject.SetActive(true);
            _stock.text = _stockFormat.Replace("{text}", GameLocalization.Text(_stockKey)).Replace("{current}", stock.ToString(CultureInfo.InvariantCulture))
                .Replace("{max}", maxStock.ToString(CultureInfo.InvariantCulture));
            _cashOnly.SetActive(cashOnly);
            _cannotSell.SetActive(false);
            _priceDisplay.SetActive(unlocked);
            _lockDisplay.SetActive(!unlocked);
            _interaction.interactable = unlocked || waiting;
            if (unlocked)
            {
                _priceText.text = Money(price);
                bool inStock = stock > 0;
                if (!inStock) _interaction.interactable = false;
                _interactionText.text = GameLocalization.Text(inStock ? _buyKey : _soldOutKey);
                _interactionImage.color = inStock && enough ? _interactableColor : _notInteractableColor;
            }
            else
            {
                _interactionText.text = GameLocalization.Text(waiting ? _unlockKey : _lockedKey);
                _interactionImage.color = waiting ? _interactableColor : _notInteractableColor;
            }
        }

        // 원작 RefreshStockText와 RefreshInteractionButton의 가방 아이템 경우다. 팔 수 없으면 버튼을 끄고 판매 불가 표시를 켠다.
        internal void ShowSellItemState(long price, bool canSell, bool cashOnly)
        {
            _stock.gameObject.SetActive(false);
            _cashOnly.SetActive(cashOnly);
            _interaction.interactable = canSell;
            _priceDisplay.SetActive(true);
            _lockDisplay.SetActive(false);
            _interactionText.text = GameLocalization.Text(_sellKey);
            _interactionImage.color = _interactableColor;
            _priceText.text = Money(price);
            _cannotSell.SetActive(!canSell);
        }

        internal void ShopEntryClicked(int index) => ShopEntrySelected?.Invoke(index);
        internal void SellCellClicked(string itemId) => SellItemSelected?.Invoke(itemId);

        // 상호작용 버튼은 원작처럼 상세가 보이는 동안에만 보이고, 닫기 버튼은 창이 열린 동안에만 보인다.
        public void OnInteractionClicked()
        {
            Log.Instance.Assert(IsShown, "닫힌 상점 창의 상호작용 버튼이 눌렸습니다.", this);
            InteractRequested?.Invoke();
        }

        public void OnExitClicked()
        {
            Log.Instance.Assert(IsShown, "닫힌 상점 창의 닫기 버튼이 눌렸습니다.", this);
            CloseRequested?.Invoke();
        }

        // 원작 FixedUpdate의 RefreshCountDown이다. 초가 바뀔 때만 글자를 다시 만들어 매 프레임 할당하지 않는다.
        void Update() => RefreshCountdown();

        void RefreshCountdown()
        {
            TimeSpan eta = _nextRefreshUtc - DateTime.UtcNow;
            if (eta < TimeSpan.Zero) eta = TimeSpan.Zero;
            long seconds = (long)eta.TotalSeconds;
            if (seconds == _shownSeconds) return;
            _shownSeconds = seconds;
            _countdown.text = string.Format(CultureInfo.InvariantCulture, "{0}{1:00}:{2:00}:{3:00}", eta.Days > 0 ? eta.Days + " - " : "",
                eta.Hours, eta.Minutes, eta.Seconds);
        }

        // 원작 ToString("n0")처럼 천 단위 구분 기호를 넣는다. 문화권에 따라 기호가 바뀌지 않게 고정한다.
        static string Money(long value) => value.ToString("n0", CultureInfo.InvariantCulture);
    }
}
