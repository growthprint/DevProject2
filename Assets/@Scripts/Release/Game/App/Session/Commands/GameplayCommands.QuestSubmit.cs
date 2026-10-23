#nullable enable
using System;

namespace Game.App
{
    // 원작 SubmitItems와 QuestTask_SubmitMoney의 제출 버튼이다. 과제, 소지품, 계좌는 소유자가 달라 과제를 먼저 확인하고,
    // 아이템이나 돈을 뺀 뒤 같은 호출에서 과제에 기록한다. 화면은 의도만 보내고 이 명령이 결과를 확정한다.
    internal static partial class GameplayCommands
    {
        // 원작 SubmitItems.Interact를 끝날 때까지 되풀이한 것과 같다. 원작 FindAllBelongsToPlayer처럼 창고(기지에서만), 가방, 펫 가방 순서로
        // 남은 수까지만 내고 낸 수를 더한다. 진행 중이 아니거나, 끝났거나, 낼 아이템이 없거나, 관문이 닫혔으면 기록 없이 false다.
        internal static bool TrySubmitQuestItems(GameSessionManager session, QuestManager quests, int questId, int taskId)
        {
            if (!quests.TryGetOpenTask(questId, taskId, QuestTaskKind.SubmitItems, out OriginalQuestTaskDefinition? task)) return false;
            int remaining = task.RequiredCount - quests.TaskCount(questId, taskId);
            if (remaining <= 0) return false;
            // 원작 FindAllBelongsToPlayer처럼 지금 씬의 소유 출처에서 낸다. 창고는 기지에서만 본다.
            ItemSources sources = ItemSourceRules.Owned(session);
            if (!session.Inventory.TrySubmitOwned(task.ItemId, remaining, sources, out int submitted)) return false;
            // 같은 호출에서 열린 과제를 확인했고 낸 수는 남은 수 이하다. 세션 관문이 열렸으면 런타임 명령 관문도 열려 있고,
            // 소지품 확정 알림의 구독자는 관문을 닫지 않으므로 낸 수는 늘 더해진다.
            quests.TryAddTaskCount(questId, taskId, submitted);
            return true;
        }

        // 원작 QuestTask_SubmitMoney.Interact의 new Cost(money).Pay()다. 원작 기본 결제처럼 계좌를 먼저 쓰고 모자란 만큼 현금으로 낸다.
        // 낼 수 없거나 관문이 닫혔으면 TryPay가 아무것도 바꾸지 않고, 이 명령도 기록 없이 false다.
        internal static bool TrySubmitQuestMoney(GameSessionManager session, EconomyManager economy, QuestManager quests, int questId,
            int taskId)
        {
            if (!quests.TryGetOpenTask(questId, taskId, QuestTaskKind.SubmitMoney, out OriginalQuestTaskDefinition? task)) return false;
            var cost = new Cost(task.Money, Array.Empty<Cost.ItemEntry>());
            // 현금 몫은 지금 씬의 소유 출처에서 낸다. 레이드 중에는 창고 현금을 쓰지 않는다.
            if (!TryPay(session, economy, cost, ItemSourceRules.Owned(session), true)) return false;
            // 같은 호출에서 열린 과제를 확인했고, 지불 확정 알림의 구독자는 관문을 닫지 않으므로 과제는 늘 끝난다.
            quests.TryFinishTask(questId, taskId);
            return true;
        }
    }
}
