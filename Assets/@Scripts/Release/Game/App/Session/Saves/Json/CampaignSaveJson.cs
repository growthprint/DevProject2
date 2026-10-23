#nullable enable

using System;
using System.Collections.Generic;
using DevProject.Serialization;
using Newtonsoft.Json.Linq;
using static Game.App.SaveJsonFields;

namespace Game.App
{
    // 캠페인 필드를 정확한 이름과 토큰 형식으로 읽고 쓴다. 값의 의미 범위는 각 값 형식의 TryCreate가 검사한다.
    internal static class CampaignSaveJson
    {
        // 필드를 하나씩 직접 기록해, 값 형식에 속성이 늘어도 저장 형식이 저절로 바뀌지 않게 한다.
        internal static JObject Write(CampaignState value) => new JObject
        {
            ["progress"] = new JObject
            {
                ["stage"] = WriteStage(value.Progress.Stage),
                ["checkpointId"] = NullableString(value.Progress.CheckpointId)
            },
            ["player"] = WritePlayer(value.Player),
            ["quests"] = WriteQuests(value.Quests),
            ["facilities"] = WriteFacilities(value.Facilities),
            ["settledRaidSequence"] = value.SettledRaidSequence,
            ["raid"] = WriteRaid(value.Raid),
            // 난이도와 플레이 상태도 캠페인 필드와 같은 파일에 한 번에 기록해 서로 어긋나지 않게 한다.
            ["difficulty"] = WriteDifficulty(value.Difficulty),
            // 소유자 기록 묶음은 파일에 드러내지 않고 gameplay 객체의 선택 필드로 함께 쓴다(사용자 결정 D1).
            ["gameplay"] = GameplaySaveJson.Write(value.Gameplay, value.Records)
        };

        // 캠페인의 여덟 필드를 모두 필수로 읽고 모르는 필드는 거부한다. 저장된 값은 추정해 채우지 않고 그대로 TryCreate에 넘긴다.
        // 팩토리가 거부하면 StrictObject.Create가 'gameSave.campaign: 값이 올바르지 않습니다.'를 기록한다. 아래 Read* 함수도 같은 방식이다.
        // gameplay 객체 하나에서 플레이 상태와 소유자 기록 묶음을 함께 읽는다. 필드는 예전과 같은 순서로 읽어 첫 오류가 바뀌지 않는다.
        internal static CampaignState Read(StrictObject value)
        {
            CampaignProgress progress = value.Object("progress", ReadProgress);
            PlayerState? player = value.NullableObject("player", ReadPlayer);
            IReadOnlyList<QuestProgress> quests = value.ObjectArray("quests", ReadQuest);
            IReadOnlyList<FacilityProgress> facilities = value.ObjectArray("facilities", ReadFacility);
            long settledRaidSequence = value.Int64("settledRaidSequence");
            RaidState? raid = value.NullableObject("raid", ReadRaid);
            DifficultyState? difficulty = value.NullableObject("difficulty", ReadDifficulty);
            (GameplayPersistenceState gameplay, CampaignRecordsState records) = value.Object("gameplay", GameplaySaveJson.ReadWithRecords);
            return value.Create(CampaignState.TryCreate(progress, player, quests, facilities, settledRaidSequence, raid, difficulty,
                gameplay, records, out CampaignState? created), created);
        }

        // 진행 단계 토큰과 체크포인트를 읽는다. 진행 토큰은 쓰기 함수로 되짚어 찾고, 람다는 캡처가 없어 대리자를 한 번만 만든다.
        // 체크포인트는 명시 null이나 문자열이고, 공백은 CampaignProgress.TryCreate가 거부한다.
        static CampaignProgress ReadProgress(StrictObject value) => value.Create(CampaignProgress.TryCreate(
            ReadToken(value, "stage", _stages, static stage => WriteStage(stage), "값은 알 수 없는 진행 단계입니다."),
            value.NullableText("checkpointId"), out CampaignProgress? created), created);

        // 난이도를 고르지 않았으면 null을 쓴다. 골랐으면 규칙 ID와 원본 규칙 키의 수치를 함께 기록한다.
        static JToken WriteDifficulty(DifficultyState? value) => WriteOptional(value, static difficulty => new JObject
        {
            ["ruleIndex"] = (int)difficulty.RuleIndex,
            ["rules"] = new JObject
            {
                ["damageFactor_ToPlayer"] = difficulty.DamageFactorToPlayer,
                ["enemyHealthFactor"] = difficulty.EnemyHealthFactor,
                ["spawnDeadBody"] = difficulty.SpawnDeadBody,
                ["fogOfWar"] = difficulty.FogOfWar,
                ["advancedDebuffMode"] = difficulty.AdvancedDebuffMode,
                ["saveDeadbodyCount"] = difficulty.SaveDeadbodyCount,
                ["recoilMultiplier"] = difficulty.RecoilMultiplier,
                ["enemyReactionTimeFactor"] = difficulty.EnemyReactionTimeFactor,
                ["enemyAttackTimeSpaceFactor"] = difficulty.EnemyAttackTimeSpaceFactor,
                ["enemyAttackTimeFactor"] = difficulty.EnemyAttackTimeFactor
            }
        });

