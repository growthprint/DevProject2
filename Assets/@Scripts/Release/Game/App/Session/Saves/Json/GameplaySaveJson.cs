#nullable enable
using System;
using System.Collections.Generic;
using DevProject.Serialization;
using Newtonsoft.Json.Linq;
using static Game.App.SaveJsonFields;

namespace Game.App
{
    // 게임플레이 저장 부분을 읽고 쓴다. 필드 이름과 자료형을 직접 적어 자동 직렬화가 저장 형식을 바꾸지 못하게 한다.
    // 하위 상태마다 ReadX와 WriteX 쌍을 둔다. ReadX는 StrictObject로 형식만 읽고, 값 규칙 검사는 각 상태의 TryCreate에 맡긴다.
    // TryCreate는 실패 이유를 주지 않으므로 StrictObject.Create가 경로를 붙인 고정 문구로 실패를 기록한다.
    internal static class GameplaySaveJson
    {
        // 메모리의 소지품, 진행 기록, 사망 기록 묶음은 파일에 드러내지 않는다. 저장 형식이 유지되도록 18개 필드를 묶음 없이 이 키 순서로 쓴다.
        // 아직 기록되지 않은 하위 상태는 null로, 확인된 빈 목록은 빈 배열로 써서 둘을 구분한다. 선택 필드도 쓰는 쪽은 늘 기록한다.
        internal static JObject Write(GameplayPersistenceState value)
        {
            InventoryPersistenceState inventory = value.Inventory;
            ProgressionPersistenceState progression = value.Progression;
            DeathPersistenceState deaths = value.Deaths;
            return new JObject
            {
                ["items"] = WriteOptional(inventory.Items, ItemGraphSaveJson.Write),
                // 포착하지 않은 퀵슬롯도 다른 하위 상태와 같은 도우미로 명시 null을 쓴다. 람다는 캡처가 없어 대리자를 한 번만 만든다.
                ["shortcuts"] = WriteOptional(inventory.Shortcuts, static shortcuts => WriteInts(shortcuts.InventoryIndexes)),
                ["clock"] = WriteClock(value.Clock),
                ["buildings"] = WriteOptional(progression.Buildings, WriteBuildings),
                ["economy"] = WriteOptional(progression.Economy, WriteEconomy),
                ["unlockedBeacons"] = WriteNullableList(progression.UnlockedBeacons, WriteBeacon),
                ["unlockedFormulas"] = WriteNullableList(progression.UnlockedFormulas, item => new JValue(item)),
                ["shops"] = WriteNullableList(progression.Shops, WriteShop),
                ["blackMarket"] = WriteOptional(progression.BlackMarket, WriteMarket),
                ["lostBodies"] = WriteNullableList(deaths.LostBodies, WriteBody),
                ["savedAtUtcTicks"] = NumberOrNull(value.SavedAtUtc?.Ticks),
                ["lastDeadCharacter"] = WriteOptional(deaths.LastDeadCharacter, ItemGraphSaveJson.Write),
                ["deadCharacterToken"] = NumberOrNull(deaths.DeadCharacterToken),
                ["experience"] = value.Experience,
                ["mapMarkers"] = WriteList(value.MapMarkers, WriteMapMarker),
                // 원작 NoteIndexData의 두 목록, 원작 Breakable과 ConstructionSite 저장 키 목록이다.
                ["notes"] = WriteNotes(value.Notes),
                ["brokenBreakables"] = WriteTexts(value.BrokenBreakables),
                ["constructionSites"] = WriteTexts(value.ConstructionSites)
            };
        }

