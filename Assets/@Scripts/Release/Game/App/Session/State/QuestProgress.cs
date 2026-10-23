#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 퀘스트 완료와 보상 수령을 다른 상태로 구분한다.
    public enum QuestStatus { Active = 0, Completed = 1, RewardClaimed = 2 }

    // 퀘스트 목표를 배열 순서 대신 명시한 ID로 식별한다.
    public sealed class QuestObjectiveProgress
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public QuestObjectiveProgress(string objectiveId, int count)
        {
            ObjectiveId = objectiveId;
            Count = count;
        }

        // TryCreate가 쓰는 검사다. 목표 ID가 있고 누적 수량은 0 이상이어야 한다.
        static bool Check(string? objectiveId, int count) => !string.IsNullOrWhiteSpace(objectiveId) && count >= 0;

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려준다.
        public static bool TryCreate(string objectiveId, int count, [NotNullWhen(true)] out QuestObjectiveProgress? value)
        {
            value = Check(objectiveId, count) ? new QuestObjectiveProgress(objectiveId, count) : null;
            return value != null;
        }

        public string ObjectiveId { get; }
        public int Count { get; }
    }

    // 퀘스트 상태와 목표 목록을 독립적인 불변 값으로 묶는다.
    public sealed class QuestProgress
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        // 호출자 배열을 공유하지 않도록 두 목록을 늘 복사하고, null 목록은 빈 목록으로 받는다.
        public QuestProgress(int originalQuestId, QuestStatus status, IReadOnlyList<QuestObjectiveProgress> objectives,
            IReadOnlyList<int>? claimedRewardIds=null)
        {
            OriginalQuestId = originalQuestId;
            Status = status;
            Objectives = ReadOnlyLists.CopyOrEmpty(objectives);
            ClaimedRewardIds = ReadOnlyLists.CopyOrEmpty(claimedRewardIds);
        }

        // TryCreate가 쓰는 검사다. 복사는 생성자가 한 번만 한다. 목표 ID와 받은 보상 ID는 중복될 수 없고,
        // 보상 ID는 0 이상이어야 한다. 진행 중(Active)인 퀘스트는 받은 보상이 없어야 한다.
        static bool Check(int originalQuestId, QuestStatus status, IReadOnlyList<QuestObjectiveProgress>? objectives,
            IReadOnlyList<int>? claimedRewardIds)
        {
            if (originalQuestId < 0 || status < QuestStatus.Active || status > QuestStatus.RewardClaimed) return false;
            if (!ReadOnlyLists.AreUnique(objectives, item => item.ObjectiveId)) return false;
            IReadOnlyList<int> claimed = claimedRewardIds ?? Array.Empty<int>();
            if (!SaveKeys.AreValidIds(claimed)) return false;
            return status != QuestStatus.Active || claimed.Count == 0;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려준다.
        // 선택 인수는 out 인수 앞에 둘 수 없어 claimedRewardIds를 필수로 받는다.
        public static bool TryCreate(int originalQuestId, QuestStatus status, IReadOnlyList<QuestObjectiveProgress> objectives,
            IReadOnlyList<int>? claimedRewardIds, [NotNullWhen(true)] out QuestProgress? value)
        {
            value = Check(originalQuestId, status, objectives, claimedRewardIds)
                ? new QuestProgress(originalQuestId, status, objectives, claimedRewardIds) : null;
            return value != null;
        }

        public int OriginalQuestId { get; }
        public QuestStatus Status { get; }
        public IReadOnlyList<QuestObjectiveProgress> Objectives { get; }
        // 원본 보상별로 받은 보상 ID다. 이 목록 없이 저장된 퀘스트도 Status가 RewardClaimed면 모두 받은 것으로 본다.
        public IReadOnlyList<int> ClaimedRewardIds { get; }
    }
}
