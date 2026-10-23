#nullable enable annotations
using System.Collections.Generic;

namespace Game.App
{
    // 원작 QuestManager.OnTaskFinished의 과제 알림과 EconomyManager.Unlock의 상점 해금 알림을 공용 HUD에 넣는다.
    // 퀘스트와 경제 소유자는 화면과 HUD를 모르므로 이 객체가 낸다. 과제 알림은 창구 화면이 닫혀 있어도 뜨므로
    // 화면 묶음(GameplayScreens)이 만들어 켜고 끄며, 보상을 받는 퀘스트 창구 화면에 넘긴다.
    internal sealed class QuestNotices
    {
        // 원작 AudioManager.path_task_finished의 FMOD 이벤트다.
        const string _taskSound = "event:/UI/mission_small";
        // 원작 QuestManager.taskFinishNotificationFormatKey다. 이름, 끝난 과제 수, 전체 과제 수를 넣는다.
        const string _taskNoticeKey = "UI_Quest_TaskFinishedNotification";
        // 원작 EconomyManager.Unlock 알림 키다. {displayName} 자리에 품목 이름을 넣는다.
        const string _unlockedKey = "Notification_StockShoopItemUnlockFormat";

        readonly GameplayWorldContext _world;
        bool _listening;

        internal QuestNotices(GameplayWorldContext world)
        {
            _world = world;
        }

        // 원작 QuestManager.OnTaskFinished의 알림은 화면이 닫혀 있어도 레이드 중 과제 완료마다 뜬다. 화면 묶음이 공용 HUD를
        // 붙일 때 켜고 뗄 때 끈다. 여러 번 불러도 된다.
        internal void Start()
        {
            if (_listening) return;
            _listening = true;
            QuestManager.TaskFinished += OnTaskFinished;
        }

        internal void Stop()
        {
            if (!_listening) return;
            _listening = false;
            QuestManager.TaskFinished -= OnTaskFinished;
        }

        void OnTaskFinished(int questId, int taskId) => NotifyTaskProgress(questId);

        // 원작 QuestManager.OnTaskFinished의 NotificationText.Push와 UI/mission_small이다. 끝난 과제 수는 지금 판정으로 센다.
        // 공용 HUD가 이 월드에 붙어 있을 때만 알린다. 창구 화면의 일부 제출도 원작 SubmitItems.Submit처럼 이 알림을 낸다.
        internal void NotifyTaskProgress(int questId)
        {
            if (!_world.CanPresent) return;
            QuestManager quests = GameManager.Quests;
            if (!quests.Catalog.TryGet(questId, out OriginalQuestDefinition? quest)) return;
            int finished = 0;
            for (int index = 0; index < quest.Tasks.Count; index++)
                if (quests.IsTaskFinished(questId, quest.Tasks[index].Id)) finished++;
            // 이름과 숫자 문구는 일지와 같은 빈 키 규칙과 고정 숫자 형식을 쓴다.
            _world.Hud.Notify(GameLocalization.Text(_taskNoticeKey).Replace("{questDisplayName}", GameLocalization.TextOrEmpty(quest.NameKey))
                .Replace("{finishedTasks}", QuestDisplayValues.Number(finished)).Replace("{totalTasks}", QuestDisplayValues.Number(quest.Tasks.Count)));
            _world.Audio.PlayOneShot(_taskSound);
        }

        // 원작 QuestReward_UnlockStockItem.OnClaim → EconomyManager.Unlock(showUI 기본 참)은 아직 열리지도 확인 대기도 아닌 품목에만
        // 알림을 띄운다. 받기 전에 이 퀘스트의 상점 해금 보상 가운데 그런 품목을 고른다. 없으면 목록을 만들지 않는다.
        internal static List<int>? LockedShopItems(int questId)
        {
            QuestManager quests = GameManager.Quests;
            if (!quests.Catalog.TryGet(questId, out OriginalQuestDefinition? quest)) return null;
            EconomyManager economy = GameManager.Economy;
            List<int>? locked = null;
            IReadOnlyList<OriginalQuestRewardDefinition> rewards = quest.Rewards;
            for (int index = 0; index < rewards.Count; index++)
            {
                OriginalQuestRewardDefinition reward = rewards[index];
                if (reward.Kind != QuestRewardKind.UnlockStockItem || economy.IsUnlockedOrWaiting(reward.ItemId)) continue;
                locked ??= new List<int>();
                locked.Add(reward.ItemId);
            }
            return locked;
        }

        // 받기가 성공한 뒤 그 호출로 새로 열렸거나 확인 대기가 된 품목마다 원작 해금 알림 문구를 공용 HUD에 넣는다.
        // 공용 HUD가 이 월드에 붙어 있을 때만 알린다. 호출자: QuestGiverScreen의 보상 받기.
        internal void NotifyUnlocked(List<int>? locked)
        {
            if (locked == null || !_world.PresentationAttached) return;
            EconomyManager economy = GameManager.Economy;
            for (int index = 0; index < locked.Count; index++)
            {
                int itemId = locked[index];
                if (economy.IsUnlockedOrWaiting(itemId)) _world.Hud.Notify(UnlockNotice(_world.Catalog, itemId));
            }
        }

        // 원작 EconomyManager.Unlock의 알림 문구다. 품목 ID는 퀘스트 보상 정의의 저작 데이터라 카탈로그 정의가 늘 있다.
        static string UnlockNotice(OriginalGameplayCatalog catalog, int itemId)
        {
            OriginalItemDefinition definition = catalog.DefinitionOf(itemId);
            return GameLocalization.Text(_unlockedKey).Replace("{displayName}", GameLocalization.Text(definition.NameKey));
        }
    }
}
