#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원작 DifficultySelection.CheckUnlocked의 선택 제한은 난이도 소유자의 현재 규칙과 슬롯 밖 공통 파일의 분노 해금을 함께 본다.
    // 두 소유자에 걸친 규칙이라 여기 두고, 준비 씬 화면의 잠금 표시와 SceneFlow의 선택·재설정 명령이 같은 판정을 쓴다.
    internal static partial class GameplayCommands
    {
        // firstSelection은 준비 단계의 첫 선택이다. 그때는 현재 규칙을 보지 않고 분노만 공통 파일 해금을 본다.
        internal static bool CanSelectDifficulty(DifficultyRules difficulty, GlobalProgress global, DifficultyRuleIndex rule, bool firstSelection) =>
            DifficultyRules.CanSelect(rule, firstSelection, difficulty.CurrentRuleIndex, global.RageUnlocked);

        // 첫 선택 후보다. 원작 화면처럼 잠긴 규칙은 고를 수 없고, 통과하면 인트로로의 일반 진행 후보를 만든다.
        internal static bool TryPrepareDifficultySelection(SessionCampaignCommands campaign, DifficultyRules difficulty, GlobalProgress global,
            DifficultyState chosen, [NotNullWhen(true)] out GameSessionSnapshot? candidate)
        {
            candidate = null;
            return CanSelectDifficulty(difficulty, global, chosen.RuleIndex, true) && campaign.TryPrepareDifficultySelection(out candidate);
        }

        // 기지 뒤 재설정 확정 후보다. 현재 규칙으로 막힌 난이도는 거절하고 아무것도 바꾸지 않는다. 통과하면 진행 단계를 그대로 둔 기지 복귀 후보다.
        internal static bool TryPrepareDifficultyReselectReturn(SessionCampaignCommands campaign, DifficultyRules difficulty, GlobalProgress global,
            DifficultyState chosen, [NotNullWhen(true)] out GameSessionSnapshot? candidate)
        {
            candidate = null;
            return CanSelectDifficulty(difficulty, global, chosen.RuleIndex, false) && campaign.TryPrepareDifficultyReselectReturn(out candidate);
        }
    }
}
