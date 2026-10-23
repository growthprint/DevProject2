#nullable enable annotations
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 원작 FormulasDetailsDisplay.Setup이 고르는 상세 모습이다. 고른 칸이 없거나 결과 ID가 음수면 자리표시, 해금 전이면 ???, 해금했으면 결과 정보다.
    internal enum FormulaIndexDetail
    {
        Nothing,
        Unknown,
        Formula,
    }

    // 원작 FormulasIndexView(레시피 도감)의 컨트롤러다. 창은 공용 HUD의 OriginalFormulasIndexView이고 값만 받아 그린다.
    // 원작은 설계도 연구 창의 도감 단추(ViewsProxy.ShowFormulasIndexView)로만 연다. 원작 View.OnOpen처럼 연구 창을 닫고 열며, 닫아도 연구 창으로 돌아가지 않는다.
    // 도감은 해금 상태를 읽기만 하므로 보내는 명령이 없다. 열린 동안 게임플레이 입력을 막고 ESC와 닫기 버튼으로 닫는다(MasterKeysIndexScreen 선례).
    // 입력 차단 핸들은 화면 공통 바탕(GameplayScreen)이 쥔다.
    internal sealed class FormulasIndexScreen : GameplayScreen
    {
        readonly GameplayWorldContext _world;
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 원작 selectedEntry 자리의 제조식 ID다. 원작 OnOpen처럼 열 때마다 비운다. 화면 상태라 저장하지 않는다.
        string? _selected;

        internal FormulasIndexScreen(GameplayWorldContext world)
        {
            _world = world;
        }

        // 원작 FormulasIndexView.Show와 OnOpen이다. 다른 화면 닫기는 부르는 쪽(GameplayScreens)이 먼저 한다.
        // 공용 HUD가 이 월드에 붙어 있지 않거나 입력 차단을 못 받으면(정리 중) 열지 않는다. 호출자: GameplayScreens.ShowFormulasIndex.
        internal void Show()
        {
            // 연결과 세션 확인, 다시 열기 전 닫기, 입력 차단은 다른 화면과 같은 순서라 공통 바탕이 맡는다.
            if (!TryBeginShow(_world, out GameSessionManager? session)) return;
            _selected = null;
            OriginalFormulasIndexView view = _world.Hud.FormulasIndex;
            Action<string> select = Select;
            Action close = Close, redraw = Redraw;
            view.EntrySelected += select;
            _subscriptions.Add(() => view.EntrySelected -= select);
            view.CloseRequested += close;
            _subscriptions.Add(() => view.CloseRequested -= close);
            // 원작 CostDisplay는 돈과 소지품이 바뀌면 배경색을 다시 칠한다. 칸과 상세도 해금, 로캘과 맞도록 같은 알림에 모두 다시 그린다.
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

        // 원작 OnClose다. 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다. 원작 FormulasIndexView는 TryQuit을 바꾸지 않아
        // ESC도 공통 바탕의 TryClose로 이 닫기를 부른다.
        internal override void Close()
        {
            if (!IsOpen) return;
            _subscriptions.Clear();
            _selected = null;
            ReleaseInput();
            if (_world.PresentationAttached) _world.Hud.FormulasIndex.Hide();
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 RefreshDetails와 FormulasDetailsDisplay.Setup의 갈래다. 해금 여부는 제조 소유자가 정한다.
        internal static FormulaIndexDetail Detail(CraftingManager crafting, CraftingFormula? formula)
        {
            if (formula == null || formula.ResultId < 0) return FormulaIndexDetail.Nothing;
            return crafting.IsFormulaUnlocked(formula.Id) ? FormulaIndexDetail.Formula : FormulaIndexDetail.Unknown;
        }

        // 원작 OnEntryClicked다. 원작처럼 같은 칸을 다시 눌러도 선택을 풀지 않는다. 칸은 Redraw가 제조식 모음에서만 만들고,
        // 목록에 없는 선택은 Redraw가 비운다.
        void Select(string formulaId)
        {
            if (!IsOpen) return;
            _selected = formulaId;
            Redraw();
        }

        // 원작 OnOpen의 칸 채우기와 RefreshDetails다. 칸 순서는 원작 CraftingFormulaCollection.Entries 순서이고, 원작처럼 hideInIndex
        // 제조식은 목록에서 뺀다. 원작 체험판 lockInDemo 제외는 제품이 체험판이 아니라 옮기지 않는다. 씬 정리 중이라 세션이 없으면 기록 없이 그리지 않는다.
        void Redraw()
        {
            if (!IsOpen || !_world.TrySession(out GameSessionManager? session)) return;
            OriginalFormulasIndexView view = _world.Hud.FormulasIndex;
            OriginalGameplayCatalog catalog = _world.Catalog;
            CraftingManager crafting = GameManager.Crafting;
            IReadOnlyList<CraftingFormula> formulas = GameplayDataSettings.CraftingFormulas.Formulas;
            CraftingFormula? selected = null;
            view.BeginEntries();
            for (int index = 0; index < formulas.Count; index++)
            {
                CraftingFormula formula = formulas[index];
                // 원작 FormulasIndexView처럼 hideInIndex 제조식은 목록에 넣지 않는다.
                if (formula.HideInIndex) continue;
                bool unlocked = crafting.IsFormulaUnlocked(formula.Id);
                // 원작 Meta(결과 아이템 메타데이터)다. 해금하지 않은 칸은 이름과 아이콘을 쓰지 않아 찾지 않는다.
                OriginalItemDefinition? result = null;
                if (unlocked) catalog.TryGet(formula.ResultId, out result);
                // 원작 DisplayName처럼 비어 있는 이름 키는 번역을 찾지 않는다.
                view.AddEntry(formula.Id, formula.ResultId >= 0, unlocked, result != null ? GameLocalization.TextOrEmpty(result.NameKey) : "",
                    formula.ResultAmount, result != null ? result.Icon : null);
                if (selected == null && formula.Id == _selected) selected = formula;
            }
            view.EndEntries();
            if (selected == null) _selected = null;
            DrawDetails(view, session, catalog, crafting, selected);
        }

        // 원작 FormulasDetailsDisplay.SetupFormula와 RefreshContent다. 결과가 카탈로그에 없으면 이름, 설명, 아이콘을 비운 채 비용만 보인다.
        void DrawDetails(OriginalFormulasIndexView view, GameSessionManager session, OriginalGameplayCatalog catalog, CraftingManager crafting,
            CraftingFormula? formula)
        {
            FormulaIndexDetail detail = Detail(crafting, formula);
            if (detail == FormulaIndexDetail.Nothing)
            {
                view.ShowNothing();
                return;
            }
            if (detail == FormulaIndexDetail.Unknown)
            {
                view.ShowUnknown();
                return;
            }
            CraftingFormula shown = formula!;
            catalog.TryGet(shown.ResultId, out OriginalItemDefinition? result);
            // 원작 DisplayName과 Description처럼 비어 있는 키는 번역을 찾지 않는다.
            view.ShowFormula(result != null ? GameLocalization.TextOrEmpty(result.NameKey) : "",
                result != null ? GameLocalization.TextOrEmpty(result.DescriptionKey) : "", result != null ? result.Icon : null);
            // 비용 표시는 제조 명령과 같은 세션 기준 소유 출처(계좌, 가방, 기지 창고, 펫 가방)로 공용 도우미가 그린다.
            CostDisplayDrawing.Draw(view.Cost, session, GameManager.Economy, catalog, shown.Cost, ItemSourceRules.Owned(session));
        }
    }
}
