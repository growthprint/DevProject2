#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 진행, 생존 상태, 퀘스트, 시설, 출격 정산 순번, 원본 플레이 상태를 한 번에 저장하는 불변 값이다. Unity 객체를 참조하지 않는다.
    // 소유물은 Gameplay.Inventory.Items의 원본 아이템 그래프만 갖고, 평면 소지품 목록은 두지 않는다.
    public sealed class CampaignState
    {
        // TryCreate의 검사다. 던지지 않고 false를 돌려주며, 성공하면 검사한 두 목록의 복사본을 돌려준다.
        // 진행과 플레이 상태는 필수이고, 출격 순번과 결과 규칙은 아래 IsValid* 함수가 판정한다. 난이도는 캠페인 수준 규칙이 없어 받지 않는다.
        // 소유자 기록 묶음도 필수다. 원작 needInspection은 퀘스트 기록마다 붙는 값이라, 확인 필요 ID는 퀘스트 기록에 있어야 한다.
        static bool Check(CampaignProgress? progress, PlayerState? player, IReadOnlyList<QuestProgress>? quests,
            IReadOnlyList<FacilityProgress>? facilities, long settledRaidSequence, RaidState? raid, GameplayPersistenceState? gameplay,
            CampaignRecordsState? records,
            [NotNullWhen(true)] out IReadOnlyList<QuestProgress>? questsCopy,
            [NotNullWhen(true)] out IReadOnlyList<FacilityProgress>? facilitiesCopy)
        {
            questsCopy = null;
            facilitiesCopy = null;
            if (progress == null) return false;
            if (!ReadOnlyLists.TryCopyUnique(quests, item => item.OriginalQuestId, out questsCopy)) return false;
            if (!ReadOnlyLists.TryCopyUnique(facilities, item => item.OriginalFacilityId, out facilitiesCopy)) return false;
            if (gameplay == null) return false;
            if (records == null || !AreRecordedQuests(records.QuestsNeedingInspection, questsCopy)) return false;
            if (!IsValidGraphPlayer(gameplay, player)) return false;
            return IsValidRaidSequence(progress, settledRaidSequence, raid) &&
                (raid?.Result == null || IsValidResultGameplay(gameplay, raid.Result));
        }

        // 저장 파일처럼 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 false를 돌려주고, 성공하면 검사한 복사본으로 만든다.
        // 소유자 기록 묶음(records)은 플레이 상태 다음 인자다. 묶음 안의 규칙은 묶음을 만들 때 이미 검사했다.
        public static bool TryCreate(CampaignProgress progress, PlayerState? player, IReadOnlyList<QuestProgress> quests,
            IReadOnlyList<FacilityProgress> facilities, long settledRaidSequence, RaidState? raid, DifficultyState? difficulty,
            GameplayPersistenceState gameplay, CampaignRecordsState records, [NotNullWhen(true)] out CampaignState? value)
        {
            if (!Check(progress, player, quests, facilities, settledRaidSequence, raid, gameplay, records,
                    out IReadOnlyList<QuestProgress>? questsCopy, out IReadOnlyList<FacilityProgress>? facilitiesCopy))
            {
                value = null;
                return false;
            }

            value = new CampaignState(progress, player, questsCopy, facilitiesCopy, settledRaidSequence, raid, difficulty,
                gameplay, records);
            return true;
        }

        // 검사와 복사를 마친 값을 그대로 대입한다. With* 메서드는 호출자가 바뀐 필드의 규칙을 먼저 지켰다고 믿고 이 생성자를 부른다.
        // 소유자 기록 묶음은 세션 명령이 바꾸지 않아 모든 With*가 그대로 넘긴다.
        CampaignState(CampaignProgress progress, PlayerState? player, IReadOnlyList<QuestProgress> quests,
            IReadOnlyList<FacilityProgress> facilities, long settledRaidSequence, RaidState? raid, DifficultyState? difficulty,
            GameplayPersistenceState gameplay, CampaignRecordsState records)
        {
            Progress = progress;
            Player = player;
            Quests = quests;
            Facilities = facilities;
            SettledRaidSequence = settledRaidSequence;
            Raid = raid;
            Difficulty = difficulty;
            Gameplay = gameplay;
            Records = records;
        }

        // 확인 필요 ID가 모두 퀘스트 기록에 있는지 본다. 퀘스트는 원작 315개 이하라 색인 없이 훑는다.
        static bool AreRecordedQuests(IReadOnlyList<int> questIds, IReadOnlyList<QuestProgress> quests)
        {
            for (int index = 0; index < questIds.Count; index++)
            {
                bool found = false;
                for (int quest = 0; quest < quests.Count && !found; quest++)
                    found = quests[quest].OriginalQuestId == questIds[index];
                if (!found) return false;
            }
            return true;
        }

        // 출격은 기지 해금 단계에서만, 마지막 정산 순번의 바로 다음 순번으로 존재한다.
        // 정산 순번이 long.MaxValue면 다음 순번이 넘치므로 더하기 전에 거절한다.
        static bool IsValidRaidSequence(CampaignProgress progress, long settledRaidSequence, RaidState? raid)
        {
            if (settledRaidSequence < 0) return false;
            if (raid == null) return true;
            return progress.Stage == CampaignStage.BaseUnlocked && settledRaidSequence < long.MaxValue &&
                raid.Sequence == settledRaidSequence + 1;
        }

        // 원본 아이템 그래프를 포착한 캠페인에는 생존 상태가 있어야 한다. 그래프 초기화, 프롤로그, 사망 복귀가 모두 플레이어를
        // 함께 만들므로 위반은 손상된 저장에서만 생긴다. 저장 읽기 검사가 쓴다.
        static bool IsValidGraphPlayer(GameplayPersistenceState gameplay, PlayerState? player) =>
            gameplay.Inventory.Items == null || player != null;

        public CampaignProgress Progress { get; }
        // 허브가 지닌 생존 수치 사본이라 세션 중 현재 값이 아니다. 현재 값은 PlayerVitals가 소유하고 저장도 그 값을 쓴다.
        // 세션에서는 그래프가 있으면 플레이어도 있어야 한다는 규칙에만 쓴다. 아직 포착하지 않은 생존 상태는 null이다.
        public PlayerState? Player { get; }
        // 저장 기록의 퀘스트이며 세션 명령이 바꾸지 않는다. 퀘스트 소유자(2-Q1 QuestManager)가 생기면 그 값이 저장에 쓰인다.
        public IReadOnlyList<QuestProgress> Quests { get; }
        public IReadOnlyList<FacilityProgress> Facilities { get; }
        // 마지막으로 정산한 출격 순번이다(진행 중인 출격은 이 값 + 1).
        public long SettledRaidSequence { get; }
        // 실행 중이거나 정산 대기 중인 하나의 출격만 유지한다.
        public RaidState? Raid { get; }

        // 허브가 지닌 난이도 사본이라 세션 중 현재 값이 아니다. 현재 난이도는 DifficultyRules가 소유하고 저장도 그 값을 쓴다.
        // null이면 난이도를 아직 고르지 않았다.
        public DifficultyState? Difficulty { get; }
        // 모든 씬 전환에서 함께 보존하는 원본 플레이 저장 값이다.
        public GameplayPersistenceState Gameplay { get; }
        // 원작 SavesSystem 키로 남는 소유자 기록의 저장 값이다. 세션 동안 갱신하지 않고 출격 정산도 바꾸지 않는다.
        // 현재 값은 GameManager 자식 소유자(특성, 열쇠, 재능, 시계, 처치 수, 플래그)가 들고 저장도 그 값을 쓴다.
        public CampaignRecordsState Records { get; }

        // 플레이어는 그대로 두고 플레이 상태만 바꾼다. 규칙은 WithGameplayAndPlayer와 같다.
        public CampaignState WithGameplay(GameplayPersistenceState gameplay) => WithGameplayAndPlayer(gameplay, Player);

        // 플레이 상태와 플레이어를 한 번에 바꾼다. 세션 명령은 검증한 값만 넘긴다.
        public CampaignState WithGameplayAndPlayer(GameplayPersistenceState gameplay, PlayerState? player)
        {
            // 소유자 기록 묶음은 바뀌지 않는다. 아래 With*도 같다.
            return new CampaignState(Progress, player, Quests, Facilities, SettledRaidSequence, Raid, Difficulty, gameplay, Records);
        }

        // 레거시 저장의 정산 대기 결과에만 쓴다. 현재 상태와 결과는 그래프 포착 여부가 같고, 같은 인스턴스의 원본 정의도 같아야 한다.
        // 포착한 그래프가 미포착 결과로 바뀌면 장착, 탄약, 유실물 상태를 잃고, 반대로 바뀌면 창고를 어떻게 보존할지 불분명해진다.
        static bool IsValidResultGameplay(GameplayPersistenceState gameplay, RaidResult result)
        {
            ItemOwnershipState? current = gameplay.Inventory.Items;
            ItemOwnershipState? captured = result.Gameplay?.Inventory.Items;
            if ((current == null) != (captured == null)) return false;
            return IsValidItemDefinitions(current, captured);
        }

        // 인벤토리 위치가 바뀌어도 같은 인스턴스의 원본 정의는 변할 수 없다. 한쪽이 없거나 같은 그래프 참조면 검사하지 않는다.
        static bool IsValidItemDefinitions(ItemOwnershipState? before, ItemOwnershipState? after)
        {
            if (before == null || after == null || ReferenceEquals(before, after)) return true;
            // before 그래프의 ID 색인으로 같은 인스턴스를 찾아, 검사마다 사전과 열거자를 새로 만들지 않는다.
            IReadOnlyList<ItemInstanceState> items = after.Items;
            for (int index = 0; index < items.Count; index++)
            {
                ItemInstanceState? old = before.Find(items[index].InstanceId);
                if (old != null && old.OriginalItemId != items[index].OriginalItemId) return false;
            }
            return true;
        }

        // 난이도만 바꾼다. 난이도에는 다른 값과 얽힌 규칙이 없다. 세션 명령은 난이도를 DifficultyRules에만
        // 반영하므로 이 메서드는 저장 기록과 테스트 값을 만들 때 쓴다.
        public CampaignState WithDifficulty(DifficultyState difficulty)
        {
            return new CampaignState(Progress, Player, Quests, Facilities, SettledRaidSequence, Raid, difficulty, Gameplay, Records);
        }

        // 진행만 바꾼다. 세션 명령은 출격이 기지 해금 단계에서만 있다는 규칙을 먼저 확인한 진행 값만 넘긴다.
        public CampaignState WithProgress(CampaignProgress progress)
        {
            return new CampaignState(progress, Player, Quests, Facilities, SettledRaidSequence, Raid, Difficulty, Gameplay, Records);
        }

        // 출격만 바꾼다. 출격 명령은 단계와 순번을 먼저 확인한 출격 값만 넘긴다.
        public CampaignState WithRaid(RaidState? raid)
        {
            return new CampaignState(Progress, Player, Quests, Facilities, SettledRaidSequence, raid, Difficulty, Gameplay, Records);
        }

        // 레거시 저장을 이어할 때(PrepareContinueSnapshot) 현재 출격의 대기 결과를 정산한다. 결과를 인수로 받지 않아 임의 보상을 넣을 수 없다.
        // 결과의 생존 수치와 플레이 상태를 쓰되 포착하지 않은 값은 현재 값을 두고, 지도 마커, 쪽지, 레벨 물체 키는 현재 슬롯 것을 유지한다.
        public CampaignState WithSettlement()
        {
            // 이어하기는 대기 결과가 있을 때만 부르므로 '!'로 null 경고를 닫는다.
            RaidResult result = Raid!.Result!;
            GameplayPersistenceState gameplay = result.Gameplay == null ? Gameplay : result.Gameplay.WithSlotRecords(Gameplay);
            return CloseRaid(result.Sequence, result.Player ?? Player, gameplay);
        }

        // 결과 없이 현재 출격을 그 순번으로 정산 완료 처리한다. 이어하기의 미완료 출격과 출격 중 사망이나 탈출에서 쓴다.
        // 다른 캠페인 값은 모두 그대로 둔다.
        public CampaignState WithRaidClosed()
        {
            // 호출자는 출격이 있을 때만 부르므로 '!'로 null 경고를 닫는다.
            return CloseRaid(Raid!.Sequence, Player, Gameplay);
        }

        // 출격을 비우고 정산 순번, 플레이어, 플레이 상태를 한 번에 대입한다.
        // 여기서는 검사하지 않는다. 결과 플레이 상태 규칙은 저장을 읽을 때 TryCreate가 검사한다.
        CampaignState CloseRaid(long settledSequence, PlayerState? player, GameplayPersistenceState gameplay) =>
            new CampaignState(Progress, player, Quests, Facilities, settledSequence, null, Difficulty, gameplay, Records);

        // 새 캠페인은 수치나 아이템을 임의로 주지 않고 준비 단계에서 시작한다.
        public static CampaignState CreateNew() => CreateAtStage(CampaignStage.Prepare);

        // 지정한 단계의 빈 캠페인을 만든다. 난이도는 고르지 않은 상태이고 플레이 상태는 새 게임 값이다.
        // 빈 목록과 출격 없음은 모든 규칙을 만족하므로 검사를 건너뛴다. 단계는 호출자가 넘기는 CampaignStage 상수다.
        // 소유자 기록도 새 게임 값(빈 묶음)으로 시작한다.
        public static CampaignState CreateAtStage(CampaignStage stage) => new CampaignState(
            new CampaignProgress(stage, null), null, Array.Empty<QuestProgress>(), Array.Empty<FacilityProgress>(), 0, null, null,
            GameplayPersistenceState.NewGame(), CampaignRecordsState.Empty);
    }
}
