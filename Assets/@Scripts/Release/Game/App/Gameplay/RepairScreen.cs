#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 ItemRepairView의 열기, 고르기, 수리, 닫기와 ItemRepair_RepairAllPanel의 목록과 모두 수리를 맡는 화면 컨트롤러다.
    // MasterKeyRegisterScreen과 같은 수명 규칙이다. 원작처럼 캐릭터 장비 칸과 가방만 보이고, 창고와 펫 가방은 보이지 않는다.
    // 판정과 가격은 명령과 같은 RepairRules로 계산하고, 수리와 모두 수리는 GameplayCommands.TryRepair, TryRepairAll로 보낸다.
    // 원작 OnRepaireOptionDone과 OnPlayerItemOperation 대신 세션 변경과 계좌 변경 알림으로 다시 그린다.
    internal sealed class RepairScreen : GameplayScreen
    {
        // 원작 willLoseDurabilityText 앞부분("UI_MaxDurability".ToPlainText())의 번역 키다. 제작 도구가 원본 CSV 값을 번역 테이블에 둔다.
        const string _maxDurabilityKey = "UI_MaxDurability";

        readonly GameplayWorldContext _world;
        readonly InventoryItemDetails _details;
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 모두 수리 목록을 다시 그릴 때마다 목록을 만들지 않도록 재사용한다.
        readonly List<ItemInstanceState> _listed = new List<ItemInstanceState>();
        // 상세와 수리 대상을 보이는 선택의 인스턴스 ID다. 원작 ItemUIUtilities.SelectedItem 자리이고 화면 상태라 저장하지 않는다.
        string? _selected;

        internal RepairScreen(GameplayWorldContext world)
        {
            _world = world;
            _details = new InventoryItemDetails(world);
        }

        // 원작 ItemRepairView.Show와 OnOpen이다. 원작처럼 선택을 비우고 연다. 공용 HUD가 이 월드에 붙어 있지 않거나
        // 입력 차단을 못 받으면(정리 중) 열지 않는다. 호출자: GameplayScreens.ShowRepair(기지 작업대 수리 항목의 ShowRepairView 동작).
        internal void Show()
        {
            // HUD 연결과 세션 확인, 열린 창 닫기, 입력 차단은 다른 화면과 같은 순서의 공통 열기 앞부분이 한다.
            if (!TryBeginShow(_world, out GameSessionManager? session)) return;
            _selected = null;
            OriginalItemRepairView view = _world.Hud.Repair;
            OriginalRepairAllPanelView repairAll = view.RepairAll;
            Action<string> select = Select;
            Action repair = Repair, repairEverything = RepairAll, close = Close, redraw = Redraw;
            view.CellSelected += select;
            _subscriptions.Add(() => view.CellSelected -= select);
            view.RepairRequested += repair;
            _subscriptions.Add(() => view.RepairRequested -= repair);
            repairAll.RepairAllRequested += repairEverything;
            _subscriptions.Add(() => repairAll.RepairAllRequested -= repairEverything);
            view.CloseRequested += close;
            _subscriptions.Add(() => view.CloseRequested -= close);
            // 수리 단추와 모두 수리 단추의 지불 가능 여부가 계좌에 달려 있어 계좌 변경도 듣는다.
            EconomyManager.Changed += redraw;
            _subscriptions.Add(() => EconomyManager.Changed -= redraw);
            // 장비, 가방, 현금 변경은 세션 변경 알림으로 받는다. 칸 수가 적어 알림마다 다시 그리고, 구독은 공용 도우미가 붙이고 뗀다.
            _subscriptions.BindSessionChanged(session, redraw);
            _subscriptions.BindLocaleChanged(redraw);
            view.Show();
            Redraw();
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 OnClose다. 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다.
        internal override void Close()
        {
            if (!IsOpen) return;
            _subscriptions.Clear();
            _selected = null;
            ReleaseInput();
            if (_world.PresentationAttached)
            {
                OriginalItemRepairView view = _world.Hud.Repair;
                _details.Hide(view.Details, true);
                view.Hide();
            }
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 ItemUIUtilities.Select다. 같은 칸이면 선택을 푼다(MasterKeyRegisterScreen과 같은 규칙).
        void Select(string itemId)
        {
            if (!IsOpen) return;
            _selected = _selected == itemId ? null : itemId;
            Redraw();
        }

        // 원작 OnRepairButtonClicked다. 명령이 판정, 지불, 내구도 변경을 한 번에 확정하고, 그 변경 알림이 다시 그린다.
        // 거절되면 화면은 그대로다.
        void Repair()
        {
            if (!IsOpen || _selected == null || !_world.TrySession(out GameSessionManager? session)) return;
            GameplayCommands.TryRepair(session, GameManager.Economy, _world.Catalog, _selected, AccountAvailable(session));
        }

        // 원작 ItemRepair_RepairAllPanel.OnButtonClicked다. 거절되면 화면은 그대로다.
        void RepairAll()
        {
            if (!IsOpen || !_world.TrySession(out GameSessionManager? session)) return;
            GameplayCommands.TryRepairAll(session, GameManager.Economy, _world.Catalog, AccountAvailable(session));
        }

        // 원작 LevelConfig.SaveCharacter다. 계좌는 기지에서만 쓴다.
        static bool AccountAvailable(GameSessionManager session) => session.Snapshot().SceneId == GameScene.BaseCamp;

        // 원작 new Cost(price)처럼 돈만 있는 비용이다. 단추 상태를 명령과 같은 CanPay로 보려고 만든다.
        static Cost Price(long money) => new Cost(money, Array.Empty<Cost.ItemEntry>());

        // 원작 OnOpen의 slotDisplay.Setup, inventoryDisplay.Setup, RefreshSelectedItemInfo와 RepairAllPanel.Refresh다.
        // 고른 아이템이 다른 명령으로 두 목록에서 사라졌으면 선택을 푼다. 씬 정리 중이라 세션이나 소유 그래프가 없으면 기록 없이 그리지 않는다.
        void Redraw()
        {
            if (!IsOpen || !_world.TryItems(out GameSessionManager? session, out ItemOwnershipState? items)) return;
            OriginalItemRepairView view = _world.Hud.Repair;
            OriginalGameplayCatalog catalog = _world.Catalog;
            // 검증된 소유 그래프라 플레이어 루트와 그 소지 컨테이너가 늘 있다(InventoryScreen과 같은 공용 질의).
            OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? character);
            if (_selected != null && !RepairRules.TryShown(items, _selected, out _)) _selected = null;

            IReadOnlyList<string> slots = OriginalCharacterSlots.All;
            for (int index = 0; index < slots.Count; index++)
            {
                string label = GameLocalization.Text(OriginalCharacterSlots.LabelKey(slots[index]));
                OriginalInventoryQueries.TryEquippedIn(character, slots[index], out string? id);
                // 칸 값은 다른 아이템 화면과 같은 공용 칸 값 도우미가 읽는다. 빈 칸이면 아이콘 없음, 빈 이름, 수량 0이다.
                ItemCellValues.Describe(items, catalog, id, out Sprite? icon, out string name, out int count);
                view.ShowSlot(index, label, id, icon, name, count);
            }

            // 원작 InventoryDisplay처럼 0번 칸부터 용량까지 빈 칸도 모두 그린다. 칸 수 규칙은 소유 그래프 질의가 정한다.
            ItemContainerState bag = character!.Inventory!;
            int capacity = OriginalInventoryQueries.CellCount(bag);
            view.BeginBag(bag.Entries.Count, capacity);
            for (int position = 0; position < capacity; position++)
            {
                string? id = items.EntryAt(character.InstanceId, position);
                // 빈 칸은 공용 칸 값 도우미가 비운 값으로 그리고, 선택이 없을 때 빈 칸이 고른 칸으로 보이지 않게 ID를 먼저 본다.
                ItemCellValues.Describe(items, catalog, id, out Sprite? icon, out string name, out int count);
                view.AddCell(id, icon, name, count, id != null && id == _selected);
            }
            view.EndBag();

            EconomyManager economy = GameManager.Economy;
            // 지불 출처는 수리 명령과 같은 세션 기본 출처 규칙으로 정한다.
            ItemSources sources = ItemSourceRules.Owned(session);
            bool account = AccountAvailable(session);
            DrawSelected(view, session, economy, items, catalog, sources, account);

            // 원작 RepairAllPanel.Refresh다. 지불 가능 여부는 명령과 같은 CanPay(원작 EconomyManager.IsEnough)다.
            // 목록의 아이템은 검증된 그래프의 노드라 카탈로그 정의가 늘 있다.
            _listed.Clear();
            long price = RepairRules.RepairAll(items, catalog, _listed, null);
            OriginalRepairAllPanelView repairAll = view.RepairAll;
            repairAll.Begin();
            foreach (ItemInstanceState listed in _listed)
            {
                OriginalItemDefinition listedDefinition = catalog.DefinitionOf(listed.OriginalItemId);
                repairAll.AddEntry(listedDefinition.Icon, GameLocalization.Text(listedDefinition.NameKey), listed.Quantity,
                    RepairRules.Current(listedDefinition, listed), listedDefinition.Durability, RepairRules.Loss(listed));
            }
            repairAll.End(price, _listed.Count > 0 && GameplayCommands.CanPay(session, economy, Price(price), sources, account));
        }

        // 원작 RefreshSelectedItemInfo다. 세 표시 조건은 원작 식 그대로다: 수리 필요 없음 = 파손 아님 && 가득 참 && 수리 가능 아이템,
        // 수리 불가 = 내구도 사용 && 수리 가능 아이템 아님 && 파손 아님, 파손 = 손실 반영 최대 < 1. 수리 단추는 지불할 수 있을 때만 켠다.
        void DrawSelected(OriginalItemRepairView view, GameSessionManager session, EconomyManager economy, ItemOwnershipState items,
            OriginalGameplayCatalog catalog, ItemSources sources, bool account)
        {
            if (_selected == null)
            {
                _details.Hide(view.Details, false);
                view.ShowNothingSelected();
                return;
            }
            // 바로 앞에서 TryShown이 확인한 ID라 Find는 실패하지 않고, 정의도 늘 있다.
            ItemInstanceState item = items.Find(_selected)!;
            OriginalItemDefinition definition = catalog.DefinitionOf(item.OriginalItemId);
            // 상세 화면은 인벤토리 화면과 같은 OriginalItemDetailsView 규칙(InventoryItemDetails)을 쓰므로 칸 셀도 공용 HUD 인벤토리 패널에 묶는다.
            // 상세 화면이 퀵슬롯을 읽지 않게 되어 퀵슬롯 인수를 넘기지 않는다.
            _details.Show(view.Details, _world.Hud.Inventory, items, item, definition, 0);
            float maximum = definition.Durability, current = RepairRules.Current(definition, item), loss = RepairRules.Loss(item);
            bool usesDurability = maximum > 0f;
            bool repairable = RepairRules.IsRepairable(definition);
            bool broken = RepairRules.IsBroken(maximum, loss);
            bool canRepair = RepairRules.CanRepair(maximum, current, loss, repairable);
            // 위 주석의 두 표시 조건을 이름 붙인 값으로 둔다. 판정 순서와 단락 평가는 원작 식 그대로다.
            bool noNeedToRepair = !broken && RepairRules.NoNeedToRepair(maximum, current, loss) && repairable;
            bool unrepairable = usesDurability && !repairable && !broken;
            view.ShowSelected(definition.Icon, GameLocalization.Text(definition.NameKey), canRepair, noNeedToRepair, unrepairable, broken);
            if (canRepair)
            {
                RepairQuote quote = RepairRules.Quote(maximum, current, loss, RepairRules.LossRatio(definition), definition.Value);
                view.ShowRepair(quote.Price, GameLocalization.Text(_maxDurabilityKey), quote.Lost,
                    GameplayCommands.CanPay(session, economy, Price(quote.Price), sources, account));
            }
            if (usesDurability) view.ShowDurability(current, maximum, loss);
            else view.HideDurability();
        }
    }
}
