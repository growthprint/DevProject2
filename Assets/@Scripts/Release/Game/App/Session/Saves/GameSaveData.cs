#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DuckCharacter.AvatarParts;

namespace Game.App
{
    // 저장 파일을 값의 소유자별로 나눈 구역이다. 비트 묶음이라 어떤 구역을 채웠는지 한 값으로 확인한다.
    [Flags]
    public enum GameSaveSections
    {
        None = 0,
        Campaign = 1 << 0,
        Appearance = 1 << 1,
        Player = 1 << 2,
        Difficulty = 1 << 3,
        Inventory = 1 << 4,
        Clock = 1 << 5,
        MapMarkers = 1 << 6,
        Experience = 1 << 7,
        // 저장 시각은 소유자가 아니라 SaveManager가 기록 직전에 쓴다.
        SavedAt = 1 << 8,
        // 쪽지 상태(NoteIndex)와, 부서진 벽과 건설 지점의 원작 키(SavedLevelObjects)다.
        Notes = 1 << 9,
        LevelObjects = 1 << 10,
        // 사용자 결정 D1의 소유자 구역이다. 퀘스트 기록과 본 적 있음·확인 필요(Quests), 특성(PerkTreeManager), 등록 열쇠(MasterKeysManager),
        // 선택 재능(EndowmentManager), 비콘 해금(BeaconManager), 처치 수(KillCounter), 낚시 경험과 컷신 재생(CampaignFlags)이다.
        Quests = 1 << 11,
        Perks = 1 << 12,
        MasterKeys = 1 << 13,
        Endowment = 1 << 14,
        Beacons = 1 << 15,
        KillCounts = 1 << 16,
        Flags = 1 << 17,
        // 기존 economy(계좌 돈, 상품 해금과 확인 대기)와 shops(상점 재고 기록)를 EconomyManager가 쓰는 구역이다.
        Economy = 1 << 18,
        // 원작 Crafting/UnlockedFormulaIDs 자리인 unlockedFormulas를 CraftingManager가 쓰는 구역이다.
        Crafting = 1 << 19,
        // 원작 BuildingManager 저장처럼 건물 설치와 토큰(buildings)을 BuildingManager가 쓰는 구역이다.
        Buildings = 1 << 20,
        // 모든 소유자가 한 번씩 쓴 묶음이다. 건물 구역도 그 소유자가 따로 쓰므로 함께 든다.
        All = Campaign | Appearance | Player | Difficulty | Inventory | Clock | MapMarkers | Experience | SavedAt | Notes | LevelObjects |
            Quests | Perks | MasterKeys | Endowment | Beacons | KillCounts | Flags | Economy | Crafting | Buildings,
    }

    // 원작 SavesSystem의 OnCollectSaveData와 OnSetFile처럼, 저장할 때 소유자들이 자기 구역을 써 넣고 불러올 때 읽어 가는 묶음이다.
    // 메인 스레드에서만 쓴다. 구역마다 그 소유자가 한 번 쓰고, 모든 구역이 차야 저장 기록이 된다. 파일 형식은 GameSaveJson이 그대로 맡는다.
    public sealed class GameSaveData
    {
        // 캠페인 구역이다. 런타임 명령이 바꾸지 않는 원본 진행 기록도 캠페인 소유자가 함께 돌려준다.
        GameScene _sceneId;
        CampaignProgress? _progress;
        IReadOnlyList<QuestProgress>? _quests;
        IReadOnlyList<FacilityProgress>? _facilities;
        long _settledRaidSequence;
        RaidState? _raid;
        ProgressionPersistenceState? _progression;
        // 구역 값 가운데 외형만 바뀔 수 있는 객체라 들어올 때와 나갈 때 복사한다. 나머지는 불변 값이라 참조를 그대로 둔다.
        DuckAvatarPreset? _appearance;
        // null 생존 수치와 null 난이도는 아직 포착하지 않았거나 고르지 않았다는 저장 값이다.
        PlayerState? _player;
        DifficultyState? _difficulty;
        // 소지품 구역은 현재 소유 그래프와 퀵슬롯, 시체 목록과 마지막 사망 기록을 함께 담는다.
        // 창고 임시 보관함도 소지품 묶음 안에 있어 같은 구역으로 온다.
        InventoryPersistenceState? _inventory;
        DeathPersistenceState? _deaths;
        GameplayClockState? _clock;
        IReadOnlyList<MapMarkerState>? _mapMarkers;
        long _experience;
        // 쪽지 구역과 레벨 물체 구역이다. 레벨 물체는 부서진 벽과 건설 지점의 원작 키 목록 두 개를 함께 받는다.
        NoteIndexState? _notes;
        IReadOnlyList<string>? _brokenBreakables;
        IReadOnlyList<string>? _constructionSites;
        // 새 게임 기록처럼 한 번도 쓰지 않은 기록은 null이다.
        DateTime? _savedAtUtc;
        // D1 소유자 구역의 값이다. 시계 구역은 날씨 시드와 기지 첫 방문 플래그를 함께 받고, 비콘 null은 옛 저장의 미포착 값이다.
        IReadOnlyList<int>? _inspectedQuests;
        IReadOnlyList<int>? _questsNeedingInspection;
        IReadOnlyList<PerkTreeSaveState>? _perks;
        IReadOnlyList<int>? _masterKeys;
        EndowmentIndex _endowment;
        IReadOnlyList<BeaconUnlockState>? _beacons;
        IReadOnlyList<KillCountState>? _killCounts;
        bool _hasFished;
        IReadOnlyList<string>? _playedCutScenes;
        int? _weatherSeed;
        bool _firstBaseVisitTimeSet;
        // 경제 구역의 값이다. 둘 다 null은 그 하위 상태를 포착하지 않은 옛 저장 값이다.
        EconomyPersistenceState? _economy;
        IReadOnlyList<StockShopState>? _shops;
        // Crafting 구역의 값이다. null은 해금 목록을 포착하지 않은 옛 저장 값이다.
        IReadOnlyList<string>? _unlockedFormulas;
        // Buildings 구역의 값이다. null은 건물 기록을 포착하지 않은 옛 저장 값이다.
        BuildingPersistenceState? _buildings;

