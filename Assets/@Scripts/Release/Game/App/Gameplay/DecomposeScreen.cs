#nullable enable annotations
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 원작 ItemDecomposeView의 열기, 고르기, 수량, 분해, 닫기를 맡는 화면 컨트롤러다. MasterKeyRegisterScreen과 같은 수명 규칙이고,
    // 입력 차단 핸들은 화면 공통 바탕(GameplayScreen)이 쥔다. 원작처럼 가방과 창고(기지) 목록을 보이고, 분해식이 없는 아이템은 보이되 고를 수 없다.
    // 분해는 SessionInventoryCommands.TryDecompose가 한 전이로 확정하고, 화면은 세션 변경 알림으로 다시 그린다. 기지 작업대와 제로존 분해 스테이션이 연다.
    // 원작 차이: 원작 PlayPutItemSFX(아이템 soundKey의 놓기 소리)는 카탈로그에 소리 키가 없어 옮기지 않는다.
    // 원작 빠른 집기 입력(UIInputManager.OnFastPick → 분해)은 제품 입력에 그 동작이 없어 옮기지 않는다.
    internal sealed class DecomposeScreen : GameplayScreen
    {
        readonly GameplayWorldContext _world;
        readonly InventoryItemDetails _details;
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 상세와 분해 대상인 선택의 인스턴스 ID다. 화면 상태라 저장하지 않는다.
        string? _selected;
        // 수량 슬라이더를 마지막으로 맞춘 선택과 그때의 겹친 수다. 원작처럼 선택이 바뀌거나 분해한 뒤에만 겹친 수 전체로 되돌린다.
        string? _countItem;
        int _countMax;

        internal DecomposeScreen(GameplayWorldContext world)
        {
            _world = world;
            _details = new InventoryItemDetails(world);
        }

        // 원작 ItemDecomposeView.Show와 OnOpen이다. 원작처럼 선택 없이 연다. 공용 HUD가 이 월드에 붙어 있지 않거나 세션이 없거나
        // 입력 차단을 못 받으면(정리 중) 열지 않는다. 호출자: GameplayScreens.ShowDecompose(ShowDecomposeView 동작).
        internal void Show()
        {
            if (!_world.TryPresentSession(out GameSessionManager? session)) return;
            Close();
            if (!TryBlockInput(_world.Runtime)) return;
            _selected = null;
            _countItem = null;
            _countMax = 0;
            OriginalItemDecomposeView view = _world.Hud.Decompose;
            Action<string> select = Select;
            Action<int> countChanged = CountChanged;
            Action decompose = Decompose, close = Close, redraw = Redraw;
            view.CellSelected += select;
            _subscriptions.Add(() => view.CellSelected -= select);
            view.CountChanged += countChanged;
            _subscriptions.Add(() => view.CountChanged -= countChanged);
            view.DecomposeRequested += decompose;
            _subscriptions.Add(() => view.DecomposeRequested -= decompose);
            view.CloseRequested += close;
            _subscriptions.Add(() => view.CloseRequested -= close);
            // 가방과 창고 변경은 세션 변경 알림으로 받는다. 칸 수가 적어 알림마다 다시 그리고,
            // 세션과 로캘 구독은 공용 구독 도우미가 붙이고 뗀다.
            _subscriptions.BindSessionChanged(session, redraw);
            _subscriptions.BindLocaleChanged(redraw);
            view.Show();
            Redraw();
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다. 제로존에서는 자동 저장 객체가 없어 아무 일도 없다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 OnClose다. 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다. 구독을 먼저 풀어 닫히는 창을 다시 그리지 않는다.
        internal override void Close()
        {
            if (!IsOpen) return;
            _subscriptions.Clear();
            _selected = null;
            _countItem = null;
            ReleaseInput();
            if (_world.PresentationAttached)
            {
                OriginalItemDecomposeView view = _world.Hud.Decompose;
                _details.Hide(view.Details, true);
                view.Hide();
            }
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 ItemUIUtilities.Select다. 같은 칸이면 선택을 푼다. 분해식이 없는 칸은 칸 View가 의도를 보내지 않는다.
        void Select(string itemId)
        {
            if (!IsOpen) return;
            _selected = _selected == itemId ? null : itemId;
            Redraw();
        }

        // 원작 OnSliderValueChanged다. 결과 표시만 그 수량으로 다시 그린다.
        void CountChanged(int count)
        {
            if (!IsOpen || _selected == null || !_world.TryItems(out GameSessionManager? session, out ItemOwnershipState? items)) return;
            ItemInstanceState? item = items.Find(_selected);
            if (item != null) DrawResult(session, item, count);
        }

        // 원작 OnDecomposeButtonClick과 DecomposeTask다. 원작처럼 슬라이더 수량으로 분해한다. 거절되면 화면은 그대로다.
        // 성공하면 세션 변경 알림이 목록을 다시 그리고, 원작 Refresh처럼 남은 묶음의 수량 슬라이더를 겹친 수 전체로 되돌린다.
        void Decompose()
        {
            if (!IsOpen || _selected == null || !_world.TrySession(out GameSessionManager? session)) return;
            string itemId = _selected;
            int count = _world.Hud.Decompose.Count;
            _countItem = null;
            if (!session.Inventory.TryDecompose(_world.Catalog, GameplayDataSettings.DecomposeDatabase, itemId, count)) Redraw();
        }

        // 원작 OnOpen의 목록 Setup과 Refresh다. 고른 아이템이 분해나 다른 명령으로 목록에서 사라졌으면 원작처럼 선택 없음으로 그린다.
        // 씬 정리 중이라 세션이나 소유 그래프가 없으면 기록 없이 그리지 않는다.
        void Redraw()
        {
            if (!IsOpen || !_world.TryItems(out GameSessionManager? session, out ItemOwnershipState? items)) return;
            OriginalItemDecomposeView view = _world.Hud.Decompose;
            OriginalGameplayCatalog catalog = _world.Catalog;
            DecomposeDatabase database = GameplayDataSettings.DecomposeDatabase;
            // 검증된 소유 그래프라 플레이어 루트와 그 소지 컨테이너가 늘 있다(InventoryScreen과 같은 공용 질의).
            OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? character);
            string bag = character!.InstanceId;
            // 원작처럼 창고는 PlayerStorage가 있는 기지에서만 보인다. 분해 명령과 등록 창들이 함께 쓰는 출처 규칙이 그 씬 차이를 정하고,
            // 창고 컨테이너 찾기는 등록 창들과 같은 공용 질의가 맡는다.
            bool storage = OriginalInventoryQueries.TryShownStorage(items, ItemSourceRules.BagAndBaseStorage(session.Snapshot().SceneId),
                out ItemContainerState? stored);

            bool selectedShown = false;
            DrawList(view, items, catalog, database, OriginalItemDecomposeView._bagList, true, bag, character.Inventory, ref selectedShown);
            DrawList(view, items, catalog, database, OriginalItemDecomposeView._storageList, storage, GameplayInventory._storage, stored,
                ref selectedShown);
            if (!selectedShown) _selected = null;

            if (_selected == null)
            {
                _countItem = null;
                view.ShowEmpty();
                _details.Hide(view.Details, false);
                return;
            }
            // 목록에 보인 선택이라 Find는 실패하지 않고, 그래프의 아이템은 저장 읽기 검사로 늘 카탈로그 정의가 있다.
            ItemInstanceState item = items.Find(_selected)!;
            OriginalItemDefinition definition = catalog.DefinitionOf(item.OriginalItemId);
            // 고른 칸은 분해식이 있는 칸뿐이라 분해 버튼이 늘 보인다. 원작 Setup처럼 판정은 그대로 넘긴다.
            view.ShowTarget(GameLocalization.Text(definition.NameKey), database.TryGet(item.OriginalItemId, out _));
            // 원작 SetupSlider다. 선택이 바뀌었거나 분해 뒤이거나 겹친 수가 바뀌었을 때만 1~겹친 수, 기본값 전체로 맞춘다.
            if (_countItem != _selected || _countMax != item.Quantity)
            {
                _countItem = _selected;
                _countMax = item.Quantity;
                view.SetCount(item.Quantity, item.Quantity);
            }
            DrawResult(session, item, view.Count);
            // 상세 화면은 인벤토리 화면과 같은 OriginalItemDetailsView 규칙(InventoryItemDetails)을 쓰므로 칸 셀도 공용 HUD 인벤토리 패널에 묶는다.
            // 상세 화면이 퀵슬롯을 읽지 않게 되어 퀵슬롯 인수를 넘기지 않는다.
            _details.Show(view.Details, _world.Hud.Inventory, items, item, definition, 0);
        }

        // 원작 RefreshResult와 CostDisplay.Setup(result, count)다. 결과 수량에 배수를 곱하고, 아이템 줄의 가진 수는 원작
        // ItemUtilities.GetItemCount처럼 소유 출처 전체에서 센다. 원작 분해 결과에는 돈이 없어(가져오기 도구가 확인) 돈 줄은 늘 숨는다.
        void DrawResult(GameSessionManager session, ItemInstanceState item, int count)
        {
            OriginalItemDecomposeView view = _world.Hud.Decompose;
            if (!GameplayDataSettings.DecomposeDatabase.TryGet(item.OriginalItemId, out Cost result))
            {
                view.ClearResult();
                return;
            }
            OriginalCostDisplayView display = view.Result;
            IReadOnlyList<Cost.ItemEntry> entries = result.Items;
            // 가진 수는 세션 기준 소유 출처에서 세고, 아이템 줄은 다른 비용 표시와 같은 공용 도우미가 수량 배수를 곱해 그린다.
            ItemSources sources = ItemSourceRules.Owned(session);
            display.BeginItems(result.Money * count, true, true, entries.Count > 0);
            // 결과 줄이 여럿이라 가진 수는 한 번 센 수량 묶음에서 읽는다.
            CostDisplayDrawing.AddItems(display, _world.Catalog, entries, session.Inventory.CountAllOwned(sources), count);
            display.EndItems();
        }

        // 원작 InventoryDisplay처럼 0번 칸부터 용량까지 빈 칸도 모두 그린다. 원작 Setup의 조건(빈 칸이거나 분해식이 있음)은
        // 거르기가 아니라 조작 가능 표시다. 분해식이 없는 아이템도 보이고 고르기만 막힌다.
        void DrawList(OriginalItemDecomposeView view, ItemOwnershipState items, OriginalGameplayCatalog catalog, DecomposeDatabase database, int list,
            bool shown, string owner, ItemContainerState? container, ref bool selectedShown)
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
                // 칸의 아이템과 정의 찾기는 다른 아이템 화면과 같은 공용 규칙이 맡는다. 빈 칸이면 빈 셀을 넣는다.
                if (!ItemCellValues.TryDescribe(items, catalog, id, out ItemInstanceState item, out OriginalItemDefinition definition))
                {
                    view.AddCell(list, null, null, "", 0, false, true);
                    continue;
                }
                bool decomposable = database.TryGet(item.OriginalItemId, out _);
                // 원작처럼 조작할 수 없는 칸은 고를 수 없으므로 선택도 풀린다.
                bool selected = decomposable && id == _selected;
                if (selected) selectedShown = true;
                view.AddCell(list, id, definition.Icon, GameLocalization.Text(definition.NameKey), item.Quantity, selected, decomposable);
            }
            view.EndList(list);
        }
    }
}
