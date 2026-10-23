#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 ItemCustomizeSelectionView와 ItemCustomizeView 두 창을 함께 맡는 화면 컨트롤러다. MasterKeyRegisterScreen과 같은 수명 규칙이고,
    // 입력 차단 핸들은 화면 공통 바탕(GameplayScreen)이 쥔다. 선택 창은 원작처럼 캐릭터 장비 칸과 가방만 보이고, 아이템 칸이 1개 이상인
    // 아이템을 고르면 "개조 시작"이 켜진다. 누르면 원작 View.Open처럼 선택 창을 닫고 개조 창을 연다. 개조 창은 대상의 칸을 고르면 가방과
    // 창고(기지)의 맨 위 칸에서 그 칸이 받는 부품을 후보로 보이고, 장착과 해제는 SessionInventoryCommands의 개조 명령이 한 번에 확정한다.
    // 원작은 이 창에 갈 수 없다(원작 차이).
    internal sealed class CustomizeScreen : GameplayScreen
    {
        readonly GameplayWorldContext _world;
        // 원작 세 ItemDetailsDisplay(선택 창 상세, 개조 대상, 고른 부품)마다 상세 규칙 하나다. 상세 규칙은 보이는 아이템을 기억한다.
        readonly InventoryItemDetails _selectionDetails, _targetDetails, _partDetails;
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 선택 창에서 고른 아이템, 개조 대상, 고른 칸 키, 고른 부품의 인스턴스 ID다. 화면 상태라 저장하지 않는다. _target이 있으면 개조 창이다.
        string? _selected, _target, _slot, _part;

        internal CustomizeScreen(GameplayWorldContext world)
        {
            _world = world;
            _selectionDetails = new InventoryItemDetails(world);
            _targetDetails = new InventoryItemDetails(world);
            _partDetails = new InventoryItemDetails(world);
        }

        // 원작 ItemCustomizeSelectionView.Show와 OnOpen이다. 공용 HUD가 이 월드에 붙어 있지 않거나 입력 차단을 못 받으면(정리 중) 열지 않는다.
        // 호출자: GameplayScreens.ShowCustomize(Workbench).
        internal void Show()
        {
            // 연결과 세션 확인, 다시 열기 전 닫기, 입력 차단은 다른 화면과 같은 순서라 공통 바탕이 맡는다.
            if (!TryBeginShow(_world, out GameSessionManager? session)) return;
            _selected = _target = _slot = _part = null;
            OriginalItemCustomizeSelectionView selection = _world.Hud.CustomizeSelection;
            OriginalItemCustomizeView customize = _world.Hud.Customize;
            Action<string> select = Select, selectPart = SelectPart;
            Action<int> selectSlot = SelectSlot;
            Action begin = BeginCustomize, equip = Equip, unequip = Unequip, redraw = Redraw;
            selection.ItemSelected += select;
            _subscriptions.Add(() => selection.ItemSelected -= select);
            selection.BeginRequested += begin;
            _subscriptions.Add(() => selection.BeginRequested -= begin);
            customize.SlotSelected += selectSlot;
            _subscriptions.Add(() => customize.SlotSelected -= selectSlot);
            customize.PartSelected += selectPart;
            _subscriptions.Add(() => customize.PartSelected -= selectPart);
            customize.EquipRequested += equip;
            _subscriptions.Add(() => customize.EquipRequested -= equip);
            customize.UnequipRequested += unequip;
            _subscriptions.Add(() => customize.UnequipRequested -= unequip);
            // 세션과 로캘 다시 그리기는 공용 구독 도우미가 붙이고 뗀다.
            _subscriptions.BindSessionChanged(session, redraw);
            _subscriptions.BindLocaleChanged(redraw);
            selection.Show();
            Redraw();
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 두 창의 OnClose다. 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다. 부품은 명령이 이미 확정해 돌려보낼 것이 없다.
        // ESC(공통 바탕의 TryClose)도 원작 View처럼 개조 창에서 선택 창으로 돌아가지 않고 모두 닫는다.
        internal override void Close()
        {
            if (!IsOpen) return;
            _subscriptions.Clear();
            _selected = _target = _slot = _part = null;
            ReleaseInput();
            if (_world.PresentationAttached) HideViews();
            BaseAutoSaver.NotifyUiActivity();
        }

        void HideViews()
        {
            OriginalItemCustomizeSelectionView selection = _world.Hud.CustomizeSelection;
            OriginalItemCustomizeView customize = _world.Hud.Customize;
            _selectionDetails.Hide(selection.Details, true);
            _targetDetails.Hide(customize.TargetDetails, true);
            _partDetails.Hide(customize.SelectedDetails, true);
            selection.Hide();
            customize.Hide();
        }

        // 원작 ItemUIUtilities.Select다. 같은 아이템이면 선택을 푼다.
        void Select(string itemId)
        {
            if (!IsOpen || _target != null) return;
            _selected = _selected == itemId ? null : itemId;
            Redraw();
        }

        // 원작 OnBeginCustomizeButtonClicked다. 선택 창을 닫고 개조 창을 연다. 시작 단추는 Redraw가 개조할 수 있는 아이템
        // (아이템 칸 1개 이상)을 고른 동안만 보이므로 여기서 다시 보지 않는다. 닫힌 뒤 늦게 온 클릭만 무시한다.
        void BeginCustomize()
        {
            if (!IsOpen) return;
            _target = _selected;
            _slot = _part = null;
            OriginalItemCustomizeSelectionView selection = _world.Hud.CustomizeSelection;
            _selectionDetails.Hide(selection.Details, true);
            selection.Hide();
            _world.Hud.Customize.Show();
            Redraw();
        }

        // 원작 OnSlotElementClicked다. 원작 SetSelection은 부품 선택을 풀지만, 꽂힌 부품이 있으면 해제 버튼이 보이도록 그 부품을 고른다
        // (원작에서 칸 클릭이 내용물을 고르는지는 확인하지 못했다).
        void SelectSlot(int index)
        {
            if (!IsOpen || _target == null || !TryDefinition(_target, out ItemInstanceState? target, out OriginalItemDefinition? definition)
                || index < 0 || index >= definition.ItemSlots.Count) return;
            _slot = definition.ItemSlots[index].Key;
            // 칸 키로 꽂힌 아이템을 찾는 규칙은 상세 화면과 같은 소유 그래프 질의가 맡는다.
            _part = OriginalInventoryQueries.SlotHolder(target.Slots, _slot);
            Redraw();
        }

        // 같은 부품이면 선택을 푼다. 닫힌 뒤나 선택 창에서 늦게 온 클릭은 무시한다.
        void SelectPart(string itemId)
        {
            if (!IsOpen || _target == null) return;
            _part = _part == itemId ? null : itemId;
            Redraw();
        }

        // 원작 OnEquipButtonClicked다. 거절되면 화면은 그대로다. 성공하면 고른 부품이 그 칸에 꽂혀 해제 버튼이 보인다.
        void Equip()
        {
            if (!IsOpen || _target == null || _slot == null || _part == null || !_world.TrySession(out GameSessionManager? session)) return;
            session.Inventory.TryCustomizePlug(_world.Catalog, _target, _slot, _part);
            Redraw();
        }

        // 원작 OnUnequipButtonClicked다. 뺀 부품은 기지면 창고, 아니면 가방으로 가므로 선택을 푼다.
        void Unequip()
        {
            if (!IsOpen || _target == null || _slot == null || !_world.TrySession(out GameSessionManager? session)) return;
            if (session.Inventory.TryCustomizeUnplug(_world.Catalog, _target, _slot)) _part = null;
            Redraw();
        }

        // 씬 정리 중이라 세션이나 소유 그래프가 없으면 기록 없이 그리지 않는다.
        void Redraw()
        {
            if (!IsOpen || !_world.TryItems(out GameSessionManager? session, out ItemOwnershipState? items)) return;
            // 검증된 소유 그래프라 플레이어 루트와 그 소지 컨테이너가 늘 있다(InventoryScreen과 같은 공용 질의).
            OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? character);
            // 상세 화면이 퀵슬롯을 읽지 않게 되어 퀵슬롯을 읽어 넘기지 않는다.
            if (_target != null)
            {
                // 다른 명령으로 대상이 장비 칸과 가방을 떠났으면 원작 창이 닫힐 때처럼 모두 닫는다.
                if (!IsTarget(items, character!, _target))
                {
                    Close();
                    return;
                }
                DrawCustomize(session, items, character!);
            }
            else DrawSelection(items, character!);
        }

        // 원작 ItemCustomizeSelectionView의 slotDisplay.Setup, inventoryDisplay.Setup, OnItemSelectionChanged, RefreshSelectedItemInfo다.
        void DrawSelection(ItemOwnershipState items, ItemInstanceState character)
        {
            OriginalItemCustomizeSelectionView view = _world.Hud.CustomizeSelection;
            OriginalGameplayCatalog catalog = _world.Catalog;
            if (_selected != null && !IsTarget(items, character, _selected)) _selected = null;
            IReadOnlyList<string> slots = OriginalCharacterSlots.All;
            view.BeginSlots();
            for (int index = 0; index < slots.Count; index++)
            {
                // 칸에 꽂힌 아이템 찾기와 칸에 보일 값은 다른 아이템 화면과 같은 공용 규칙이 정한다.
                string? id = OriginalInventoryQueries.SlotHolder(character.Slots, slots[index]);
                ItemCellValues.Describe(items, catalog, id, out Sprite? icon, out string name, out int count);
                view.AddSlot(index, GameLocalization.Text(OriginalCharacterSlots.LabelKey(slots[index])), id, icon, name, count, id != null && id == _selected);
            }
            view.EndSlots();
            ItemContainerState bag = character.Inventory!;
            // 칸 수 규칙은 다른 아이템 화면과 같은 소유 그래프 질의가 정한다.
            int capacity = OriginalInventoryQueries.CellCount(bag);
            view.BeginBag(bag.Entries.Count, capacity);
            for (int position = 0; position < capacity; position++)
            {
                string? id = items.EntryAt(character.InstanceId, position);
                // 칸에 보일 값은 다른 아이템 화면과 같은 공용 규칙이 정한다.
                ItemCellValues.Describe(items, catalog, id, out Sprite? icon, out string name, out int count);
                view.AddBagCell(position, id, icon, name, count, id != null && id == _selected);
            }
            view.EndBag();
            if (_selected != null && TryDefinition(_selected, out ItemInstanceState? item, out OriginalItemDefinition? definition))
            {
                _selectionDetails.Show(view.Details, _world.Hud.Inventory, items, item, definition, 0);
                view.ShowSelected(true, GameLocalization.Text(definition.NameKey), definition.Icon, definition.ItemSlots.Count > 0);
            }
            else
            {
                _selectionDetails.Hide(view.Details, false);
                view.ShowSelected(false, "", null, false);
            }
        }

        // 원작 ItemCustomizeView의 대상 Setup, RefreshSelectionIndicator, RefreshAvaliableItems, RefreshDetails다.
        // 후보는 원작 avaliableInventories(가방, 기지면 창고)의 맨 위 아이템 중 Slot.CanPlug를 통과하는 것을 칸 번호 순서로 든다.
        // 상세 화면이 퀵슬롯을 읽지 않게 되어 퀵슬롯 인수를 받지 않는다.
        void DrawCustomize(GameSessionManager session, ItemOwnershipState items, ItemInstanceState character)
        {
            OriginalItemCustomizeView view = _world.Hud.Customize;
            OriginalGameplayCatalog catalog = _world.Catalog;
            // IsTarget이 소유 그래프에서 확인한 대상이라 정의가 늘 있다.
            TryDefinition(_target!, out ItemInstanceState? target, out OriginalItemDefinition? definition);
            _targetDetails.Show(view.TargetDetails, _world.Hud.Inventory, items, target!, definition!, 0);
            IReadOnlyList<OriginalItemSlot> slots = definition!.ItemSlots;
            OriginalItemSlot? chosen = null;
            view.BeginSlots();
            for (int index = 0; index < slots.Count; index++)
            {
                OriginalItemSlot slot = slots[index];
                // 칸에 꽂힌 아이템 찾기와 칸에 보일 값은 다른 아이템 화면과 같은 공용 규칙이 정한다.
                string? id = OriginalInventoryQueries.SlotHolder(target!.Slots, slot.Key);
                ItemCellValues.Describe(items, catalog, id, out Sprite? icon, out string name, out int count);
                bool selected = slot.Key == _slot;
                if (selected) chosen = slot;
                view.AddSlot(index, slot.LabelKey.Length > 0 ? GameLocalization.Text(slot.LabelKey) : "?", id, icon, name, count, selected);
            }
            view.EndSlots();
            if (chosen == null) _slot = null;
            // 칸에 꽂힌 아이템 찾기는 상세 화면과 같은 소유 그래프 질의가 맡는다.
            string? content = _slot != null ? OriginalInventoryQueries.SlotHolder(target!.Slots, _slot) : null;

            bool partShown = _part != null && _part == content;
            int candidates = 0;
            view.BeginItems();
            if (chosen != null)
            {
                AddCandidates(view, items, catalog, chosen, character.InstanceId, character.Inventory, ref candidates, ref partShown);
                // 창고 후보는 기지에서만 보인다. 그 씬 차이와 창고 컨테이너 찾기는 등록 창들과 같은 공용 질의가 정한다.
                if (OriginalInventoryQueries.TryShownStorage(items, ItemSourceRules.Owned(session), out ItemContainerState? stored))
                    AddCandidates(view, items, catalog, chosen, GameplayInventory._storage, stored, ref candidates, ref partShown);
            }
            view.EndItems(chosen != null, candidates);
            if (!partShown) _part = null;

            if (_part != null && TryDefinition(_part, out ItemInstanceState? part, out OriginalItemDefinition? partDefinition))
                _partDetails.Show(view.SelectedDetails, _world.Hud.Inventory, items, part, partDefinition, 0);
            else _partDetails.Hide(view.SelectedDetails, false);
            view.ShowButtons(_part != null && _part != content, _part != null && _part == content);
        }

        // 한 가방의 맨 위 아이템 중 고른 칸이 받는 것을 칸 번호 순서로 후보 줄에 더한다. 대상 자신은 원작 CanPlug처럼 뺀다.
        // 대상은 장비 칸이나 가방 맨 위에 있으므로 맨 위 아이템이 대상을 품는 경우(원작 조상 금지)는 없다.
        void AddCandidates(OriginalItemCustomizeView view, ItemOwnershipState items, OriginalGameplayCatalog catalog, OriginalItemSlot slot,
            string owner, ItemContainerState? container, ref int candidates, ref bool partShown)
        {
            if (container == null) return;
            // 칸 수 규칙은 다른 아이템 화면과 같은 소유 그래프 질의가 정한다.
            int capacity = OriginalInventoryQueries.CellCount(container);
            for (int position = 0; position < capacity; position++)
            {
                string? id = items.EntryAt(owner, position);
                // 빈 칸과 대상 자신은 건너뛴다. 칸의 아이템과 정의 찾기는 다른 아이템 화면과 같은 공용 규칙이 맡는다.
                if (id == _target || !ItemCellValues.TryDescribe(items, catalog, id, out ItemInstanceState item, out OriginalItemDefinition definition))
                    continue;
                if (!slot.Accepts(definition)) continue;
                bool selected = id == _part;
                if (selected) partShown = true;
                view.AddItem(id, definition.Icon, GameLocalization.Text(definition.NameKey), item.Quantity, selected);
                candidates++;
            }
        }

        // 원작 선택 창이 보이는 자리(캐릭터 장비 칸, 가방 맨 위 칸)에 있는지 본다. 개조 대상도 같은 자리여야 한다(GameplayInventory.CustomizePlug).
        static bool IsTarget(ItemOwnershipState items, ItemInstanceState character, string id) =>
            items.PositionIn(character.InstanceId, id) != null || OriginalInventoryQueries.EquippedSlot(character, id) != null;

        bool TryDefinition(string id, out ItemInstanceState? item, out OriginalItemDefinition? definition)
        {
            item = null;
            definition = null;
            if (!_world.TryItems(out _, out ItemOwnershipState? items)) return false;
            item = items.Find(id);
            return item != null && _world.Catalog.TryGet(item.OriginalItemId, out definition);
        }
    }
}