        // 지금까지 채운 구역이다. 저장마다 모든 소유자가 쓰면 All이 된다.
        public GameSaveSections Written { get; private set; }

        // 캠페인 소유자가 쓴다. 출격 순번처럼 구역을 넘는 규칙은 TryToRecord가 저장마다 다시 검사한다.
        // 퀘스트는 Quests 구역이 따로 받는다. 진행 기록의 비콘 목록은 Beacons 구역 값이 대신한다.
        // 진행 기록의 경제와 상점 기록도 Economy 구역 값이 대신한다.
        public void SetCampaign(GameScene sceneId, CampaignProgress progress,
            IReadOnlyList<FacilityProgress> facilities, long settledRaidSequence, RaidState? raid, ProgressionPersistenceState progression)
        {
            _sceneId = sceneId;
            _progress = progress;
            _facilities = facilities;
            _settledRaidSequence = settledRaidSequence;
            _raid = raid;
            _progression = progression;
            Written |= GameSaveSections.Campaign;
        }

        // 소유자가 넘긴 외형을 나중에 고쳐도 모은 값이 바뀌지 않게 복사해 둔다.
        public void SetAppearance(DuckAvatarPreset appearance)
        {
            _appearance = appearance.Clone();
            Written |= GameSaveSections.Appearance;
        }

        // 아래 값 하나짜리 구역은 그 값을 소유한 컴포넌트가 저장마다 한 번씩 쓴다.
        public void SetPlayer(PlayerState? player)
        {
            _player = player;
            Written |= GameSaveSections.Player;
        }

        public void SetDifficulty(DifficultyState? difficulty)
        {
            _difficulty = difficulty;
            Written |= GameSaveSections.Difficulty;
        }

        // 사망 기록이 남은 소유 그래프와 한 번에 바뀌므로 두 묶음을 한 구역으로 받는다.
        public void SetInventory(InventoryPersistenceState inventory, DeathPersistenceState deaths)
        {
            _inventory = inventory;
            _deaths = deaths;
            Written |= GameSaveSections.Inventory;
        }

        // 시계 구역은 원작 WeatherManagerData의 시드와 FirstTimeToBaseTimeSetted도 함께 받는다. 시계 소유자는 늘 시드를 뽑아 두므로 null을 넘기지 않는다.
        public void SetClock(GameplayClockState clock, int weatherSeed, bool firstBaseVisitTimeSet)
        {
            _clock = clock;
            _weatherSeed = weatherSeed;
            _firstBaseVisitTimeSet = firstBaseVisitTimeSet;
            Written |= GameSaveSections.Clock;
        }

        public void SetMapMarkers(IReadOnlyList<MapMarkerState> mapMarkers)
        {
            _mapMarkers = mapMarkers;
            Written |= GameSaveSections.MapMarkers;
        }