        // campaign.gameplay용이다. 위 18개 필드 뒤에 소유자 기록 묶음의 선택 필드를 이 키 순서로 붙인다(사용자 결정 D1).
        // 쓰는 쪽은 늘 기록한다. 날씨 시드만 아직 뽑지 않은 기록(null)이면 쓰지 않는다. Why: 명시 null은 읽기가 거부하고, 빠진 필드는 다음 불러오기에서 새로 뽑는다.
        // 출격 결과의 gameplay는 소유자 기록이 없어 위 Write를 그대로 쓴다.
        // 창고 임시 보관함(storageBuffer, 2026-10-20 D1 추가)도 campaign.gameplay에만 쓴다. 항목마다 items와 같은 그래프 형식이다.
        // 출격 결과는 레거시 기록이라 보관함을 담지 않는다. Why: 보관함은 그 형식보다 나중에 생겼고, 이어하기가 결과를 먼저 정산한다.
        internal static JObject Write(GameplayPersistenceState value, CampaignRecordsState records)
        {
            JObject json = Write(value);
            json["storageBuffer"] = WriteList(value.Inventory.StorageBuffer, ItemGraphSaveJson.Write);
            json["perks"] = WriteList(records.Perks, WritePerkTree);
            json["masterKeys"] = WriteInts(records.MasterKeys);
            json["endowment"] = (int)records.Endowment;
            if (records.WeatherSeed.HasValue) json["weatherSeed"] = records.WeatherSeed.Value;
            json["firstBaseVisitTimeSet"] = records.FirstBaseVisitTimeSet;
            json["hasFished"] = records.HasFished;
            json["playedCutScenes"] = WriteTexts(records.PlayedCutScenes);
            json["killCounts"] = WriteList(records.KillCounts, WriteKillCount);
            json["inspectedQuests"] = WriteInts(records.InspectedQuests);
            json["questsNeedingInspection"] = WriteInts(records.QuestsNeedingInspection);
            return json;
        }

        // campaign.gameplay용이다. 같은 객체에서 위 18개 필드를 먼저 읽고 소유자 기록 필드를 읽는다. 모르는 필드 검사는 이 콜백이 끝난 뒤 한 번 한다.
        internal static (GameplayPersistenceState Gameplay, CampaignRecordsState Records) ReadWithRecords(StrictObject value)
        {
            // 캠페인 gameplay만 보관함 필드를 읽는다.
            GameplayPersistenceState gameplay = ReadState(value, true);
            return (gameplay, ReadRecords(value));
        }

        // 소유자 기록 필드는 모두 선택이다. 없으면 빈 목록, 재능 0(None), 플래그 거짓, 날씨 시드 미뽑음(null)으로 읽고, 명시 null은 거부한다.
        // 목록의 빈 값, 음수, 중복과 재능, 시드 범위는 CampaignRecordsState.TryCreate가, 확인 필요 ID의 퀘스트 대조는 CampaignState가 거부한다.
        static CampaignRecordsState ReadRecords(StrictObject value)
        {
            // 목록 필드는 공용 선택 배열 도우미로 읽는다. 읽는 순서가 첫 오류를 정하므로 필드 순서는 쓰기와 같게 둔다.
            IReadOnlyList<PerkTreeSaveState> perks = OptionalObjectArray(value, "perks", ReadPerkTree);
            IReadOnlyList<int> masterKeys = OptionalInt32Array(value, "masterKeys");
            int endowment = value.Optional("endowment", 0, (gameplay, name) => gameplay.Int32(name));
            int? weatherSeed = value.Optional("weatherSeed", (int?)null, (gameplay, name) => (int?)gameplay.Int32(name));
            bool firstBaseVisitTimeSet = value.Optional("firstBaseVisitTimeSet", false, (gameplay, name) => gameplay.Boolean(name));
            bool hasFished = value.Optional("hasFished", false, (gameplay, name) => gameplay.Boolean(name));
            IReadOnlyList<string> playedCutScenes = OptionalTextArray(value, "playedCutScenes");
            IReadOnlyList<KillCountState> killCounts = OptionalObjectArray(value, "killCounts", ReadKillCount);
            IReadOnlyList<int> inspectedQuests = OptionalInt32Array(value, "inspectedQuests");
            IReadOnlyList<int> questsNeedingInspection = OptionalInt32Array(value, "questsNeedingInspection");
            return value.Create(CampaignRecordsState.TryCreate(perks, masterKeys, (EndowmentIndex)endowment, weatherSeed, firstBaseVisitTimeSet,
                hasFished, playedCutScenes, killCounts, inspectedQuests, questsNeedingInspection, out CampaignRecordsState? created), created);
        }

