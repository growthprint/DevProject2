#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 QuestManager처럼 퀘스트 진행, 완료 기록, 본 적 있음 목록을 모든 씬에 걸쳐 소유한다. 저장은 Quests 구역 하나다.
    // GameManager 프리팹 자식의 컴포넌트이고 GameManager.Quests로 찾는다. 과제 종류별 알림은 Combat과 Progress 부분 파일이 받는다.
    // 다른 소유자 값은 런타임 구성이 넘긴 조회로만 읽는다. 메인 스레드에서만 쓴다. 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed partial class QuestManager : SessionSectionOwner
    {
        // 값이 바뀌었다는 신호만 보낸다. 수락, 완료, 과제 진행, 보상 받음, 본 적 있음, 불러오기 때 낸다. 구독자는 조회 멤버를 직접 읽는다.
        public static event Action? Changed;
        // 원작 Quest.onQuestActivated다. 과제 생성기와 수락 대화가 듣는다.
        public static event Action<int>? QuestActivated;
        // 원작 Quest.onQuestCompleted다. 퀘스트 완료로 여는 재능 해금 같은 다른 소유자가 듣는다.
        public static event Action<int>? QuestCompleted;
        // 원작 QuestManager.OnTaskFinishedEvent(퀘스트 ID, 과제 ID)다. 과제 완료 알림과 창구 머리 위 표시가 듣는다.
        public static event Action<int, int>? TaskFinished;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            Changed = null;
            QuestActivated = null;
            QuestCompleted = null;
            TaskFinished = null;
        }

        // 원작 QuestCollection과 관계 그래프를 담은 순수 데이터다. 프리팹 참조가 없어 씬과 상관없이 읽는다.
        [SerializeField] OriginalQuestCatalog _catalog = null!;

        // 아래는 런타임 구성이 잇는 다른 소유자 조회다. 잇기 전에는 레벨 0, 처치 수 0, 아이템 0개로 보고, 불러온 뒤 보상 받기는 하지 않는다.
        Func<int> _level = () => 0;
        Func<string, int> _killCount = _ => 0;
        Func<int, long> _submitCount = _ => 0;
        Action _claimAfterLoad = () => { };
        // 진행 중과 완료 퀘스트 기록이다. 원작 활성 목록처럼 수락 순서로 붙고, 완료하면 원작 기록 목록처럼 끝으로 간다.
        // 과제 진행과 받음 표시는 같은 자리의 값을 바꾸므로 번호로 훑는 쪽은 그 사이에도 같은 순서를 본다.
        readonly List<QuestProgress> _records = new List<QuestProgress>();
        // 원작 everInspectedQuest다. 창구 목록에서 그 퀘스트를 고르면 붙는다.
        readonly List<int> _inspected = new List<int>();
        // 원작 퀘스트별 needInspection 저장 값이 참인 기록 ID다. 원작은 상세를 보거나 보상을 모두 받으면 거짓으로 둔다.
        readonly HashSet<int> _needInspection = new HashSet<int>();
        // 정의 없는 저장 퀘스트를 한 번만 보고하려고 보고한 ID를 남긴다.
        readonly HashSet<int> _reportedMissing = new HashSet<int>();
        // 원작은 불러오기에서 완료 퀘스트의 자동 보상을 바로 받는다. 파일 읽기는 값을 바꾸지 않으므로 명령을 받는 첫 프레임으로 미룬다.
        bool _claimPending;

        // 과제 정의를 찾는 데이터다. 창구 화면과 과제 부분 파일이 정의 목록과 이름 키를 여기서 읽는다.
        internal OriginalQuestCatalog Catalog => _catalog;

        // 모든 퀘스트 기록이다. 과제 진행은 같은 자리를 바꾸므로 foreach 대신 번호로 훑는다.
        public IReadOnlyList<QuestProgress> Records => _records;

        // GameRuntime이 구성 때 한 번 부른다. 가용성은 레벨과 처치 수를, 확인 필요 표시는 제출 아이템 수를 읽는다.
        // claimAfterLoad는 불러온 뒤 명령을 받는 첫 프레임에 한 번 돈다(GameplayCommands.RestoreQuestRewardsAfterLoad).
        internal void ConfigureSessionLinks(Func<int> level, Func<string, int> killCount, Func<int, long> submitCount, Action claimAfterLoad)
        {
            _level = level;
            _killCount = killCount;
            _submitCount = submitCount;
            _claimAfterLoad = claimAfterLoad;
        }

        // 불러온 뒤 자동 보상 받기를 관문이 열린 첫 프레임에 한 번만 한다. 그 전에는 매 프레임 표시 하나만 본다.
        void Update()
        {
            if (!_claimPending || !AcceptsCommands) return;
            _claimPending = false;
            _claimAfterLoad();
        }

        // 원작 IsQuestFinished다. 완료 상태와 보상을 다 받은 상태가 모두 끝난 퀘스트다.
        public bool IsFinished(int questId)
        {
            int index = IndexOf(questId);
            return index >= 0 && _records[index].Status != QuestStatus.Active;
        }

        // 원작 IsQuestActive다. 아래 Find는 창구 상세와 과제 부분 파일이 기록 하나를 읽는 경로다.
        public bool IsActive(int questId) => IsActiveAt(IndexOf(questId));

        public QuestProgress? Find(int questId)
        {
            int index = IndexOf(questId);
            return index >= 0 ? _records[index] : null;
        }

        // 아래 둘은 원작 AreQuestFinished와 AreQuestsActive다. 건설 조건, 재능, 씬 조건이 읽는다.
        public bool AreFinished(IReadOnlyList<int> ids)
        {
            for (int index = 0; index < ids.Count; index++)
                if (!IsFinished(ids[index])) return false;
            return true;
        }

        public bool AreActive(IReadOnlyList<int> ids)
        {
            for (int index = 0; index < ids.Count; index++)
                if (!IsActive(ids[index])) return false;
            return true;
        }

        // 원작 IsQuestAvaliable과 MeetsPrerequisit이다. 기록이 없고, 레벨이 되며, 관계 그래프의 일반 노드가 있고, 선행이 모두 끝났고,
        // 조건이 모두 참이어야 한다. 원작처럼 씬은 보지 않는다. 데모 잠금은 정식판이라 보지 않는다.
        public bool IsAvailable(int questId)
        {
            if (IndexOf(questId) >= 0 || !_catalog.TryGet(questId, out OriginalQuestDefinition? quest)) return false;
            if (quest.RequireLevel > _level() || !quest.InRelationGraph || !AreFinished(quest.PrerequisiteIds)) return false;
            return QuestRules.AreConditionsMet(quest.Conditions, _killCount);
        }

        // 원작 GetAllQuestsByQuestGiverID처럼 씬과 상관없이 창구 ID로 고르고 원작 QuestCollection 순서를 따른다. 호출마다 새 목록이다.
        public IReadOnlyList<int> AvailableFor(QuestGiverId giver)
        {
            var ids = new List<int>();
            IReadOnlyList<OriginalQuestDefinition> quests = _catalog.Quests;
            for (int index = 0; index < quests.Count; index++)
                if (quests[index].Giver == giver && IsAvailable(quests[index].Id)) ids.Add(quests[index].Id);
            return ids;
        }

        // 원작 GetActiveQuestsFromGiver와 GetHistoryQuestsFromGiver다. 원작 수락의 창구 덮어쓰기는 창구 화면이 늘 정의의 창구로 받아
        // 결과가 같으므로 저장하지 않고 정의의 창구로 고른다. 정의 없는 저장 퀘스트는 어느 창구에도 보이지 않는다. 호출마다 새 목록이다.
        public IReadOnlyList<int> ActiveFor(QuestGiverId giver) => RecordsFor(giver, true);

        public IReadOnlyList<int> FinishedFor(QuestGiverId giver) => RecordsFor(giver, false);

        // 원작 EverInspected다. 창구 화면의 "받을 수 있고 본 적 없음" 빨간 점이 읽는다.
        public bool EverInspected(int questId) => _inspected.Contains(questId);

        // 원작 Quest.NeedInspection이다. 진행 중이 아니면 본 적 없을 때만 참이다. 진행 중이면 과제가 모두 끝났거나,
        // 끝나지 않은 제출 과제의 아이템이 요구 수만큼 있거나, 저장된 확인 필요 값이 참이면 참이다.
        public bool NeedsInspection(int questId)
        {
            int index = IndexOf(questId);
            if (!IsActiveAt(index)) return !EverInspected(questId);
            if (_catalog.TryGet(questId, out OriginalQuestDefinition? quest)
                && (AreTasksFinished(quest, _records[index]) || AnySubmitReady(quest, _records[index]))) return true;
            return _needInspection.Contains(questId);
        }

        // 원작 AnyActiveQuestNeedsInspection이다. 그 창구의 진행 중 퀘스트만 본다.
        public bool AnyNeedsInspection(QuestGiverId giver)
        {
            for (int index = 0; index < _records.Count; index++)
            {
                QuestProgress record = _records[index];
                if (record.Status == QuestStatus.Active && IsGiver(record.OriginalQuestId, giver)
                    && NeedsInspection(record.OriginalQuestId)) return true;
            }
            return false;
        }

        // 원작 QuestGiver.RefreshInspectionIndicator다. 확인할 진행 퀘스트가 있거나 받을 퀘스트가 하나라도 있으면 머리 위 표시를 켠다.
        public bool ShowsGiverMarker(QuestGiverId giver)
        {
            if (AnyNeedsInspection(giver)) return true;
            IReadOnlyList<OriginalQuestDefinition> quests = _catalog.Quests;
            for (int index = 0; index < quests.Count; index++)
                if (quests[index].Giver == giver && IsAvailable(quests[index].Id)) return true;
            return false;
        }

        // 원작 Task.IsFinished다. forceFinish, 저장 수 판정, 실시간 판정(Progress 부분 파일의 IsLiveTaskFinished) 중 하나가 참이면 끝났다.
        // 기록이 없거나 정의에 없는 과제는 끝나지 않았다.
        public bool IsTaskFinished(int questId, int taskId)
        {
            int index = IndexOf(questId);
            if (index < 0 || !_catalog.TryGet(questId, out OriginalQuestDefinition? quest)
                || !quest.TryGetTask(taskId, out OriginalQuestTaskDefinition? task)) return false;
            return IsTaskFinished(_records[index], task);
        }

        // 저장된 제출, 처치, 사용 수다. 참거짓 과제는 0 또는 1이고 기록이 없으면 0이다.
        public int TaskCount(int questId, int taskId)
        {
            int index = IndexOf(questId);
            return index >= 0 ? QuestRules.CountOf(_records[index], taskId) : 0;
        }

        // 원작 AreTasksFinished다. 창구 화면의 완료 버튼과 받을 보상 표시가 읽는다.
        public bool AreTasksFinished(int questId)
        {
            int index = IndexOf(questId);
            return index >= 0 && _catalog.TryGet(questId, out OriginalQuestDefinition? quest) && AreTasksFinished(quest, _records[index]);
        }

        // 원작 Reward.Claimed다. 기록이 없으면 받지 않았다.
        public bool IsRewardClaimed(int questId, int rewardId)
        {
            int index = IndexOf(questId);
            return index >= 0 && QuestRules.IsRewardClaimed(_records[index], rewardId);
        }

        // 원작 ActivateQuest다. 창구 화면의 수락 버튼만 부른다. 가용성과 창구를 다시 확인하고 진행 중 기록을 만든다.
        // 실시간 과제는 저장 값이 없어 조회 때 판정되므로 따로 평가하지 않는다. 거절은 기록 없이 false다.
        public bool TryActivate(int questId, QuestGiverId giver)
        {
            if (!AcceptsCommands || !IsAvailable(questId)) return false;
            // 가용성 확인이 정의를 찾았으므로 늘 있다. 원작 시험 퀘스트 -1은 받을 수 없는 레벨이라 여기에 오지 않는다.
            _catalog.TryGet(questId, out OriginalQuestDefinition? quest);
            if (quest!.Giver != giver) return false;
            if (!QuestProgress.TryCreate(questId, QuestStatus.Active, Array.Empty<QuestObjectiveProgress>(), null,
                    out QuestProgress? record)) return false;
            _records.Add(record);
            ObserverDispatch.InvokeEachValue(QuestActivated, questId);
            ObserverDispatch.InvokeEach(Changed);
            return true;
        }

        // 원작 SetEverInspected다. 창구 목록에서 퀘스트를 고를 때 부른다. 이미 본 퀘스트도 참이고 아무것도 바꾸지 않는다.
        public bool TryMarkInspected(int questId)
        {
            if (!AcceptsCommands || !_catalog.TryGet(questId, out _)) return false;
            if (_inspected.Contains(questId)) return true;
            _inspected.Add(questId);
            ObserverDispatch.InvokeEach(Changed);
            return true;
        }

        // 원작 QuestViewDetails가 상세를 보일 때의 NeedInspection = false다. 저장된 확인 필요 값만 지우고 계산되는 확인 필요는 그대로다.
        public bool TryClearNeedInspection(int questId)
        {
            if (!AcceptsCommands) return false;
            if (_needInspection.Remove(questId)) ObserverDispatch.InvokeEach(Changed);
            return true;
        }

        // 원작 Quest.TryComplete다. 창구 화면의 완료 버튼만 부른다. 진행 중이고 과제가 모두 끝났을 때만 완료로 바꾸고,
        // 원작 기록 목록처럼 기록을 끝으로 옮긴다. 보상은 GameplayCommands의 보상 받기 명령이 따로 준다.
        public bool TryComplete(int questId)
        {
            if (!AcceptsCommands) return false;
            int index = IndexOf(questId);
            if (!IsActiveAt(index)) return false;
            if (!_catalog.TryGet(questId, out OriginalQuestDefinition? quest)) return false;
            QuestProgress record = _records[index];
            if (!AreTasksFinished(quest, record)) return false;
            _records.RemoveAt(index);
            _records.Add(new QuestProgress(questId, QuestStatus.Completed, record.Objectives));
            ObserverDispatch.InvokeEachValue(QuestCompleted, questId);
            ObserverDispatch.InvokeEach(Changed);
            return true;
        }

        // 과제 부분 파일이 처치, 사용, 제출 수를 더할 때 부른다. 진행 중 퀘스트의 끝나지 않은 저장 과제만 바꾸고
        // 결과는 0과 요구 수 사이로 자른다. 요구 수에 닿으면 TaskFinished를 낸다. 바뀐 것이 없으면 false다.
        internal bool TryAddTaskCount(int questId, int taskId, int delta)
        {
            if (delta == 0 || !TryGetOpenStoredTask(questId, taskId, out int index, out OriginalQuestTaskDefinition? task)) return false;
            int current = QuestRules.CountOf(_records[index], taskId);
            int target = QuestRules.TargetCount(task);
            long next = Math.Min(Math.Max((long)current + delta, 0), target);
            if (next == current) return false;
            SetCount(index, task, (int)next);
            return true;
        }

        // 과제 부분 파일이 이벤트, 위치, 탈출, 돈, 특성 과제를 끝낼 때 부른다. 진행 중 퀘스트의 끝나지 않은 저장 과제만 끝낸다.
        internal bool TryFinishTask(int questId, int taskId)
        {
            if (!TryGetOpenStoredTask(questId, taskId, out int index, out OriginalQuestTaskDefinition? task)) return false;
            SetCount(index, task, QuestRules.TargetCount(task));
            return true;
        }

        // 보상 받기 명령(GameplayCommands)이 지급 전에 본다. 완료 상태이고 정의에 있는 보상을 아직 받지 않았을 때만 참이다.
        // RewardClaimed 기록은 받은 ID 목록이 없어도 모두 받은 것이라 거짓이다.
        internal bool CanClaimReward(int questId, int rewardId)
        {
            if (!AcceptsCommands) return false;
            int index = IndexOf(questId);
            if (index < 0 || _records[index].Status != QuestStatus.Completed) return false;
            if (!_catalog.TryGet(questId, out OriginalQuestDefinition? quest) || !quest.TryGetReward(rewardId, out _)) return false;
            return !QuestRules.IsRewardClaimed(_records[index], rewardId);
        }

        // 보상 받기 명령이 같은 호출에서 지급한 뒤 부른다. 그 명령이 CanClaimReward로 확인한 보상만 넘기므로 결과를 돌려주지 않는다.
        // 원작 NotifyRewardClaimed처럼 모든 보상을 받으면 RewardClaimed로 바꾸고 저장된 확인 필요 값을 지운다.
        internal void MarkRewardClaimed(int questId, int rewardId)
        {
            int index = IndexOf(questId);
            QuestProgress record = _records[index];
            _catalog.TryGet(questId, out OriginalQuestDefinition? quest);
            int[] claimed = ReadOnlyLists.Appended(record.ClaimedRewardIds, rewardId);
            bool all = QuestRules.AreRewardsClaimed(quest!, claimed);
            _records[index] = new QuestProgress(questId, all ? QuestStatus.RewardClaimed : QuestStatus.Completed, record.Objectives, claimed);
            if (all) _needInspection.Remove(questId);
            ObserverDispatch.InvokeEach(Changed);
        }

        // 저장마다 새 배열을 만들어 쓴다. 확인 필요 ID는 기록 순서로 쓰며 늘 기록에 있는 퀘스트다.
        private protected override void WriteSection(GameSaveData data)
        {
            var flagged = new List<int>(_needInspection.Count);
            for (int index = 0; index < _records.Count; index++)
                if (_needInspection.Contains(_records[index].OriginalQuestId)) flagged.Add(_records[index].OriginalQuestId);
            data.SetQuests(_records.ToArray(), _inspected.ToArray(), flagged.ToArray());
        }

        // 불러온 기록으로 바꾼다. 기록은 코덱이 ID 중복과 음수를 거부한 값이다. 원작은 불러오기가 퀘스트 복제본을 새로 만들어
        // 과제 생성 기록도 처음으로 돌아가므로 함께 비운다. 정의 없는 저장 퀘스트는 남기고 ID마다 한 번 보고한다.
        private protected override void ReadSection(GameSaveData data)
        {
            _records.Clear();
            _records.AddRange(data.Quests);
            _inspected.Clear();
            _inspected.AddRange(data.InspectedQuests);
            _needInspection.Clear();
            _needInspection.UnionWith(data.QuestsNeedingInspection);
            ClearTaskHelperRecord();
            for (int index = 0; index < _records.Count; index++)
            {
                int questId = _records[index].OriginalQuestId;
                if (!_catalog.TryGet(questId, out _) && _reportedMissing.Add(questId))
                    Log.Instance.Warning("저장된 퀘스트 " + questId + "의 원작 정의가 없어 이 퀘스트의 명령을 막습니다.", this);
            }
            _claimPending = true;
            ObserverDispatch.InvokeEach(Changed);
        }

        // 기록은 원작 퀘스트 315개 이하라 색인 없이 훑는다. 색인을 두면 완료 때 순서를 옮길 때마다 다시 맞춰야 한다.
        int IndexOf(int questId)
        {
            for (int index = 0; index < _records.Count; index++)
                if (_records[index].OriginalQuestId == questId) return index;
            return -1;
        }

        // IndexOf 결과 자리에 진행 중 기록이 있는지다. 진행 조회와 완료, 확인 필요, 과제 확인이 같은 판정을 쓴다.
        bool IsActiveAt(int index) => index >= 0 && _records[index].Status == QuestStatus.Active;

        bool IsGiver(int questId, QuestGiverId giver) => _catalog.TryGet(questId, out OriginalQuestDefinition? quest) && quest.Giver == giver;

        List<int> RecordsFor(QuestGiverId giver, bool active)
        {
            var ids = new List<int>();
            for (int index = 0; index < _records.Count; index++)
            {
                QuestProgress record = _records[index];
                if ((record.Status == QuestStatus.Active) == active && IsGiver(record.OriginalQuestId, giver)) ids.Add(record.OriginalQuestId);
            }
            return ids;
        }

        // 원작 Task.IsFinished의 forceFinish 우선 판정 뒤 저장 수와 실시간 판정을 본다. 실시간 판정은 Progress 부분 파일의 질의다.
        bool IsTaskFinished(QuestProgress record, OriginalQuestTaskDefinition task) =>
            task.ForceFinish || QuestRules.IsCountFinished(task, QuestRules.CountOf(record, task.Id)) || IsLiveTaskFinished(task);

        bool AreTasksFinished(OriginalQuestDefinition quest, QuestProgress record)
        {
            IReadOnlyList<OriginalQuestTaskDefinition> tasks = quest.Tasks;
            for (int index = 0; index < tasks.Count; index++)
                if (!IsTaskFinished(record, tasks[index])) return false;
            return true;
        }

        // 원작 SubmitItems.NeedInspection이다. 원작 CheckItemEnough처럼 남은 수가 아니라 요구 수 전체와 지금 가진 수를 비교한다.
        bool AnySubmitReady(OriginalQuestDefinition quest, QuestProgress record)
        {
            IReadOnlyList<OriginalQuestTaskDefinition> tasks = quest.Tasks;
            for (int index = 0; index < tasks.Count; index++)
            {
                OriginalQuestTaskDefinition task = tasks[index];
                if (task.Kind == QuestTaskKind.SubmitItems && !IsTaskFinished(record, task) && _submitCount(task.ItemId) >= task.RequiredCount)
                    return true;
            }
            return false;
        }

        // 과제 진행 두 명령의 공통 확인이다. 열린 과제 확인에 더해 관문과 저장 과제인지를 본다. 모두 읽기라 순서는 결과를 바꾸지 않는다.
        bool TryGetOpenStoredTask(int questId, int taskId, out int index, [NotNullWhen(true)] out OriginalQuestTaskDefinition? task) =>
            TryGetUnfinishedTask(questId, taskId, out index, out task) && AcceptsCommands && QuestRules.IsStored(task.Kind);

        // 과제 진행 명령과 제출 확인(TryGetOpenTask)이 함께 쓰는 코어다. 진행 중 기록, 정의, 과제가 있고 아직 안 끝났을 때만 참이다.
        // 관문은 보지 않는다. 제출 명령은 뒤의 소지품 명령과 과제 기록이 관문을 보므로 확인 단계에서는 읽기만 한다.
        bool TryGetUnfinishedTask(int questId, int taskId, out int index, [NotNullWhen(true)] out OriginalQuestTaskDefinition? task)
        {
            task = null;
            index = IndexOf(questId);
            if (!IsActiveAt(index)) return false;
            if (!_catalog.TryGet(questId, out OriginalQuestDefinition? quest)) return false;
            if (!quest.TryGetTask(taskId, out OriginalQuestTaskDefinition? found) || IsTaskFinished(_records[index], found)) return false;
            task = found;
            return true;
        }

        // 그 과제 수만 바꾼 진행 중 기록으로 바꾸고 알린다. 원작 Task.ReportStatusChanged처럼 상태 알림 뒤에 과제 완료 알림을 낸다.
        void SetCount(int index, OriginalQuestTaskDefinition task, int count)
        {
            QuestProgress record = _records[index];
            _records[index] = new QuestProgress(record.OriginalQuestId, QuestStatus.Active, QuestRules.WithCount(record, task.Id, count));
            ObserverDispatch.InvokeEach(Changed);
            if (IsTaskFinished(_records[index], task)) ObserverDispatch.InvokeEachValue(TaskFinished, record.OriginalQuestId, task.Id);
        }
    }
}