        public void SetExperience(long experience)
        {
            _experience = experience;
            Written |= GameSaveSections.Experience;
        }

        // 쪽지 소유자가 저장마다 한 번 쓴다.
        public void SetNotes(NoteIndexState notes)
        {
            _notes = notes;
            Written |= GameSaveSections.Notes;
        }

        // 소유자가 저장마다 새로 만든 배열을 넘기므로 복사하지 않는다. 키 규칙은 TryToRecord가 플레이 상태 규칙으로 검사한다.
        public void SetLevelObjects(IReadOnlyList<string> brokenBreakables, IReadOnlyList<string> constructionSites)
        {
            _brokenBreakables = brokenBreakables;
            _constructionSites = constructionSites;
            Written |= GameSaveSections.LevelObjects;
        }

        // 아래 D1 구역은 그 소유자가 저장마다 한 번 쓴다. 소유자가 저장마다 새로 만든 목록을 넘기므로 복사하지 않고,
        // 규칙은 TryToRecord가 기록 규칙으로 검사한다. 퀘스트 구역은 QuestManager만 쓰므로 기록과 본 적 있음, 확인 필요 목록을 한 호출로 받는다.
        public void SetQuests(IReadOnlyList<QuestProgress> quests, IReadOnlyList<int> inspectedQuests, IReadOnlyList<int> questsNeedingInspection)
        {
            _quests = quests;
            _inspectedQuests = inspectedQuests;
            _questsNeedingInspection = questsNeedingInspection;
            Written |= GameSaveSections.Quests;
        }

        public void SetPerks(IReadOnlyList<PerkTreeSaveState> perks)
        {
            _perks = perks;
            Written |= GameSaveSections.Perks;
        }

        public void SetMasterKeys(IReadOnlyList<int> masterKeys)
        {
            _masterKeys = masterKeys;
            Written |= GameSaveSections.MasterKeys;
        }

        public void SetEndowment(EndowmentIndex endowment)
        {
            _endowment = endowment;
            Written |= GameSaveSections.Endowment;
        }

        // null은 비콘을 포착하지 않은 옛 저장 값이다. 해금하기 전까지는 그대로 null로 되돌려 쓴다.
        public void SetBeacons(IReadOnlyList<BeaconUnlockState>? unlockedBeacons)
        {
            _beacons = unlockedBeacons;
            Written |= GameSaveSections.Beacons;
        }

        public void SetKillCounts(IReadOnlyList<KillCountState> killCounts)
        {
            _killCounts = killCounts;
            Written |= GameSaveSections.KillCounts;
        }

        public void SetFlags(bool hasFished, IReadOnlyList<string> playedCutScenes)
        {
            _hasFished = hasFished;
            _playedCutScenes = playedCutScenes;
            Written |= GameSaveSections.Flags;
        }

        // EconomyManager가 저장마다 한 번 쓴다. null은 포착하지 않은 옛 저장 값이라 그대로 되돌려 쓴다.
        // 상점 키 중복과 경제 목록 규칙은 TryToRecord가 진행 기록 규칙으로 검사한다.
        public void SetEconomy(EconomyPersistenceState? economy, IReadOnlyList<StockShopState>? shops)
        {
            _economy = economy;
            _shops = shops;
            Written |= GameSaveSections.Economy;
        }

        // CraftingManager가 저장마다 한 번 쓴다. 빈 ID와 중복 규칙은 TryToRecord가 진행 기록 규칙으로 검사한다.
        public void SetUnlockedFormulas(IReadOnlyList<string>? unlockedFormulas)
        {
            _unlockedFormulas = unlockedFormulas;
            Written |= GameSaveSections.Crafting;
        }

        // BuildingManager가 저장마다 한 번 쓴다. null은 포착하지 않은 옛 저장 값이라 그대로 되돌려 쓴다.
        // 합칠 때는 캠페인 구역 진행 기록의 건물 값 대신 이 값을 쓴다.
        public void SetBuildings(BuildingPersistenceState? buildings)
        {
            _buildings = buildings;
            Written |= GameSaveSections.Buildings;
        }

        // SaveManager만 소유자 수집이 끝난 뒤 부른다. UTC가 아니면 TryToRecord가 플레이 상태 규칙으로 거절한다.
        internal void SetSavedAt(DateTime utc)
        {
            _savedAtUtc = utc;
            Written |= GameSaveSections.SavedAt;
        }

