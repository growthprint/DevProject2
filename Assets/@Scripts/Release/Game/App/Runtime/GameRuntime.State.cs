#nullable enable annotations
using System;

namespace Game.App
{
    // 세션 상태 소유자가 플레이어 명령을 받을지 정하는 조건과 그 연결, 세션 허브와 저장 알림의 연결을 맡는다.
    // 메인 스레드에서만 읽는다.
    public partial class GameRuntime
    {
        // 플레이 세션이 있는지다. 상태 소유자의 값은 세션이 있을 때만 의미가 있으므로 읽는 쪽이 먼저 확인한다.
        public bool HasSession => _session != null;

        // ConnectChildren이 묶은 두 흐름이다. 루트가 파괴된 뒤에도 도는 정리와 명령 관문이 Live 대신 이 참조를 읽는다.
        SceneFlow _scenes;
        CustomizationFlow _customization;

        // 상태 소유자의 Try* 명령 관문이다. 전환 없이 플레이나 일시정지 중일 때, 또는 편집 확정 동안만 열고, 두 경우 모두 확정 세션이 있다.
        // 편집 확정은 독점 작업 안에서 돌아 CanMutateSession이 거짓이므로, 그동안은 편집 담당의 확정 표시가 소유자 명령을 연다.
        // 소유자는 연결(ConnectChildren) 뒤에만 이 관문을 받아 편집 흐름이 늘 있고, 확정 표시는 루트 파괴 뒤에도 남는 C# 값이라 그대로 읽는다.
        public bool AcceptsSessionCommands => CanMutateSession || _customization.IsCommittingCustomization;

        // 제품 구성과 테스트 하위 클래스가 함께 부른다. GameManager의 저장 관리자, 씬 흐름, 편집 흐름에 이 Runtime을 묶고,
        // 상태 소유자에 명령 관문을 연결한다. 테스트 하위 클래스는 대역을 넣은 GameManager를 먼저 등록해야 한다.
        // 소유자는 명령마다 조건을 다시 읽으므로 한 번만 연결하면 된다. 난이도 소유자는 플레이어 명령이 없어 조건을 받지 않는다.
        private protected void ConnectChildren()
        {
            // 저장 관리자와 두 흐름은 루트가 파괴된 뒤의 정리나 늦은 작업에서도 읽으므로 묶을 때 잡아 둔다.
            // 씬 흐름은 저장 지점을 직접 부르므로 저장 관리자도 함께 받는다.
            SaveManager saves = GameManager.Saves;
            saves.Bind(this);
            _scenes = GameManager.Scenes;
            _customization = GameManager.Customization;
            _scenes.Bind(this, saves);
            _customization.Bind(this);
            Func<bool> accepts = () => AcceptsSessionCommands;
            GameManager.Clock.Configure(accepts);
            GameManager.Markers.Configure(accepts);
            // 쪽지 해금과 레벨 물체 기록도 같은 관문으로 막는다.
            GameManager.Notes.Configure(accepts);
            GameManager.LevelObjects.Configure(accepts);
            // 비콘 해금, 처치 수 기록, 낚시 경험과 컷신 재생 기록도 같은 관문으로 막는다.
            GameManager.Beacons.Configure(accepts);
            GameManager.KillCounts.Configure(accepts);
            GameManager.Flags.Configure(accepts);
            // 건물 배치와 회수도 같은 관문으로 막는다. 퀘스트 연결이 이 소유자를 넘기므로 그보다 먼저 잡는다.
            BuildingManager buildings = GameManager.Buildings;
            buildings.Configure(accepts);
            // 특성 연구 시작과 확정도 같은 관문으로 막는다. 구성 전에는 특성 소유자가 모든 명령을 거절한다.
            GameManager.Perks.Configure(accepts);
            // 제조식 해금도 같은 관문으로 막는다. 구성 전에는 제조 소유자가 모든 해금을 거절한다.
            GameManager.Crafting.Configure(accepts);
            GameManager.Experience.Configure(accepts);
            GameManager.Appearance.Configure(accepts);
            // 경제 소유자는 구성 전에 모든 명령을 거절하므로, 지불·반환·해금이 제품에서 열리도록 같은 관문을 넘긴다.
            GameManager.Economy.Configure(accepts);
            // 피해, 생존 틱, 아이템 효과 같은 생존 수치 명령도 같은 관문으로 막는다.
            _vitals.Configure(accepts);
            ConnectQuests(accepts, buildings);
            ConnectEndowment(accepts);
        }

        // 재능 소유자는 해금 여부를 공통 파일 기록(GlobalProgress)에서 읽고, 퀘스트 변경 알림마다 완료 퀘스트의 재능 해금을 명령에 맡긴다.
        // 원작 UnlockEndowmentWhenQuestComplete 자리다. 정리 뒤 늦은 알림도 잡아 둔 참조를 쓴다.
        void ConnectEndowment(Func<bool> accepts)
        {
            QuestManager quests = GameManager.Quests;
            GlobalProgress global = GameManager.GlobalProgress;
            GameManager.Endowment.Configure(accepts, global.IsEndowmentUnlocked,
                () => GameplayCommands.UnlockEndowmentsOfFinishedQuests(quests, global));
        }

