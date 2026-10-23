#nullable enable
using System.Collections.Generic;

namespace Game.App
{
    // 원작 Reward.Claim의 퀘스트 보상 받기다. 받음 표시는 QuestManager가, 경험치는 ExperienceManager가, 계좌 돈과 상점 품목 해금은
    // EconomyManager가, 아이템은 세션 소지품이 소유한다. 모든 소유자를 먼저 확인하고 아이템, 경험치, 돈, 해금, 받음 표시 순서로 적용한다.
    // 원작 아이템 보상은 비동기지만 여기서는 기다림 없이 한 호출에서 끝내 지급과 받음 표시가 같은 저장에 들어간다.
    internal static partial class GameplayCommands
    {
        // 원작이 퀘스트 보상 아이템의 FromInfoKey에 넣는 출처 키다. 아이템 저장 값(fromInfo)이라 바꾸지 않는다.
        internal const string _questRewardSource = "From_QuestReward";

        // 원작 RewardEntry 클릭이다. 그 보상 하나만 받는다. 호출자: 2-Q4 보상 줄.
        internal static bool TryClaimQuestReward(GameSessionManager session, QuestManager quests, ExperienceManager experience,
            EconomyManager economy, OriginalGameplayCatalog catalog, int questId, int rewardId)
        {
            if (!quests.CanClaimReward(questId, rewardId)) return false;
            // 같은 호출에서 CanClaimReward가 정의와 보상을 찾았으므로 둘 다 늘 있다.
            quests.Catalog.TryGet(questId, out OriginalQuestDefinition? quest);
            quest!.TryGetReward(rewardId, out OriginalQuestRewardDefinition? reward);
            return TryClaimRewards(session, quests, experience, economy, catalog, questId, new[] { reward! });
        }

        // 원작 QuestCompletePanel의 "모두 받기"다. 받지 않은 보상을 모두 받는다. 하나라도 줄 수 없으면 아무것도 받지 않는다.
        // 호출자: 2-Q4 완료 패널.
        internal static bool TryClaimAllQuestRewards(GameSessionManager session, QuestManager quests, ExperienceManager experience,
            EconomyManager economy, OriginalGameplayCatalog catalog, int questId) =>
            TryClaimRewards(session, quests, experience, economy, catalog, questId, Claimable(quests, questId, false));

        // 원작 AutoClaim 보상(경험치, 돈, 상점 품목 해금)만 받는다. 아이템 보상은 원작처럼 남아 나중에 상세에서 받는다.
        // 받을 것이 없으면 false다. 호출자: 2-Q4 완료 패널 닫기와 창구 화면 닫기, 불러온 뒤 첫 명령 시점의 복원.
        internal static bool TryClaimQuestAutoRewards(QuestManager quests, ExperienceManager experience, EconomyManager economy, int questId) =>
            TryClaimRewards(null, quests, experience, economy, null, questId, Claimable(quests, questId, true));

        // 원작 Quest.SetupSaveData의 불러오기 처리다. 완료 퀘스트마다 원작 NotifyReload처럼 상점 품목 해금을 다시 걸고,
        // 받지 않은 자동 보상을 받는다. 해금은 이미 있으면 경제 소유자가 조용히 넘긴다. QuestManager가 불러온 뒤 관문이 열린 첫 프레임에 한 번 부른다.
        internal static void RestoreQuestRewardsAfterLoad(QuestManager quests, ExperienceManager experience, EconomyManager economy)
        {
            IReadOnlyList<QuestProgress> records = quests.Records;
            // 받음 표시는 기록을 같은 자리에서 바꾸므로 번호로 훑는다.
            for (int index = 0; index < records.Count; index++)
            {
                QuestProgress record = records[index];
                if (record.Status == QuestStatus.Active || !quests.Catalog.TryGet(record.OriginalQuestId, out OriginalQuestDefinition? quest)) continue;
                IReadOnlyList<OriginalQuestRewardDefinition> rewards = quest.Rewards;
                for (int reward = 0; reward < rewards.Count; reward++)
                    if (rewards[reward].Kind == QuestRewardKind.UnlockStockItem) economy.TryUnlock(rewards[reward].ItemId, true);
                TryClaimQuestAutoRewards(quests, experience, economy, record.OriginalQuestId);
            }
        }