        // 트리 항목은 정확히 treeId와 perks, 특성 항목은 정확히 name, unlocked, unlocking, beginUtcTicks다.
        static JObject WritePerkTree(PerkTreeSaveState value) => new JObject
        {
            ["treeId"] = value.TreeId, ["perks"] = WriteList(value.Perks, WritePerk)
        };
        static PerkTreeSaveState ReadPerkTree(StrictObject value) => value.Create(PerkTreeSaveState.TryCreate(value.Text("treeId"),
            value.ObjectArray("perks", ReadPerk), out PerkTreeSaveState? created), created);
        static JObject WritePerk(PerkSaveEntry value) => new JObject
        {
            ["name"] = value.Name, ["unlocked"] = value.Unlocked, ["unlocking"] = value.Unlocking, ["beginUtcTicks"] = value.BeginUtcTicks
        };
        static PerkSaveEntry ReadPerk(StrictObject value) => value.Create(PerkSaveEntry.TryCreate(value.Text("name"), value.Boolean("unlocked"),
            value.Boolean("unlocking"), value.Int64("beginUtcTicks"), out PerkSaveEntry? created), created);

        // 처치 수 항목은 정확히 enemyKey와 count다.
        static JObject WriteKillCount(KillCountState value) => new JObject { ["enemyKey"] = value.EnemyKey, ["count"] = value.Count };
        static KillCountState ReadKillCount(StrictObject value) => value.Create(KillCountState.TryCreate(value.Text("enemyKey"),
            value.Int32("count"), out KillCountState? created), created);

        // 원작 NoteIndexData처럼 해금한 쪽지와 읽은 쪽지 키를 각각 문자열 배열로 쓴다.
        static JObject WriteNotes(NoteIndexState value) => new JObject
        {
            ["unlockedNotes"] = WriteTexts(value.Unlocked),
            ["readNotes"] = WriteTexts(value.Read)
        };
        // 두 목록 모두 필수다. 빈 키와 중복은 NoteIndexState.TryCreate가 거부한다.
        static NoteIndexState ReadNotes(StrictObject value) => value.Create(NoteIndexState.TryCreate(
            value.Array("unlockedNotes", _textItem), value.Array("readNotes", _textItem), out NoteIndexState? created), created);

        // 18개 필드를 모두 읽은 뒤 세 묶음을 만들어 GameplayPersistenceState로 합친다. TryCreate 인자는 모두 필수라 필드가 늘면 여기서 컴파일 오류가 난다.
        // null을 허용하는 필드도 키는 있어야 한다. 키 누락은 experience(0), mapMarkers, notes, brokenBreakables, constructionSites(빈 목록)만
        // 허용하고, 명시 null은 거부한다.
        // 사망 토큰은 새로 만들지 않고 uint 범위만 받는다. null이나 중복 ID, 잘못된 좌표가 있는 마커는 빈 지도로 숨기지 않고 오류로 낸다.
        // 오류는 처음 것만 기록된다. 앞 묶음이 실패하면 뒤 TryCreate는 null 입력을 받아 조용히 실패하며, 검사 함수는 어떤 입력에도 던지지 않는다.
        // 출격 결과의 gameplay용이다. 보관함 필드를 읽지 않으므로 그 필드가 있으면 모르는 필드로 거부되고, 보관함은 빈 목록이다.
        internal static GameplayPersistenceState Read(StrictObject value) => ReadState(value, false);