        // 아래 읽기는 FileLoaded 구독자가 쓴다. 구독자가 받는 묶음은 FromRecord가 모든 구역을 채운 것이다.
        public GameScene SceneId => _sceneId;
        public CampaignProgress Progress => _progress!;
        public IReadOnlyList<QuestProgress> Quests => _quests!;
        public IReadOnlyList<FacilityProgress> Facilities => _facilities!;
        public long SettledRaidSequence => _settledRaidSequence;
        // 레거시 결과도 그대로 담는다. 이어하기는 FileLoaded 전에 그 결과를 정산한다.
        public RaidState? Raid => _raid;
        public ProgressionPersistenceState Progression => _progression!;
        // 구독자 하나가 외형을 고쳐도 다른 구독자와 이 묶음에 새지 않도록 읽을 때마다 복사본을 준다.
        public DuckAvatarPreset Appearance => _appearance!.Clone();
        public PlayerState? Player => _player;
        public DifficultyState? Difficulty => _difficulty;
        public InventoryPersistenceState Inventory => _inventory!;
        public DeathPersistenceState Deaths => _deaths!;
        public GameplayClockState Clock => _clock!;
        public IReadOnlyList<MapMarkerState> MapMarkers => _mapMarkers!;
        public long Experience => _experience;
        // 쪽지와 레벨 물체 구역의 읽기다.
        public NoteIndexState Notes => _notes!;
        public IReadOnlyList<string> BrokenBreakables => _brokenBreakables!;
        public IReadOnlyList<string> ConstructionSites => _constructionSites!;
        // D1 구역의 읽기다. 날씨 시드 null은 그 필드가 없던 기록이라 시계 소유자가 새로 뽑는다. 비콘 null은 미포착이다.
        public int? WeatherSeed => _weatherSeed;
        public bool FirstBaseVisitTimeSet => _firstBaseVisitTimeSet;
        public IReadOnlyList<int> InspectedQuests => _inspectedQuests!;
        public IReadOnlyList<int> QuestsNeedingInspection => _questsNeedingInspection!;
        public IReadOnlyList<PerkTreeSaveState> Perks => _perks!;
        public IReadOnlyList<int> MasterKeys => _masterKeys!;
        public EndowmentIndex Endowment => _endowment;
        public IReadOnlyList<BeaconUnlockState>? UnlockedBeacons => _beacons;
        public IReadOnlyList<KillCountState> KillCounts => _killCounts!;
        public bool HasFished => _hasFished;
        public IReadOnlyList<string> PlayedCutScenes => _playedCutScenes!;
        // 경제 구역의 읽기다. null은 미포착이다.
        public EconomyPersistenceState? Economy => _economy;
        public IReadOnlyList<StockShopState>? Shops => _shops;
        // Crafting 구역의 읽기다. null은 미포착이고 CraftingManager가 빈 목록으로 본다.
        public IReadOnlyList<string>? UnlockedFormulas => _unlockedFormulas;
        // Buildings 구역의 읽기다. null은 미포착이다.
        public BuildingPersistenceState? Buildings => _buildings;

        // 읽은 파일이나 새 게임 기록을 구역으로 나눈다. 다시 TryToRecord로 합치면 같은 JSON이 나오도록 모든 값을 빠짐없이 옮긴다.
        // 외형 getter가 이미 복사본을 주므로 한 번 더 복사하지 않는다.
        internal static GameSaveData FromRecord(GameSessionSnapshot record)
        {
            CampaignState campaign = record.Campaign;
            GameplayPersistenceState gameplay = campaign.Gameplay;
            CampaignRecordsState records = campaign.Records;
            return new GameSaveData
            {
                _sceneId = record.SceneId,
                _progress = campaign.Progress,
                _quests = campaign.Quests,
                _facilities = campaign.Facilities,
                _settledRaidSequence = campaign.SettledRaidSequence,
                _raid = campaign.Raid,
                _progression = gameplay.Progression,
                _appearance = record.Appearance,
                _player = campaign.Player,
                _difficulty = campaign.Difficulty,
                _inventory = gameplay.Inventory,
                _deaths = gameplay.Deaths,
                _clock = gameplay.Clock,
                _mapMarkers = gameplay.MapMarkers,
                _experience = gameplay.Experience,
                // 쪽지와 레벨 물체 구역도 빠짐없이 옮겨 다시 합치면 같은 JSON이 나오게 한다.
                _notes = gameplay.Notes,
                _brokenBreakables = gameplay.BrokenBreakables,
                _constructionSites = gameplay.ConstructionSites,
                _savedAtUtc = gameplay.SavedAtUtc,
                // D1 구역도 빠짐없이 옮긴다. 비콘은 진행 기록 안의 목록을 Beacons 구역으로 꺼낸다.
                _inspectedQuests = records.InspectedQuests,
                _questsNeedingInspection = records.QuestsNeedingInspection,
                _perks = records.Perks,
                _masterKeys = records.MasterKeys,
                _endowment = records.Endowment,
                _beacons = gameplay.Progression.UnlockedBeacons,
                _killCounts = records.KillCounts,
                _hasFished = records.HasFished,
                _playedCutScenes = records.PlayedCutScenes,
                _weatherSeed = records.WeatherSeed,
                _firstBaseVisitTimeSet = records.FirstBaseVisitTimeSet,
                // 진행 기록 안의 경제와 상점 기록을 Economy 구역으로 꺼낸다.
                _economy = gameplay.Progression.Economy,
                _shops = gameplay.Progression.Shops,
                // 진행 기록 안의 해금 제조식 목록을 Crafting 구역으로 꺼낸다.
                _unlockedFormulas = gameplay.Progression.UnlockedFormulas,
                // 진행 기록 안의 건물 설치와 토큰을 Buildings 구역으로 꺼낸다.
                _buildings = gameplay.Progression.Buildings,
                Written = GameSaveSections.All,
            };
        }

