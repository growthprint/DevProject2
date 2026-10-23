#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.App
{
    // 원작 QuestTask_KillCount가 Health.OnDead의 피해 정보에서 읽는 값 중 과제 거르기에 쓰는 것만 담는다.
    // 퀘스트 소유자가 씬을 읽지 않도록 처치가 일어난 논리 씬도 알리는 쪽이 넘긴다.
    internal readonly struct QuestKillInfo
    {
        internal QuestKillInfo(string enemyNameKey, bool fromPlayer, int weaponItemId, bool crit, Func<int, bool>? attackerHasBuff,
            GameScene scene)
        {
            EnemyNameKey = enemyNameKey;
            FromPlayer = fromPlayer;
            WeaponItemId = weaponItemId;
            Crit = crit;
            AttackerHasBuff = attackerHasBuff;
            Scene = scene;
        }

        // 죽은 적 프리셋의 nameKey다. 원작은 프리셋이 없는 적을 적 지정 과제에서 세지 않으므로 빈 값은 어느 적 지정과도 맞지 않는다.
        internal string EnemyNameKey { get; }
        // 원작 fromCharacter가 플레이어 팀이거나 주인공인지다. 거짓이면 어떤 처치 과제도 세지 않는다.
        internal bool FromPlayer { get; }
        // 원작 fromWeaponItemID다. 피해 경로가 무기를 넘기지 않으면 0이라 무기 지정 과제는 세지 않는다.
        internal int WeaponItemId { get; }
        // 원작 crit > 0이다. 원작 requireHeadShot과 withoutHeadShot이 이 값을 본다.
        internal bool Crit { get; }
        // 원작 fromCharacter.HasBuff다. 판정할 수 없으면 null이고, 그때 버프가 필요한 과제는 세지 않는다.
        internal Func<int, bool>? AttackerHasBuff { get; }
        internal GameScene Scene { get; }
    }

    // 원작 SceneReference.LoadedScene.isLoaded 판정을 제품 논리 씬으로 바꾼다. 범위 안 원작 지도는 제로존과 기지뿐이라
    // 다른 원작 씬 ID는 늘 로드되지 않은 것으로 본다. 그래서 범위 밖 지도 퀘스트는 받을 수 있어도 그 과제가 진행되지 않는다(D3).
    internal static class QuestScenes
    {
        internal const string GroundZero = "Level_GroundZero_Main";
        internal const string Base = "Base";

        internal static bool IsLoaded(string originalSceneId, GameScene loaded) =>
            (loaded == GameScene.ZeroZone && string.Equals(originalSceneId, GroundZero, StringComparison.Ordinal))
            || (loaded == GameScene.BaseCamp && string.Equals(originalSceneId, Base, StringComparison.Ordinal));

        // 원작 Quest와 QuestTask_KillCount의 SceneRequirementSatisfied다. 씬 ID가 비어 있으면 어느 씬에서든 참이다.
        internal static bool Satisfied(string originalSceneId, GameScene loaded) =>
            string.IsNullOrEmpty(originalSceneId) || IsLoaded(originalSceneId, loaded);
    }

    // 처치, 탈출, 레벨 시작 알림과 과제 생성 도우미의 세션 기록을 맡는 QuestManager 부분이다. 처치와 탈출은 QuestManager.cs의
    // TryAddTaskCount와 TryFinishTask로만 진행 값을 바꾸므로, 진행 중이 아니거나 끝난 과제와 명령 관문은 그쪽이 거른다.
    partial class QuestManager
    {
        // 원작 SpawnItemForTask와 SpawnPrefabForTask의 spawned 필드다. 원작 퀘스트 복제본처럼 세션 동안만 기억하고 저장하지 않는다
        // (2026-10-20 사용자 결정). 그래서 같은 세션의 다음 레이드에서는 다시 만들지 않는다. 키는 "{questId}/{taskId}/{componentID}"다.
        readonly HashSet<string> _spawnedTaskHelpers = new HashSet<string>(StringComparer.Ordinal);

        // 과제 생성기(QuestTaskSpawner)와 처치·탈출 알림이 진행 중 퀘스트의 과제 정의를 훑는 목록이다. 카탈로그 목록은 바뀌지 않으므로
        // 기록이 낸 알림이 다시 들어와도 훑던 목록이 바뀌지 않는다. 카탈로그는 프리팹 참조라 늘 있고 GameManagerPrefabTests가 확인한다.
        internal IReadOnlyList<OriginalQuestDefinition> CatalogQuests => _catalog.Quests;

        internal bool IsTaskHelperSpawned(int questId, int taskId, string componentId) =>
            _spawnedTaskHelpers.Contains(TaskHelperKey(questId, taskId, componentId));

        // 원작처럼 위치를 찾은 뒤에만 기록한다. 이 씬에 위치가 없어 만들지 못한 도우미는 다음 레벨에서 다시 시도한다.
        internal void MarkTaskHelperSpawned(int questId, int taskId, string componentId) =>
            _spawnedTaskHelpers.Add(TaskHelperKey(questId, taskId, componentId));

        // 원작은 불러오기가 퀘스트 복제본을 새로 만들어 spawned 필드도 처음으로 돌아간다. 불러오기 처리(QuestManager.cs)가 부른다.
        void ClearTaskHelperRecord() => _spawnedTaskHelpers.Clear();

        static string TaskHelperKey(int questId, int taskId, string componentId) =>
            questId.ToString(CultureInfo.InvariantCulture) + "/" + taskId.ToString(CultureInfo.InvariantCulture) + "/" + componentId;

        // 원작 QuestTask_KillCount.Health_OnDead다. 플레이어 쪽이 죽인 적만 보고, 거르기를 통과한 진행 중 처치 과제마다 1을 더한다.
        // 원작처럼 처치 과제는 퀘스트의 요구 씬이 아니라 과제 자신의 씬만 본다. 요구 수 상한은 TryAddTaskCount가 지킨다.
        internal void NotifyEnemyKilled(QuestKillInfo info)
        {
            if (!info.FromPlayer) return;
            foreach ((OriginalQuestDefinition quest, OriginalQuestTaskDefinition task) in ActiveTasks())
                if (task.Kind == QuestTaskKind.KillCount && KillTaskMatches(task, info)) TryAddTaskCount(quest.Id, task.Id, 1);
        }

        // 원작 거르기 순서와 같다. 적 지정이 빈 과제는 모든 적을 센다.
        static bool KillTaskMatches(OriginalQuestTaskDefinition task, in QuestKillInfo info)
        {
            if (task.RequireWeapon && info.WeaponItemId != task.WeaponItemId) return false;
            if (!QuestScenes.Satisfied(task.SceneId, info.Scene)) return false;
            if (task.RequireCrit && !info.Crit) return false;
            if (task.ForbidCrit && info.Crit) return false;
            if (task.RequireBuff && (info.AttackerHasBuff == null || !info.AttackerHasBuff(task.BuffId))) return false;
            return string.IsNullOrEmpty(task.EnemyNameKey) || string.Equals(task.EnemyNameKey, info.EnemyNameKey, StringComparison.Ordinal);
        }

        // 원작 QuestTask_Evacuate.OnEvacuated다. 과제 씬이 비어 있으면 퀘스트의 요구 씬을, 있으면 과제 씬을 본다.
        // 탈출 저장보다 먼저 불려야 끝난 과제가 같은 저장에 들어간다. scene은 탈출한 월드의 논리 씬이다.
        internal void NotifyEvacuated(GameScene scene)
        {
            foreach ((OriginalQuestDefinition quest, OriginalQuestTaskDefinition task) in ActiveTasks())
            {
                if (task.Kind != QuestTaskKind.Evacuate) continue;
                bool satisfied = string.IsNullOrEmpty(task.SceneId)
                    ? QuestScenes.Satisfied(quest.RequireSceneId, scene)
                    : QuestScenes.IsLoaded(task.SceneId, scene);
                if (satisfied) TryFinishTask(quest.Id, task.Id);
            }
        }

        // 원작 처치와 사용 과제의 OnLevelInitialized다. resetOnLevelInitialized가 켜진 과제는 레벨이 시작될 때 센 수를 0으로 되돌린다.
        // 원작은 레벨마다 QuestManager를 새로 불러오고 Task.Init이 그때 끝난 과제를 꺼 두므로, 끝난 과제는 되돌리지 않는다.
        // 레벨 준비는 씬 전환 작업 안이라 명령 관문이 닫혀 있다. 그래서 관문을 보는 TryAddTaskCount 대신 기록을 바로 바꾼다.
        internal void NotifyLevelInitialized()
        {
            foreach ((OriginalQuestDefinition quest, OriginalQuestTaskDefinition task) in ActiveTasks())
            {
                if (!task.ResetOnLevelInitialized || (task.Kind != QuestTaskKind.KillCount && task.Kind != QuestTaskKind.UseItem)) continue;
                // 반복자가 진행 중인 기록을 확인한 퀘스트라 자리가 늘 있다. 과제 수 바꾸기는 같은 자리를 새 값으로 바꿀 뿐이다.
                int index = IndexOf(quest.Id);
                QuestProgress record = _records[index];
                if (QuestRules.CountOf(record, task.Id) > 0 && !IsTaskFinished(record, task)) SetCount(index, task, 0);
            }
        }

        // 처치, 탈출, 레벨 시작 알림이 함께 쓰는 순회다. 카탈로그 순서로 진행 중 퀘스트의 과제를 하나씩 내준다.
        // 지연 반복이라 진행 중인지는 그 퀘스트 차례가 왔을 때 한 번 본다. 그래서 앞 퀘스트의 기록 변경이 뒤 퀘스트 확인에 그대로 보인다.
        IEnumerable<(OriginalQuestDefinition quest, OriginalQuestTaskDefinition task)> ActiveTasks()
        {
            IReadOnlyList<OriginalQuestDefinition> quests = _catalog.Quests;
            for (int i = 0; i < quests.Count; i++)
            {
                OriginalQuestDefinition quest = quests[i];
                if (!IsActive(quest.Id)) continue;
                IReadOnlyList<OriginalQuestTaskDefinition> tasks = quest.Tasks;
                for (int t = 0; t < tasks.Count; t++) yield return (quest, tasks[t]);
            }
        }
    }
}
