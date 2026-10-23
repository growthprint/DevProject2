#nullable enable
using System.Collections.Generic;

namespace Game.App
{
    // 제조 소유자(CraftingManager), 경제, 세션 소지품에 걸친 제조와 설계도 등록 명령이다(원작 CraftingManager.Craft,
    // FormulasRegisterView.OnSubmitButtonClicked). 소유자를 모두 확인한 뒤 차례로 적용한다. 메인 스레드에서만 부른다.
    internal static partial class GameplayCommands
    {
        // 원작이 제조 결과 아이템의 FromInfoKey에 넣는 출처 키다. 아이템 저장 값(fromInfo)이라 바꾸지 않는다.
        internal const string _craftingSource = "UI_Crafting";

        // 원작 Craft다. 원작은 해금과 장소 태그를 화면만 거르지만 명령도 같은 조건을 본다. 비용은 원작 Cost.Pay 기본값처럼 계좌와 현금을 쓰고,
        // 아이템은 가방, 기지 창고, 펫 가방에서 낸다. 결과는 원작 Return(toPlayerInventory: true)처럼 소지품, 창고, 창고 임시 보관함 순서로 간다.
        // 어느 확인이든 실패하면 기록 없이 false이고 아무것도 바뀌지 않는다. 호출자: CraftScreen(제조 버튼).
        internal static bool TryCraft(GameSessionManager session, EconomyManager economy, CraftingManager crafting, OriginalGameplayCatalog catalog,
            string formulaId, string stationTag)
        {
            if (!crafting.AcceptsCommands || !crafting.IsFormulaUnlocked(formulaId)) return false;
            if (!GameplayDataSettings.CraftingFormulas.TryGet(formulaId, out CraftingFormula? formula) ||
                !CraftingManager.MatchesStation(formula, stationTag)) return false;
            // 비용은 지금 씬의 소유 출처에서 낸다. 레이드 중에는 창고 아이템을 쓰지 않는다.
            if (!TryPay(session, economy, formula.Cost, ItemSourceRules.Owned(session), true)) return false;
            // 지불이 같은 관문을 통과했고 넘친 결과는 임시 보관함이 받으므로 지급은 늘 성공한다. 지불로 빈 칸도 결과가 쓴다.
            // 원작 Craft는 결과 아이템마다 FromInfoKey를 UI_Crafting으로 정한다(CraftingManager.cs:120). 결과에 돈이 없어 계좌는 건드리지 않는다.
            session.Inventory.TryDeliver(catalog, new[] { (formula.ResultId, formula.ResultAmount) }, ItemDelivery.PlayerThenStorage,
                _craftingSource, out _);
            return true;
        }

        // 원작 등록 제출이다. 칸 태그(Formula와 등록대 추가 태그)가 맞고, 가방이나 기지 창고의 맨 위 칸에 있고, 그 종류의 제조식 ID가 있고,
        // 아직 해금되지 않았을 때만 그 아이템을 빼고 해금한다. 원작 결함 그대로 없는 제조식을 가리키는 설계도도 빼지만 아무것도 해금하지 않는다.
        // unlocked는 원작 강한 알림(UI_Formulas_RegisterSucceedFormat)의 제조식이고, 없는 제조식이면 null이다. 원작은 이때 오류 로그만 남긴다.
        // 거절되면 기록 없이 false이고 아무것도 바뀌지 않는다. 원작도 이미 해금된 설계도 제출은 아무 일도 하지 않는다. 호출자: FormulaRegisterScreen.
        internal static bool TryRegisterFormula(GameSessionManager session, CraftingManager crafting, OriginalGameplayCatalog catalog,
            string itemId, IReadOnlyList<string> stationTags, out CraftingFormula? unlocked)
        {
            unlocked = null;
            if (!crafting.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = session.Snapshot();
            ItemInstanceState? item = snapshot.Campaign.Gameplay.Inventory.Items?.Find(itemId);
            if (item == null) return false;
            // 저장 읽기의 카탈로그 검사를 지난 소유 그래프의 아이템이라 정의가 늘 있다.
            OriginalItemDefinition definition = catalog.DefinitionOf(item.OriginalItemId);
            if (!CraftingManager.IsRegistrable(definition, stationTags)) return false;
            CraftingFormulaCollection formulas = GameplayDataSettings.CraftingFormulas;
            if (!formulas.TryGetFormulaOfItem(definition.Id, out string formulaId) || formulaId.Length == 0 ||
                crafting.IsFormulaUnlocked(formulaId)) return false;
            // 원작 등록 창은 가방과 PlayerStorage(기지에만 있음)만 보이고 펫 가방은 보이지 않는다. 등록 창들이 함께 쓰는 출처 규칙이다.
            ItemSources sources = ItemSourceRules.BagAndBaseStorage(snapshot.SceneId);
            if (!session.Inventory.TryRemoveOwned(itemId, sources, out _)) return false;
            if (!formulas.TryGet(formulaId, out CraftingFormula? formula)) return true;
            // 관문과 아직 잠김은 위에서 봤고, 기본 해금은 불러올 때 늘 집합에 들어가 거기서 걸러지므로 정의가 있으면 바로 연다.
            crafting.Unlock(formulaId);
            unlocked = formula;
            return true;
        }
    }
}