        // withStorageBuffer는 캠페인 gameplay일 때만 참이다. 보관함은 레벨 물체 키 다음에 선택 필드로 읽는다.
        static GameplayPersistenceState ReadState(StrictObject value, bool withStorageBuffer)
        {
            ItemOwnershipState? items = value.NullableObject("items", ItemGraphSaveJson.Read);
            // 정수와 문자열 배열 항목은 SaveJsonFields의 공용 읽기 대리자로 읽어 필드마다 같은 람다를 만들지 않는다.
            IReadOnlyList<int>? shortcuts = value.NullableArray("shortcuts", _int32Item);
            GameplayClockState clock = value.Object("clock", ReadClock);
            BuildingPersistenceState? buildings = value.NullableObject("buildings", ReadBuildings);
            EconomyPersistenceState? economy = value.NullableObject("economy", ReadEconomy);
            IReadOnlyList<BeaconUnlockState>? beacons = value.NullableArray("unlockedBeacons",
                (reader, token, path) => reader.ObjectItem(token, path, ReadBeacon));
            IReadOnlyList<string>? formulas = value.NullableArray("unlockedFormulas", _textItem);
            IReadOnlyList<StockShopState>? shops = value.NullableArray("shops",
                (reader, token, path) => reader.ObjectItem(token, path, ReadShop));
            BlackMarketState? blackMarket = value.NullableObject("blackMarket", ReadMarket);
            IReadOnlyList<LostBodyState>? lostBodies = value.NullableArray("lostBodies",
                (reader, token, path) => reader.ObjectItem(token, path, ReadBody));
            long? savedTicks = value.NullableInt64("savedAtUtcTicks");
            ItemOwnershipState? lastDeadCharacter = value.NullableObject("lastDeadCharacter", ItemGraphSaveJson.Read);
            long? deadToken = value.NullableInt64("deadCharacterToken");
            long experience = value.Optional("experience", 0L, (gameplay, name) => gameplay.Int64(name));
            IReadOnlyList<MapMarkerState> mapMarkers = OptionalObjectArray(value, "mapMarkers", ReadMapMarker);
            // 쪽지와 레벨 물체 키는 이 필드가 없는 저장도 읽도록 선택 필드다. 키 목록의 빈 키와 중복은 GameplayPersistenceState가 거부한다.
            NoteIndexState notes = value.Optional("notes", NoteIndexState.Empty, (gameplay, name) => gameplay.Object(name, ReadNotes));
            IReadOnlyList<string> brokenBreakables = OptionalTextArray(value, "brokenBreakables");
            IReadOnlyList<string> constructionSites = OptionalTextArray(value, "constructionSites");
            // 보관함 필드가 없는 저장은 빈 보관함이고, 명시 null과 null 항목은 거부한다. 루트 개수 규칙은 InventoryPersistenceState가 본다.
            IReadOnlyList<ItemOwnershipState> storageBuffer = withStorageBuffer
                ? OptionalObjectArray(value, "storageBuffer", ItemGraphSaveJson.Read)
                : Array.Empty<ItemOwnershipState>();
            // 퀵슬롯, 저장 시각, 사망 토큰 검사와 묶음 생성은 모든 필드를 읽은 뒤에 이 순서로 한다.
            ItemShortcutState? shortcutState = shortcuts == null ? null : value.Create(ItemShortcutState.TryCreate(shortcuts,
                out ItemShortcutState? createdShortcuts), createdShortcuts);
            DateTime? savedAtUtc = savedTicks.HasValue ? UtcTicks(value, "savedAtUtcTicks", savedTicks.Value) : (DateTime?)null;
            uint? deadCharacterToken = deadToken.HasValue ? ToUInt32(value, "deadCharacterToken", deadToken.Value) : (uint?)null;
            InventoryPersistenceState inventory = value.Create(InventoryPersistenceState.TryCreate(
                items: items,
                shortcuts: shortcutState,
                storageBuffer: storageBuffer,
                value: out InventoryPersistenceState? createdInventory), createdInventory);
            ProgressionPersistenceState progression = value.Create(ProgressionPersistenceState.TryCreate(
                buildings: buildings,
                economy: economy,
                unlockedBeacons: beacons,
                unlockedFormulas: formulas,
                shops: shops,
                blackMarket: blackMarket,
                value: out ProgressionPersistenceState? createdProgression), createdProgression);
            DeathPersistenceState deaths = value.Create(DeathPersistenceState.TryCreate(
                lostBodies: lostBodies,
                lastDeadCharacter: lastDeadCharacter,
                deadCharacterToken: deadCharacterToken,
                value: out DeathPersistenceState? createdDeaths), createdDeaths);
            return value.Create(GameplayPersistenceState.TryCreate(
                inventory: inventory,
                clock: clock,
                progression: progression,
                deaths: deaths,
                mapMarkers: mapMarkers,
                experience: experience,
                savedAtUtc: savedAtUtc,
                notes: notes,
                brokenBreakables: brokenBreakables,
                constructionSites: constructionSites,
                value: out GameplayPersistenceState? created), created);
        }

        // DateTime이 표현할 수 있는 틱만 받는다. 범위 밖이면 필드 오류를 기록하고, 판독이 실패하므로 쓰이지 않을 기본값을 돌려준다.
        static DateTime UtcTicks(StrictObject value, string name, long ticks)
        {
            if (ticks >= 0L && ticks <= DateTime.MaxValue.Ticks) return new DateTime(ticks, DateTimeKind.Utc);
            value.FailField(name, "값이 범위를 벗어났습니다.");
            return default;
        }

