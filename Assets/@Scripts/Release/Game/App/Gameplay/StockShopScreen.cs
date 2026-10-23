#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 StockShop.ShowUI → StockShopView.SetupAndShow처럼 상점 인스턴스 하나에 공용 HUD의 상점 창을 연다. NoteScreen과 같은 수명 규칙이다.
    // 열 때 상점에 새로고침 검사(TryOpen)를 요청하고, 열린 동안 게임플레이 입력을 막는다. 구매, 판매, 해금 확인은 상점(StockShop)과 경제 소유자로 보내고
    // 성공했을 때만 소리와 알림을 낸다. 화면 콜백은 읽기만 하고, 소유자가 확정한 뒤 바뀐 알림으로 다시 그린다.
    // 판매 목록은 원작처럼 가방, 펫 가방, 창고 순이고, 지불·판매와 같은 씬 기본 출처(ItemSourceRules.Owned)에 든 목록만 보인다.
    internal sealed class StockShopScreen : GameplayScreen
    {
        const string _buySound = "event:/UI/buy", _sellSound = "event:/UI/sell";
        // 원작 StockShop.purchaseNotificationTextFormatKey다. {itemDisplayName} 자리에 이름을 넣는다. 해금 알림은 QuestNotices가 낸다.
        const string _purchasedKey = "UI_StockShop_PurchasedNotification";

        readonly GameplayWorldContext _world;
        // 레이드 구매에서 가방에 못 넣은 아이템을 발밑에 떨구는 월드 생성기다. 낚시·전투 떨굼과 같은 것이다(5-M).
        readonly DroppedItemSpawner _drops;
        readonly InventoryItemDetails _details;
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        StockShop? _shop;
        // 원작 선택은 상점 줄 하나 또는 가방 아이템 하나다. 둘 다 없으면 상세를 숨긴다. 선택은 화면 상태라 저장하지 않는다.
        int _selectedEntry = -1;
        string? _selectedItem;

        internal StockShopScreen(GameplayWorldContext world, DroppedItemSpawner drops)
        {
            _world = world;
            _drops = drops;
            _details = new InventoryItemDetails(world);
        }

        // 원작 StockShop.ShowUI다. 공용 HUD가 이 월드에 붙어 있지 않거나, 입력 차단을 못 받거나(정리 중), 새로고침 기록을 못 쓰면(경제 관문 닫힘)
        // 열지 않고 아무것도 바꾸지 않는다. 다른 상점이 열려 있으면 그 창을 닫고 연다. 호출자: 2-W 상점 열기(GameplayScreens).
        internal void Show(StockShop shop)
        {
            // HUD 연결과 세션 조회는 월드 문맥이 한 번에 본다. 세션 조회가 앱 루트 유무도 함께 본다.
            if (!_world.TryPresentSession(out GameSessionManager? session)) return;
            Close();
            if (!TryBlockInput(_world.Runtime)) return;
            DateTime now = DateTime.UtcNow;
            DifficultyState? difficulty = GameManager.Difficulty.Current;
            // 새로고침 기록을 못 쓰면 받은 입력 차단을 바로 돌려주고 열지 않는다.
            if (!shop.TryOpen(GameManager.Economy, _world.Catalog, difficulty != null && difficulty.AdvancedDebuffMode, now))
            {
                ReleaseInput();
                return;
            }
            _shop = shop;
            _selectedEntry = -1;
            _selectedItem = null;
            OriginalStockShopView view = _world.Hud.Shop;
            Action<int> selectEntry = SelectEntry;
            Action<string> selectItem = SelectItem;
            Action interact = Interact, close = Close, redraw = Redraw;
            view.ShopEntrySelected += selectEntry;
            _subscriptions.Add(() => view.ShopEntrySelected -= selectEntry);
            view.SellItemSelected += selectItem;
            _subscriptions.Add(() => view.SellItemSelected -= selectItem);
            view.InteractRequested += interact;
            _subscriptions.Add(() => view.InteractRequested -= interact);
            view.CloseRequested += close;
            _subscriptions.Add(() => view.CloseRequested -= close);
            shop.Changed += redraw;
            _subscriptions.Add(() => shop.Changed -= redraw);
            EconomyManager.Changed += redraw;
            _subscriptions.Add(() => EconomyManager.Changed -= redraw);
            // 가방, 창고, 펫 가방 변경은 세션 변경 알림으로 받는다. 줄 수가 적어 알림마다 다시 그리고,
            // 세션과 로캘 구독은 공용 구독 도우미가 붙이고 뗀다.
            _subscriptions.BindSessionChanged(session, redraw);
            _subscriptions.BindLocaleChanged(redraw);
            view.Show(GameLocalization.Text(shop.DisplayNameKey), now + shop.NextRefreshEta(now));
            Redraw();
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다. 기지 밖에는 자동 저장이 없어 무시된다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 구독과 입력 차단을 돌려주고 창을 닫는다. 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다.
        internal override void Close()
        {
            if (!IsOpen) return;
            _subscriptions.Clear();
            ReleaseInput();
            _shop = null;
            _selectedEntry = -1;
            _selectedItem = null;
            if (_world.PresentationAttached)
            {
                _details.Hide(_world.Hud.Shop.Details, true);
                _world.Hud.Shop.Hide();
            }
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 StockShopItemEntry.OnPointerClick이다. 해금 확인 대기 품목은 먼저 확인(경제 소유자 명령)하고, 같은 줄이면 선택을 푼다.
        // 해금 확인 대기 판정은 상점 구매와 같은 상점 판정을 쓴다.
        void SelectEntry(int index)
        {
            if (_shop == null) return;
            StockShopItem item = _shop.Items[index];
            EconomyManager economy = GameManager.Economy;
            if (_shop.IsWaitingForUnlock(economy, index)) economy.TryConfirmUnlock(item.ItemId);
            _selectedItem = null;
            _selectedEntry = _selectedEntry == index ? -1 : index;
            Redraw();
        }

        // 원작 판매 칸 선택(ItemUIUtilities.Select)이다. 상점 줄 선택은 풀고, 같은 칸이면 선택을 푼다.
        void SelectItem(string itemId)
        {
            if (_shop == null) return;
            _selectedEntry = -1;
            _selectedItem = _selectedItem == itemId ? null : itemId;
            Redraw();
        }

        // 원작 OnInteractionButtonClicked다. 가방 아이템이면 판매, 상점 줄이면 구매나 해금 확인이다. 거절되면 화면과 소리는 그대로다.
        void Interact()
        {
            if (_shop == null || !_world.TrySession(out GameSessionManager? session)) return;
            EconomyManager economy = GameManager.Economy;
            OriginalGameplayCatalog catalog = _world.Catalog;
            StockShop shop = _shop;
            if (_selectedItem != null)
            {
                if (!shop.TrySell(session, economy, catalog, _selectedItem)) return;
                _world.Audio.PlayOneShot(_sellSound);
                // 원작처럼 판 뒤 선택을 지운다. 판매는 상점 재고를 바꾸지 않아 소지품 변경 알림이 이미 다시 그렸으므로 한 번 더 그린다.
                _selectedItem = null;
                Redraw();
                return;
            }
            if (_selectedEntry < 0) return;
            int index = _selectedEntry;
            if (shop.TryBuy(session, economy, catalog, index, out IReadOnlyList<ItemOwnershipState> dropped) != StockShopPurchase.Bought) return;
            _world.Audio.PlayOneShot(_buySound);
            // 품목 ID는 저작 데이터라 카탈로그 정의가 늘 있다. 이름 자리 규칙은 다른 알림과 같은 GameLocalization 도우미가 정한다.
            OriginalItemDefinition definition = catalog.DefinitionOf(shop.Items[index].ItemId);
            _world.Hud.Notify(GameLocalization.WithItemName(GameLocalization.Text(_purchasedKey), GameLocalization.Text(definition.NameKey)));
            // 기지 구매는 가방, 창고, 보관함 순으로 받아 떨굴 것이 없다. 레이드 상점(비밀 상인)에서 가방이 차면 원작처럼
            // 낚시와 전투 떨굼과 같은 규칙으로 플레이어 발밑 둘레에 떨군다.
            if (dropped.Count > 0) _drops.SpawnAroundPlayer(dropped);
        }

        // 원작 Setup과 OnSelectionChanged다. 보이는 품목 줄, 판매 목록 세 개, 고른 것의 상세와 버튼을 소유자 값으로 다시 그린다.
        // 씬 정리 중이라 세션이나 소유 그래프가 없으면 기록 없이 그리지 않는다.
        void Redraw()
        {
            if (_shop == null || !_world.TryItems(out GameSessionManager? session, out ItemOwnershipState? items)) return;
            OriginalStockShopView view = _world.Hud.Shop;
            EconomyManager economy = GameManager.Economy;
            OriginalGameplayCatalog catalog = _world.Catalog;
            StockShop shop = _shop;
            IReadOnlyList<StockShopItem> stock = shop.Items;
            bool entryShown = false;
            view.BeginShopItems();
            for (int index = 0; index < stock.Count; index++)
            {
                // 원작 Setup처럼 entry.Show인 품목만 줄로 만든다. 품목 ID는 저작 데이터라 카탈로그 정의가 늘 있다.
                // 해금과 확인 대기 판정은 구매 명령과 같은 상점 판정을 쓴다.
                if (!shop.IsShown(index)) continue;
                OriginalItemDefinition definition = catalog.DefinitionOf(stock[index].ItemId);
                bool unlocked = shop.IsUnlocked(economy, index);
                bool waiting = shop.IsWaitingForUnlock(economy, index);
                view.AddShopItem(index, definition.Icon, GameLocalization.Text(definition.NameKey), StockShopRules.BuyCount(definition),
                    shop.BuyPrice(catalog, index), index == _selectedEntry, unlocked, waiting, shop.StockAt(index) <= 0);
                // 원작 줄은 해금됐거나 확인 대기일 때만 켜진다. 꺼진 줄의 선택은 원작처럼 누를 수 없으므로 아래에서 푼다.
                if (index == _selectedEntry) entryShown = unlocked || waiting;
            }
            view.EndShopItems();
            if (!entryShown) _selectedEntry = -1;

            // 검증된 소유 그래프라 플레이어 루트와 그 소지 컨테이너가 늘 있다(InventoryScreen과 같은 공용 질의).
            OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? character);
            // 판매 목록 출처는 판매 명령과 같은 세션 기본 출처 규칙으로 정한다.
            ItemSources sources = ItemSourceRules.Owned(session);
            bool itemShown = false;
            SellList(view, items, OriginalStockShopView._bagList, true, character!.InstanceId, character.Inventory!, ref itemShown);
            // 원작처럼 펫 가방은 그래프에 있을 때, 창고는 기지(원작 PlayerStorage가 있을 때)에서만 보인다. 출처 규칙이 그 씬 차이를 정하고,
            // 창고 판정은 등록, 분해, 개조 화면과 같은 공용 질의가 한다.
            ItemContainerState? petBag = null;
            bool pet = (sources & ItemSources.Pet) != 0 && OriginalInventoryQueries.TryContainer(items, GameplayInventory._pet, out petBag);
            SellList(view, items, OriginalStockShopView._petList, pet, GameplayInventory._pet, petBag, ref itemShown);
            bool storage = OriginalInventoryQueries.TryShownStorage(items, sources, out ItemContainerState? stored);
            SellList(view, items, OriginalStockShopView._storageList, storage, GameplayInventory._storage, stored, ref itemShown);
            if (!itemShown) _selectedItem = null;

            // 상세 표시가 세션을 쓰지 않게 되어 넘기지 않는다.
            ShowSelection(view, items, economy, catalog, shop);
        }

        // 원작 InventoryDisplay처럼 0번 칸부터 용량까지 빈 칸도 모두 그린다. 원작 Setup의 세 번째 인자(e == null || e.CanBeSold)는
        // 거르기가 아니라 조작 가능 여부라서 팔 수 없는 아이템도 보이고, 고르면 판매 불가 표시가 켜진다.
        void SellList(OriginalStockShopView view, ItemOwnershipState items, int list, bool shown, string owner, ItemContainerState? container,
            ref bool selectedShown)
        {
            if (!shown || container == null)
            {
                view.BeginSellList(list, false, 0, 0);
                view.EndSellList(list);
                return;
            }
            // 칸 수 규칙은 다른 아이템 화면과 같은 소유 그래프 질의가 정한다.
            int capacity = OriginalInventoryQueries.CellCount(container);
            view.BeginSellList(list, true, container.Entries.Count, capacity);
            for (int position = 0; position < capacity; position++)
            {
                string? id = items.EntryAt(owner, position);
                // 칸 값은 다른 아이템 화면과 같은 공용 칸 값 도우미가 읽는다. 빈 칸이면 비운 값이고 고른 칸이 될 수 없다.
                ItemCellValues.Describe(items, _world.Catalog, id, out Sprite? icon, out string name, out int count);
                bool selected = id != null && id == _selectedItem;
                if (selected) selectedShown = true;
                view.AddSellCell(list, id, icon, name, count, selected);
            }
            view.EndSellList(list);
        }

        // 원작 OnSelectionChanged, RefreshStockText, RefreshInteractionButton이다. 상점 줄은 아직 소유하지 않은 새 아이템 한 묶음으로 상세를 보인다.
        // 상세 화면이 퀵슬롯을 읽지 않게 되어, 퀵슬롯을 읽으려고 받던 세션을 받지 않는다.
        void ShowSelection(OriginalStockShopView view, ItemOwnershipState items, EconomyManager economy,
            OriginalGameplayCatalog catalog, StockShop shop)
        {
            // 상세 화면은 인벤토리 화면과 같은 OriginalItemDetailsView 규칙(InventoryItemDetails)을 쓰므로 칸 셀도 공용 HUD 인벤토리 패널에 묶는다.
            HudInventoryPanel panel = _world.Hud.Inventory;
            if (_selectedEntry >= 0)
            {
                StockShopItem item = shop.Items[_selectedEntry];
                // 품목 ID는 저작 데이터라 카탈로그 정의가 늘 있다. 해금 판정은 줄 표시와 같은 상점 판정을 쓴다.
                OriginalItemDefinition definition = catalog.DefinitionOf(item.ItemId);
                // 상세 화면이 퀵슬롯을 읽지 않게 되어 퀵슬롯 인수를 넘기지 않는다.
                _details.Show(view.Details, panel, null, null, definition, StockShopRules.BuyCount(definition));
                bool unlocked = shop.IsUnlocked(economy, _selectedEntry);
                bool waiting = shop.IsWaitingForUnlock(economy, _selectedEntry);
                long price = shop.BuyPrice(catalog, _selectedEntry);
                // 원작 버튼 색은 new Cost(price).Enough라 현금 전용 상점에서도 계좌 돈을 센다(S3 7절, 원작대로).
                view.ShowShopItemState(shop.StockAt(_selectedEntry), item.MaxStock, unlocked, waiting, price, economy.Money >= price, !shop.AccountAvailable);
                return;
            }
            if (_selectedItem != null)
            {
                // 판매 목록에 남은 칸이라 Find는 실패하지 않고, 그래프의 아이템은 저장 읽기 검사로 늘 카탈로그 정의가 있다.
                ItemInstanceState item = items.Find(_selectedItem)!;
                OriginalItemDefinition definition = catalog.DefinitionOf(item.OriginalItemId);
                _details.Show(view.Details, panel, items, item, definition, 0);
                view.ShowSellItemState(shop.SellPrice(catalog, items, _selectedItem), StockShopRules.CanBeSold(definition), !shop.AccountAvailable);
                return;
            }
            _details.Hide(view.Details, false);
        }
    }
}