        // 저장된 규칙은 현재 프리셋으로 덮지 않고 정확한 키와 자료형 그대로 복원한다. 여기서는 숫자 표현만 검사하고,
        // 규칙 ID와 음수 수치는 DifficultyState.TryCreate가 거부한다. rules 콜백 안에서 불러 거부 경로가 'gameSave.campaign.difficulty.rules'가 된다.
        static DifficultyState ReadDifficulty(StrictObject value)
        {
            var ruleIndex = (DifficultyRuleIndex)value.Int32("ruleIndex");
            return value.Object("rules", rules => rules.Create(DifficultyState.TryCreate(ruleIndex,
                rules.FiniteFloat("damageFactor_ToPlayer"),
                rules.FiniteFloat("enemyHealthFactor"),
                rules.Boolean("spawnDeadBody"),
                rules.Boolean("fogOfWar"),
                rules.Boolean("advancedDebuffMode"),
                rules.Int32("saveDeadbodyCount"),
                rules.FiniteFloat("recoilMultiplier"),
                rules.FiniteFloat("enemyReactionTimeFactor"),
                rules.FiniteFloat("enemyAttackTimeSpaceFactor"),
                rules.FiniteFloat("enemyAttackTimeFactor"),
                out DifficultyState? created), created));
        }

        // 생존 값을 포착하지 않았으면 null을 쓰고, 포착했으면 네 수치를 기록한다.
        static JToken WritePlayer(PlayerState? value) => WriteOptional(value, static player => new JObject
        {
            ["health"] = player.Health,
            ["stamina"] = player.Stamina,
            ["energy"] = player.Energy,
            ["hydration"] = player.Hydration
        });

        // 생존 상태의 네 수치를 유한 float로 읽는다. 음수는 PlayerState.TryCreate가 거부한다.
        static PlayerState ReadPlayer(StrictObject value) => value.Create(PlayerState.TryCreate(value.FiniteFloat("health"),
            value.FiniteFloat("stamina"), value.FiniteFloat("energy"), value.FiniteFloat("hydration"),
            out PlayerState? created), created);

        // 퀘스트와 목표의 저장 ID, 받은 보상 ID 목록을 모두 기록한다.
        static JArray WriteQuests(IReadOnlyList<QuestProgress> quests) => WriteList(quests, quest => new JObject
        {
            ["originalQuestId"] = quest.OriginalQuestId,
            ["status"] = WriteQuestStatus(quest.Status),
            ["objectives"] = WriteList(quest.Objectives, objective => new JObject
            {
                ["objectiveId"] = objective.ObjectiveId,
                ["count"] = objective.Count
            }),
            ["claimedRewardIds"] = WriteInts(quest.ClaimedRewardIds)
        });

        // 상태 토큰은 쓰기 함수로 되짚어 찾는다. claimedRewardIds만 선택 필드라, 없으면 빈 목록으로 읽고 RewardClaimed 상태는
        // 그대로 둔다. 명시 null은 거부한다. 목표나 보상 ID의 중복은 QuestProgress.TryCreate가 거부한다.
        static QuestProgress ReadQuest(StrictObject value) => value.Create(QuestProgress.TryCreate(value.Int32("originalQuestId"),
            ReadToken(value, "status", _questStatuses, static status => WriteQuestStatus(status), "값은 알 수 없는 퀘스트 상태입니다."),
            value.ObjectArray("objectives", ReadObjective), OptionalInt32Array(value, "claimedRewardIds"),
            out QuestProgress? created), created);

        // 목표 ID와 수량을 읽는다. 공백 ID와 음수는 QuestObjectiveProgress.TryCreate가 거부한다.
        static QuestObjectiveProgress ReadObjective(StrictObject value) => value.Create(QuestObjectiveProgress.TryCreate(
            value.Text("objectiveId"), value.Int32("count"), out QuestObjectiveProgress? created), created);

        // 시설의 원본 ID와 확정 단계만 기록한다.
        static JArray WriteFacilities(IReadOnlyList<FacilityProgress> facilities) => WriteList(facilities, facility => new JObject
        {
            ["originalFacilityId"] = facility.OriginalFacilityId,
            ["level"] = facility.Level
        });

