#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.App
{
    // 원작 Task.IsFinished, Reward.Claimed, Condition.Evaluate의 판정 규칙이다. 진행 값은 저장 퀘스트 기록에서 읽기만 하고,
    // 값을 바꾸는 일은 QuestManager가 맡는다. 할당이 없도록 목록을 번호로 훑는다.
    internal static class QuestRules
    {
        // 저장 목표 ID는 원작 과제 ID의 10진 문자열이다. 저장 파일이 원작 과제 ID로 진행을 찾는다.
        internal static string ObjectiveId(int taskId) => taskId.ToString(CultureInfo.InvariantCulture);

        // 원작 건설과 비콘 과제는 저장 값 없이 소유자에게 매번 묻는다. 그 답은 QuestManager의 Progress 파일이 준다.
        internal static bool IsLive(QuestTaskKind kind) => kind == QuestTaskKind.ConstructBuilding || kind == QuestTaskKind.UnlockBeacon;

        // 저장 목표로 끝나는 과제다. 진행 안 됨 과제는 원작 미니게임과 우주선 확인이라 범위 안에서 끝나지 않는다.
        internal static bool IsStored(QuestTaskKind kind) => kind != QuestTaskKind.NotProgressed && !IsLive(kind);

        // 원작 제출, 처치, 사용 과제는 센 수를 요구 수와 비교하고, 나머지 저장 과제는 원작 bool을 0과 1로 담는다.
        internal static int TargetCount(OriginalQuestTaskDefinition task) => IsCountedKind(task.Kind) ? task.RequiredCount : 1;

        // 원작에서 수를 세는 과제 종류다. 제출, 처치, 사용 과제가 여기에 든다.
        static bool IsCountedKind(QuestTaskKind kind) =>
            kind == QuestTaskKind.SubmitItems || kind == QuestTaskKind.KillCount || kind == QuestTaskKind.UseItem;

        // 원작처럼 요구 수에 닿으면 끝난다. 원작 forceFinish와 실시간 판정은 QuestManager가 이 판정 앞에서 본다.
        internal static bool IsCountFinished(OriginalQuestTaskDefinition task, int count) => IsStored(task.Kind) && count >= TargetCount(task);

        // 기록에 그 과제 목표가 없으면 0이다. 매 프레임 불릴 수 있어 목표 ID 문자열을 만들지 않고 숫자로 비교한다.
        internal static int CountOf(QuestProgress record, int taskId)
        {
            IReadOnlyList<QuestObjectiveProgress> objectives = record.Objectives;
            for (int index = 0; index < objectives.Count; index++)
                if (IsObjective(objectives[index].ObjectiveId, taskId)) return objectives[index].Count;
            return 0;
        }

        // 그 과제 목표만 새 수로 바꾼 목록이다. 목표가 없으면 끝에 붙인다. 목표 ID가 겹치지 않으므로 기록의 신뢰 생성자에 넘긴다.
        internal static IReadOnlyList<QuestObjectiveProgress> WithCount(QuestProgress record, int taskId, int count)
        {
            IReadOnlyList<QuestObjectiveProgress> objectives = record.Objectives;
            var changed = new List<QuestObjectiveProgress>(objectives.Count + 1);
            bool replaced = false;
            for (int index = 0; index < objectives.Count; index++)
            {
                QuestObjectiveProgress objective = objectives[index];
                bool match = !replaced && IsObjective(objective.ObjectiveId, taskId);
                changed.Add(match ? new QuestObjectiveProgress(objective.ObjectiveId, count) : objective);
                replaced |= match;
            }
            if (!replaced) changed.Add(new QuestObjectiveProgress(ObjectiveId(taskId), count));
            return changed;
        }

        // 받은 보상 ID 없이 RewardClaimed로 저장된 기록도 모든 보상을 받은 것으로 본다.
        internal static bool IsRewardClaimed(QuestProgress record, int rewardId) =>
            record.Status == QuestStatus.RewardClaimed || Contains(record.ClaimedRewardIds, rewardId);

        // 원작 Quest.AreRewardsClaimed다. 보상이 모두 받은 목록에 있으면 참이다.
        internal static bool AreRewardsClaimed(OriginalQuestDefinition quest, IReadOnlyList<int> claimed)
        {
            IReadOnlyList<OriginalQuestRewardDefinition> rewards = quest.Rewards;
            for (int index = 0; index < rewards.Count; index++)
                if (!Contains(claimed, rewards[index].Id)) return false;
            return true;
        }

        // 원작 MeetsPrerequisit의 prerequisit 조건이다. 하나라도 거짓이면 받을 수 없다.
        internal static bool AreConditionsMet(IReadOnlyList<OriginalQuestConditionDefinition> conditions, Func<string, int> killCount)
        {
            for (int index = 0; index < conditions.Count; index++)
                if (!IsConditionMet(conditions[index], killCount)) return false;
            return true;
        }

        // 원작 RequireEnemyKilled.Evaluate처럼 적 nameKey의 영구 처치 수가 요구 수 이상이면 참이고, 적 프리셋이 비어 있던 빈 키는 거짓이다.
        // RequireSaveDataBool은 범위 밖 폭풍 지역 저장 값을 보고 나머지 형식도 판정할 소유자가 없어 거짓으로 둔다.
        static bool IsConditionMet(OriginalQuestConditionDefinition condition, Func<string, int> killCount) =>
            condition.OriginalTypeName == "RequireEnemyKilled" && condition.Key.Length > 0 && killCount(condition.Key) >= condition.Value;

        static bool IsObjective(string objectiveId, int taskId) =>
            int.TryParse(objectiveId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int id) && id == taskId;

        static bool Contains(IReadOnlyList<int> values, int value)
        {
            for (int index = 0; index < values.Count; index++)
                if (values[index] == value) return true;
            return false;
        }
    }
}
