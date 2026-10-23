#nullable enable annotations
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 원작 CraftView의 열기, 분류 고르기, 제조식 고르기, 제조, 완료 알림, 닫기를 맡는 화면 컨트롤러다. RepairScreen과 같은 수명 규칙이고,
    // 입력 차단 핸들은 화면 공통 바탕(GameplayScreen)이 쥔다. 목록은 제조 소유자의 해금 제조식(제조식 모음 순서)을 진입점 장소 태그와 고른 분류로 거른다.
    // 제조는 GameplayCommands.TryCraft로 보낸다. 원작 OnPlayerItemOperation과 OnMoneyChanged 대신 세션, 계좌, 해금 변경 알림으로 다시 그린다.
    // 원작은 제조 단추 색을 고를 때만 칠하지만 이 화면은 다시 그릴 때마다 칠한다(원작 차이).
    internal sealed class CraftScreen : GameplayScreen
    {
        // 원작 CraftView.notificationFormatKey(LevelManager.prefab 값)다. 제작 도구가 원본 CSV 값을 번역 테이블에 둔다.
        const string _notificationKey = "ItemCraftedNotification";

        readonly GameplayWorldContext _world;
        readonly InventoryItemDetails _details;
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 원작 InteractCrafter.requireTag(FilterCraft 조건), currentFilterIndex, selectedEntry 자리다. 화면 상태라 저장하지 않는다.
        string _requireTag = "";
        int _filter;
        string? _selected;

        internal CraftScreen(GameplayWorldContext world)
        {
            _world = world;
            _details = new InventoryItemDetails(world);
        }

        // 원작 CraftView.SetupAndOpenView와 OnOpen이다. 원작처럼 선택을 비우고 첫 분류(전체)로 연다. 공용 HUD가 이 월드에 붙어 있지 않거나
        // 입력 차단을 못 받으면(정리 중) 열지 않는다. 호출자: GameplayScreens.ShowCraft(기지 제조 장소의 InteractCrafter).
        internal void Show(InteractCrafter crafter)
        {
            // 연결과 세션 확인, 다시 열기 전 닫기, 입력 차단은 다른 화면과 같은 순서라 공통 바탕이 맡는다.
            if (!TryBeginShow(_world, out GameSessionManager? session)) return;
            _requireTag = crafter.RequireTag;
            _filter = 0;
            _selected = null;
            OriginalCraftView view = _world.Hud.Craft;
            Action<string> select = Select;
            Action<int> filter = SetFilter;
            Action craft = Craft, close = Close, redraw = Redraw;
            view.EntrySelected += select;
            _subscriptions.Add(() => view.EntrySelected -= select);
            view.FilterSelected += filter;
            _subscriptions.Add(() => view.FilterSelected -= filter);
            view.CraftRequested += craft;
            _subscriptions.Add(() => view.CraftRequested -= craft);
            view.CloseRequested += close;
            _subscriptions.Add(() => view.CloseRequested -= close);
            // 비용 표시와 목록 배경색이 계좌에 달려 있고, 설계도 등록이나 특성으로 목록이 늘 수 있어 두 소유자 변경도 듣는다.
            EconomyManager.Changed += redraw;
            _subscriptions.Add(() => EconomyManager.Changed -= redraw);
            CraftingManager.Changed += redraw;
            _subscriptions.Add(() => CraftingManager.Changed -= redraw);
            // 세션과 로캘 다시 그리기는 공용 구독 도우미가 붙이고 뗀다.
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
                OriginalCraftView view = _world.Hud.Craft;
                _details.Hide(view.Details, true);
                view.Hide();
            }
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 SetSelection이다. 원작처럼 같은 줄을 다시 눌러도 선택을 풀지 않는다.
        void Select(string formulaId)
        {
            if (!IsOpen) return;
            _selected = formulaId;
            Redraw();
        }

        // 원작 SetFilter다. 선택을 비우고 다시 그린다. 번호는 Redraw가 분류 목록의 반복 번호로만 단추에 붙여 늘 범위 안이다.
        void SetFilter(int index)
        {
            if (!IsOpen) return;
            _filter = index;
            _selected = null;
            Redraw();
        }

        // 원작 CraftTask와 OnCraftFinished다. 명령이 확인, 지불, 지급을 한 번에 확정하고, 그 변경 알림이 다시 그린다.
        // 성공하면 원작 Cost.Return이 만든 결과 묶음마다 완료 알림을 하나씩 띄운다. 거절되면 화면은 그대로다.
        void Craft()
        {
            if (!IsOpen || _selected == null || !_world.TrySession(out GameSessionManager? session)) return;
            if (!GameplayDataSettings.CraftingFormulas.TryGet(_selected, out CraftingFormula? formula)) return;
            if (!GameplayCommands.TryCraft(session, GameManager.Economy, GameManager.Crafting, _world.Catalog, _selected, _requireTag)) return;
            // 원작은 만든 아이템이 없으면 알림도 없다. 카탈로그에 없는 결과는 지급 명령도 아이템을 만들지 못하므로 알리지 않는다.
            if (!_world.Catalog.TryGet(formula.ResultId, out OriginalItemDefinition? result)) return;
            // 원작 문구의 {itemDisplayName} 자리표시는 설계도 등록 알림과 같은 현지화 규칙으로 채운다.
            string text = GameLocalization.WithItemName(GameLocalization.Text(_notificationKey), GameLocalization.Text(result!.NameKey));
            int stacks = CraftedStacks(formula.ResultAmount, result.MaxStack);
            for (int index = 0; index < stacks; index++) _world.Hud.Notify(text);
        }

        // 원작 Cost.Return이 만드는 아이템 수다. 쌓이는 아이템(최대 겹침 2 이상)은 최대 겹침씩 묶고, 아니면 하나씩 만든다.
        internal static int CraftedStacks(int amount, int maxStack)
        {
            if (amount <= 0) return 0;
            return maxStack > 1 ? (amount + maxStack - 1) / maxStack : amount;
        }

        // 원작 RefreshList와 CountFilter의 분류 조건이다. 태그가 없는 분류(전체)는 원작처럼 결과 ID를 보지 않고 모두 든다.
        // 태그가 있으면 원작 CheckFilter처럼 결과 ID가 0 이상이고 결과 아이템 태그 중 하나라도 분류 태그와 같아야 한다.
        // resultTags가 null이면 결과 아이템이 카탈로그에 없어 태그를 모르는 것이다.
        internal static bool MatchesFilter(int resultId, IReadOnlyList<string>? resultTags, IReadOnlyList<string> requireTags)
        {
            if (requireTags.Count == 0) return true;
            if (resultId < 0 || resultTags == null) return false;
            for (int index = 0; index < requireTags.Count; index++)
                for (int tag = 0; tag < resultTags.Count; tag++)
                    if (string.Equals(resultTags[tag], requireTags[index], StringComparison.Ordinal)) return true;
            return false;
        }

        // 원작 RefreshFilterButtons, RefreshList, RefreshDetails다. 씬 정리 중이라 세션이 없으면 기록 없이 그리지 않는다.
        // 분류 번호는 열 때 0이고 SetFilter만 바꾸므로 범위 안이다. 분류가 없으면 목록을 채우지 않는다.
        void Redraw()
        {
            if (!IsOpen || !_world.TrySession(out GameSessionManager? session)) return;
            OriginalCraftView view = _world.Hud.Craft;
            OriginalGameplayCatalog catalog = _world.Catalog;
            EconomyManager economy = GameManager.Economy;
            IReadOnlyList<CraftingFormula> formulas = GameManager.Crafting.UnlockedFormulas(_requireTag);
            IReadOnlyList<OriginalCraftView.Filter> filters = view.Filters;

            view.BeginFilters();
            for (int index = 0; index < filters.Count; index++)
            {
                int count = 0;
                for (int formula = 0; formula < formulas.Count; formula++)
                    if (Matches(catalog, formulas[formula], filters[index])) count++;
                if (count < 1) continue;
                view.AddFilter(index, GameLocalization.Text(filters[index].NameKey), filters[index].Icon, index == _filter);
            }
            view.EndFilters();

            // 비용 출처는 제조 명령과 같은 세션 기준 소유 출처다. 계좌와 현금, 그리고 가방, 기지 창고, 펫 가방이다(원작 Cost.Enough).
            ItemSources sources = ItemSourceRules.Owned(session);
            // 제조식마다 가방 전체를 다시 세지 않도록 이 다시 그리기의 소유 수를 한 번 센다(Docs/runtime-boundaries.md#main-thread-split).
            OwnedItemCounts owned = session.Inventory.CountAllOwned(sources);
            CraftingFormula? selected = null;
            view.BeginEntries();
            if (filters.Count > 0)
            {
                OriginalCraftView.Filter current = filters[_filter];
                foreach (CraftingFormula formula in formulas)
                {
                    if (!Matches(catalog, formula, current)) continue;
                    catalog.TryGet(formula.ResultId, out OriginalItemDefinition? result);
                    // 원작 Setup의 "{이름} x{수량}"이다. 카탈로그에 없는 결과는 이름 없이 수량만 보인다.
                    string label = (result != null ? GameLocalization.Text(result.NameKey) : "") + " x" + formula.ResultAmount;
                    bool isSelected = formula.Id == _selected;
                    if (isSelected) selected = formula;
                    view.AddEntry(formula.Id, result != null ? result.Icon : null, label,
                        GameplayCommands.CanPay(session, economy, formula.Cost, owned, true), isSelected);
                }
            }
            view.EndEntries();
            // 고른 제조식이 지금 목록에 없으면 선택을 푼다. 해금은 되돌아가지 않아 보통은 분류를 바꿀 때만 생긴다.
            if (selected == null) _selected = null;
            DrawSelected(view, session, economy, catalog, selected, owned);
        }

        // 원작 RefreshTask다. 원작이 만든 임시 결과 아이템 대신 카탈로그 정의를 상자 칸처럼 상세에 보인다.
        // 원작처럼 쌓이는 결과만 결과 수량을 수량으로 쓴다. 제조 단추 색과 비용은 명령과 같은 CanPay 출처로 판정한다.
        void DrawSelected(OriginalCraftView view, GameSessionManager session, EconomyManager economy, OriginalGameplayCatalog catalog,
            CraftingFormula? formula, OwnedItemCounts owned)
        {
            if (formula == null || !catalog.TryGet(formula.ResultId, out OriginalItemDefinition? result))
            {
                _details.Hide(view.Details, false);
                view.ShowNothingSelected();
                return;
            }
            OriginalItemDefinition definition = result!;
            _details.Show(view.Details, _world.Hud.Inventory, null, null, definition, definition.MaxStack > 1 ? formula.ResultAmount : 1);
            Cost cost = formula.Cost;
            bool enough = GameplayCommands.CanPay(session, economy, cost, owned, true);
            view.ShowSelected(enough);
            // 원작 CostDisplay.Setup이다. 제조 단추 색과 같은 넉넉함 값과 목록이 센 수량 묶음을 넘겨 공용 도우미가 그린다.
            CostDisplayDrawing.Draw(view.Cost, catalog, cost, owned, CostDisplayDrawing.OwnedMoney(economy, owned), enough);
        }

        // 분류 하나의 조건을 결과 아이템 카탈로그 태그로 본다.
        static bool Matches(OriginalGameplayCatalog catalog, CraftingFormula formula, OriginalCraftView.Filter filter)
        {
            IReadOnlyList<string> requireTags = filter.RequireTags;
            if (requireTags.Count == 0) return true;
            return MatchesFilter(formula.ResultId, catalog.TryGet(formula.ResultId, out OriginalItemDefinition? result) ? result!.Tags : null, requireTags);
        }
    }
}
