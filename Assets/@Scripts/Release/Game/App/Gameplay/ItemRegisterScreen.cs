#nullable enable annotations
using System;

namespace Game.App
{
    // 원작 MasterKeysRegisterView와 FormulasRegisterView가 같은 흐름이라 열기, 칸 옮기기, 제출, 닫기를 이 바탕이 맡는다. 원작처럼 가방과
    // 창고(기지) 목록만 보이고 펫 가방은 보이지 않는다. 아이템을 더블클릭하면 등록 칸으로 옮기고, 등록 버튼이 그 아이템을 제출한다.
    // 칸의 아이템은 세션 소지품에 그대로 두고 목록에서만 빈 칸으로 보여, 창이 열린 동안 저장되거나 씬이 정리되어도 잃지 않는다. 칸에서 뺄 때와
    // 창을 닫을 때는 원작 SendToPlayer처럼 창고 아이템을 가방으로 옮기고, 가방이 차면 창고에 남긴다. 원작 닫기는 가방이 차면 아이템을 잃는데,
    // 그 결함은 옮기지 않는다(원작 차이). 등록 판정, 등록 명령, 소유자 알림은 파생 화면이 정한다.
    internal abstract class ItemRegisterScreen : GameplayScreen
    {
        // 원작 sfx_Register "UI/register"의 FMOD 경로다. 두 등록 창이 같은 소리를 쓴다.
        const string _registerSound = "event:/UI/register";

        private protected readonly GameplayWorldContext _world;
        readonly InventoryItemDetails _details;
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 등록 칸이 가리키는 아이템과 상세를 보이는 선택의 인스턴스 ID다. 화면 상태라 저장하지 않는다.
        string? _slotItem, _selected;

        private protected ItemRegisterScreen(GameplayWorldContext world)
        {
            _world = world;
            _details = new InventoryItemDetails(world);
        }

        // 공용 HUD의 이 등록 창이다. 두 창은 같은 등록 View 바탕이라 이 흐름이 같은 멤버로 그린다.
        private protected abstract OriginalItemRegisterView View { get; }

        // 원작 등록 칸의 표시 이름 키다. 원작은 첫 requireTags 태그의 표시 이름을 쓴다.
        private protected abstract string SlotLabelKey { get; }

        // 열린 동안만 받을 소유자 변경 알림과 창별 추가 의도를 붙인다. 공통 창 의도 다음, 세션과 로캘 구독 앞에 붙는다.
        private protected abstract void BindOwnerEvents(UiEventSubscriptions subscriptions, Action redraw);

        // 원작 EntryFunc_CanOperate(칸에 넣을 수 있는지)를 돌려주고 EntryFunc_ShouldHighligh를 highlight로 낸다.
        private protected abstract bool IsOperable(OriginalItemDefinition definition, out bool highlight);

        // 원작 RefreshRecordExistsIndicator다. 등록 칸 아이템이 이미 등록됐는지 본다.
        private protected abstract bool IsRecorded(OriginalItemDefinition definition);

        // 등록 명령을 보낸다. 명령이 확인, 소모, 등록을 한 전이로 확정한다. 성공해 알릴 문구가 있으면 notice에 담고, 없으면 null이다.
        private protected abstract bool TryRegister(GameSessionManager session, string itemId, out string? notice);

        // 창이 열린 동안만 쓰는 창별 상태를 비운다. 닫을 때 칸 아이템을 돌려보낸 뒤, 입력 차단을 풀기 전에 부른다.
        private protected virtual void ResetOpenState()
        {
        }

