#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원작이 SavesSystem 키로 슬롯에 남기는 진행 기록 묶음이다(사용자 결정 D1, 2026-10-19와 2026-10-20 추가 승인).
    // 특성, 등록 열쇠, 선택 재능, 날씨 시드와 기지 첫 방문 플래그, 낚시 경험, 컷신 재생, 처치 수, 퀘스트 본 적 있음과 확인 필요를 담는다.
    // 저장 기록의 값이라 세션 동안 갱신하지 않는다. 현재 값은 GameManager 자식 소유자가 들고 저장도 그 값을 쓴다.
    // 출격 정산과 상관없이 슬롯 전체에 남는다. 파일에서는 campaign.gameplay의 선택 필드로 묶음 없이 쓴다(GameplaySaveJson).
    public sealed class CampaignRecordsState
    {
        // 원작 WeatherManager 강수 시드의 상한(제외)이다. GameClock이 같은 범위로 뽑는다.
        internal const int _weatherSeedLimit = 100000;

        // TryCreate가 쓰는 검사다. 목록마다 null 항목과 키 중복을 거부하고, 열쇠와 퀘스트 ID는 음수를, 컷신 ID는 빈 값을 거부한다.
        // 퀘스트 확인 필요 ID가 퀘스트 기록에 있는지는 CampaignState가 본다. 판정만 하고 복사하지 않으며, 어떤 입력에도 던지거나 기록하지 않는다.
        static bool Check(IReadOnlyList<PerkTreeSaveState>? perks, IReadOnlyList<int>? masterKeys, EndowmentIndex endowment,
            int? weatherSeed, IReadOnlyList<string>? playedCutScenes, IReadOnlyList<KillCountState>? killCounts,
            IReadOnlyList<int>? inspectedQuests, IReadOnlyList<int>? questsNeedingInspection)
        {
            if (!ReadOnlyLists.AreUnique(perks, tree => tree.TreeId)) return false;
            if (!SaveKeys.AreValidIds(masterKeys)) return false;
            if (endowment < EndowmentIndex.None || endowment > EndowmentIndex.Marksman) return false;
            if (weatherSeed.HasValue && (weatherSeed.Value < 0 || weatherSeed.Value >= _weatherSeedLimit)) return false;
            if (!SaveKeys.AreValid(playedCutScenes)) return false;
            if (!ReadOnlyLists.AreUnique(killCounts, kill => kill.EnemyKey)) return false;
            if (!SaveKeys.AreValidIds(inspectedQuests)) return false;
            return SaveKeys.AreValidIds(questsNeedingInspection);
        }

        // 저장 로드와 구역 합치기의 생성 경로다. 모든 인자가 필수이고 null 목록은 누락으로 거절하며, 목록은 여기서 한 번 복사한다.
        // 날씨 시드 null은 아직 뽑지 않았다는 값이다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(IReadOnlyList<PerkTreeSaveState> perks, IReadOnlyList<int> masterKeys, EndowmentIndex endowment,
            int? weatherSeed, bool firstBaseVisitTimeSet, bool hasFished, IReadOnlyList<string> playedCutScenes,
            IReadOnlyList<KillCountState> killCounts, IReadOnlyList<int> inspectedQuests, IReadOnlyList<int> questsNeedingInspection,
            [NotNullWhen(true)] out CampaignRecordsState? value)
        {
            value = Check(perks, masterKeys, endowment, weatherSeed, playedCutScenes, killCounts, inspectedQuests, questsNeedingInspection)
                ? new CampaignRecordsState(ReadOnlyLists.CopyOrEmpty(perks), ReadOnlyLists.CopyOrEmpty(masterKeys), endowment, weatherSeed,
                    firstBaseVisitTimeSet, hasFished, ReadOnlyLists.CopyOrEmpty(playedCutScenes), ReadOnlyLists.CopyOrEmpty(killCounts),
                    ReadOnlyLists.CopyOrEmpty(inspectedQuests), ReadOnlyLists.CopyOrEmpty(questsNeedingInspection))
                : null;
            return value != null;
        }

        // 검증과 복사를 마친 값을 그대로 대입하는 유일한 경로다. TryCreate와 Empty만 쓴다.
        CampaignRecordsState(IReadOnlyList<PerkTreeSaveState> perks, IReadOnlyList<int> masterKeys, EndowmentIndex endowment,
            int? weatherSeed, bool firstBaseVisitTimeSet, bool hasFished, IReadOnlyList<string> playedCutScenes,
            IReadOnlyList<KillCountState> killCounts, IReadOnlyList<int> inspectedQuests, IReadOnlyList<int> questsNeedingInspection)
        {
            Perks = perks;
            MasterKeys = masterKeys;
            Endowment = endowment;
            WeatherSeed = weatherSeed;
            FirstBaseVisitTimeSet = firstBaseVisitTimeSet;
            HasFished = hasFished;
            PlayedCutScenes = playedCutScenes;
            KillCounts = killCounts;
            InspectedQuests = inspectedQuests;
            QuestsNeedingInspection = questsNeedingInspection;
        }

        // 새 게임과, 이 필드들이 없는 옛 저장의 값이다. 재능은 원작 기본값 None(알바 덕)이고 날씨 시드는 아직 뽑지 않았다.
        // 값을 바꾸면 새 게임 저장 JSON이 달라진다.
        public static CampaignRecordsState Empty { get; } = new CampaignRecordsState(Array.Empty<PerkTreeSaveState>(), Array.Empty<int>(),
            EndowmentIndex.None, null, false, false, Array.Empty<string>(), Array.Empty<KillCountState>(), Array.Empty<int>(), Array.Empty<int>());

        // 원작 PerkTree_<id> 저장이다. 해금되었거나 연구 중인 특성만 담고, 없는 특성은 잠김이다.
        public IReadOnlyList<PerkTreeSaveState> Perks { get; }
        // 원작 MasterKeys의 활성 열쇠 아이템 ID이며 등록한 순서다.
        public IReadOnlyList<int> MasterKeys { get; }
        // 원작 Endowment_SelectedIndex다.
        public EndowmentIndex Endowment { get; }
        // 원작 WeatherManagerData의 시드와 FirstTimeToBaseTimeSetted다. 시계 구역 값이라 GameClock이 쓴다.
        public int? WeatherSeed { get; }
        public bool FirstBaseVisitTimeSet { get; }
        // 원작 HasFished(RequireHasFished 조건)와, playOnce 컷신의 원작 CutScene id(원작 키 CutScene_<id>) 목록이다.
        public bool HasFished { get; }
        public IReadOnlyList<string> PlayedCutScenes { get; }
        // 원작 SavesCounter의 적 키별 처치 수다. RequireEnemyKilled 퀘스트 조건이 읽는다.
        public IReadOnlyList<KillCountState> KillCounts { get; }
        // 원작 QuestManager.everInspectedQuest와, 퀘스트별 needInspection이 참인 퀘스트 ID다.
        public IReadOnlyList<int> InspectedQuests { get; }
        public IReadOnlyList<int> QuestsNeedingInspection { get; }
    }
}