        // 모든 구역을 저장 로드와 같은 팩터리로 합친다. 구역은 소유자 구독이나 FromRecord가 모두 채운다.
        // 구역마다 따로 모은 값이라 구역을 넘는 규칙을 저장마다 다시 검사하고, 실패해도 던지거나 기록하지 않고 "<경로>: <이유>" 문구를 돌려준다.
        internal bool TryToRecord([NotNullWhen(true)] out GameSessionSnapshot? record, out string error)
        {
            record = null;
            // 캠페인 구역의 진행 기록에서 암시장만 가져오고, 건물은 Buildings, 비콘은 Beacons, 경제와 상점은 Economy,
            // 해금 제조식은 Crafting 구역 값으로 넣어 다시 만든다. 세션 기록의 그 값들은 쓰지 않는다.
            ProgressionPersistenceState? progression = null;
            if (_progression == null || !ProgressionPersistenceState.TryCreate(_buildings, _economy, _beacons,
                    _unlockedFormulas, _shops, _progression.BlackMarket, out progression))
            {
                // 이 검사는 Economy, Crafting 구역 값도 보므로 결함 보고가 원인 구역을 빠뜨리지 않게 함께 적는다.
                error = "campaign.gameplay: 진행 기록, 비콘, 경제, 상점, 제조식 구역 값이 진행 기록 규칙에 맞지 않습니다.";
                return false;
            }
            // 레벨 물체 키 목록은 소유자가 따로 모은 값이라 빈 키와 중복 규칙도 여기서 함께 검사된다.
            if (!GameplayPersistenceState.TryCreate(_inventory!, _clock!, progression, _deaths!, _mapMarkers!, _experience, _savedAtUtc,
                    _notes!, _brokenBreakables!, _constructionSites!, out GameplayPersistenceState? gameplay))
            {
                error = "campaign.gameplay: 구역 값이 플레이 상태 규칙에 맞지 않습니다.";
                return false;
            }
            // D1 소유자 구역을 기록 묶음으로 합친다. 구역이 빠졌으면 null 목록이라 여기서 거절된다.
            if (!CampaignRecordsState.TryCreate(_perks!, _masterKeys!, _endowment, _weatherSeed, _firstBaseVisitTimeSet, _hasFished,
                    _playedCutScenes!, _killCounts!, _inspectedQuests!, _questsNeedingInspection!, out CampaignRecordsState? records))
            {
                error = "campaign.gameplay: 소유자 기록 구역 값이 기록 규칙에 맞지 않습니다.";
                return false;
            }
            if (!CampaignState.TryCreate(_progress!, _player, _quests!, _facilities!, _settledRaidSequence, _raid, _difficulty, gameplay,
                    records, out CampaignState? campaign))
            {
                error = "campaign: 구역 값이 캠페인 규칙에 맞지 않습니다.";
                return false;
            }
            if (!GameSessionSnapshot.TryCreate(_sceneId, _appearance!, campaign, out record))
            {
                error = "root: 씬이나 외형이 저장 규칙에 맞지 않습니다.";
                return false;
            }
            error = string.Empty;
            return true;
        }
    }
}
