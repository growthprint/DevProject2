#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원본 플레이 상태 전체를 하나의 저장 후보로 묶는다. 소지품, 진행 기록, 사망 기록 묶음은 미포착 null과 빈 값, 교차 규칙을
    // 각자 지키며, 묶음 사이에 걸친 규칙은 없다.
    public sealed class GameplayPersistenceState
    {
        // TryCreate가 쓰는 검사다. 묶음 안의 규칙은 각 묶음을 만들 때 이미 검사했으므로 여기서는 누락과 묶음 밖 값만 본다.
        // 판정만 하고 복사하지 않으며, 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(InventoryPersistenceState? inventory, GameplayClockState? clock, ProgressionPersistenceState? progression,
            DeathPersistenceState? deaths, IReadOnlyList<MapMarkerState>? mapMarkers, long experience, DateTime? savedAtUtc,
            NoteIndexState? notes, IReadOnlyList<string>? brokenBreakables, IReadOnlyList<string>? constructionSites)
        {
            if (inventory == null) return false;
            if (clock == null) return false;
            if (progression == null) return false;
            if (deaths == null) return false;
            if (!CheckMapMarkers(mapMarkers)) return false;
            // 쪽지는 만들 때 목록 규칙을 이미 검사했으므로 누락만 본다. 레벨 물체 키 목록은 빈 키와 중복을 거부한다.
            if (notes == null) return false;
            if (!SaveKeys.AreValid(brokenBreakables) || !SaveKeys.AreValid(constructionSites)) return false;
            if (experience < 0) return false;
            if (savedAtUtc.HasValue && savedAtUtc.Value.Kind != DateTimeKind.Utc)
                return false;
            return true;
        }

        // 저장 로드용 생성 경로다. 모든 인자가 필수이고 null 목록은 누락으로 거절하며, 마커와 레벨 물체 키 목록은 여기서 한 번 복사한다.
        // 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(InventoryPersistenceState inventory, GameplayClockState clock,
            ProgressionPersistenceState progression, DeathPersistenceState deaths, IReadOnlyList<MapMarkerState> mapMarkers,
            long experience, DateTime? savedAtUtc, NoteIndexState notes, IReadOnlyList<string> brokenBreakables,
            IReadOnlyList<string> constructionSites, [NotNullWhen(true)] out GameplayPersistenceState? value)
        {
            value = Check(inventory, clock, progression, deaths, mapMarkers, experience, savedAtUtc, notes, brokenBreakables, constructionSites)
                ? new GameplayPersistenceState(inventory, clock, progression, deaths,
                ReadOnlyLists.CopyOrEmpty(mapMarkers), experience, savedAtUtc, notes, ReadOnlyLists.CopyOrEmpty(brokenBreakables),
                ReadOnlyLists.CopyOrEmpty(constructionSites)) : null;

            return value != null;
        }

        // 검증과 복사를 마친 값을 그대로 대입하는 유일한 경로다. 모든 인자가 필수라 필드가 늘면 호출부에서 컴파일 오류가 난다.
        // TryCreate와 각 With*가 이 경로를 쓰며, With*는 바뀐 값만 넣고 나머지 묶음과 목록은 참조를 그대로 공유한다.
        GameplayPersistenceState(InventoryPersistenceState inventory, GameplayClockState clock,
            ProgressionPersistenceState progression, DeathPersistenceState deaths, IReadOnlyList<MapMarkerState> mapMarkers,
            long experience, DateTime? savedAtUtc, NoteIndexState notes, IReadOnlyList<string> brokenBreakables,
            IReadOnlyList<string> constructionSites)
        {
            Inventory = inventory;
            Clock = clock;
            Progression = progression;
            Deaths = deaths;
            MapMarkers = mapMarkers;
            Experience = experience;
            SavedAtUtc = savedAtUtc;
            Notes = notes;
            BrokenBreakables = brokenBreakables;
            ConstructionSites = constructionSites;
        }

        // 지도 마커 목록이 없거나 null 항목이나 ID 중복이 있으면 false다. TryCreate의 Check가 쓴다.
        static bool CheckMapMarkers(IReadOnlyList<MapMarkerState>? mapMarkers) =>
            ReadOnlyLists.AreUnique(mapMarkers, marker => marker.Id);

        // 현재 소유 그래프와 퀵슬롯이다. 사망 당시 그래프는 Deaths가 따로 보관한다.
        public InventoryPersistenceState Inventory { get; }
        // 저장 기록의 시계이며 세션 동안 갱신하지 않는다. 현재 시계는 GameClock이 소유하고 저장도 그 값을 쓴다.
        public GameplayClockState Clock { get; }
        // 런타임 명령이 바꾸지 않는 원본 진행 기록이다.
        public ProgressionPersistenceState Progression { get; }
        // 시체 목록과 마지막 사망 캐릭터의 기록, 그 토큰이다.
        public DeathPersistenceState Deaths { get; }
        // 저장 기록의 지도 마커이며 세션 동안 갱신하지 않는다. 현재 마커는 MapMarkers 컴포넌트가 소유하고 표시와 저장도 그 값을 쓴다.
        public IReadOnlyList<MapMarkerState> MapMarkers { get; }
        // 저장 기록의 총 누적 경험치이며 세션 동안 갱신하지 않는다. 현재 값은 ExperienceManager가 소유하고 저장도 그 값을 쓴다.
        // 레벨은 저장하지 않고 OriginalExperience 규칙으로 계산한다.
        public long Experience { get; }
        public DateTime? SavedAtUtc { get; }
        // 저장 기록의 쪽지 상태와 레벨 물체 키이며 세션 동안 갱신하지 않는다. 현재 값은 NoteIndex와 SavedLevelObjects가 소유하고
        // 저장도 그 값을 쓴다. 부서진 벽은 원작 Breakable.saveKey, 건설 지점은 원작 ConstructionSite._key다.
        public NoteIndexState Notes { get; }
        public IReadOnlyList<string> BrokenBreakables { get; }
        public IReadOnlyList<string> ConstructionSites { get; }

        // 퀵슬롯만 바꾸고 현재 소유 그래프는 공유한다. 플레이어 입력은 SessionInventoryCommands.TryBindShortcut이 먼저 거절하고,
        // 그래프와의 교차 규칙을 지킨 퀵슬롯만 넘긴다.
        public GameplayPersistenceState WithShortcuts(ItemShortcutState shortcuts)
        {
            // 퀵슬롯 등록이 창고 임시 보관함을 지우지 않도록 현재 보관함을 그대로 넘긴다.
            return new GameplayPersistenceState(new InventoryPersistenceState(Inventory.Items, shortcuts, Inventory.StorageBuffer),
                Clock, Progression, Deaths, MapMarkers, Experience, SavedAtUtc, Notes, BrokenBreakables, ConstructionSites);
        }

        // 출격과 상관없이 슬롯 전체에 남는 기록(지도 마커, 쪽지, 레벨 물체 키)만 slot의 값으로 바꾼다. 레거시 출격 결과를 정산할 때
        // 결과의 플레이 상태가 현재 슬롯의 이 기록을 지우지 않게 한다. 목록은 검증된 불변 값이라 복사하지 않는다.
        public GameplayPersistenceState WithSlotRecords(GameplayPersistenceState slot)
        {
            return new GameplayPersistenceState(Inventory, Clock, Progression, Deaths, slot.MapMarkers,
                Experience, SavedAtUtc, slot.Notes, slot.BrokenBreakables, slot.ConstructionSites);
        }

        // 저장 시각만 바꾸고 나머지 하위 상태는 참조를 공유한다. 호출자는 코드가 만든 UTC 시각만 넘긴다.
        public GameplayPersistenceState WithSavedAtUtc(DateTime utc)
        {
            return new GameplayPersistenceState(Inventory, Clock, Progression, Deaths, MapMarkers, Experience, utc, Notes, BrokenBreakables,
                ConstructionSites);
        }

        // 소유 그래프를 바꾸고 현재 퀵슬롯을 새 그래프에 맞춘다. 나머지 하위 상태는 공유한다. 그래프는 작업 사본이 소유 규칙을
        // 지켜 만든 null 아닌 값이고 퀵슬롯은 소지품 묶음이 맞춘다. 다른 퀵슬롯을 함께 정하려면 이 뒤에 WithShortcuts를 부른다.
        public GameplayPersistenceState WithItems(ItemOwnershipState items)
        {
            return new GameplayPersistenceState(Inventory.WithItems(items), Clock, Progression, Deaths, MapMarkers,
                Experience, SavedAtUtc, Notes, BrokenBreakables, ConstructionSites);
        }

        // 시체 목록만 바꾸고 마지막 사망 기록과 토큰은 보존한다. null은 미포착이다. 시체 회수는 검증된 목록에서 하나를 지우거나
        // 같은 표식의 남은 유실물로 바꿀 뿐이라 규칙을 다시 검사하지 않는다.
        public GameplayPersistenceState WithLostBodies(IReadOnlyList<LostBodyState>? lostBodies)
        {
            return new GameplayPersistenceState(Inventory, Clock, Progression,
                new DeathPersistenceState(lostBodies, Deaths.LastDeadCharacter, Deaths.DeadCharacterToken), MapMarkers, Experience,
                SavedAtUtc, Notes, BrokenBreakables, ConstructionSites);
        }

        // 사망 한 번의 결과를 반영한다. 남은 소유 그래프와 퀵슬롯, 시체 목록과 마지막 사망 기록을 두 묶음으로 받는다.
        // 묶음 안의 규칙과 남은 그래프, 사망 기록이 있다는 것은 사망 계산이 묶음을 만들 때 지킨다.
        // 남은 위험: 로드가 시체 토큰과 사망 토큰의 순서를 검사하지 않아, 손상된 저장에서는 새 시체 토큰이 이미 있는 토큰과 겹칠 수 있다.
        public GameplayPersistenceState WithDeathRecord(InventoryPersistenceState inventory, DeathPersistenceState deaths)
        {
            return new GameplayPersistenceState(inventory, Clock, Progression, deaths, MapMarkers, Experience, SavedAtUtc, Notes,
                BrokenBreakables, ConstructionSites);
        }

        // 새 게임 값을 정하는 유일한 곳이다. 원본 캐릭터가 생기기 전이라 아이템 그래프는 미포착(null)이고 퀵슬롯 여섯 칸은 비어 있다.
        // 시계는 기록된 플레이 시간 0으로 시작하고 날짜와 시각은 아직 포착하지 않는다. 진행과 사망 기록은 빈 값이고 첫 사망 토큰은 0이다.
        // 그 밖의 미포착 null은 저장 로드(TryCreate)에서만 생긴다. 쪽지와 레벨 물체 키는 빈 목록이다. 값을 바꾸면 새 게임 저장 JSON이 달라진다.
        public static GameplayPersistenceState NewGame() => new GameplayPersistenceState(
            new InventoryPersistenceState(null, ItemShortcutState.Empty()),
            new GameplayClockState(0, 0, null, null),
            new ProgressionPersistenceState(
                new BuildingPersistenceState(Array.Empty<BuildingInstallationState>(), Array.Empty<BuildingTokenState>()),
                new EconomyPersistenceState(0, Array.Empty<int>(), Array.Empty<int>()),
                Array.Empty<BeaconUnlockState>(), Array.Empty<string>(), Array.Empty<StockShopState>(), null),
            new DeathPersistenceState(Array.Empty<LostBodyState>(), null, 0),
            Array.Empty<MapMarkerState>(), 0, null, NoteIndexState.Empty, Array.Empty<string>(), Array.Empty<string>());
    }
}