        // 사망 토큰과 출격 ID는 uint라 그 범위의 정수만 받는다. 범위 밖이면 필드 오류를 기록하고, 쓰이지 않을 0을 돌려준다.
        static uint ToUInt32(StrictObject value, string name, long number)
        {
            if (number >= 0L && number <= uint.MaxValue) return (uint)number;
            value.FailField(name, "값이 정수 범위를 벗어났습니다.");
            return 0u;
        }

        static JObject WriteClock(GameplayClockState value) => new JObject
        {
            ["recordedPlayTicks"] = value.RecordedPlayTicks,
            ["historicalPlayTicks"] = NumberOrNull(value.HistoricalPlayTicks),
            ["days"] = NumberOrNull(value.Days),
            ["secondsOfDay"] = NumberOrNull(value.SecondsOfDay)
        };
        static GameplayClockState ReadClock(StrictObject value) => value.Create(GameplayClockState.TryCreate(
            value.Int64("recordedPlayTicks"), value.NullableInt64("historicalPlayTicks"), value.NullableInt64("days"),
            value.NullableFiniteDouble("secondsOfDay"), out GameplayClockState? created), created);

        static JObject WriteBeacon(BeaconUnlockState value) => new JObject
        {
            ["beaconId"] = value.BeaconId, ["beaconIndex"] = value.BeaconIndex
        };
        static BeaconUnlockState ReadBeacon(StrictObject value) => value.Create(BeaconUnlockState.TryCreate(value.Text("beaconId"),
            value.Int32("beaconIndex"), out BeaconUnlockState? created), created);

        // 씬은 열거형 값 대신 GameScenes.Id의 저장 문자열로 쓴다. 읽을 때는 ReadScene이 모르는 문자열을 필드 오류로 거부한다.
        static JObject WriteMapMarker(MapMarkerState value) => new JObject
        {
            ["id"] = value.Id, ["sceneId"] = GameScenes.Id(value.SceneId), ["regionId"] = value.RegionId,
            ["x"] = value.X, ["y"] = value.Y, ["z"] = value.Z,
            ["iconId"] = value.IconId, ["colorId"] = value.ColorId
        };
        static MapMarkerState ReadMapMarker(StrictObject value) => value.Create(MapMarkerState.TryCreate(value.Text("id"),
            ReadScene(value, "sceneId"), value.Text("regionId"), value.FiniteFloat("x"), value.FiniteFloat("y"), value.FiniteFloat("z"),
            value.Int32("iconId"), value.Int32("colorId"), out MapMarkerState? created), created);

        static JObject WriteBuildings(BuildingPersistenceState value) => new JObject
        {
            ["installations"] = WriteList(value.Installations, WriteInstallation),
            ["tokens"] = WriteList(value.Tokens, WriteBuildingToken)
        };
        static BuildingPersistenceState ReadBuildings(StrictObject value) => value.Create(BuildingPersistenceState.TryCreate(
            value.ObjectArray("installations", ReadInstallation), value.ObjectArray("tokens", ReadBuildingToken),
            out BuildingPersistenceState? created), created);
        static JObject WriteInstallation(BuildingInstallationState value) => new JObject
        {
            ["instanceId"] = value.InstanceId, ["originalBuildingId"] = value.OriginalBuildingId,
            ["areaId"] = value.AreaId, ["x"] = value.X, ["y"] = value.Y, ["rotation"] = value.Rotation
        };
        static BuildingInstallationState ReadInstallation(StrictObject value) => value.Create(BuildingInstallationState.TryCreate(
            value.Int32("instanceId"), value.Text("originalBuildingId"), value.Text("areaId"), value.Int32("x"), value.Int32("y"),
            value.Int32("rotation"), out BuildingInstallationState? created), created);
        static JObject WriteBuildingToken(BuildingTokenState value) => new JObject
        {
            ["originalBuildingId"] = value.OriginalBuildingId, ["amount"] = value.Amount
        };
        static BuildingTokenState ReadBuildingToken(StrictObject value) => value.Create(BuildingTokenState.TryCreate(
            value.Text("originalBuildingId"), value.Int32("amount"), out BuildingTokenState? created), created);

        // 경제 항목은 정확히 money, unlockedItems, awaitingConfirmation이다. 두 ID 목록은 공용 정수 배열 도우미로 읽고 쓴다.
        static JObject WriteEconomy(EconomyPersistenceState value) => new JObject
        {
            ["money"] = value.Money,
            ["unlockedItems"] = WriteInts(value.UnlockedItems),
            ["awaitingConfirmation"] = WriteInts(value.AwaitingConfirmation)
        };
        static EconomyPersistenceState ReadEconomy(StrictObject value) => value.Create(EconomyPersistenceState.TryCreate(
            value.Int64("money"), value.Array("unlockedItems", _int32Item), value.Array("awaitingConfirmation", _int32Item),
            out EconomyPersistenceState? created), created);