        // 원작 Show와 OnOpen의 나머지다. 호출자는 TryBeginShow로 연결, 세션, 입력 차단을 받은 뒤 창별 상태를 정하고 부른다.
        private protected void Open(GameSessionManager session)
        {
            _slotItem = null;
            _selected = null;
            OriginalItemRegisterView view = View;
            Action<string> select = Select, plug = Plug;
            Action selectSlot = SelectSlot, unplug = Unplug, submit = Submit, close = Close, redraw = Redraw;
            view.CellSelected += select;
            _subscriptions.Add(() => view.CellSelected -= select);
            view.CellActivated += plug;
            _subscriptions.Add(() => view.CellActivated -= plug);
            view.SlotSelected += selectSlot;
            _subscriptions.Add(() => view.SlotSelected -= selectSlot);
            view.SlotActivated += unplug;
            _subscriptions.Add(() => view.SlotActivated -= unplug);
            view.SubmitRequested += submit;
            _subscriptions.Add(() => view.SubmitRequested -= submit);
            view.CloseRequested += close;
            _subscriptions.Add(() => view.CloseRequested -= close);
            // 가방과 창고 변경은 세션 변경 알림으로, 등록 변경은 파생 화면이 붙이는 소유자 알림으로 받는다. 칸 수가 적어 알림마다
            // 다시 그린다. 세션과 로캘 구독은 공용 구독 도우미가 붙이고 뗀다.
            BindOwnerEvents(_subscriptions, redraw);
            _subscriptions.BindSessionChanged(session, redraw);
            _subscriptions.BindLocaleChanged(redraw);
            view.Show();
            Redraw();
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다. 칸의 아이템도 세션 소지품에 있어 그대로 저장된다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 OnClose다. 구독을 먼저 풀어 칸 아이템을 돌려보내는 변경 알림이 닫히는 창을 다시 그리지 않게 한다.
        // 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다. 세션 관문이 닫혀 옮기지 못하면 아이템은 있던 창고 칸에 남는다.
        internal sealed override void Close()
        {
            if (!IsOpen) return;
            _subscriptions.Clear();
            string? slotItem = _slotItem;
            _slotItem = null;
            _selected = null;
            if (slotItem != null) SendToBag(slotItem);
            ResetOpenState();
            ReleaseInput();
            if (_world.PresentationAttached)
            {
                OriginalItemRegisterView view = View;
                _details.Hide(view.Details, true);
                view.Hide();
            }
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 ItemUIUtilities.Select다. 같은 칸이면 선택을 푼다. 조작할 수 없는 칸은 칸 View가 의도를 보내지 않는다.
        void Select(string itemId)
        {
            if (!IsOpen) return;
            _selected = _selected == itemId ? null : itemId;
            Redraw();
        }

        // 등록 칸의 아이템을 고르거나 푼다. 닫힌 뒤나 칸이 빈 동안 늦게 온 클릭은 무시한다.
        void SelectSlot()
        {
            if (!IsOpen || _slotItem == null) return;
            _selected = _selected == _slotItem ? null : _slotItem;
            Redraw();
        }

        // 원작 OnInventoryItemDoubleClicked다. 등록 칸 필터(CanPlug)를 지나는 아이템만 칸 View가 보낸다. 칸에 있던 아이템은 원작 Plug의
        // unpluggedItem → SendToPlayer처럼 가방으로 보낸다. 새 아이템을 먼저 칸에 두어, 돌려보내기의 변경 알림이 그리는 목록에도 새 아이템이 빈 칸으로 보인다.
        void Plug(string itemId)
        {
            if (!IsOpen || itemId == _slotItem) return;
            string? previous = _slotItem;
            _slotItem = itemId;
            View.HideSuccess();
            if (previous != null) SendToBag(previous);
            Redraw();
        }

        // 원작 OnSlotDoubleClicked다. 칸의 아이템을 원작 SendToPlayer처럼 가방으로 보낸다.
        void Unplug()
        {
            if (!IsOpen || _slotItem == null) return;
            string itemId = _slotItem;
            _slotItem = null;
            View.HideSuccess();
            SendToBag(itemId);
            Redraw();
        }

        // 원작 OnSubmitButtonClicked다. 거절되면 원작처럼 화면과 소리는 그대로다. 성공하면 칸을 비우고, 알릴 문구가 있으면 알린 뒤
        // 소리를 낸다.
        void Submit()
        {
            if (!IsOpen || _slotItem == null || !_world.TrySession(out GameSessionManager? session)) return;
            string itemId = _slotItem;
            if (!TryRegister(session, itemId, out string? notice)) return;
            _slotItem = null;
            if (_selected == itemId) _selected = null;
            if (notice != null) _world.Hud.Notify(notice);
            _world.Audio.PlayOneShot(_registerSound);
            OriginalItemRegisterView view = View;
            Redraw();
            // 원작은 아이템을 없앤 칸 변경 알림이 성공 표시를 먼저 끄고, 그 뒤 성공 표시를 켠다.
            view.ShowSuccess();
        }

        // 원작 SendToPlayer의 소지품 우선이다. 가방 맨 위 칸 아이템은 원작에서도 가방으로 돌아가므로 그대로 둔다.
        // 창고에 있던 아이템만 가방 첫 빈 칸(같은 종류는 합침)으로 옮긴다. 가방이 차거나 관문이 닫혔으면 원작 창고 대체처럼 창고에 남긴다.
        void SendToBag(string itemId)
        {
            if (!_world.TryItems(out GameSessionManager? session, out ItemOwnershipState? items)) return;
            if (items.PositionIn(GameplayInventory._storage, itemId) == null) return;
            session.Inventory.TryMove(_world.Catalog, itemId, InventoryLocation.Carried(InventoryLocation._anyPosition), out _);
        }

        // 원작 OnOpen의 목록 Setup, registerSlotDisplay.Setup, RefreshRecordExistsIndicator, OnItemSelectionChanged다.
        // 칸 아이템이 다른 명령으로 목록에서 사라졌으면 칸을 비운다. 씬 정리 중이라 세션이나 소유 그래프가 없으면 기록 없이 그리지 않는다.
        void Redraw()
        {
            if (!IsOpen || !_world.TryItems(out GameSessionManager? session, out ItemOwnershipState? items)) return;
            OriginalItemRegisterView view = View;
            OriginalGameplayCatalog catalog = _world.Catalog;
            // 검증된 소유 그래프라 플레이어 루트와 그 소지 컨테이너가 늘 있다(InventoryScreen과 같은 공용 질의).
            OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? character);
            string bag = character!.InstanceId;
            // 원작처럼 창고는 PlayerStorage가 있는 기지에서만 보인다. 출처 규칙이 그 씬 차이를 정하고, 창고 찾기와 칸 아이템이
            // 아직 보이는 목록에 있는지는 공용 질의가 정한다.
            bool storage = OriginalInventoryQueries.TryShownStorage(items, ItemSourceRules.Owned(session), out ItemContainerState? stored);
            if (_slotItem != null && !OriginalInventoryQueries.SlotStillListed(items, bag, storage, _slotItem)) _slotItem = null;

            bool selectedShown = false;
            DrawList(view, items, catalog, OriginalItemRegisterView._bagList, true, bag, character.Inventory, ref selectedShown);
            DrawList(view, items, catalog, OriginalItemRegisterView._storageList, storage, GameplayInventory._storage, stored, ref selectedShown);

            ItemInstanceState? slot = _slotItem != null ? items.Find(_slotItem) : null;
            if (slot != null)
            {
                // 그래프의 아이템은 저장 읽기 검사로 늘 카탈로그 정의가 있다.
                OriginalItemDefinition definition = catalog.DefinitionOf(slot.OriginalItemId);
                bool selected = _selected == _slotItem;
                if (selected) selectedShown = true;
                view.ShowSlot(GameLocalization.Text(SlotLabelKey), _slotItem, definition.Icon, GameLocalization.Text(definition.NameKey), slot.Quantity,
                    selected, IsRecorded(definition));
            }
            else view.ShowSlot(GameLocalization.Text(SlotLabelKey), null, null, "", 0, false, false);
            if (!selectedShown) _selected = null;

            // 상세 화면은 인벤토리 화면과 같은 OriginalItemDetailsView 규칙(InventoryItemDetails)을 쓰므로 칸 셀도 공용 HUD 인벤토리 패널에 묶는다.
            // 목록에 보인 선택이라 Find는 실패하지 않고, 그래프의 아이템은 늘 카탈로그 정의가 있다.
            if (_selected != null)
            {
                ItemInstanceState item = items.Find(_selected)!;
                OriginalItemDefinition definition = catalog.DefinitionOf(item.OriginalItemId);
                _details.Show(view.Details, _world.Hud.Inventory, items, item, definition, 0);
            }
            else _details.Hide(view.Details, false);
        }

        // 원작 InventoryDisplay처럼 0번 칸부터 용량까지 빈 칸도 모두 그린다. 등록 칸으로 옮긴 아이템은 원작처럼 빈 칸으로 보인다.
        // 원작 Setup의 두 조건은 거르기가 아니라 표시다. 등록할 수 없는 아이템도 보이고 조작만 막힌다.
        void DrawList(OriginalItemRegisterView view, ItemOwnershipState items, OriginalGameplayCatalog catalog, int list, bool shown, string owner,
            ItemContainerState? container, ref bool selectedShown)
        {
            if (!shown || container == null)
            {
                view.BeginList(list, false, 0, 0);
                view.EndList(list);
                return;
            }
            // 칸 수 규칙은 다른 아이템 화면과 같은 소유 그래프 질의가 정한다.
            int capacity = OriginalInventoryQueries.CellCount(container);
            view.BeginList(list, true, container.Entries.Count, capacity);
            for (int position = 0; position < capacity; position++)
            {
                string? id = items.EntryAt(owner, position);
                // 빈 칸과 등록 칸으로 옮긴 아이템은 빈 셀이다. 칸의 아이템과 정의 찾기는 다른 아이템 화면과 같은 공용 규칙이 맡는다.
                if (id == _slotItem || !ItemCellValues.TryDescribe(items, catalog, id, out ItemInstanceState item, out OriginalItemDefinition definition))
                {
                    view.AddCell(list, null, null, "", 0, false, false, true);
                    continue;
                }
                bool registrable = IsOperable(definition, out bool highlight);
                // 원작처럼 조작할 수 없는 칸은 고를 수 없으므로 선택도 풀린다.
                bool selected = registrable && id == _selected;
                if (selected) selectedShown = true;
                view.AddCell(list, id, definition.Icon, GameLocalization.Text(definition.NameKey), item.Quantity, selected, highlight, registrable);
            }
            view.EndList(list);
        }
    }
}
