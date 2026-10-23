#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원본 GameplayUICanvas/Tabs/ViewArea/LootView/Main의 장비 칸, 가방, 펫 가방, 상자와 창고 화면을 표시하고 입력을 받는다.
    // 명령은 씬 소유자인 InventoryScreen이 이벤트로 받는다. 프리팹 참조는 생성자로 다시 받지 않고 SharedHudView의 직렬화 필드를 직접 읽는다.
    internal sealed class HudInventoryPanel
    {
        readonly SharedHudView _hud;
        // 화면 소유 셀을 재사용하고 새 목록을 채우기 전에 셀 연결을 끊는다.
        UiTemplateCollection<OriginalInventoryCell>? _cells;
        // 원본 IItemDragSource처럼 놓을 때까지 끌기를 시작한 칸 자리를 기억한다. 시작한 셀이나 장비 칸도 함께 두어
        // 다른 칸의 끌기 끝 알림이 지금 끌기를 끝내지 않게 한다. 시작한 칸이 있는 동안이 끌기 중이다.
        Component? _dragOwner;
        InventoryCellRef _dragSource;
        // 파괴 검사 없이 참조로 본다. 시작한 칸이 먼저 파괴돼도 StopDrag가 끌기 상태를 비울 수 있게 한다.
        bool Dragging => !ReferenceEquals(_dragOwner, null);
        // 마지막으로 선택, 활성화, 단축 등록을 보낸 칸의 아이템 ID다. 목록을 다시 채우면 그 아이템이 다른 패널로 옮겨 갔어도
        // 같은 ID의 셀에 주황 테두리를 다시 그린다. 소유 아이템 ID는 GUID라 상자 칸 번호 ID와 겹치지 않는다.
        // _selectedCell은 이번 목록에서 그 아이템을 보이는 셀이고, _markedCell은 지금 테두리를 켠 셀이다.
        string? _selectedId;
        OriginalInventoryCell? _selectedCell, _markedCell;
        // 원본 InventoryEntry.hovering처럼 포인터가 올라간 셀이다. 숫자키 등록이 이 셀의 현재 칸을 읽는다.
        OriginalInventoryCell? _hoveredCell;
        // Tabs의 하위 Canvas다. 열림 여부를 매 프레임 읽는 곳이 있어 한 번 찾아 둔다.
        Canvas? _tabsCanvas;
        // Tabs 아래 스크롤 테두리다. Tabs를 끄지 않고 숨기므로 다시 보일 때 테두리를 직접 다시 계산시킨다.
        OriginalScrollViewBorderFrame[]? _borderFrames;
        // 소지품, 펫 가방, 가장 큰 상자(10칸)를 여유 있게 덮는 미리 만들 칸 수다. 기지 창고처럼 더 큰 목록은 첫 채우기에서 모자란 만큼만 만든다.
        const int _prewarmedCells = 64;

        internal HudInventoryPanel(SharedHudView hud) => _hud = hud;

        // 원본 Tabs 화면(_inventory)이 보이는 동안을 인벤토리 열림으로 본다. Tabs는 숨겨도 켜 둔 채라 Canvas 표시로 판단한다.
        public bool IsOpen => _hud._inventory.activeSelf && TabsCanvas.enabled;

        Canvas TabsCanvas
        {
            get
            {
                if (_tabsCanvas == null) _tabsCanvas = _hud._inventory.GetComponent<Canvas>();
                return _tabsCanvas;
            }
        }

        // EditMode 테스트가 Awake 없이 채우기 메서드를 부르므로 셀 풀은 처음 쓸 때 한 번 만든다.
        // 셀 템플릿은 원본 InventoryEntry 프리팹(UI/Common/InventoryEntry)이다.
        UiTemplateCollection<OriginalInventoryCell> Cells => _cells ??= new UiTemplateCollection<OriginalInventoryCell>(_hud._cell, cell => cell.ResetBinding());
        public event Action? CloseRequested;
        public event Action? UseRequested;
        public event Action? StoreAllRequested;
        // 원본 InventoryDisplay 정리 버튼이다. 인수는 창고 쪽 버튼인지다.
        public event Action<bool>? SortRequested;
        // 왼쪽 클릭은 선택, 더블클릭과 우클릭은 활성화, 퀵슬롯에 놓기는 단축 등록이다. 모두 칸 자리를 값으로 넘긴다.
        public event Action<InventoryCellRef>? ItemSelected;
        public event Action<InventoryCellRef>? ItemActivated;
        public event Action<InventoryCellRef, int>? ShortcutRequested;
        // 원본 IItemDragSource.OnStartDragItem과 OnEndDragItem이다. 소유자는 그 사이에 장비 칸의 장착 가능 표시를 켠다.
        public event Action<InventoryCellRef>? DragStarted;
        public event Action? DragEnded;
        // 끌던 칸을 다른 칸이나 장비 칸에 놓으면 (출발, 도착) 자리를 넘긴다. 이동 규칙은 소유자가 정한다.
        public event Action<InventoryCellRef, InventoryCellRef>? DropRequested;

        // HUD를 처음 준비할 때 인벤토리 화면과 끌기 아이콘을 닫고 장비 칸 효과를 비운다. 닫기가 Tabs를 켠 채 숨기므로
        // 하위 요소의 Awake, OnEnable, 첫 레이아웃이 여기서 끝난다. Play Mode에서는 칸도 미리 만들어 첫 열기에 몰리지 않게 한다.
        // HUD는 앱 수명이라 이 비용은 앱 시작 때 한 번만 든다.
        internal void Initialize()
        {
            CloseAndUnbindSlots();
            _hud._dragPointer.Hide();
            if (Application.isPlaying) Cells.Prewarm(_hud._carried, _prewarmedCells);
        }

        // 소유자가 바뀔 때 인벤토리를 닫고, 끌기와 장비 칸 연결을 끊고, 셀을 풀로 돌려보내고, 선택 표시를 비운다.
        // 떠나는 소유자에게는 끌기 끝을 알리지 않는다. 상태 표시 갱신은 상태 표시의 Reset이 맡는다.
        internal void Reset()
        {
            CloseAndUnbindSlots();
            StopDrag(false);
            _cells?.Clear();
            _selectedId = null;
            _selectedCell = null;
            _hoveredCell = null;
            Select("", false);
        }

        // 인벤토리 화면을 닫고 장비 칸 열 개와 상세 화면 칸의 연결과 표시 효과를 끊는다. 처음 준비와 소유자 전환이 같은 상태로 만든다.
        void CloseAndUnbindSlots()
        {
            UiVisibility.SetCanvasShown(_hud._inventory, false);
            foreach (OriginalSlotCell slot in _hud._slots) slot.ResetBinding();
            _hud._details.Hide(true);
        }

        // 원본 LootView의 ItemDetails 화면이다. 표시할 값은 화면 소유자가 정한다.
        internal OriginalItemDetailsView Details => _hud._details;

        // 끌던 아이템을 꽂을 수 있는 상세 화면 칸에 원본 PluggableIndicator를 켠다. 판정은 소유자가 하고, 칸 번호는 지금 보이는 칸 범위다.
        internal void SetDetailPluggable(int index, bool pluggable) => _hud._details.Slots[index].SetPluggable(pluggable);

        // 상세 화면 칸이 아이템을 거절하면 원본 DenialPunch로 알린다. 놓기 결과로 칸이 다시 그려져 줄었으면 보이지 않으므로 건너뛴다.
        internal void DenyDetailSlot(int index)
        {
            IReadOnlyList<OriginalSlotCell> slots = _hud._details.Slots;
            if (index < slots.Count) slots[index].DenialPunch();
        }

        // 고정 버튼의 프리팹 영속 OnClick이 SharedHudView를 거쳐 부르는 명령 알림이다. 규칙은 Docs/rules/ui.md에 있다.
        internal void RequestClose() => CloseRequested?.Invoke();
        internal void RequestUse() => UseRequested?.Invoke();
        internal void RequestStoreAll() => StoreAllRequested?.Invoke();
        internal void RequestSort(bool storage) => SortRequested?.Invoke(storage);

        // 모달의 열림 상태와 HUD 표시를 한 번에 전환한다. 숨긴 셀은 레이캐스트에서 빠져 끌기 끝과 포인터 나가기 알림을 받지 못하므로
        // 닫기 전에 끌기를 끝내 소유자에게 알리고 올라간 셀을 잊으며, 장착 가능 표시는 페이드 없이 숨긴다.
        // Tabs는 끄지 않고 Canvas로 숨겨, 다시 열 때 하위 요소의 OnEnable 연쇄와 ScrollRect의 강제 레이아웃 재계산이 없다.
        public void SetOpen(bool visible)
        {
            if (!visible)
            {
                StopDrag(true);
                _hoveredCell = null;
                foreach (OriginalSlotCell slot in _hud._slots) slot.HidePluggable();
                // 원본 LootView를 닫을 때처럼 상세 화면도 페이드 없이 숨긴다.
                _hud._details.Hide(true);
            }
            UiVisibility.SetCanvasShown(_hud._inventory, visible);
            if (visible)
            {
                _hud.HideInteraction();
                // 원본 ScrollViewBorderFrame은 화면이 켜질 때 테두리를 다시 계산한다. 숨김 중 바뀐 목록도 같은 시점에 맞춘다.
                _borderFrames ??= _hud._inventory.GetComponentsInChildren<OriginalScrollViewBorderFrame>(true);
                foreach (OriginalScrollViewBorderFrame frame in _borderFrames) frame.RefreshWhenShown();
            }
            _hud.Status.RefreshLayout();
        }

        // 호출자는 스냅샷이 바뀔 때만 목록을 다시 채워 매 프레임 오브젝트를 만들지 않는다.
        // 앞 목록의 셀은 연결만 끊어 앞에서부터 다시 쓰고, 다 채운 뒤 End가 남은 셀만 숨긴다. 고정 인벤토리 계층은 파괴하지 않는다.
        // lootTargetOpen은 상자나 창고가 열렸는지이고, storage는 그중 창고인지다. pet은 펫 가방이 있어 펫 영역을 보일지이며,
        // 펫 가방을 모르는 호출자가 펫 영역을 켜지 않도록 기본값은 false다.
        public void Begin(string title, bool lootTargetOpen, bool storage, bool pet = false)
        {
            _hud._title.text = title;
            // 원본 LootView.OnOpen처럼 상자도 창고도 없으면 LootTarget 영역을 페이드 없이 바로 숨긴다. 제목과 격자도 그 안에 있어 함께 숨는다.
            UiVisibility.Set(_hud._lootTarget, lootTargetOpen);
            // 원본 LootView.OnOpen처럼 펫 가방이 있을 때만 펫 영역(InventoryDisplay_Pet)을 켠다. 펫 칸 격자도 그 안에 있어 함께 숨는다.
            UiVisibility.Set(_hud._petInventory, pet);
            // 원본 LootView처럼 모두 넣기 버튼은 창고를 열었을 때만 보인다. 정리 버튼은 원본 InventoryDisplay.Setup처럼
            // showSortButton이 켜진 소지품 쪽은 늘 켜고, LootTarget 쪽은 showSortButton이 켜진 원작 상자가 기지 창고뿐이라 창고일 때만 켠다.
            _hud._storeAll.gameObject.SetActive(storage);
            _hud._sortCarried.gameObject.SetActive(true);
            _hud._sortStorage.gameObject.SetActive(storage);
            Cells.Begin();
            _selectedCell = null;
            Select("", false);
        }

        // 원본 SlotDisplay처럼 장비 칸 하나에 칸 이름과 꽂힌 아이템을 표시한다. id가 null이면 빈 칸이다.
        // 칸 순서는 OriginalCharacterSlots.All이고, 호출자는 저작된 열 칸 범위의 색인만 넘기므로 범위를 검사하지 않는다.
        public void ShowSlot(int index, string label, string? id, string itemName, Sprite? icon, int count) =>
            _hud._slots[index].Bind(this, new InventoryCellRef(InventoryPanel.Slot, index, id), label, itemName, icon, count);

        // 원본 InventoryDisplay처럼 용량까지의 모든 칸을 그린다. id가 null이면 빈 칸이다.
        // 소지품 칸은 가방 그리드에, 펫 가방 칸은 펫 그리드에, 창고나 상자 칸은 LootTarget 그리드에 부른 순서대로 둔다.
        // 셀 풀은 Begin이 늘 먼저 만든다. 세 그리드가 같은 템플릿과 입력 알림을 쓴다.
        public void AddCell(InventoryPanel panel, int position, string? id, string itemName, Sprite? icon, int count)
        {
            Transform parent = panel switch
            {
                InventoryPanel.Other => _hud._other,
                InventoryPanel.Pet => _hud._pet,
                _ => _hud._carried
            };
            OriginalInventoryCell cell = _cells.Acquire(parent);
            cell.Bind(this, new InventoryCellRef(panel, position, id), itemName, icon, count);
            cell.gameObject.SetActive(true);
            if (id != null && id == _selectedId) _selectedCell = cell;
        }

        // Begin 뒤 AddCell로 채우지 않은 남은 셀을 숨긴다. 목록을 채운 호출자가 같은 갱신 안에서 Begin 다음에 부르므로 셀 풀이 늘 있다.
        // 끌기를 시작한 셀이 숨으면 끌기 끝 알림이 오지 않으므로 여기서 끌기를 끝낸다.
        public void End()
        {
            _cells.End();
            if (_dragOwner != null && !_dragOwner.gameObject.activeInHierarchy) StopDrag(true);
        }

        // 선택한 아이템 이름과 사용 버튼 표시를 갱신한다. 문구가 있는 동안만 선택한 셀에 원본 주황 테두리를 켠다.
        internal void Select(string text, bool canUse)
        {
            _hud._selected.text = text;
            _hud._use.gameObject.SetActive(canUse);
            OriginalInventoryCell? marked = text.Length > 0 ? _selectedCell : null;
            if (marked == _markedCell) return;
            if (_markedCell != null) _markedCell.SetMarked(false);
            if (marked != null) marked.SetMarked(true);
            _markedCell = marked;
        }

        // 아래는 셀과 장비 칸이 부르는 입력 알림이다. 장비 칸은 선택 테두리가 없어 view에 null을 넘긴다.
        internal void SelectCell(InventoryCellRef cell, OriginalInventoryCell? view)
        {
            Remember(cell, view);
            ItemSelected?.Invoke(cell);
        }

        internal void ActivateCell(InventoryCellRef cell, OriginalInventoryCell? view)
        {
            Remember(cell, view);
            ItemActivated?.Invoke(cell);
        }

        internal void RequestShortcut(InventoryCellRef cell, OriginalInventoryCell view, int slot)
        {
            Remember(cell, view);
            ShortcutRequested?.Invoke(cell, slot);
        }

        // 셀의 포인터 들어오기 알림이다. 상자와 창고 셀도 기억하고, 소지품 칸만 등록할지는 화면 소유자가 정한다.
        internal void Hover(OriginalInventoryCell view) => _hoveredCell = view;

        // 다른 셀의 들어오기가 먼저 온 뒤 늦게 온 나가기는 새 셀을 지우지 않는다.
        internal void Unhover(OriginalInventoryCell view)
        {
            if (ReferenceEquals(_hoveredCell, view)) _hoveredCell = null;
        }

        // 포인터가 올라간 찬 셀의 현재 칸을 돌려준다. 풀로 돌아간 셀은 칸이 비어 false다.
        internal bool TryHovered(out InventoryCellRef cell)
        {
            cell = _hoveredCell != null ? _hoveredCell.Cell : default;
            return !cell.IsEmpty;
        }

        // 소유자는 이 알림 안에서 Select로 선택 문구를 보이므로, 그 전에 테두리를 그릴 칸을 기억한다.
        // 상세 화면 칸은 원본처럼 선택을 바꾸지 않으므로 기억하지 않아, 상세 화면이 보이는 아이템의 테두리가 남는다.
        void Remember(InventoryCellRef cell, OriginalInventoryCell? view)
        {
            if (cell.Panel == InventoryPanel.Attachment) return;
            _selectedId = cell.Id;
            _selectedCell = view;
        }

        // 찬 칸이 끌기를 시작하면 원본 ItemDraggingPointerDisplay처럼 포인터를 따라가는 아이콘을 보인다.
        internal void BeginDrag(Component owner, InventoryCellRef source, Sprite? icon)
        {
            _dragOwner = owner;
            _dragSource = source;
            _hud._dragPointer.Show(icon);
            DragStarted?.Invoke(source);
        }

        // 끌기 중이고 출발 칸이 아닌 곳에 놓았을 때만 이동을 요청한다. EventSystem은 놓기를 끌기 끝보다 먼저 보낸다.
        internal void Drop(InventoryCellRef target)
        {
            if (!Dragging || (target.Panel == _dragSource.Panel && target.Position == _dragSource.Position)) return;
            DropRequested?.Invoke(_dragSource, target);
        }

        // 끌기를 시작한 컴포넌트가 보낸 끝 알림만 받는다.
        internal void EndDrag(Component owner)
        {
            if (Dragging && ReferenceEquals(owner, _dragOwner)) StopDrag(true);
        }

        // 끌기가 없으면 아무것도 하지 않아, 닫기와 끌기 끝이 겹쳐도 끝 알림은 한 번만 간다.
        void StopDrag(bool notify)
        {
            if (!Dragging) return;
            _dragOwner = null;
            _dragSource = default;
            _hud._dragPointer.Hide();
            if (notify) DragEnded?.Invoke();
        }

        // 원본 SlotDisplay.OnStartDragItem처럼 끌던 아이템을 꽂을 수 있는 장비 칸에 표시를 켠다. 판정은 소유자가 한다.
        internal void SetPluggable(int slotIndex, bool pluggable) => _hud._slots[slotIndex].SetPluggable(pluggable);

        // 원본 OnEndDragItem처럼 모든 장비 칸과 상세 화면 칸의 장착 가능 표시를 페이드로 끈다.
        internal void ClearPluggable()
        {
            foreach (OriginalSlotCell slot in _hud._slots) slot.SetPluggable(false);
            foreach (OriginalSlotCell slot in _hud._details.Slots) slot.SetPluggable(false);
        }

        // 장비 칸이 아이템을 거절하면 원본 DenialPunch로 알린다.
        internal void DenySlot(int slotIndex) => _hud._slots[slotIndex].DenialPunch();

        // 원본 소지품 표의 용량과 중량을 실제 소유 그래프 값으로 갱신한다.
        internal void ShowStats(int count, int? capacity, float weight, float maximum)
        {
            // 무제한 컨테이너의 null 용량을 유한 값으로 추정하지 않고 ∞로 표시한다.
            _hud._inventoryCapacity.text = "(" + count + "/" + (capacity?.ToString() ?? "∞") + ")";
            _hud._inventoryWeight.text = weight.ToString("F1") + "/" + maximum.ToString("0.#") + "KG";
            // 상태 HUD 게이지와 같은 채움 비율 규칙을 쓴다.
            _hud._inventoryWeightFill.fillAmount = HudStatusDisplay.GaugeFill(weight, maximum);
        }
    }
}