        // 정의 순서대로 아직 받을 수 있는 보상이다. 정의 없는 퀘스트와 완료 전 퀘스트는 빈 목록이다.
        static List<OriginalQuestRewardDefinition> Claimable(QuestManager quests, int questId, bool autoOnly)
        {
            var claimable = new List<OriginalQuestRewardDefinition>();
            if (!quests.Catalog.TryGet(questId, out OriginalQuestDefinition? quest)) return claimable;
            IReadOnlyList<OriginalQuestRewardDefinition> rewards = quest.Rewards;
            for (int index = 0; index < rewards.Count; index++)
                if ((!autoOnly || rewards[index].AutoClaim) && quests.CanClaimReward(questId, rewards[index].Id)) claimable.Add(rewards[index]);
            return claimable;
        }

        // 경험치와 돈은 합계로 넘침을 먼저 거절하고, 아이템은 원작 Push(toBufferDirectly: true)처럼 창고 임시 보관함으로 보낸다.
        // 실패할 수 있는 아이템 지급을 먼저 해서 거절되면 아무것도 바뀌지 않는다. 아이템이 없는 호출은 세션과 카탈로그를 받지 않는다.
        static bool TryClaimRewards(GameSessionManager? session, QuestManager quests, ExperienceManager experience, EconomyManager economy,
            OriginalGameplayCatalog? catalog, int questId, IReadOnlyList<OriginalQuestRewardDefinition> rewards)
        {
            if (rewards.Count == 0) return false;
            long experienceTotal = 0;
            long moneyTotal = 0;
            var items = new List<(int id, int count)>();
            for (int index = 0; index < rewards.Count; index++)
            {
                OriginalQuestRewardDefinition reward = rewards[index];
                if (reward.Kind == QuestRewardKind.Experience) experienceTotal += reward.Amount;
                else if (reward.Kind == QuestRewardKind.Money) moneyTotal += reward.Amount;
                else if (reward.Kind == QuestRewardKind.Item) items.Add((reward.ItemId, reward.Amount));
            }
            // 원작은 AddExp와 계좌 Add가 참일 때만 받음 표시를 한다. 0도 두 소유자의 관문을 함께 본다.
            if (!experience.CanAdd(experienceTotal) || !economy.CanAdd(moneyTotal)) return false;
            // 원작 RewardItem은 보상 아이템마다 FromInfoKey를 From_QuestReward로 정한다(RewardItem.cs:84).
            if (items.Count > 0 && !session!.Inventory.TryDeliver(catalog!, items, ItemDelivery.Buffer, _questRewardSource, out _)) return false;
            for (int index = 0; index < rewards.Count; index++)
            {
                OriginalQuestRewardDefinition reward = rewards[index];
                // 같은 호출에서 합계로 CanAdd를 통과했으므로 보상마다 더해도 늘 들어간다. 원작처럼 보상마다 레벨 알림이 날 수 있다.
                if (reward.Kind == QuestRewardKind.Experience) experience.Add(reward.Amount);
                else if (reward.Kind == QuestRewardKind.Money) economy.Add(reward.Amount);
                // 원작 EconomyManager.Unlock의 기본값처럼 확인 대기로 연다. 이미 열렸으면 false지만 원작처럼 받음 처리는 막지 않는다.
                else if (reward.Kind == QuestRewardKind.UnlockStockItem) economy.TryUnlock(reward.ItemId, true);
            }
            for (int index = 0; index < rewards.Count; index++)
                quests.MarkRewardClaimed(questId, rewards[index].Id);
            return true;
        }
    }
}