        // 불러올 때 재고를 새로 만들지 않도록 저장된 갱신 시각과 잔량을 그대로 쓴다.
        // 상점 인스턴스 키(shopKey, D1)는 필수 필드라 그 필드가 없는 옛 항목은 거부된다. 실제 슬롯의 shops는 빈 배열이었다(2026-10-20).
        static JObject WriteShop(StockShopState value) => new JObject
        {
            ["shopKey"] = value.ShopKey, ["merchantId"] = value.MerchantId, ["lastRefreshBinary"] = value.LastRefreshBinary,
            ["stocks"] = WriteList(value.Stocks, WriteStock)
        };
        static StockShopState ReadShop(StrictObject value) => value.Create(StockShopState.TryCreate(value.Text("shopKey"),
            value.Text("merchantId"), value.Int64("lastRefreshBinary"), value.ObjectArray("stocks", ReadStock),
            out StockShopState? created), created);
        static JObject WriteStock(ShopStockState value) => new JObject { ["originalItemId"] = value.OriginalItemId, ["stock"] = value.Stock };
        static ShopStockState ReadStock(StrictObject value) => value.Create(ShopStockState.TryCreate(value.Int32("originalItemId"),
            value.Int32("stock"), out ShopStockState? created), created);

        // 암시장 수요와 공급의 가격 배율은 다시 정하지 않고 저장된 값을 그대로 유지한다.
        static JObject WriteMarketEntry(MarketEntryState value) => new JObject
        {
            ["originalItemId"] = value.OriginalItemId, ["remaining"] = value.Remaining,
            ["priceFactor"] = value.PriceFactor, ["batchCount"] = value.BatchCount
        };
        static MarketEntryState ReadMarketEntry(StrictObject value) => value.Create(MarketEntryState.TryCreate(
            value.Int32("originalItemId"), value.Int32("remaining"), value.FiniteFloat("priceFactor"), value.Int32("batchCount"),
            out MarketEntryState? created), created);
        static JObject WriteMarket(BlackMarketState value) => new JObject
        {
            ["valid"] = value.Valid, ["lastRefreshBinary"] = value.LastRefreshBinary, ["refreshChance"] = value.RefreshChance,
            ["demands"] = WriteList(value.Demands, WriteMarketEntry), ["supplies"] = WriteList(value.Supplies, WriteMarketEntry)
        };
        static BlackMarketState ReadMarket(StrictObject value) => value.Create(BlackMarketState.TryCreate(value.Boolean("valid"),
            value.Int64("lastRefreshBinary"), value.Int32("refreshChance"), value.ObjectArray("demands", ReadMarketEntry),
            value.ObjectArray("supplies", ReadMarketEntry), out BlackMarketState? created), created);

        // 시체의 아이템 트리는 사망 당시의 기록이라 현재 소유 트리와 합치지 않고 따로 검증한다.
        // 그래서 같은 아이템이 양쪽에 기록되어 있어도 충돌로 보지 않는다.
        static JObject WriteBody(LostBodyState value) => new JObject
        {
            ["raidId"] = value.RaidId, ["subSceneId"] = value.SubSceneId, ["x"] = value.X, ["y"] = value.Y, ["z"] = value.Z,
            ["valid"] = value.Valid, ["spawned"] = value.Spawned, ["touched"] = value.Touched, ["items"] = ItemGraphSaveJson.Write(value.Items)
        };
        // 출격 ID는 다른 필드보다 먼저 읽어 uint 범위를 검사한다.
        static LostBodyState ReadBody(StrictObject value) => value.Create(LostBodyState.TryCreate(
            ToUInt32(value, "raidId", value.Int64("raidId")), value.Text("subSceneId"), value.FiniteFloat("x"), value.FiniteFloat("y"),
            value.FiniteFloat("z"), value.Boolean("valid"), value.Boolean("spawned"), value.Boolean("touched"),
            value.Object("items", ItemGraphSaveJson.Read), out LostBodyState? created), created);
    }
}
