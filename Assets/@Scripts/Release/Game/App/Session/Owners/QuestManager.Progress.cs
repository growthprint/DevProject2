#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원작 사용, 이벤트, 특성, 건설, 비콘 과제의 진행과 제출 명령의 과제 확인이다. 원작은 과제 컴포넌트마다 알림을 구독했지만,
    // 여기서는 소유자 하나가 알림을 받아 진행 중 퀘스트의 맞는 과제에 기록한다. 메인 스레드에서만 쓴다.
    partial class QuestManager
    {
        // 진행 목록은 창구별로만 읽으므로 모든 창구 값을 한 번 만들어 둔다.
        static readonly QuestGiverId[] _givers = (QuestGiverId[])Enum.GetValues(typeof(QuestGiverId));

        // 원작 실시간 과제(건설, 특성, 비콘)가 다른 소유자에게 묻는 질의다. 퀘스트 소유자가 다른 소유자를 직접 모르도록 런타임 구성이 넘긴다.
        // 이 과제들은 저장 값이 없어 물을 때마다 지금 상태를 읽는다. 구성 전에는 모두 거짓이라 실시간 과제는 끝나지 않은 것으로 판정한다.
        Func<string, bool> _buildingExists = _ => false;
        Func<string, string, bool> _perkUnlocked = (_, _) => false;
        Func<string, int, bool> _beaconUnlocked = (_, _) => false;

        // 런타임 구성이 한 번 부른다. 건물은 원작 BuildingManager.Any(buildingID), 특성은 트리 ID와 특성 오브젝트 이름으로 찾는
        // 원작 Perk.Unlocked, 비콘은 원작 BeaconManager.GetBeaconUnlocked(ID, 번호) 자리다.
        internal void ConfigureWorldQueries(Func<string, bool> buildingExists, Func<string, string, bool> perkUnlocked,
            Func<string, int, bool> beaconUnlocked)
        {
            _buildingExists = buildingExists;
            _perkUnlocked = perkUnlocked;
            _beaconUnlocked = beaconUnlocked;
        }

        // 원작 TaskEvent.OnTaskEvent 구독이다. 기반 클래스가 Awake와 OnDestroy를 저장 구독에 쓰므로 켜짐과 꺼짐에 맞춘다.
        // GameManager 자식이라 세션 내내 켜져 있어 두 시점의 결과는 같다.
        void OnEnable() => TaskEventEmitter.TaskEventRaised += OnTaskEvent;

        void OnDisable() => TaskEventEmitter.TaskEventRaised -= OnTaskEvent;

        // 저장 값이 없는 원작 과제(건설, 비콘)와 이미 해금한 특성 과제를 지금 상태로 판정한다. IsTaskFinished가 저장 값 판정과 함께 본다.
        // 원작 특성 과제는 수락과 레벨 시작 때 이미 해금이면 끝남을 남긴다. 특성은 다시 잠기지 않아 지금 상태를 읽어도 결과가 같다.
        internal bool IsLiveTaskFinished(OriginalQuestTaskDefinition task) => task.Kind switch
        {
            QuestTaskKind.ConstructBuilding => _buildingExists(task.BuildingId),
            QuestTaskKind.UnlockBeacon => _beaconUnlocked(task.BeaconId, task.BeaconIndex),
            QuestTaskKind.UnlockPerk => _perkUnlocked(task.PerkTreeId, task.PerkName),
            _ => false
        };

        // 제출 명령이 아이템이나 돈을 빼기 전에 보는 확인이다. 진행 중 퀘스트에 그 종류의 과제가 있고 아직 안 끝났을 때만 참이다.
        // 정의 없는 저장 퀘스트는 거짓이다. 그 보고는 불러올 때 한 번 하므로 여기서는 기록하지 않는다. 과제 진행 명령과 같은 코어로 본다.
        internal bool TryGetOpenTask(int questId, int taskId, QuestTaskKind kind,
            [NotNullWhen(true)] out OriginalQuestTaskDefinition? task) =>
            TryGetUnfinishedTask(questId, taskId, out _, out task) && task.Kind == kind;

        // 원작 QuestTask_UseItem의 Item.onUseStatic이다. 플레이어의 소비품 사용 명령이 성공한 뒤 부른다.
        // 원작처럼 요구 수에 닿은 과제는 더 세지 않는다. 그 거절은 TryAddTaskCount가 한다.
        internal void NotifyItemUsed(int itemId)
        {
            foreach ((int questId, int taskId) in OpenTasks(QuestTaskKind.UseItem, task => task.ItemId == itemId))
                TryAddTaskCount(questId, taskId, 1);
        }

        // 원작 Perk.onUnlockStateChanged다. 특성 해금 명령이 처음 해금한 뒤 부른다. 원작처럼 끝남을 저장 값으로 남긴다.
        internal void NotifyPerkUnlocked(string treeId, string perkName)
        {
            foreach ((int questId, int taskId) in OpenTasks(QuestTaskKind.UnlockPerk,
                         task => string.Equals(task.PerkTreeId, treeId, StringComparison.Ordinal)
                             && string.Equals(task.PerkName, perkName, StringComparison.Ordinal)))
                TryFinishTask(questId, taskId);
        }

        // 원작 QuestTask_TaskEvent.OnTaskEvent처럼 키가 같은 과제만 끝낸다. 끝난 과제와 진행 중이 아닌 퀘스트는 TryFinishTask가 거절한다.
        void OnTaskEvent(string key)
        {
            foreach ((int questId, int taskId) in OpenTasks(QuestTaskKind.TaskEvent,
                         task => string.Equals(task.EventKey, key, StringComparison.Ordinal)))
                TryFinishTask(questId, taskId);
        }

        // 진행 중 퀘스트에서 kind 과제 중 matches가 참인 것을 먼저 모은다. 기록이 낸 알림을 받은 쪽이 같은 알림을 다시 내도
        // 훑던 목록이 바뀌지 않게 호출마다 새 목록을 쓴다. 드문 알림이라 할당을 아끼지 않고, 퀘스트를 두 번 세지 않게 ID로 거른다.
        List<(int questId, int taskId)> OpenTasks(QuestTaskKind kind, Func<OriginalQuestTaskDefinition, bool> matches)
        {
            var found = new List<(int questId, int taskId)>();
            var visited = new HashSet<int>();
            foreach (QuestGiverId giver in _givers)
            {
                foreach (int questId in ActiveFor(giver))
                {
                    if (!visited.Add(questId) || !Catalog.TryGet(questId, out OriginalQuestDefinition? quest)) continue;
                    foreach (OriginalQuestTaskDefinition task in quest.Tasks)
                        if (task.Kind == kind && matches(task)) found.Add((questId, task.Id));
                }
            }
            return found;
        }
    }
}
