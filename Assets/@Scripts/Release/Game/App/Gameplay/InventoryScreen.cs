#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.App
{
    // 전투 월드와 프롤로그의 인벤토리, 전리품, 창고 화면을 맡는다. 화면 자체는 공용 HUD의 원본 LootView다.
    // 씬 참조는 GameplayWorldContext로 받고, 행동 상태와 아이템 사용은 IInventoryActions에 맡기며, 컨트롤러를 역참조하지 않는다.
    internal sealed class InventoryScreen
    {
        readonly GameplayWorldContext _world;
        readonly IInventoryActions _combat;
        IDisposable? _inventoryBlock;
        InteractableLootbox? _openedLoot;
        bool _storage;
        // 마지막으로 고른 셀이다. 사용 버튼이 이 셀을 대상으로 하고, 다시 그릴 때 아이템이 옮겨 간 셀로 따라간다.
        // 기본값은 빈 셀이라 선택 없음과 같다. 테스트가 리플렉션으로 이 이름을 읽는다.
        InventoryCellRef _selection;
        // 마지막으로 소지 무게를 계산한 소유 그래프와 그 무게다. 그래프는 불변이라 같은 참조면 무게도 같으므로,
        // 원본 Inventory.CachedWeight처럼 그래프가 바뀐 갱신에서만 다시 더한다.
        ItemOwnershipState? _weightGraph;
        float _weight;
        // 원본 LootView.RefreshDetails처럼 고른 셀의 아이템을 상세 화면(ItemDetails)에 보인다. 상세 화면 칸 번호를 칸 키로 바꾸는 것도 맡는다.
        readonly InventoryItemDetails _details;
        internal InventoryScreen(GameplayWorldContext world, IInventoryActions combat)
        {
            _world = world;
            _combat = combat;
            _details = new InventoryItemDetails(world);
        }
        // 원본 LootView의 버튼, 셀 클릭, 끌어 놓기 명령은 공용 HUD의 인벤토리 패널이 내고 이 화면이 받는다. 전투 월드와 프롤로그가
        // 표시를 연결할 때 자기 구독 묶음을 넘기고, 해제는 그 묶음이 표시를 뗄 때 한다. 처리기 목록은 이 화면만 안다.
        internal void BindPanel(UiEventSubscriptions subscriptions)
        {
            HudInventoryPanel panel = _world.Hud.Inventory;
            subscriptions.Bind(() => panel.CloseRequested += Close, () => panel.CloseRequested -= Close);
            subscriptions.Bind(() => panel.UseRequested += Use, () => panel.UseRequested -= Use);
            subscriptions.Bind(() => panel.StoreAllRequested += StoreAll, () => panel.StoreAllRequested -= StoreAll);
            // 원본 InventoryDisplay 정리 버튼도 같은 패널이 낸다.
            subscriptions.Bind(() => panel.SortRequested += Sort, () => panel.SortRequested -= Sort);
            subscriptions.Bind(() => panel.ItemSelected += Select, () => panel.ItemSelected -= Select);
            subscriptions.Bind(() => panel.ItemActivated += Activate, () => panel.ItemActivated -= Activate);
            subscriptions.Bind(() => panel.ShortcutRequested += BindShortcut, () => panel.ShortcutRequested -= BindShortcut);
            subscriptions.Bind(() => panel.DragStarted += BeginDrag, () => panel.DragStarted -= BeginDrag);
            subscriptions.Bind(() => panel.DragEnded += EndDrag, () => panel.DragEnded -= EndDrag);
            subscriptions.Bind(() => panel.DropRequested += Drop, () => panel.DropRequested -= Drop);
        }
        // 열림 여부는 입력 차단 핸들을 쥐고 있는지로만 판단한다. HUD 표시 상태는 다시 읽지 않는다.
        internal bool IsOpen => _inventoryBlock != null;
        // 원본 InventoryEntry.OnShortcutInput처럼 인벤토리가 열린 동안 숫자키는 포인터가 올라간 셀을 그 칸에 등록한다.
        // 올라간 셀이 없으면 원본처럼 아무것도 하지 않는다.
        internal void BindShortcut(int index)
        {
            if (_world.Hud.Inventory.TryHovered(out InventoryCellRef cell)) Bind(cell, index);
        }
        // 공용 HUD에서 셀을 퀵슬롯에 끌어 놓을 때 온다. 셀을 먼저 선택해 이름을 보이고, 고른 셀 대신 놓은 셀을 등록한다.
        // 선택 표시가 실패해도 엉뚱한 아이템이 등록되지 않게 하려는 것이다.
        // 원작 ItemShortcutEditorEntry.OnDrop처럼 상자, 시체, 창고, 펫 가방의 아이템은 먼저 소지품으로 보낸 뒤 등록한다.
        // 숫자키 등록(원작 InventoryEntry.OnShortcutInput)은 보내지 않고 소지품 칸만 받는다.
        internal void BindShortcut(InventoryCellRef cell, int index)
        {
            Select(cell);
            if (cell.Panel != InventoryPanel.Carried && !TrySendToCarried(cell, out cell)) return;
            Bind(cell, index);
        }

        // 원작 ItemUtilities.SendToPlayer(dontMerge: false)처럼 상자 칸은 줍고, 창고와 펫 가방 아이템은 소지품에 합쳐 넣는다.
        // 합쳐져 원래 ID가 사라지면 같은 종류의 소지품 묶음을 등록한다. 원작 퀵슬롯도 종류 ID로 같은 종류의 다른 묶음을 찾는다.
        // 장비 칸과 부착물 칸은 원작처럼 이미 플레이어에게 있어 보내지 않고, 소지품 칸이 아니므로 등록하지 않는다.
        bool TrySendToCarried(InventoryCellRef cell, out InventoryCellRef carried)
        {
            carried = default;
            if (cell.IsEmpty || !CanEditInventory) return false;
            int type;
            string? id = null;
            if (IsLootCell(cell))
            {
                if (!TryLootKey(cell.Position, out string key)) return false;
                type = _openedLoot!.ItemTypes[cell.Position];
                Collect(type, _openedLoot.Counts[cell.Position], key);
            }
            else if (cell.Panel == InventoryPanel.Pet || cell.Panel == InventoryPanel.Other && StorageShown)
            {
                if (!_world.TryItems(out ItemOwnershipState? before)) return false;
                id = cell.Id!;
                type = before!.Find(id)!.OriginalItemId;
                MoveOwned(id, InventoryLocation.Carried(InventoryLocation._anyPosition));
            }
            else return false;
            if (!_world.TryItems(out ItemOwnershipState? items)) return false;
            // 검증된 소유 그래프라 플레이어 루트와 소지 컨테이너가 늘 있다.
            OriginalInventoryQueries.TryPlayerItem(items!, out ItemInstanceState? player);
            string owner = player!.InstanceId;
            if (id != null && items!.PositionIn(owner, id) is int moved)
            {
                carried = new InventoryCellRef(InventoryPanel.Carried, moved, id);
                return true;
            }
            // IReadOnlyList 열거자의 박싱을 피하려고 색인으로 돈다.
            IReadOnlyList<ItemPositionState> entries = player.Inventory!.Entries;
            for (int index = 0; index < entries.Count; index++)
                if (items!.Find(entries[index].InstanceId)!.OriginalItemId == type)
                {
                    carried = new InventoryCellRef(InventoryPanel.Carried, entries[index].Position, entries[index].InstanceId);
                    return true;
                }
            return false;
        }
        // 원본 ItemShortcut.Set처럼 퀵슬롯은 소지 인벤토리 칸만 가리키므로 차 있는 소지품 셀만 등록하고 펫 가방 셀은 무시한다.
        // 소비품이 아니어서 거절되면 원본처럼 표시 없이 무시하고, 씬 정리 중이라 세션이 없으면 기록 없이 무시한다.
        void Bind(InventoryCellRef cell, int index)
        {
            if (cell.Panel != InventoryPanel.Carried || cell.IsEmpty) return;
            if (_world.TrySession(out GameSessionManager? session))
                session!.Inventory.TryBindShortcut(_world.Catalog, index, cell.Id!);
        }
        // 공용 HUD 셀 더블클릭이나 우클릭이다. 원본 LootView처럼 창고가 열려 있으면 소지품, 펫 가방, 장비는 창고로, 창고 아이템은
        // 펫 가방이 아니라 소지품으로 합쳐 넣고, 상자 칸은 줍는다. 창고가 없으면 사용 시간이 있는 소지품과 펫 가방 아이템만 쓴다.
        // 원본과 달리 장착은 하지 않고 끌어 놓기로만 한다.
        internal void Activate(InventoryCellRef cell)
        {
            // 원본 ItemDetailsDisplay.OnElementDoubleClicked처럼 상세 화면 칸의 부착물은 빼서 플레이어에게 보낸다.
            // 원본 상세 칸은 contentSelectable이 꺼져 있어 선택을 바꾸지 않는다.
            if (cell.Panel == InventoryPanel.Attachment)
            {
                if (CanEditInventory && !cell.IsEmpty) Unplug(cell.Id!);
                return;
            }
            // 인벤토리를 바꿀 수 없으면 선택도 바꾸지 않는다. 선택 표시가 찾은 정의를 그대로 쓰고,
            // 표시에 실패하면 엉뚱한 아이템에 행동하지 않게 바로 끝낸다.
            if (!CanEditInventory || !TrySelect(cell, out OriginalItemDefinition? definition)) return;
            switch (cell.Panel)
            {
                // 원본은 펫 가방도 소지품과 같은 더블클릭 처리기를 쓴다. 상자가 열려 있으면 원본은 둘 다 상자로 보내지만,
                // 이 게임은 상자에 넣지 않으므로 창고가 없을 때처럼 사용한다.
                case InventoryPanel.Carried:
                case InventoryPanel.Pet:
                    if (StorageShown) MoveOwned(cell.Id!, InventoryLocation.Storage(InventoryLocation._anyPosition));
                    else if (definition!.UseTime > 0) Use();
                    break;
                case InventoryPanel.Slot:
                    if (StorageShown) MoveOwned(cell.Id!, InventoryLocation.Storage(InventoryLocation._anyPosition));
                    break;
                default:
                    if (_openedLoot != null) Take(_openedLoot, cell.Position);
                    else if (StorageShown) MoveOwned(cell.Id!, InventoryLocation.Carried(InventoryLocation._anyPosition));
                    break;
            }
        }
        // 열린 상자가 플레이어에게서 이 거리를 벗어나면 화면을 닫는다. 원작 LootView는 거리로 닫지 않아 이 기준에 짝이 없다.
        const float _lootCloseDistance = 3f;
        // SqrDistance는 제곱 거리라 기준 거리도 제곱해 비교한다. 꺼진 영역은 무한대라 화면이 닫힌다.
        internal void CloseIfOutOfRange()
        {
            if (_openedLoot == null) return;
            // 기준점은 원작 F 탐색 구 중심과 같은 높이로 둔다.
            Vector3 probe = _world.PlayerPosition + Vector3.up * OriginalInteractionSearch._probeHeight;
            if (_openedLoot.SqrDistance(probe) > _lootCloseDistance * _lootCloseDistance) Close();
        }
        // 인벤토리 키 입력이다. 원본 OnUIInventoryInput(:411-431)처럼 기지 밖에서는 행동 중에도 소지품을 열고, 지연 상호작용은 계속된다.
        // 기지에서는 원본 창고 상자 상호작용(CharacterMainControl.Interact :2423-2440)이라 행동이 없을 때만 소지품과 창고(PlayerStorage)를 연다.
        internal void Toggle(InputAction.CallbackContext context)
        {
            if (IsOpen) { Close(); return; }
            // 탭 단추 줄의 가방 단추와 같은 열기 조건을 쓰도록 ShowCarried로 모았다.
            ShowCarried();
        }

        // 원작 ViewsProxy.ShowInventoryView다. 인벤토리 키(Toggle)와 탭 단추 줄의 BackpackButton이 함께 쓴다. 기지에서는 원작처럼 창고 상자
        // 상호작용이라 행동이 없을 때만 소지품과 창고를, 밖에서는 소지품만 연다. 이미 열려 있으면 Open이 상자 대상을 지우고 소지품 화면으로 바꾼다.
        // 다른 화면이 입력을 막고 있으면 겹쳐 열지 않으므로, 탭 단추를 받은 컨트롤러가 열린 탭 화면을 먼저 닫는다.
        internal void ShowCarried()
        {
            // 씬 정리 중이라 세션이 없으면 기록 없이 열지 않는다. 시뮬레이션 준비는 Open이 상태를 바꾸기 전에 본다.
            if ((!IsOpen && _world.Runtime.GameplayInput.IsBlocked) || !_world.TrySession(out GameSessionManager? session)) return;
            bool baseCamp = session!.Snapshot().SceneId == GameScene.BaseCamp;
            if (baseCamp && _combat.ActionInProgress) return;
            Open(null, baseCamp);
        }

        // 화면을 연다. loot가 있으면 그 상자를, storage면 창고를 함께 보인다. 이미 열려 있으면 대상과 선택만 바꾼다.
        internal void Open(InteractableLootbox? loot, bool storage)
        {
            // 공용 HUD가 이 월드에 연결되지 않았으면 다른 소유자의 HUD를 바꾸지 않도록 기록 없이 끝낸다.
            // 비활성화로 연결이 끊긴 뒤 늦게 들어온 토글 입력이 이 경우다.
            if (!_world.SimulationReady || !_world.PresentationAttached) return;
            // 입력 차단은 처음 열 때만 얻어 차단 수를 1로 유지한다. Runtime이 정리 중이라 거절하면 대상과 선택을 바꾸기 전에 끝낸다.
            if (_inventoryBlock == null)
            {
                if (!_world.Runtime.GameplayInput.TryAcquire(out IDisposable? block)) return;
                _inventoryBlock = block;
            }
            // 셀 자리는 열린 대상마다 다르므로 고른 셀을 비운다.
            _openedLoot = loot; _storage = storage; _selection = default;
            // SetInventory(true)는 상호작용 안내도 함께 내린다.
            _world.Hud.Inventory.SetOpen(true); Refresh();
            // 원본이 화면(ManagedUIElement)을 열 때처럼 기지 씬의 자동 저장을 요청한다. 기지 밖에는 자동 저장이 없어 무시된다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 입력 차단을 반납하고 화면을 닫는다. 실제로 열려 있었을 때만 기지 자동 저장을 요청한다.
        // 씬 정리가 무조건 부르는 닫기는 저장을 요청하지 않는다.
        internal void Close()
        {
            bool wasOpen = IsOpen;
            _inventoryBlock?.Dispose();
            _inventoryBlock = null;
            // 공용 HUD는 이 월드에 연결된 동안에만 닫는다. 다른 소유자의 HUD나 Play Mode 종료로 파괴된 HUD는 건드리지 않는다.
            // 입력 차단 반납과 선택 비우기는 연결 여부와 관계없이 한다.
            // 상세 화면도 함께 닫고, 칸 키를 바꿀 때 쓰는 보이던 아이템을 잊는다.
            if (_world.PresentationAttached)
            {
                _world.Hud.Inventory.SetOpen(false);
                _details.Hide(_world.Hud.Inventory.Details, true);
            }
            _openedLoot = null;
            _selection = default;
            // 실제로 열려 있던 화면을 닫을 때만 기지 씬의 자동 저장에 알린다.
            if (wasOpen) BaseAutoSaver.NotifyUiActivity();
        }

        // ESC 처리가 묻는다. 열려 있었으면 닫고 true를 돌려준다.
        internal bool TryClose()
        {
            bool open = IsOpen;
            if (open) Close();
            return open;
        }

        // 확정 세션 기준으로 장비 칸, 빈 칸을 포함한 소지품, 펫 가방, 창고 칸, 상자 칸, 용량과 중량을 다시 그리고 아직 남아 있는 선택을 복원한다.
        internal void Refresh()
        {
            // 씬 정리 중이라 세션이나 소유 그래프가 없으면 기록 없이 그리지 않는다.
            if (!IsOpen || !_world.TryItems(out ItemOwnershipState? items)) return;
            SharedHudView hud = _world.Hud;
            // 백그라운드 저장 때문에 다시 그려도 플레이어가 고른 선택이 풀리지 않도록 먼저 기억해 둔다.
            InventoryCellRef selected = _selection;
            // 읽기만 하므로 GameplayInventory 작업 사본 없이 불변 그래프를 직접 조회한다.
            ItemOwnershipState inventory = items!;
            bool storage = StorageShown;
            // 원본 LootView.OnOpen이 PetProxy.PetInventory가 있을 때만 펫 영역을 보이듯, 그래프에 펫 가방이 있을 때만 펫 영역과 칸을 그린다.
            // 펫 가방은 세션의 그래프 준비가 늘 더하므로, 없을 때는 기록 없이 숨기기만 한다.
            bool pet = OriginalInventoryQueries.TryContainer(inventory, GameplayInventory._pet, out ItemContainerState? petBag);
            // 원본 LootView처럼 LootTarget 영역은 상자나 창고가 열렸을 때만 보이고, 모두 넣기와 창고 정리 버튼은 창고가 열렸을 때만 보인다.
            hud.Inventory.Begin(GameLocalization.Text(_openedLoot != null ? _openedLoot.InteractNameKey : storage ? "gameplay.storage" : "gameplay.inventory"),
                _openedLoot != null || storage, storage, pet);
            // 검증된 소유 그래프라 플레이어 루트와 그 소지 컨테이너가 늘 있다. 저장 읽기 검사가 둘을 요구하고 명령은 지우지 않는다.
            // 루트 조회는 다른 화면 경로와 같은 공용 질의를 쓴다.
            OriginalInventoryQueries.TryPlayerItem(inventory, out ItemInstanceState? character);
            ItemContainerState carried = character!.Inventory!;
            // 원본 캐릭터 칸 10개를 원본 순서로 그린다. 칸 이름은 원본처럼 그 칸이 받는 태그의 표시 이름이다.
            IReadOnlyList<string> slots = OriginalCharacterSlots.All;
            for (int index = 0; index < slots.Count; index++)
            {
                OriginalInventoryQueries.TryEquippedIn(character, slots[index], out string? equipped);
                // 칸 값은 다른 아이템 화면과 같은 규칙으로 읽는다. 빈 칸이면 이름과 아이콘이 비어 있다.
                ItemCellValues.Describe(inventory, _world.Catalog, equipped, out Sprite? icon, out string name, out int count);
                hud.Inventory.ShowSlot(index, GameLocalization.Text(OriginalCharacterSlots.LabelKey(slots[index])), equipped, name, icon, count);
            }
            // 탄창과 장비, 부착물 속 아이템까지 재귀로 더하고 창고, 펫 가방, 시체의 무게는 넣지 않는다. 상세 화면 무게와 같은 공용 질의를 쓴다.
            if (!ReferenceEquals(_weightGraph, inventory))
            {
                _weight = OriginalInventoryQueries.TotalWeight(inventory, _world.Catalog, character.InstanceId);
                _weightGraph = inventory;
            }
            AddCells(inventory, InventoryPanel.Carried, character.InstanceId, carried);
            // 펫 칸은 소지품 칸 바로 뒤에 채운다. 열린 상자나 창고의 칸 수가 바뀌어도 앞쪽 셀이 다른 격자로 옮겨 가지 않게 하려는 것이다.
            if (pet) AddCells(inventory, InventoryPanel.Pet, GameplayInventory._pet, petBag!);
            if (_openedLoot != null)
            {
                for (int i = 0; i < _openedLoot.ItemTypes.Count; i++)
                {
                    // 회수한 칸은 루팅 키로 빈 칸이 된다. 정의가 없는 칸은 집을 수 없어 빈 칸으로 그린다.
                    // 칸 자리가 상자 칸 번호라서 내용 칸 수는 늘 상자 내용 수와 같다.
                    if (_world.LootTaken(LootKey(_openedLoot, i)) ||
                        !_world.Catalog.TryGet(_openedLoot.ItemTypes[i], out OriginalItemDefinition? definition))
                        hud.Inventory.AddCell(InventoryPanel.Other, i, null, "", null, 0);
                    else
                        hud.Inventory.AddCell(InventoryPanel.Other, i, i.ToString(), GameLocalization.Text(definition!.NameKey), definition.Icon,
                            _openedLoot.Counts[i]);
                }
                // 원작 LootView처럼 상자 용량(inventorySize)만큼 칸을 그린다. 내용 뒤 칸은 빈 칸이다.
                for (int i = _openedLoot.ItemTypes.Count; i < _openedLoot.Capacity; i++) hud.Inventory.AddCell(InventoryPanel.Other, i, null, "", null, 0);
            }
            else if (storage && TryInventoryContainer(inventory, GameplayInventory._storage, out ItemContainerState? stored))
                AddCells(inventory, InventoryPanel.Other, GameplayInventory._storage, stored!);
            // 이번에 채우지 않은 셀만 숨긴다. 계속 보이는 셀은 끄고 켜지 않아 셀마다 다시 그리지 않는다.
            hud.Inventory.End();
            // 최대 무게는 장비 효과까지 더한 플레이어 GAS MaxWeight 현재값이다. 원본도 캐릭터 스탯의 현재값을 쓴다.
            hud.Inventory.ShowStats(carried.Entries.Count, carried.Capacity, _weight,
                _world.Runtime.Vitals.Abilities.GetCurrent(CharacterAttributes.MaxWeight));
            _selection = default;
            // 이동, 소비, 회수된 아이템의 선택은 풀고, 아직 보이는 아이템은 지금 놓인 셀로 다시 선택한다.
            // 창고로 간 아이템에 사용 버튼이 남지 않게 하려는 것이다. 다시 고르지 못하면 원본 LootView.RefreshDetails처럼 상세 화면을 숨긴다.
            if (!TryLocate(inventory, selected, out InventoryCellRef current) || !TrySelect(current, out _))
                _details.Hide(hud.Inventory.Details, false);
        }

        // 다른 패널에는 상자와 창고 중 하나만 보인다. 호출자는 상자나 창고 하나만 넘겨 열므로 창고 여부가 곧 창고 표시 여부다.
        bool StorageShown => _storage;

        // 원본 CA_UseItem의 CanEditInventory false처럼 아이템 사용 중에는 인벤토리를 바꾸지 않는다. 시뮬레이션이 멈춘 동안에도 바꾸지 않는다.
        bool CanEditInventory => _world.SimulationReady && !_combat.UsingItem;

        // 상자가 열려 있으면 다른 패널 셀은 상자 칸이고, 셀 자리가 상자 칸 번호다.
        bool IsLootCell(InventoryCellRef cell) => cell.Panel == InventoryPanel.Other && _openedLoot != null;

        // 열린 상자의 칸을 아직 회수하지 않았으면 true와 루팅 키를 돌려준다. 호출자는 IsLootCell로 상자가 열린 것을 먼저 본다.
        // 칸 번호는 Refresh가 그 상자의 칸 수만큼 그린 셀 자리다. 상자는 화면이 닫혀 있을 때만 바뀌고, Open이 같은 호출에서 셀을 다시 그린다.
        bool TryLootKey(int index, out string key)
        {
            key = LootKey(_openedLoot!, index);
            // 내용 뒤의 빈 칸은 집을 것이 없다.
            return index < _openedLoot!.ItemTypes.Count && !_world.LootTaken(key);
        }

        // 고른 셀의 아이템이 지금 화면 어디에 보이는지 HUD 셀이 아니라 그래프의 실제 위치로 찾는다. 상자 칸은 회수 전까지만 남는다.
        // 보이는 창고 칸, 소지품 칸, 펫 가방 칸, 장비 칸 순으로 찾고, 화면에 없으면 false다. 펫 가방 칸은 가방이 있을 때만 찾히고 그때 늘 보인다.
        bool TryLocate(ItemOwnershipState items, InventoryCellRef cell, out InventoryCellRef current)
        {
            current = cell;
            if (cell.IsEmpty) return false;
            if (IsLootCell(cell)) return TryLootKey(cell.Position, out _);
            string id = cell.Id!;
            if (StorageShown && items.PositionIn(GameplayInventory._storage, id) is int stored)
            {
                current = new InventoryCellRef(InventoryPanel.Other, stored, id);
                return true;
            }
            // 검증된 소유 그래프라 플레이어 루트가 늘 있다.
            OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? player);
            if (items.PositionIn(player!.InstanceId, id) is int carried)
            {
                current = new InventoryCellRef(InventoryPanel.Carried, carried, id);
                return true;
            }
            // 펫 가방은 창고처럼 저장 인벤토리라 그 키가 칸 소유자다.
            if (items.PositionIn(GameplayInventory._pet, id) is int pet)
            {
                current = new InventoryCellRef(InventoryPanel.Pet, pet, id);
                return true;
            }
            // 장착 여부는 인스턴스에서 슬롯을 거꾸로 찾는 공용 질의로 판정해, 세션 명령과 같은 규칙을 쓴다.
            ItemSlotState? slot = OriginalInventoryQueries.EquippedSlot(player, id);
            int index = slot != null ? OriginalCharacterSlots.IndexOf(slot.Key) : -1;
            current = new InventoryCellRef(InventoryPanel.Slot, index, id);
            return index >= 0;
        }

        // 컨테이너 조회 순서는 세션 명령과 같은 공용 질의가 정한다. 화면은 찾지 못한 구조 결함을 ID마다 한 번 기록하는 일만 맡는다.
        bool TryInventoryContainer(ItemOwnershipState inventory, string id, out ItemContainerState? container)
        {
            if (OriginalInventoryQueries.TryContainer(inventory, id, out container)) return true;
            _world.ReportDefectOnce("container:" + id, "인벤토리 컨테이너가 없습니다: " + id);
            return false;
        }

        // 원본 InventoryDisplay처럼 컨테이너의 0번 칸부터 용량까지 빈 칸도 모두 그린다. 빈 칸은 끌어 놓을 자리라서 필요하다.
        void AddCells(ItemOwnershipState inventory, InventoryPanel panel, string owner, ItemContainerState container)
        {
            HudInventoryPanel cells = _world.Hud.Inventory;
            // 칸 수 규칙은 다른 아이템 화면과 같은 소유 그래프 질의가 정한다.
            int count = OriginalInventoryQueries.CellCount(container);
            for (int position = 0; position < count; position++)
            {
                string? id = inventory.EntryAt(owner, position);
                // 칸 값은 장비 칸과 같은 규칙으로 읽고, 빈 칸은 이름과 아이콘 없이 그린다.
                ItemCellValues.Describe(inventory, _world.Catalog, id, out Sprite? icon, out string name, out int quantity);
                cells.AddCell(panel, position, id, name, icon, quantity);
            }
        }

        // 셀이 가리키는 아이템의 정의를 찾는다. 빈 셀, 회수한 상자 칸, 그래프에 없는 ID는 기록 없이 false다. 셀 ID는 화면 입력이라서다.
        // 씬 정리 중이라 그래프가 없어도 기록 없이 false다.
        bool TryCellDefinition(InventoryCellRef cell, out OriginalItemDefinition? definition)
        {
            definition = null;
            if (cell.IsEmpty) return false;
            // 저작 전리품 ID는 EditMode 테스트가 카탈로그와 맞춰 두므로 정의 누락은 콘텐츠 결함뿐이다.
            if (IsLootCell(cell))
                return TryLootKey(cell.Position, out _) && _world.Catalog.TryGet(_openedLoot!.ItemTypes[cell.Position], out definition);
            if (!_world.TryItems(out ItemOwnershipState? items)) return false;
            ItemInstanceState? item = items!.Find(cell.Id!);
            // 그래프의 아이템은 저장 읽기 검사로 늘 정의가 있다.
            return item != null && _world.Catalog.TryGet(item.OriginalItemId, out definition);
        }

        // 공용 HUD 셀 선택 이벤트를 받는다. 이벤트 형식에 맞춰 결과는 버린다. 원본 상세 화면 칸은 contentSelectable이 꺼져 있어
        // 그 칸의 부착물을 눌러도 선택과 상세 화면이 바뀌지 않는다.
        internal void Select(InventoryCellRef cell)
        {
            if (cell.Panel != InventoryPanel.Attachment) TrySelect(cell, out _);
        }

        // 셀을 선택으로 기억하고 이름과 사용 버튼, 원본 ItemDetails 상세 화면을 표시한다. 표시했으면 true와 정의를 돌려줘
        // Activate가 다시 조회하지 않게 한다. 정의를 못 찾으면 선택과 표시를 둘 다 그대로 둔다. 사용 버튼이 표시와 다른 아이템을 대상으로 하지 않게 하려는 것이다.
        bool TrySelect(InventoryCellRef cell, out OriginalItemDefinition? definition)
        {
            if (!TryCellDefinition(cell, out definition)) return false;
            _selection = cell;
            // 사용은 사용 시간이 있는 소지품 칸과 펫 가방 칸 아이템만 된다. 원본도 펫 가방 아이템을 바로 쓴다.
            // 아이템 사용 중에는 원본 CA_UseItem의 CanEditInventory false처럼 숨긴다.
            bool canUse = IsUsablePanel(cell.Panel) && definition!.UseTime > 0 && !_combat.UsingItem;
            _world.Hud.Inventory.Select(GameLocalization.Text(definition!.NameKey), canUse);
            ShowDetails(cell, definition);
            return true;
        }

        // 원본 LootView.RefreshDetails처럼 고른 아이템을 상세 화면에 보인다. 상자 칸은 아직 소유하지 않은 아이템이라 정의와 상자 수량만 보이고,
        // 그 칸에는 끌어 놓을 수 없다. 소유 아이템은 정의를 찾은 같은 그래프에서 읽는다.
        void ShowDetails(InventoryCellRef cell, OriginalItemDefinition definition)
        {
            HudInventoryPanel panel = _world.Hud.Inventory;
            if (IsLootCell(cell))
            {
                // 상세 화면이 퀵슬롯을 읽지 않게 되어 퀵슬롯 인수를 넘기지 않는다.
                _details.Show(panel.Details, panel, null, null, definition, _openedLoot!.Counts[cell.Position]);
                return;
            }
            // TryCellDefinition이 같은 호출에서 그래프의 아이템을 찾았으므로 그래프와 아이템이 늘 있다.
            // 퀵슬롯을 읽으려고 받던 세션은 이제 필요 없다.
            _world.TryItems(out ItemOwnershipState? items);
            _details.Show(panel.Details, panel, items, items!.Find(cell.Id!), definition, 0);
        }

        // 원본 SlotDisplay처럼 끌기를 시작하면 끄는 아이템을 받는 장비 칸과 상세 화면 칸을 강조한다. 끌어 온 칸 자신과,
        // 상세 화면이 보이는 아이템 자신을 끌 때의 그 칸들은 강조하지 않는다. 정의를 못 찾으면 받는 칸을 알 수 없어 강조하지 않는다.
        internal void BeginDrag(InventoryCellRef cell)
        {
            if (!TryCellDefinition(cell, out OriginalItemDefinition? definition)) return;
            IReadOnlyList<string> slots = OriginalCharacterSlots.All;
            for (int index = 0; index < slots.Count; index++)
                _world.Hud.Inventory.SetPluggable(index,
                    OriginalCharacterSlots.Accepts(slots[index], definition!) && !(cell.Panel == InventoryPanel.Slot && cell.Position == index));
            IReadOnlyList<OriginalItemSlot> shown = _details.ShownSlots;
            bool editable = _details.Owner != null && cell.Id != _details.Owner;
            for (int index = 0; index < shown.Count; index++)
                _world.Hud.Inventory.SetDetailPluggable(index,
                    editable && shown[index].Accepts(definition!) && !(cell.Panel == InventoryPanel.Attachment && cell.Position == index));
        }

        // 놓았거나 취소한 끌기가 끝나면 장비 칸 강조를 모두 끈다.
        internal void EndDrag() => _world.Hud.Inventory.ClearPluggable();

        // 원본 LootView의 끌어 놓기다. 장비 칸에 놓으면 장착하고, 소지품, 펫 가방, 창고 칸에 놓으면 그 칸으로 옮긴다.
        // 상자에서는 꺼내기만 하고 상자 쪽에 넣지는 않는다. 차 있는 칸의 맞바꾸기와 합치기 규칙은 세션 명령이 정한다.
        internal void Drop(InventoryCellRef source, InventoryCellRef target)
        {
            // 빈 셀은 옮길 것이 없다. 제자리 놓기는 공용 HUD 인벤토리 패널이 걸러 이 요청을 내지 않는다.
            if (source.IsEmpty || !CanEditInventory) return;
            InventoryLocation to;
            switch (target.Panel)
            {
                case InventoryPanel.Slot:
                    IReadOnlyList<string> slots = OriginalCharacterSlots.All;
                    to = InventoryLocation.Equipment(slots[target.Position]);
                    break;
                case InventoryPanel.Carried:
                    to = InventoryLocation.Carried(target.Position);
                    break;
                // 펫 칸은 펫 가방이 있을 때만 보이므로 그 칸에 놓을 수 있으면 가방도 있다.
                case InventoryPanel.Pet:
                    to = InventoryLocation.Pet(target.Position);
                    break;
                case InventoryPanel.Attachment:
                    // 상세 화면 칸은 보이는 소유 아이템의 원본 칸이다. 상자 칸 아이템의 상세 화면에는 놓을 수 없다.
                    if (!_details.TrySlotKey(target.Position, out string owner, out string slot)) return;
                    to = InventoryLocation.ItemSlot(owner, slot);
                    break;
                default:
                    if (!StorageShown) return;
                    to = InventoryLocation.Storage(target.Position);
                    break;
            }
            // 씬 정리 중이라 세션이 없으면 기록 없이 옮기지 않는다.
            if (!_world.TrySession(out GameSessionManager? session)) return;
            InventoryRejection rejection;
            if (IsLootCell(source))
            {
                // 상자 칸은 루팅 키를 출처로 넘겨, 넣기와 같은 커밋에서 회수가 기록되게 한다. 이미 회수한 칸이면 무시한다.
                if (!TryLootKey(source.Position, out string key)) return;
                InteractableLootbox point = _openedLoot!;
                // 사건 집합이 없는 프롤로그도 같은 칸을 두 번 집지 않도록 성공한 키를 이 월드에 기억한다.
                if (session!.Inventory.TryTakeLoot(_world.Catalog, point.ItemTypes[source.Position], point.Counts[source.Position], key, to,
                    out rejection)) _world.RememberLoot(key);
            }
            else session!.Inventory.TryMove(_world.Catalog, source.Id!, to, out rejection);
            // 가방이 가득 차면 원본 NotificationText처럼 알리고, 장비 칸이 받지 않는 종류면 원본 DenialPunch로 그 칸을 흔든다.
            // 두 거절은 함께 오지 않는다. 차 있는 칸 같은 다른 거절은 표시 없이 무시하는 입력이거나, 세션이 이미 기록한 구조 결함이다.
            // 펫 가방의 Sticky 거절도 원본 Inventory.acceptSticky 검사처럼 표시 없이 끝난다.
            _world.NotifyIfInventoryFull(rejection);
            if (rejection == InventoryRejection.SlotMismatch && target.Panel == InventoryPanel.Slot)
                _world.Hud.Inventory.DenySlot(target.Position);
            else if (rejection == InventoryRejection.SlotMismatch && target.Panel == InventoryPanel.Attachment)
                _world.Hud.Inventory.DenyDetailSlot(target.Position);
        }

        // 원본 ItemUtilities.SendToPlayer처럼 상세 화면 칸의 부착물을 빼서 소지품에 합쳐 넣고, 가방이 차 있으면 열린 창고에 넣는다.
        // 원본은 빈 장비 칸에 먼저 꽂지만 이 게임의 더블클릭은 장착하지 않는다. 둘 다 안 되면 가방 가득 참을 알린다.
        void Unplug(string id)
        {
            if (!_world.TrySession(out GameSessionManager? session)) return;
            session!.Inventory.TryMove(_world.Catalog, id, InventoryLocation.Carried(InventoryLocation._anyPosition), out InventoryRejection rejection);
            if (rejection == InventoryRejection.InventoryFull && StorageShown)
                session.Inventory.TryMove(_world.Catalog, id, InventoryLocation.Storage(InventoryLocation._anyPosition), out rejection);
            _world.NotifyIfInventoryFull(rejection);
        }

        // 원본 LootView 모두 넣기처럼 창고가 열려 있을 때 소지품을 창고에 합쳐 넣는다. 창고가 차면 원본처럼 알림 없이 멈춘다.
        // 인벤토리를 바꿀 수 없는 동안에는 다른 인벤토리 변경처럼 받지 않는다.
        internal void StoreAll()
        {
            if (!StorageShown || !CanEditInventory) return;
            // 씬 정리 중이라 세션이 없으면 기록 없이 넣지 않는다. 스택 합치기의 최대 수량은 카탈로그 정의가 정한다.
            if (_world.TrySession(out GameSessionManager? session)) session!.Inventory.TryStoreAll(_world.Catalog, out _);
        }

        // 원본 InventoryDisplay 정리 버튼이다. 창고 쪽 버튼은 창고가 보일 때만 받는다. 원본 CA_UseItem의 CanEditInventory false처럼
        // 아이템 사용 중에는 정리하지 않고, 씬 정리 중이라 세션이 없으면 기록 없이 무시한다. 결과는 커밋 알림이 다시 그린다.
        internal void Sort(bool storage)
        {
            if ((storage && !StorageShown) || !CanEditInventory) return;
            if (_world.TrySession(out GameSessionManager? session)) session!.Inventory.TrySort(_world.Catalog, storage);
        }

        // 더블클릭으로 소유 아이템을 반대쪽에 합쳐 넣는다. 가방이 가득 차면 원본 NotificationText처럼 알리고, 다른 거절은
        // 표시 없이 무시하는 입력이거나 세션이 이미 기록한 구조 결함이다. 씬 정리 중이라 세션이 없으면 기록 없이 무시한다.
        void MoveOwned(string id, InventoryLocation to)
        {
            if (!_world.TrySession(out GameSessionManager? session)) return;
            // 성공한 이동은 거절 코드가 None이라 결과와 상관없이 거절 코드로 알린다.
            session!.Inventory.TryMove(_world.Catalog, id, to, out InventoryRejection rejection);
            _world.NotifyIfInventoryFull(rejection);
        }

        // 루팅 완료 키 규약은 CampaignEventLog가 소유한다.
        static string LootKey(InteractableLootbox point, int index) => CampaignEventLog.LootKey(point.SourceId, index);

        // 상자 칸 하나를 줍는다. 이미 회수한 칸은 무시한다.
        internal void Take(InteractableLootbox point, int index)
        {
            // 내용 뒤의 빈 칸과 이미 집은 칸은 무시한다.
            if (index >= point.ItemTypes.Count) return;
            string key = LootKey(point, index);
            if (!_world.LootTaken(key)) Collect(point.ItemTypes[index], point.Counts[index], key);
        }

        // 상자 칸 하나를 세션 메모리에만 한 번 커밋한다. 파일은 다음 저장 지점이 쓰며, 목록은 커밋 알림이 다시 그린다.
        // 가방이 가득 찬 거절만 원본 NotificationText처럼 알리고, 다른 거절은 기록하지 않는다. 정의 누락과 구조 결함은 세션이 기록하고,
        // 칸 수량과 ID 형식은 OriginalGameplaySceneDataTests가 막으며, 닫힌 게이트는 전환이나 정리 중의 정상 거절이다.
        void Collect(int type, int count, string key)
        {
            // 씬 정리 중이라 세션이 없으면 기록 없이 줍지 않는다.
            if (!_world.TrySession(out GameSessionManager? session)) return;
            // 사건 집합이 없는 프롤로그도 같은 칸을 두 번 집지 않도록 성공한 키를 이 월드에 기억한다.
            if (session!.Inventory.TryCollectLoot(_world.Catalog, new[] { type }, new[] { count }, new[] { key }, out _,
                out InventoryRejection rejection)) _world.RememberLoot(key);
            // 어떤 거절을 알릴지는 컨텍스트가 한곳에서 정한다.
            _world.NotifyIfInventoryFull(rejection);
        }

        // 사용 버튼이나 더블클릭으로 온다. 고른 셀이 차 있는 소지품 칸이나 펫 가방 칸일 때만 쓴다. 전투 시뮬레이션이 행동 우선순위를 따져
        // 사용을 시작했을 때만 화면을 닫는다. 원본 LootView도 CharacterMainControl.OnMainCharacterStartUseItem에서 닫힌다.
        internal void Use()
        {
            // 쓸 수 있는 칸 판정은 선택 표시의 사용 버튼과 같은 규칙이다.
            if (!IsUsablePanel(_selection.Panel) || _selection.IsEmpty || !_world.SimulationReady) return;
            if (_combat.TryBeginUse(_selection.Id!)) Close();
        }

        // 사용 버튼 표시와 사용이 함께 쓰는 칸 판정이다. 소지품 칸과 펫 가방 칸 아이템만 쓸 수 있다.
        static bool IsUsablePanel(InventoryPanel panel) => panel == InventoryPanel.Carried || panel == InventoryPanel.Pet;
    }
}