        // 퀘스트 소유자는 가용성에 레벨과 처치 수를, 확인 필요 표시에 제출 아이템 수를, 실시간 과제에 건물, 특성, 비콘 기록을 읽는다.
        // 다른 소유자 형식을 모르게 조회로 넘기고, 불러온 뒤 자동 보상 받기는 보상 명령에 맡긴다. 정리 뒤 늦은 호출도 잡아 둔 참조를 쓴다.
        void ConnectQuests(Func<bool> accepts, BuildingManager buildings)
        {
            QuestManager quests = GameManager.Quests;
            ExperienceManager experience = GameManager.Experience;
            KillCounter kills = GameManager.KillCounts;
            EconomyManager economy = GameManager.Economy;
            quests.Configure(accepts);
            quests.ConfigureSessionLinks(() => experience.Level, kills.GetKillCount, CountOwnedForQuests,
                () => GameplayCommands.RestoreQuestRewardsAfterLoad(quests, experience, economy));
            // 건설, 특성, 비콘 과제는 각 소유자의 지금 기록을 읽는다. 원작 과제 ConstructBuilding처럼 토큰으로 돌려받은 건물은 세지 않고,
            // 원작 UnlockPerk처럼 연구를 확정한 특성만 해금으로 본다. 건설 조회는 토큰 포함 여부를 기본값으로 두려고 람다로 감싼다.
            BeaconManager beacons = GameManager.Beacons;
            PerkTreeManager perks = GameManager.Perks;
            quests.ConfigureWorldQueries(id => buildings.Any(id), perks.IsUnlocked, beacons.IsUnlocked);
        }

        // 원작 ItemUtilities.GetItemCount처럼 플레이어 소유 출처에서 센다. 레이드 중에는 창고를 빼고, 세션이 없으면 0이다.
        long CountOwnedForQuests(int originalItemId)
        {
            GameSessionManager? session = _session;
            if (session == null) return 0;
            return session.Inventory.CountOwned(originalItemId, ItemSourceRules.Owned(session));
        }

        // 저장 관리자가 저장 직전에 부른다. 세션 허브는 캠페인, 소지품 구역만 확정 스냅샷에서 채우고,
        // 외형, 플레이어, 난이도, 시계, 지도 마커, 경험치, 퀘스트, 건물, D1 소유자 구역은 각 소유 컴포넌트가 채운다. 저장 시각은 저장 관리자가 붙인다.
        // 퀘스트 구역을 세션 값으로 쓰면 소유자 값을 덮으므로 QuestManager만 쓴다.
        // 진행 기록 안의 경제와 상점 기록은 EconomyManager의 Economy 구역 값이 대신한다. 창고 임시 보관함은 소지품 구역에 함께 간다.
        // 저장 지점은 플레이나 일시정지 중인 세션에서만 저장하므로 세션은 늘 있다.
        // 앱 시작(StartCoreAsync)이 구독하고 앱 정리(CleanupCoreAsync)가 가장 먼저 끊는다.
        internal void CollectHubSections(GameSaveData data)
        {
            GameSessionSnapshot snapshot = _session.Snapshot();
            CampaignState campaign = snapshot.Campaign;
            GameplayPersistenceState gameplay = campaign.Gameplay;
            // 진행 기록 안의 건물 설치와 토큰은 BuildingManager가 Buildings 구역으로 따로 쓰고, 기록으로 합칠 때 그 값이 대신하므로
            // 진행 기록은 세션 값 그대로 넘긴다.
            data.SetCampaign(snapshot.SceneId, campaign.Progress, campaign.Facilities,
                campaign.SettledRaidSequence, campaign.Raid, gameplay.Progression);
            data.SetInventory(gameplay.Inventory, gameplay.Deaths);
        }

        // 저장 관리자가 새 게임이나 저장 파일을 불러오면 부른다. 세션이 없으면 그 기록으로 세션을 만들고, 있으면 후보로 채택한다.
        // 메뉴의 슬롯 선택도 같은 알림을 보내지만, 세션 진입 중이 아니면 세션을 만들지 않아 메뉴는 세션 없는 상태로 남는다.
        internal void LoadHubSections(GameSaveData data)
        {
            if (_session == null && SessionState != GameSessionState.Starting) return;
            // 불러온 구역은 파일 경계나 새 게임 기본값에서 검증한 기록을 나눈 것이라 다시 합치면 늘 기록이 된다.
            // 이 왕복은 코드 불변식이라 결과를 보지 않고, SaveManagerCollectTests가 저장 픽스처와 새 게임과 이어하기 기록으로 확인한다.
            data.TryToRecord(out GameSessionSnapshot? record, out _);
            if (_session == null) ReplaceSession(record!);
            else _session.Commit(record!);
        }
    }
}