        // 시설 ID와 정수 단계를 읽는다. 공백 ID와 음수 단계는 FacilityProgress.TryCreate가 거부한다.
        static FacilityProgress ReadFacility(StrictObject value) => value.Create(FacilityProgress.TryCreate(
            value.Text("originalFacilityId"), value.Int32("level"), out FacilityProgress? created), created);

        // 출격이 없으면 null로 쓰고 있으면 순번과 대기 결과를 함께 기록한다.
        static JToken WriteRaid(RaidState? value) => WriteOptional(value, static raid => new JObject
        {
            ["sequence"] = raid.Sequence,
            ["raidId"] = raid.RaidId,
            ["result"] = WriteRaidResult(raid.Result)
        });

        // 출격 순번과 ID, 정산 대기 결과를 읽는다. 결과는 명시 null이나 객체이고, 출격과의 순번과 ID 대조는 RaidState.TryCreate가 한다.
        static RaidState ReadRaid(StrictObject value) => value.Create(RaidState.TryCreate(value.Int64("sequence"), value.Text("raidId"),
            value.NullableObject("result", ReadRaidResult), out RaidState? created), created);

        // 정산 대기 결과를 하나의 객체로 기록한다. 잔존 소지품은 gameplay의 원본 그래프 안에 있다.
        static JToken WriteRaidResult(RaidResult? value) => WriteOptional(value, static result => new JObject
        {
            ["sequence"] = result.Sequence,
            ["raidId"] = result.RaidId,
            ["outcome"] = WriteOutcome(result.Outcome),
            // 정산 후보의 생존 값과 원본 플레이 상태도 저장한다. 포착하지 않은 값은 명시 null이다.
            ["player"] = WritePlayer(result.Player),
            ["gameplay"] = WriteOptional(result.Gameplay, GameplaySaveJson.Write)
        });

        // 정산 결과의 다섯 필드를 모두 필수로 읽는다. 포착하지 않은 생존 값과 플레이 상태는 명시 null로 보존한다.
        // 결과 토큰은 쓰기 함수로 되짚어 찾고, 순번과 ID 규칙은 RaidResult.TryCreate가 검사한다.
        static RaidResult ReadRaidResult(StrictObject value) => value.Create(RaidResult.TryCreate(value.Int64("sequence"),
            value.Text("raidId"),
            ReadToken(value, "outcome", _outcomes, static outcome => WriteOutcome(outcome), "값은 알 수 없는 출격 결과입니다."),
            value.NullableObject("player", ReadPlayer),
            value.NullableObject("gameplay", GameplaySaveJson.Read), out RaidResult? created), created);

        // 열거형 이름이 바뀌어도 저장 형식이 유지되도록, 이름을 자동 변환하지 않고 고정 토큰으로 기록한다.
        // 읽기도 쓰기 함수로 토큰을 되짚어 찾으므로 토큰은 이 함수와 아래 두 쓰기 함수의 switch에만 적는다.
        static string WriteStage(CampaignStage stage) => stage switch
        {
            CampaignStage.Prepare => "prepare",
            CampaignStage.Intro => "intro",
            CampaignStage.Prologue => "prologue",
            CampaignStage.Tutorial => "tutorial",
            CampaignStage.BaseUnlocked => "base-unlocked",
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };

        // 퀘스트 상태도 고정 토큰으로 기록한다.
        static string WriteQuestStatus(QuestStatus status) => status switch
        {
            QuestStatus.Active => "active",
            QuestStatus.Completed => "completed",
            QuestStatus.RewardClaimed => "reward-claimed",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        // 출격 결과도 고정 토큰으로 기록한다.
        static string WriteOutcome(RaidOutcome outcome) => outcome switch
        {
            RaidOutcome.Extracted => "extracted",
            RaidOutcome.Died => "died",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };

        // 읽기가 저장 토큰을 쓰기 함수로 되짚어 찾을 값 목록이다. 쓰기 switch에 값을 더하면 여기에도 더한다.
        // 정의된 값만 담아 읽기는 쓰기 switch의 기본 갈래에 닿지 않고, 첫 값은 모르는 토큰일 때 돌려주는 자리값이다.
        static readonly CampaignStage[] _stages =
        {
            CampaignStage.Prepare, CampaignStage.Intro, CampaignStage.Prologue, CampaignStage.Tutorial, CampaignStage.BaseUnlocked
        };
        static readonly QuestStatus[] _questStatuses = { QuestStatus.Active, QuestStatus.Completed, QuestStatus.RewardClaimed };
        static readonly RaidOutcome[] _outcomes = { RaidOutcome.Extracted, RaidOutcome.Died };
    }
}
