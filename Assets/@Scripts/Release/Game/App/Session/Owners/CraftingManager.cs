#nullable enable
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 CraftingManager처럼 해금한 제조식 ID를 소유한다. GameManager 프리팹 자식의 컴포넌트이고 GameManager.Crafting으로 찾는다.
    // 저장은 unlockedFormulas이고 Crafting 구역으로 쓴다. 정의는 GameplayDataSettings.CraftingFormulas가 갖는다. 해금은 되돌리지 않는다.
    // 제조와 설계도 등록은 GameplayCommands.Crafting, 특성 해금은 GameplayCommands.Perks가 부른다. 메인 스레드에서만 쓴다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed class CraftingManager : SessionSectionOwner
    {
        // 원작 OnFormulaUnlocked 자리의 신호다. 파일 읽기와 새 해금 뒤에만 낸다. 구독자는 IsFormulaUnlocked를 다시 읽는다.
        public static event Action? Changed;

        // 원작 등록 칸이 늘 요구하는 태그(FormulasRegisterView.formulaTag)다. 원본 Tag 자산 이름이다.
        const string _formulaTag = "Formula";

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => Changed = null;

        // 원작 unlockedFormulaIDs다. 불러올 때 기본 해금을 더해 정렬하고 새 해금은 끝에 붙여, 저장 순서도 원작과 같다.
        // 정의에 없는 ID도 지우지 않고 다시 쓴다.
        readonly OrderedKeySet _unlocked = new OrderedKeySet();

        // 원작 Save처럼 기본 해금까지 담은 목록 전체를 쓴다. 목록은 계속 바뀌므로 저장마다 새 배열로 넘긴다.
        private protected override void WriteSection(GameSaveData data) => data.SetUnlockedFormulas(_unlocked.ToArray());

        // 원작 Load다. 저장 목록(null은 빈 목록)에 기본 해금을 더하고 정렬한 뒤 알린다. 저장 목록은 코덱이 빈 ID와 중복을 거절한 값이다.
        // 원작 Sort의 문화권 비교 대신 서수 비교를 쓴다. 숫자 ID만 있는 원작 데이터에서는 같은 순서다.
        private protected override void ReadSection(GameSaveData data)
        {
            _unlocked.Clear();
            IReadOnlyList<string>? saved = data.UnlockedFormulas;
            if (saved != null)
                for (int index = 0; index < saved.Count; index++) _unlocked.Add(saved[index]);
            IReadOnlyList<CraftingFormula> formulas = GameplayDataSettings.CraftingFormulas.Formulas;
            for (int index = 0; index < formulas.Count; index++)
                if (formulas[index].UnlockByDefault) _unlocked.Add(formulas[index].Id);
            _unlocked.SortOrdinal();
            ObserverDispatch.InvokeEach(Changed);
        }

        // 원작 IsFormulaUnlocked다. 빈 ID는 거짓이다. 호출자: 제조와 등록 명령, 설계도 등록 화면, 제조식 해금 표시 조건.
        internal bool IsFormulaUnlocked(string formulaId) => !string.IsNullOrEmpty(formulaId) && _unlocked.Contains(formulaId);

        // 원작 ItemUtilities.IsRegistered의 제조식 부분이다. 그 아이템 종류의 설계도 제조식이 해금됐으면 참이다.
        // 없는 제조식을 가리키는 설계도는 해금될 수 없어 늘 거짓이다. 호출자: 상세 화면 "등록됨" 표시, 설계도 등록 화면.
        internal bool IsItemRegistered(int originalItemId) =>
            GameplayDataSettings.CraftingFormulas.TryGetFormulaOfItem(originalItemId, out string formulaId) && IsFormulaUnlocked(formulaId);

        // 원작 CraftView 목록이다. 해금 순서가 아니라 제조식 모음 순서이고, 장소 태그는 InteractCrafter.FilterCraft로 거른다.
        // 화면을 열고 다시 그릴 때만 부르므로 새 목록을 돌려준다. 호출자: CraftScreen.
        internal IReadOnlyList<CraftingFormula> UnlockedFormulas(string requireTag)
        {
            var result = new List<CraftingFormula>();
            IReadOnlyList<CraftingFormula> formulas = GameplayDataSettings.CraftingFormulas.Formulas;
            for (int index = 0; index < formulas.Count; index++)
                if (_unlocked.Contains(formulas[index].Id) && MatchesStation(formulas[index], requireTag)) result.Add(formulas[index]);
            return result;
        }

        // 원작 InteractCrafter.FilterCraft다. 장소 태그가 비었거나 공백이면 모든 제조식이 맞다.
        internal static bool MatchesStation(CraftingFormula formula, string requireTag)
        {
            if (string.IsNullOrWhiteSpace(requireTag)) return true;
            IReadOnlyList<string> tags = formula.Tags;
            for (int index = 0; index < tags.Count; index++)
                if (string.Equals(tags[index], requireTag, StringComparison.Ordinal)) return true;
            return false;
        }

        // 원작 등록 칸의 CanPlug 태그 판정이다. Formula와 등록대 추가 태그를 모두 가져야 하고 제외 태그는 없다.
        // 등록 명령과 등록 화면의 강조, 칸 넣기가 같은 판정을 쓴다. 호출자: GameplayCommands.TryRegisterFormula, FormulaRegisterScreen.
        internal static bool IsRegistrable(OriginalItemDefinition definition, IReadOnlyList<string> stationTags)
        {
            if (!definition.HasTag(_formulaTag)) return false;
            for (int index = 0; index < stationTags.Count; index++)
                if (!definition.HasTag(stationTags[index])) return false;
            return true;
        }

        // 원작 UnlockFormula의 추가다. 등록 명령이 같은 호출에서 관문, 정의 있음, 아직 잠김을 확인한 ID만 넘긴다.
        // 기본 해금은 불러올 때 집합에 들어가 그 확인에서 걸러지므로 늘 새로 더해진다.
        internal void Unlock(string formulaId)
        {
            _unlocked.Add(formulaId);
            ObserverDispatch.InvokeEach(Changed);
        }

        // 원작 UnlockFormula 특성 동작이다. requirePerk가 "<트리 ID>/<특성 이름>"인 제조식 중 아직 없는 것만 더하고, 더했으면 한 번 알린다.
        // 관문이 닫혔으면 기록 없이 넘긴다. 두 번 불러도 같다. 호출자: GameplayCommands.TryConfirmPerk, RestorePerkEffects.
        // 키는 두 호출자 모두 PerkKey가 만든 "트리/이름"이라 빈 값이 오지 않는다.
        internal void UnlockPerkFormulas(string perkKey)
        {
            if (!AcceptsCommands) return;
            bool added = false;
            IReadOnlyList<CraftingFormula> formulas = GameplayDataSettings.CraftingFormulas.Formulas;
            for (int index = 0; index < formulas.Count; index++)
            {
                CraftingFormula formula = formulas[index];
                if (formula.UnlockByDefault || !string.Equals(formula.RequirePerk, perkKey, StringComparison.Ordinal)) continue;
                // 이미 해금한 제조식은 더하지 않으므로, 새로 더한 것이 있을 때만 알린다.
                if (_unlocked.Add(formula.Id)) added = true;
            }
            if (added) ObserverDispatch.InvokeEach(Changed);
        }
    }
}
