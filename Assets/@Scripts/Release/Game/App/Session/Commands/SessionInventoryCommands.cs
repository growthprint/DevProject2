#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Game.App
{
    // 원본 아이템 소유 그래프를 바꾸는 소지품 명령이다. 월드 표시가 저장 데이터를 직접 바꾸지 않도록 세션 게이트를 거친다.
    // 계산은 GameplayInventory의 작업 사본에서 하고, 성공한 명령의 결과 그래프만 마지막에 한 번 확정한다.
    // 게이트나 게임 규칙에 걸리면 기록 없이 false다. 버그와 콘텐츠 결함은 발견한 곳에서 Log.Instance.Fail로 보고한다.
    // 수리·분해 작업대 명령이 자기 partial 파일에 명령을 두도록 partial로 연다.
    internal sealed partial class SessionInventoryCommands
    {
        readonly GameSessionManager _session;

        internal SessionInventoryCommands(GameSessionManager session) => _session = session;

        // 원본 플레이어 정의의 인벤토리 용량으로 빈 소유 그래프를 처음 만들고, 펫 가방(Inventory_Safe)이 없으면 빈 가방을 더한다.
        // 새 게임과 펫 가방이 없던 저장이 같은 길을 쓴다. 둘 다 있으면 확정하지 않고, 바뀌면 한 번만 확정한다.
        // 월드 첫 초기화와 튜토리얼 재시작이 함께 부르고 명령 관문은 보지 않는다. 실제 생존 값은 PlayerVitals가 소유한다.
        internal void EnsureGraph(OriginalGameplayCatalog catalog)
        {
            GameSessionSnapshot snapshot = _session.Snapshot();
            CampaignState campaign = snapshot.Campaign;
            ItemOwnershipState? current = campaign.Gameplay.Inventory.Items;
            // 플레이어 정의는 씬 카탈로그에 늘 저작돼 있고 씬 데이터 테스트가 확인하므로 조회는 늘 성공한다.
            // 그래프 생성과 펫 가방 추가도 실패하지 않는다. ItemOwnershipState는 Unity 객체가 아니라 ??를 써도 된다.
            OriginalItemDefinition character = catalog.PlayerCharacter;
            ItemOwnershipState graph = GameplayInventory.WithPetBag(
                current ?? GameplayInventory.Initialize(character.Id, OriginalCharacter.InventoryCapacity(character)),
                OriginalCharacter.PetCapacity(character));
            if (ReferenceEquals(graph, current)) return;
            // 그래프가 이미 있던 캠페인은 플레이어도 있으므로 인벤토리 명령과 같은 확정으로 그래프만 바꾼다. 위 조회와 계산은 세션을
            // 바꾸지 않아 확정 시점의 스냅샷도 같다. 퀵슬롯은 WithItems가 맞추며 소지 칸은 그대로라 바뀌지 않는다.
            if (current != null)
            {
                CommitGameplay(graph);
                return;
            }
            PlayerState? player = campaign.Player;
            // 그래프가 있으면 플레이어도 있어야 하므로 허브 사본에 없으면 새 게임 기본값을 둔다. 정의 수치는 테스트가 확인하는
            // 빌드 데이터라 새 플레이어를 검증 없이 만든다. PlayerState는 Unity 객체가 아니라 ??=를 써도 된다.
            player ??= OriginalCharacter.NewOriginalPlayer(character);
            // 미포착 상태에서 빈 그래프를 처음 올리는 변경은 그래프 변경 규칙을 지킨다. 퀵슬롯은 WithItems가 맞추므로
            // 재검증 없는 WithItems와, 허브 사본에 플레이어를 함께 넣는 WithGameplayAndPlayer를 쓴다.
            _session.Commit(snapshot.WithCampaign(campaign.WithGameplayAndPlayer(campaign.Gameplay.WithItems(graph), player)));
        }

        // 원작 LevelManager.InitLevel이 기지 레벨마다 하는 정리다. 정의 태그에 DestroyInBase가 있는 아이템을 소지품 맨 위 칸,
        // 캐릭터 칸, 펫 가방 맨 위 칸에서 하위 트리째 지우고 메모리에만 확정한다. 지운 것이 없으면 확정하지 않는다.
        // 기지 월드 첫 초기화가 EnsureGraph 다음에 부르며 명령 관문은 보지 않는다. 지운 아이템의 퀵슬롯은 WithItems가 비운다.
        internal void DestroyInBase(OriginalGameplayCatalog catalog)
        {
            // 이어하기와 저장 지점이 그래프의 아이템 ID를 카탈로그와 대조하므로 정의 조회는 늘 성공한다. 정리는 실패하지 않는다.
            ChangeInventory(value => value.DestroyInBase(item => catalog.DefinitionOf(item.OriginalItemId).HasTag(_destroyInBaseTag)),
                out _);
        }

        // 원작 아이템 정의의 태그 이름이다. 가져온 정의 데이터의 태그 문자열과 같아야 한다.
        const string _destroyInBaseTag = "DestroyInBase";

        // 원본 LootView의 모두 줍기처럼 항목을 순서대로 지급하고 메모리에 한 번 확정한다. 각 항목은 전부 주거나 아예 주지 않고,
        // 넣을 수 없는 항목은 건너뛴다. 지급한 항목의 원본 키는 같은 확정에서 튜토리얼이나 출격 진행에 기록한다.
        // 유일한 호출자 InventoryScreen.Take가 회수하지 않은 상자 칸 하나로 세 목록을 함께 만든다. 하나도 못 넣으면 false다.
        // rejection은 건너뛴 첫 항목의 거절 코드이고, 없으면 None이다. 구조 결함은 Invalid로 전체를 거절한다.
        internal bool TryCollectLoot(OriginalGameplayCatalog catalog, IReadOnlyList<int> types, IReadOnlyList<int> counts,
            IReadOnlyList<string> sourceIds, out int collected, out InventoryRejection rejection)
        {
            collected = 0;
            rejection = InventoryRejection.None;
            if (!_session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = _session.Snapshot();
            CampaignState campaign = snapshot.Campaign;
            // 월드 첫 초기화가 소유 그래프를 만든 뒤에만 상자 화면이 열린다.
            ItemOwnershipState graph = campaign.Gameplay.Inventory.Items!;
            var taken = new List<string>(sourceIds.Count);
            // 항목을 건너뛰었는지는 rejection이 None이 아닌지로 안다.
            for (int index = 0; index < types.Count; index++)
            {
                // 화면은 정의를 찾은 상자 칸만 그리고 고르게 하므로 정의 조회는 늘 성공한다.
                OriginalItemDefinition loot = catalog.DefinitionOf(types[index]);
                int count = counts[index];
                if (TryInventory(graph, inventory => AddLoot(inventory, loot, count), out ItemOwnershipState? next,
                    out InventoryRejection rule))
                {
                    graph = next;
                    taken.Add(sourceIds[index]);
                }
                // 구조 결함은 발견한 곳에서 이미 보고했다. 건너뛰지 않고 확정 없이 전체를 거절한다.
                else if (rule == InventoryRejection.Invalid)
                {
                    rejection = rule;
                    return false;
                }
                else if (rejection == InventoryRejection.None) rejection = rule;
            }
            // 하나도 못 넣었으면 첫 거절 코드를 rejection에 담은 채 기록 없이 거절한다.
            if (taken.Count == 0) return false;
            // 끌어 놓기 루팅과 같은 CommitLoot로 그래프와 주운 키를 한 번에 확정한다.
            CommitLoot(snapshot, graph, taken);
            collected = taken.Count;
            return true;
        }

        // 루팅 결과 그래프와 주운 칸의 원본 키를 캠페인 복사 한 번으로 확정한다. 키는 튜토리얼과 출격 중에만 기록한다.
        void CommitLoot(GameSessionSnapshot snapshot, ItemOwnershipState graph, IReadOnlyList<string> taken)
        {
            CampaignState campaign = snapshot.Campaign;
            // TryInventory가 작업 사본으로 만든 그래프이고 퀵슬롯은 WithItems가 맞추므로 재검증 없는 WithItems와 WithGameplay를 쓴다.
            CampaignState committed = campaign.WithGameplay(campaign.Gameplay.WithItems(graph));
            // 키는 화면이 저작 지점 ID로 만든 아직 회수하지 않은 키라 재검증 없는 AddRange를 쓴다. 주운 키를 한 번에 더해 정렬된 집합을 한 번만 만든다.
            // AddRange는 단계를 그대로 둔 진행 값을 주므로 WithProgress의 출격 규칙에 걸리지 않는다.
            if (CampaignEventLog.IsRecording(campaign))
                committed = committed.WithProgress(CampaignEventLog.AddRange(committed, taken));
            _session.Commit(snapshot.WithCampaign(committed));
        }

        // 상자 항목 하나를 원본 최대 스택 단위로 나눠 소지품에 쌓는다. 같은 종류의 스택을 먼저 채운다.
        // 공간 부족은 AddAndMerge의 InventoryFull 거절을 돌려주고, 작업 사본째 버려 아무것도 지급하지 않는다.
        static InventoryResult AddLoot(GameplayInventory inventory, OriginalItemDefinition definition, int count)
        {
            string character = inventory.CharacterId;
            for (int remaining = count; remaining > 0;)
            {
                int amount = Math.Min(remaining, definition.MaxStack);
                // 새 루팅 아이템은 1 이상 최대 스택 이하 수량이라 NewItem이 늘 만든다.
                InventoryResult added = inventory.AddAndMerge(character, NewItem(definition, amount), definition.MaxStack, false, out _);
                if (!added.IsOk) return added;
                remaining -= amount;
            }
            return InventoryResult.Ok;
        }

        // 루팅과 아이템 지급(TryDeliver)의 새 아이템을 새 GUID로 만든다. 호출자는 1 이상 최대 스택 이하의 수량만 넘기고, 정의는 테스트가 확인하는 빌드 데이터다.
        // 원작은 경로마다 새 아이템의 FromInfoKey에 출처 키를 넣으므로(제조, 상점 구매, 퀘스트 보상, 분해) 같은 생성 경로에서 받는다.
        // 원작이 넣지 않는 경로는 null이다. 아이템 상태는 불변이라 같은 값으로 출처만 채운 노드를 만든다.
        static ItemInstanceState NewItem(OriginalItemDefinition definition, int count, string? fromInfo = null)
        {
            // 저장 아이템 ID 형식은 영속 ID 생성 한곳에서 정한다.
            ItemInstanceState item = definition.CreateItem(PersistentIds.New(), count);
            if (fromInfo == null) return item;
            return new ItemInstanceState(item.InstanceId, item.OriginalItemId, item.Quantity, item.AbsoluteDurability, item.DurabilityLoss,
                item.Inspected, fromInfo, item.Slots, item.Inventory, item.Variables);
        }

        // 루팅 상자 칸 하나를 끌어 놓은 자리에 지급하고, 그 칸의 원본 키와 함께 메모리에 한 번 확정한다. 소지품이나 펫 가방 칸에 다른
        // 아이템이 있거나 같은 종류 스택에 수량이 다 들어가지 않으면 Occupied, 장비 칸 규칙에 걸리면 SlotMismatch, 펫 가방에 Sticky면 Sticky다.
        // 유일한 호출자 InventoryScreen.Drop은 상자가 열려 있으면 창고 칸을 보이지 않으므로 창고를 목적지로 넘기지 않는다.
        // 화면은 용량 안의 칸과 아직 회수하지 않은 상자 칸만 넘긴다.
        internal bool TryTakeLoot(OriginalGameplayCatalog catalog, int type, int count, string sourceId, InventoryLocation to,
            out InventoryRejection rejection)
        {
            rejection = InventoryRejection.None;
            if (!_session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = _session.Snapshot();
            // 월드 첫 초기화가 소유 그래프를 만든 뒤에만 상자 화면이 열린다.
            ItemOwnershipState graph = snapshot.Campaign.Gameplay.Inventory.Items!;
            // 화면은 정의를 찾은 상자 칸만 그리고 끌게 하므로 정의 조회는 늘 성공한다.
            OriginalItemDefinition loot = catalog.DefinitionOf(type);
            var rules = new CatalogRules(catalog);
            if (!TryInventory(graph, inventory => TakeLoot(inventory, loot, count, to, rules.Rules),
                    out ItemOwnershipState? next, out rejection)) return false;
            CommitLoot(snapshot, next, new[] { sourceId });
            return true;
        }

        // 루팅 칸 하나를 목적지에 놓는다. 소지품 칸, 펫 가방 칸, 장비 칸에는 원본 최대 스택만큼 만든 첫 스택을 놓고, 나머지는
        // AddLoot처럼 소지품에 쌓는다. 어디서든 들어가지 않으면 작업 사본째 버려 아무것도 지급하지 않는다.
        static InventoryResult TakeLoot(GameplayInventory inventory, OriginalItemDefinition definition, int count, InventoryLocation to,
            InventoryRules rules)
        {
            if (to.Area == InventoryArea.Carried && to.Position == InventoryLocation._anyPosition) return AddLoot(inventory, definition, count);
            // 원작 LootView처럼 acceptSticky가 거짓인 펫 가방은 Sticky 아이템을 받지 않는다.
            if (to.Area == InventoryArea.Pet && definition.Sticky) return InventoryResult.Reject(InventoryRejection.Sticky);
            int first = Math.Min(count, definition.MaxStack);
            // 새 루팅 아이템은 1 이상 최대 스택 이하 수량이라 NewItem이 늘 만든다.
            ItemInstanceState item = NewItem(definition, first);
            // 수량이 최대 스택보다 많으면 첫 스택이 이미 가득 차 있어 차 있는 칸에 합쳐지지 않는다. 그래서 칸 하나에 전부 들어갈 때만 합쳐진다.
            // 장비와 아이템 칸 목적지는 InventoryLocation.Equipment와 ItemSlot으로만 만들어 칸 키가 늘 있다.
            // 펫 가방에 칸 번호 없이 놓으면 첫 스택을 AddAndMerge로 넣는다.
            InventoryResult placed = to.Area switch
            {
                InventoryArea.Equipment => inventory.Plug(inventory.CharacterId, to.Slot!, item, rules),
                InventoryArea.ItemSlot => inventory.Plug(to.Owner!, to.Slot!, item, rules),
                InventoryArea.Pet when to.Position == InventoryLocation._anyPosition =>
                    inventory.AddAndMerge(GameplayInventory._pet, item, definition.MaxStack, false, out _),
                _ => inventory.AddAt(inventory.ContainerOf(to.Area), to.Position, item, definition.MaxStack, rules)
            };
            if (!placed.IsOk) return placed;
            return AddLoot(inventory, definition, count - first);
        }

        // 원본 무덤 상자의 모두 줍기처럼 사망 표식 번호 deathToken의 유효한 유실물에서 들어가는 아이템만 원래 ID 그대로 되찾아
        // 메모리에 한 번 확정한다. 남은 아이템은 같은 표식의 유실물에 두어 다시 회수할 수 있다. 일부만 되찾으면 true와 InventoryFull,
        // 하나도 못 넣으면 기록 없이 false와 InventoryFull이다. 이미 소유한 유실물 같은 상태 불일치는 발견한 곳에서 보고한다.
        internal bool TryRecoverLostBody(OriginalGameplayCatalog catalog, uint deathToken, out InventoryRejection rejection)
        {
            rejection = InventoryRejection.None;
            if (!_session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = _session.Snapshot();
            GameplayPersistenceState gameplay = snapshot.Campaign.Gameplay;
            // 유실물 지점은 유실물 목록에서 생기고 소유 그래프는 월드 첫 초기화가 만들므로 둘 다 있다. 지점은 유효한 유실물마다
            // 하나이고 회수 확정 알림이 같은 프레임에 지점을 다시 맞추므로, 지점의 표식으로 유실물을 늘 찾는다.
            ItemOwnershipState source = gameplay.Inventory.Items!;
            IReadOnlyList<LostBodyState> bodies = gameplay.Deaths.LostBodies!;
            LostBodyState body = bodies.FirstOrDefault(value => value.RaidId == deathToken && value.Valid)!;
            var rules = new CatalogRules(catalog);
            ItemOwnershipState? left = null;
            // 다른 인벤토리 명령과 같은 작업 사본 경로로 계산한다. 규칙 거절은 코드만 넘기고, 구조 결함은 발견한 곳이 이미 보고했다.
            if (!TryInventory(source, body.Items,
                    (inventory, dropped) => inventory.Recover(dropped, rules.Rules.SlotsFor, out left),
                    out ItemOwnershipState? graph, out rejection)) return false;
            // 유실물의 RaidId는 사망 토큰이다. 다 되찾으면 그 유실물을 지우고, 남으면 남은 아이템만 든 같은 표식의 유실물로 바꾼다.
            // 그래프와 남은 유실물 그래프는 작업 사본이 소유 규칙을 지켜 만들었으므로 다시 검증하지 않는 With*를 쓴다.
            ItemOwnershipState rest = left!;
            var remaining = new List<LostBodyState>(bodies.Count);
            foreach (LostBodyState value in bodies)
            {
                if (value.RaidId != body.RaidId) remaining.Add(value);
                else if (rest.Roots.Count > 0)
                    remaining.Add(new LostBodyState(value.RaidId, value.SubSceneId, value.X, value.Y, value.Z, value.Valid, value.Spawned,
                        value.Touched, rest));
            }
            if (rest.Roots.Count > 0) rejection = InventoryRejection.InventoryFull;
            _session.Commit(snapshot.WithCampaign(snapshot.Campaign.WithGameplay(gameplay.WithItems(graph).WithLostBodies(remaining))));
            return true;
        }

        // 원작 끌어 놓기(InventoryEntry.OnDrop, SlotDisplay.OnDrop) 한 번을 메모리에 한 번 확정한다. 계산 규칙은 GameplayInventory.Move에 있다.
        // 창고 칸은 기지에서만 열리는 창고 화면에만 보이고, 화면은 확정 그래프 용량 안의 칸만 그리므로 칸 번호는 늘 용량 안이다.
        // 규칙 거절은 코드로 돌려준다.
        internal bool TryMove(OriginalGameplayCatalog catalog, string itemId, InventoryLocation to, out InventoryRejection rejection)
        {
            rejection = InventoryRejection.None;
            if (!_session.AcceptsCommands) return false;
            var rules = new CatalogRules(catalog);
            return ChangeInventory(value => value.Move(itemId, to, rules.Rules), out rejection);
        }

        // 원작 LootView.OnStoreAllButtonClicked처럼 기지 창고에 소지품을 칸 순서대로 넣고 메모리에 한 번 확정한다. 하나라도 옮기면
        // true이고, 들어가지 않는 아이템에서 멈췄으면 rejection이 InventoryFull이다. 옮길 것이 없으면 기록 없이 false와 None이다.
        // 창고 화면은 기지에서만 열려 이 명령도 기지에서만 온다. 합칠 스택 수를 정의에서 읽으므로 카탈로그를 받는다.
        internal bool TryStoreAll(OriginalGameplayCatalog catalog, out InventoryRejection rejection)
        {
            rejection = InventoryRejection.None;
            if (!_session.AcceptsCommands) return false;
            var rules = new CatalogRules(catalog);
            int moved = 0;
            bool stopped = false;
            if (!ChangeInventory(value => value.StoreAll(rules.Rules, out moved, out stopped), out rejection)) return false;
            if (stopped) rejection = InventoryRejection.InventoryFull;
            return moved > 0;
        }

        // 원작 InventoryDisplay 정리 버튼(Inventory.Sort)처럼 소지품이나 기지 창고를 정리하고 메모리에 한 번 확정한다. 계산 규칙은
        // GameplayInventory.Sort에 있다. 창고 정리 버튼은 원작 창고 상자처럼 창고 화면이 열리는 기지에서만 받는다.
        // 정렬 키와 합칠 스택 수를 정의에서 읽으므로 카탈로그를 받는다.
        internal bool TrySort(OriginalGameplayCatalog catalog, bool storage)
        {
            if (!_session.AcceptsCommands) return false;
            var rules = new CatalogRules(catalog);
            return ChangeInventory(value => value.Sort(storage ? GameplayInventory._storage : value.CharacterId, rules.Rules), out _);
        }

        // 원작 CharacterMainControl.UpdateInventoryCapacity처럼 소지품 용량을 맞춘다. 음수는 0으로 본다. 유일한 호출자인 프레임 끝
        // 용량 맞추기가 같은 프레임에 명령 게이트, 소유 그래프, 같은 용량인지를 먼저 본다. 줄어 들어가지 않는 아이템은 소유 그래프에서 빠져
        // dropped에 루트 하나짜리 그래프로 담기고, 호출자가 땅에 떨어뜨린다. 합칠 스택 수를 정의에서 읽으므로 카탈로그를 받는다.
        // 구조 결함은 발견한 곳에서 보고하고 false다.
        internal bool TrySetCarriedCapacity(OriginalGameplayCatalog catalog, int capacity, out IReadOnlyList<ItemOwnershipState> dropped) =>
            TrySetCapacity(catalog, null, capacity, out dropped);

        // 원작 PetProxy.Update처럼 펫 가방 용량을 PetCapcity에 맞춘다. 계약과 줄어들 때의 규칙은 TrySetCarriedCapacity와 같다.
        // 원작은 넘친 칸을 그대로 두지만 이 컨테이너는 용량 밖 칸을 가질 수 없어 넘친 아이템을 dropped로 돌려준다.
        internal bool TrySetPetCapacity(OriginalGameplayCatalog catalog, int capacity, out IReadOnlyList<ItemOwnershipState> dropped) =>
            TrySetCapacity(catalog, GameplayInventory._pet, capacity, out dropped);

        // 원작 PlayerStorage.Capacity(DefaultCapacity + 해금된 AddPlayerStorage 합)처럼 창고 칸 수를 capacity까지 넓히고 메모리에 한 번 확정한다.
        // 특성은 다시 잠기지 않아 줄이지 않는다. 이미 그 이상이면 확정 없이 true다. 늘리기만 하므로 넘쳐 떨어지는 아이템이 없다.
        // 관문이 닫혔으면 기록 없이 false, 창고가 없는 구조 결함은 발견한 곳에서 보고하고 false다.
        // 호출자: GameplayCommands.TryConfirmPerk(창고 확장 특성 확정)와 RestorePerkEffects(월드 진입 재적용).
        internal bool TryExpandStorage(OriginalGameplayCatalog catalog, int capacity)
        {
            if (!_session.AcceptsCommands) return false;
            // 특성 명령은 월드 첫 초기화가 소유 그래프를 만든 뒤에만 온다. 용량을 아직 모르는 창고(null)는 비교가 거짓이라 아래에서 맞춘다.
            IReadOnlyList<StoredInventoryState> inventories = _session.Snapshot().Campaign.Gameplay.Inventory.Items!.Inventories;
            for (int index = 0; index < inventories.Count; index++)
                if (inventories[index].Key == GameplayInventory._storage && inventories[index].Inventory.Capacity >= capacity) return true;
            return TrySetCapacity(catalog, GameplayInventory._storage, capacity, out _);
        }

        // 소지품, 펫 가방, 창고 용량 맞추기가 함께 쓰는 계산과 확정이다. container가 null이면 소지품이다. 음수는 0으로 본다.
        bool TrySetCapacity(OriginalGameplayCatalog catalog, string? container, int capacity, out IReadOnlyList<ItemOwnershipState> dropped)
        {
            dropped = Array.Empty<ItemOwnershipState>();
            int target = Math.Max(0, capacity);
            var rules = new CatalogRules(catalog);
            List<ItemOwnershipState>? removed = null;
            if (!ChangeInventory(value => value.SetCapacity(container ?? value.CharacterId, target, rules.Rules, out removed), out _)) return false;
            if (removed != null) dropped = removed;
            return true;
        }

        // 땅에 떨어진 아이템 하나(루트 하나짜리 그래프)를 원래 ID 그대로 소지품에 줍고 메모리에 한 번 확정한다. 장착은 하지 않는다.
        // 자리가 없으면 기록 없이 false와 InventoryFull이다. 바닥 아이템은 저장하지 않고 주우면 사라지므로 이미 소유한 ID가 오지 않는다.
        internal bool TryPickUp(OriginalGameplayCatalog catalog, ItemOwnershipState dropped, out InventoryRejection rejection)
        {
            rejection = InventoryRejection.None;
            if (!_session.AcceptsCommands) return false;
            var rules = new CatalogRules(catalog);
            return ChangeInventory(value => value.PickUp(dropped, rules.Rules), out rejection);
        }

        // 실제 소지 탄약을 총기 안으로 옮기고, 넣을 탄약이 없거나 탄창이 가득 차면 NoAmmo로 거절한다. 유일한 호출자인 전투 장전이
        // 같은 프레임에 무기 소유와 정의를 확인하고 구경이 맞는 소지 탄약만 고른다.
        internal bool TryReload(OriginalGameplayCatalog catalog, string id, int bulletType)
        {
            if (!_session.AcceptsCommands) return false;
            // 용량은 장전 시작 판정과 같이 부착물 modifier를 반영한 MagazineCapacity로 읽어 두 판정이 어긋나지 않게 한다.
            // 호출자가 같은 프레임에 확인한 무기라 확정 그래프에 늘 있다. 장전은 무기의 칸을 바꾸지 않아 확정 그래프로 미리 읽는다.
            ItemOwnershipState items = _session.Snapshot().Campaign.Gameplay.Inventory.Items!;
            int capacity = catalog.Effective(items, items.Find(id)!).MagazineCapacity;
            return ChangeInventory(value =>
            {
                InventoryResult reloaded = value.Reload(id, bulletType, capacity, out int loaded);
                if (!reloaded.IsOk) return reloaded;
                return loaded == 0 ? InventoryResult.Reject(InventoryRejection.NoAmmo) : InventoryResult.Ok;
            }, out _);
        }

        // 원본 탄종 교체의 TakeOutAllBullets다. 총의 탄을 소지품에 합치고, 자리가 없어 남은 묶음은 dropped로 돌려줘 호출자가 바닥에 떨군다.
        // 플레이어 입력이 부르므로 명령 게이트가 닫혀 있으면 기록 없이 false다. 호출자는 같은 프레임에 그래프에서 확인한 총만 넘긴다.
        internal bool TryUnloadMagazine(OriginalGameplayCatalog catalog, string id, out IReadOnlyList<ItemOwnershipState> dropped)
        {
            dropped = Array.Empty<ItemOwnershipState>();
            if (!_session.AcceptsCommands) return false;
            var rules = new CatalogRules(catalog);
            List<ItemOwnershipState>? removed = null;
            if (!ChangeInventory(value => value.Unload(id, rules.Rules, out removed), out _)) return false;
            if (removed != null) dropped = removed;
            return true;
        }

        // 빈 탄창으로 쏘면 EmptyMagazine으로 거절한다. 원본 ItemSetting_Gun.UseABullet은 기지 레벨에서 탄을 쓰지 않으므로,
        // 기지에서는 같은 판정만 하고 그래프를 확정하지 않는다.
        internal bool TryFire(string id)
        {
            if (!_session.AcceptsCommands) return false;
            bool consume = _session.Snapshot().SceneId != GameScene.BaseCamp;
            // 발사는 자주 불리므로 인수를 상태 값으로 넘겨 캡처 없는 람다를 쓴다. 발사마다 닫힘 객체를 만들지 않는다.
            return ChangeInventory((Weapon: id, Consume: consume), (value, shot) =>
            {
                InventoryResult spent = value.Shoot(shot.Weapon, out bool fired, shot.Consume);
                if (!spent.IsOk) return spent;
                return fired ? InventoryResult.Ok : InventoryResult.Reject(InventoryRejection.EmptyMagazine);
            }, out _);
        }

        // 원본 ItemShortcut.IsItemValid처럼 소지품에 있는 무기 태그 없는 아이템을 퀵슬롯에 연결한다. 원본 Set_Local(:183-230)처럼
        // 새 칸에 등록하고, 같은 아이템이나 같은 종류를 가리키는 다른 칸은 비우므로 교차 규칙이 지켜진다.
        internal bool TryBindShortcut(OriginalGameplayCatalog catalog, int slot, string id)
        {
            if (!_session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = _session.Snapshot();
            // 퀵슬롯 번호는 화면의 고정 칸 범위에서 오고, 소유 그래프는 월드 첫 초기화가 만든 뒤에만 화면이 열린다.
            ItemOwnershipState items = snapshot.Campaign.Gameplay.Inventory.Items!;
            // 작업 사본을 만들지 않고 소유 그래프 색인으로 소지품 칸과 아이템을 찾는다. 화면은 차 있는 소지품 셀의 ID만 넘기므로
            // 둘 다 늘 찾는다. 세션 그래프에는 플레이어 루트가 늘 있다.
            string character = items.CharacterId!;
            int? position = items.PositionIn(character, id);
            ItemInstanceState item = items.Find(id)!;
            // 이어하기와 저장 지점이 소유 그래프의 아이템 ID를 카탈로그와 대조하므로 정의 조회는 늘 성공한다.
            // 원본처럼 'Weapon' 태그가 있는 총은 기록 없이 거절한다. 근접 무기는 원본 태그가 MeleeWeapon이라 등록된다. 태그 이름은 수리 규칙과 같다.
            if (catalog.DefinitionOf(item.OriginalItemId).HasTag(RepairRules._weaponTag)) return false;
            int[] indexes = (snapshot.Campaign.Gameplay.Inventory.Shortcuts ?? ItemShortcutState.Empty()).InventoryIndexes.ToArray();
            // 칸 번호는 위에서 늘 찾으므로 '!'로 꺼낸다.
            for (int index = 0; index < indexes.Length; index++)
            {
                if (index == slot || indexes[index] < 0) continue;
                if (ClashesWith(items, character, indexes[index], position!.Value, item)) indexes[index] = ItemShortcutState._emptyIndex;
            }
            // 확정된 퀵슬롯은 교차 규칙을 지키고, 위 반복이 같은 칸이나 같은 종류를 가리키는 다른 칸을 비우므로 새 퀵슬롯도 규칙을
            // 지킨다. 그래서 검증 없는 생성자로 만들고 재검증 없는 WithShortcuts와 WithGameplay로 확정한다.
            indexes[slot] = position!.Value;
            CampaignState campaign = snapshot.Campaign;
            var shortcuts = new ItemShortcutState(indexes);
            _session.Commit(snapshot.WithCampaign(campaign.WithGameplay(campaign.Gameplay.WithShortcuts(shortcuts))));
            return true;
        }

        // 원본 Set_Local처럼 다른 퀵슬롯이 새로 등록할 소지 칸이나 같은 종류의 아이템을 가리키면 겹친다. 겹친 칸은 비운다.
        // shortcut은 그 퀵슬롯이 가리키는 소지 칸 번호이고 빈 칸일 수 있다.
        static bool ClashesWith(ItemOwnershipState items, string character, int shortcut, int position, ItemInstanceState item)
        {
            string? other = items.EntryAt(character, shortcut);
            ItemInstanceState? otherItem = other == null ? null : items.Find(other);
            return shortcut == position || otherItem != null && otherItem.OriginalItemId == item.OriginalItemId;
        }

        // 원작 ItemSetting_Skill.OnSkillReleased의 reduceCount다. 던진 스킬 아이템 하나를 소지품에서 빼고, 원작처럼 기지에서는 줄이지 않는다.
        // 손에 든 스킬 아이템은 소지품을 떠나면 손에서 내려가므로 던지는 순간 소지품에 있다.
        internal bool TryConsumeThrown(string id)
        {
            if (!_session.AcceptsCommands) return false;
            if (_session.Snapshot().SceneId == GameScene.BaseCamp) return true;
            return ChangeInventory(value => value.Consume(value.CharacterId, id, 1), out _);
        }

        // 소비품 하나를 소지품이나 펫 가방에서 빼고 소유 그래프만 확정한다. 원작처럼 펫 가방의 아이템도 바로 쓸 수 있다.
        // 회복 효과는 GameplayCommands.TryUseItem이 생존 값 소유자에 적용하도록 찾은 정의를 effect로 돌려준다.
        // 사용 동작은 정의를 찾은 사용 시간 있는 아이템에만 시작하므로 그 정의가 늘 있다. 두 가방 밖의 아이템은 NotUsable로 거절한다.
        internal bool TryConsumeOriginalItem(OriginalGameplayCatalog catalog, string id,
            [NotNullWhen(true)] out OriginalItemDefinition? effect)
        {
            effect = null;
            if (!_session.AcceptsCommands) return false;
            OriginalItemDefinition? found = null;
            if (!TryInventory(value =>
            {
                if (!value.TryUsableHolder(id, out string? holder)) return InventoryResult.Reject(InventoryRejection.NotUsable);
                // 사용 중에는 화면이 인벤토리 편집을 막아 아이템이 그 가방에 남고, 사용을 시작할 때 같은 아이템의 정의를 찾았다.
                // 그래서 두 조회는 늘 성공한다.
                value.TryItem(id, out ItemInstanceState? item);
                found = catalog.DefinitionOf(item!.OriginalItemId);
                return value.Consume(holder, id, 1);
            }, out ItemOwnershipState? graph, out _)) return false;
            CommitGameplay(graph);
            // 명령이 성공했으면 람다가 찾은 소비품 정의가 found에 들어 있다.
            effect = found!;
            return true;
        }

        // 원작 TryGetRequiredItem이다. sources가 고른 캐릭터 칸과 가방을 본다. 호출자는 열쇠 출처(ItemSourceRules.Keys)를 넘긴다.
        // 확정 그래프를 읽기만 하므로 명령 관문을 보지 않는다. 그래프가 아직 없으면 없는 것이다.
        internal bool HasRequiredItem(int originalItemId, ItemSources sources)
        {
            ItemOwnershipState? items = _session.Snapshot().Campaign.Gameplay.Inventory.Items;
            return items != null && new GameplayInventory(items).TryFindRequired(originalItemId, sources, out _, out _);
        }

        // 원작 UseRequiredItem이다. 찾은 아이템 하나를 쓰고 메모리에 확정한다. 내구도와 중첩 규칙은 원본 정의를 따른다.
        internal bool TryUseRequiredItem(OriginalGameplayCatalog catalog, int originalItemId, ItemSources sources)
        {
            if (!_session.AcceptsCommands) return false;
            // 필요 아이템 ID는 제작 도구가 원본에서 옮긴 빌드 데이터라 카탈로그 정의가 늘 있다.
            OriginalItemDefinition definition = catalog.DefinitionOf(originalItemId);
            bool usesDurability = definition.Durability > 0;
            int maximum = definition.MaxStack;
            if (!TryInventory(inventory => inventory.TryFindRequired(originalItemId, sources, out string? id, out string? parent)
                    ? inventory.UseRequired(id, parent, usesDurability, maximum)
                    : InventoryResult.Reject(InventoryRejection.NotOwned), out ItemOwnershipState? graph, out _)) return false;
            CommitGameplay(graph);
            return true;
        }

        // 원작 ItemUtilities.GetItemCount다. 현금 수(원본 ID 451)도 이 값으로 센다. 확정 그래프를 읽기만 하므로 명령 관문을 보지 않고,
        // 그래프가 아직 없으면 0이다.
        internal long CountOwned(int originalItemId, ItemSources sources)
        {
            ItemOwnershipState? items = _session.Snapshot().Campaign.Gameplay.Inventory.Items;
            // 비용 확인이 매 프레임 부르므로 작업 사본을 만들지 않고 확정 그래프에서 바로 센다.
            return items != null ? GameplayInventory.Count(items, originalItemId, sources) : 0;
        }

        // 화면 다시 그리기 한 번이 비용 여러 개를 볼 때 쓰는 출처별 수량 묶음이다. 확정 그래프를 한 번만 훑고 명령 관문은 보지 않는다.
        // 그래프가 아직 없으면 빈 묶음이라 HasOwned처럼 어떤 비용도 넉넉하지 않다.
        internal OwnedItemCounts CountAllOwned(ItemSources sources)
        {
            ItemOwnershipState? items = _session.Snapshot().Campaign.Gameplay.Inventory.Items;
            return items != null
                ? new OwnedItemCounts(GameplayInventory.CountAll(items, sources), sources, true)
                : new OwnedItemCounts(new Dictionary<int, long>(), sources, false);
        }

        // 원작 Cost.Enough의 아이템 부분이다. 같은 ID 항목은 합쳐서 본다. 비용 확인이 매 프레임 부르므로 확정 그래프를 읽기만 하고
        // 명령 관문을 보지 않는다. 그래프가 아직 없으면 부족하다.
        internal bool HasOwned(IReadOnlyList<(int id, int amount)> items, ItemSources sources)
        {
            ItemOwnershipState? graph = _session.Snapshot().Campaign.Gameplay.Inventory.Items;
            // 상호작용 초점 갱신이 매 프레임 부르므로 작업 사본을 만들지 않고 확정 그래프에서 바로 본다.
            return graph != null && GameplayInventory.Has(graph, items, sources);
        }

        // 지불 확인(GameplayCommands.CanPay)이 현금 몫을 붙인 배열 없이 묻는 경로다. 저작 비용 항목 끝에 extra 하나가 붙은 목록처럼
        // 위 HasOwned와 같은 규칙으로 본다. extra 수량이 0 이하면 붙이지 않은 것이다. 그래프가 아직 없으면 부족하다.
        internal bool HasOwned(IReadOnlyList<Cost.ItemEntry> items, (int id, int amount) extra, ItemSources sources)
        {
            ItemOwnershipState? graph = _session.Snapshot().Campaign.Gameplay.Inventory.Items;
            return graph != null && GameplayInventory.Has(graph, items, extra, sources);
        }

        // 원작 ConsumeItems(Cost)다. 모두 충분할 때만 출처 순서로 모아 내구도가 낮은 것부터 쓰고 메모리에 한 번 확정한다.
        // 돈이 걸린 비용은 CostRules.TryPlan이 현금 몫을 items에 붙이고, 이 명령이 성공한 뒤에만 계좌 몫을 뺀다.
        // 세션 관문은 여기서, 아이템 수는 Consume이 한 번씩 본다. GameplayCommands.TryPay는 이 두 확인에 기대 다시 보지 않는다.
        internal bool TryConsumeOwned(IReadOnlyList<(int id, int amount)> items, ItemSources sources)
        {
            if (!_session.AcceptsCommands) return false;
            return ChangeInventory((items: items, sources: sources), (inventory, cost) => inventory.Consume(cost.items, cost.sources), out _);
        }

        // 원작 SubmitItems.Interact다. 정렬 없이 출처 순서로 최대 maximum개를 내고 낸 수를 submitted로 돌려준다. 일부만 낼 수 있다.
        // 하나도 없거나 관문이 닫혔으면 기록 없이 false이고 submitted는 0이다.
        internal bool TrySubmitOwned(int originalItemId, int maximum, ItemSources sources, out int submitted)
        {
            submitted = 0;
            if (!_session.AcceptsCommands) return false;
            int taken = 0;
            if (!TryInventory(inventory => inventory.Submit(originalItemId, maximum, sources, out taken), out ItemOwnershipState? graph, out _))
                return false;
            CommitGameplay(graph);
            submitted = taken;
            return true;
        }

        // 원작 StockShop.Sell의 Detach와 DestroyTree다. sources가 고른 가방(창고, 가방, 펫 가방)의 맨 위 칸에서 그 ID의 아이템을
        // 하위 트리(부착물, 내용물)째 빼고 메모리에 한 번 확정한다. 뺀 트리는 루트 하나짜리 그래프로 removed에 담는다.
        // 관문이 닫혔거나 고른 가방의 맨 위 칸에 없으면 기록 없이 false다. 호출자: GameplayCommands.TrySellShopItem(2-P2 상점 화면, 5-M 비밀 상인).
        internal bool TryRemoveOwned(string itemId, ItemSources sources, [NotNullWhen(true)] out ItemOwnershipState? removed)
        {
            removed = null;
            if (!_session.AcceptsCommands) return false;
            ItemOwnershipState? taken = null;
            if (!TryInventory(inventory => inventory.Remove(itemId, sources, out taken), out ItemOwnershipState? graph, out _)) return false;
            CommitGameplay(graph);
            // 명령이 성공했으면 람다가 뺀 트리가 taken에 들어 있다.
            removed = taken!;
            return true;
        }

        // 원작 Cost.Return, PlayerStorage.Push, 상점 구매처럼 새 아이템을 원본 최대 스택 단위로 만들어 delivery가 고른 곳에 보내고 한 번 확정한다.
        // 창고는 원작 PlayerStorage.Instance처럼 기지에서만 쓴다. 소지품과 창고에 다 들어가지 않은 수량은 창고 임시 보관함 끝에 차례로 붙고,
        // PlayerThenDrop만 보관함 대신 dropped로 돌려줘 호출자가 바닥에 떨군다. 관문이 닫혔으면 기록 없이 false이고, 구조 결함은 발견한 곳이 보고한다.
        // 호출자: 제조 결과(GameplayCommands.TryCraft), 퀘스트 보상, 상점 구매와 판매 대금, 낚시 결과.
        // fromInfo는 새 아이템마다 넣는 원작 FromInfoKey다. 원작이 넣지 않는 경로는 null을 넘긴다. 새 호출자가 출처를 빠뜨리지 않도록 기본값을 두지 않는다.
        internal bool TryDeliver(OriginalGameplayCatalog catalog, IReadOnlyList<(int id, int count)> items, ItemDelivery delivery, string? fromInfo,
            out IReadOnlyList<ItemOwnershipState> dropped)
        {
            dropped = Array.Empty<ItemOwnershipState>();
            if (!_session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = _session.Snapshot();
            bool toCarried = delivery == ItemDelivery.PlayerThenStorage || delivery == ItemDelivery.PlayerThenDrop;
            bool toStorage = snapshot.SceneId == GameScene.BaseCamp
                && (delivery == ItemDelivery.PlayerThenStorage || delivery == ItemDelivery.Storage);
            InventoryPersistenceState state = snapshot.Campaign.Gameplay.Inventory;
            // 아이템 지급은 월드 첫 초기화가 소유 그래프를 만든 뒤에만 온다.
            ItemOwnershipState source = state.Items!;
            var inventory = new GameplayInventory(source);
            var left = new List<ItemOwnershipState>();
            for (int index = 0; index < items.Count; index++)
            {
                // 아이템 ID는 제작 도구가 원본에서 옮긴 빌드 데이터라 카탈로그 정의가 늘 있다.
                OriginalItemDefinition definition = catalog.DefinitionOf(items[index].id);
                for (int remaining = items[index].count; remaining > 0;)
                {
                    int amount = Math.Min(remaining, definition.MaxStack);
                    // 원작처럼 지급하는 새 아이템마다 호출자의 출처 키를 남긴다.
                    if (!inventory.Deliver(NewItem(definition, amount, fromInfo), definition.MaxStack, toCarried, toStorage,
                            out ItemOwnershipState? rest).IsOk) return false;
                    if (rest != null) left.Add(rest);
                    remaining -= amount;
                }
            }
            ItemOwnershipState graph = inventory.Build();
            bool drop = delivery == ItemDelivery.PlayerThenDrop;
            if (drop || left.Count == 0)
            {
                if (!ReferenceEquals(graph, source)) CommitGameplay(graph);
                if (drop) dropped = left;
                return true;
            }
            // 넘친 그래프를 보관함 끝에 붙이는 확정은 개조와 분해 명령과 같다.
            CommitWithBuffer(snapshot, graph, left);
            return true;
        }

        // 원작 PlayerStorage.TakeBufferItem이다. 기지에서 창고 임시 보관함의 index번째 그래프 하나를 원래 ID 그대로 창고에 합쳐 넣고
        // 보관함에서 빼며, 그래프와 보관함을 한 번에 확정한다. 남은 항목의 순서는 그대로다. 원작 범위 검사(index <= Count) 결함은 고쳐 개수 이상은 거절한다.
        // 관문이 닫혔거나, 기지가 아니거나, 번호가 범위 밖이거나, 창고에 빈 칸이 없으면 기록 없이 false다. 호출자: 2-P3 보관함 화면.
        internal bool TryTakeBufferItem(OriginalGameplayCatalog catalog, int index)
        {
            if (!_session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = _session.Snapshot();
            if (snapshot.SceneId != GameScene.BaseCamp) return false;
            InventoryPersistenceState state = snapshot.Campaign.Gameplay.Inventory;
            IReadOnlyList<ItemOwnershipState> buffer = state.StorageBuffer;
            if (index < 0 || index >= buffer.Count) return false;
            var rules = new CatalogRules(catalog);
            // 기지 월드 첫 초기화가 소유 그래프를 만든 뒤에만 보관함 화면이 열린다.
            if (!TryInventory(state.Items!, (Taken: buffer[index], Rules: rules.Rules),
                    (inventory, take) => inventory.StoreBuffered(take.Taken, take.Rules), out ItemOwnershipState? graph, out _)) return false;
            var rest = new List<ItemOwnershipState>(buffer.Count - 1);
            for (int position = 0; position < buffer.Count; position++)
                if (position != index) rest.Add(buffer[position]);
            CommitInventory(snapshot, graph, rest);
            return true;
        }

        // 원작 ItemCustomizeView.OnEquipButtonClicked(개조 장착)다. 계산 규칙은 GameplayInventory.CustomizePlug에 있다. 칸에 있던 부품은
        // 사용자 결정(2026-10-20)대로 기지면 창고 먼저(합치기 → 빈 칸), 아니면 가방에 돌려주고, 넘친 것은 창고 임시 보관함 끝에 붙여 한 번에 확정한다.
        // 관문이 닫혔거나 규칙에 걸리면 기록 없이 false이고 아무것도 바꾸지 않는다. 호출자: CustomizeScreen(Workbench로 여는 개조 창).
        internal bool TryCustomizePlug(OriginalGameplayCatalog catalog, string targetId, string slotKey, string partId)
        {
            if (!_session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = _session.Snapshot();
            bool atBase = snapshot.SceneId == GameScene.BaseCamp;
            var rules = new CatalogRules(catalog);
            var left = new List<ItemOwnershipState>(1);
            if (!TryInventory(inventory => inventory.CustomizePlug(targetId, slotKey, partId, atBase, rules.Rules, left),
                    out ItemOwnershipState? graph, out _)) return false;
            CommitWithBuffer(snapshot, graph, left);
            return true;
        }

        // 원작 ItemCustomizeView.OnUnequipButtonClicked(개조 해제)다. 계산 규칙은 GameplayInventory.CustomizeUnplug에 있고,
        // 뺀 부품의 행선지와 넘침 처리는 TryCustomizePlug와 같다. 빈 칸이나 없는 칸은 기록 없이 false다. 호출자: CustomizeScreen.
        internal bool TryCustomizeUnplug(OriginalGameplayCatalog catalog, string targetId, string slotKey)
        {
            if (!_session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = _session.Snapshot();
            bool atBase = snapshot.SceneId == GameScene.BaseCamp;
            var rules = new CatalogRules(catalog);
            ItemOwnershipState? rest = null;
            if (!TryInventory(inventory => inventory.CustomizeUnplug(targetId, slotKey, atBase, rules.Rules, out rest),
                    out ItemOwnershipState? graph, out _)) return false;
            CommitWithBuffer(snapshot, graph, rest != null ? new[] { rest } : Array.Empty<ItemOwnershipState>());
            return true;
        }

        // 성공한 명령의 그래프와, 들어가지 못해 돌려받은 루트 하나짜리 그래프들을 확정한다. 남은 것이 없으면 그래프만, 있으면 창고 임시 보관함
        // 끝에 차례로 붙여 한 번에 확정한다. snapshot은 명령을 계산한 그 확정 상태다. 개조, 분해, 아이템 지급이 함께 쓴다.
        void CommitWithBuffer(GameSessionSnapshot snapshot, ItemOwnershipState graph, IReadOnlyList<ItemOwnershipState> left)
        {
            if (left.Count == 0)
            {
                CommitGameplay(graph);
                return;
            }
            IReadOnlyList<ItemOwnershipState> current = snapshot.Campaign.Gameplay.Inventory.StorageBuffer;
            var buffer = new List<ItemOwnershipState>(current.Count + left.Count);
            buffer.AddRange(current);
            buffer.AddRange(left);
            CommitInventory(snapshot, graph, buffer);
        }

        // 확정 세션의 소유 그래프로 인벤토리 명령을 계산한다.
        bool TryInventory(Func<GameplayInventory, InventoryResult> command, [NotNullWhen(true)] out ItemOwnershipState? graph,
            out InventoryRejection rejection)
        {
            graph = null;
            rejection = InventoryRejection.None;
            // 아이템 사용은 월드 첫 초기화가 소유 그래프를 만든 뒤에만 끝난다.
            ItemOwnershipState source = _session.Snapshot().Campaign.Gameplay.Inventory.Items!;
            return TryInventory(source, command, out graph, out rejection);
        }

        // 주어진 소유 그래프의 작업 사본으로 인벤토리 명령을 계산한다. 루팅은 앞 항목의 결과 그래프를 넘겨 차례로 쌓는다.
        // 계산은 아래 제네릭 오버로드가 하고, 이 오버로드는 명령 대리자를 상태 인자로 넘긴다.
        static bool TryInventory(ItemOwnershipState source, Func<GameplayInventory, InventoryResult> command,
            [NotNullWhen(true)] out ItemOwnershipState? graph, out InventoryRejection rejection) =>
            TryInventory(source, command, (inventory, run) => run(inventory), out graph, out rejection);

        // 명령 인수를 state로 받으므로 호출자가 캡처 없는 람다를 넘길 수 있다. 실패한 사본은 버려 확정 세션을 바꾸지 않는다.
        // 규칙 거절은 기록 없이 코드를 돌려주고, 구조 결함은 발견한 곳이 이미 보고했다.
        static bool TryInventory<TState>(ItemOwnershipState source, TState state, Func<GameplayInventory, TState, InventoryResult> command,
            [NotNullWhen(true)] out ItemOwnershipState? graph, out InventoryRejection rejection)
        {
            graph = null;
            var inventory = new GameplayInventory(source);
            InventoryResult result = command(inventory, state);
            rejection = result.Rejection;
            if (!result.IsOk) return false;
            // 명령이 성공하면 작업 사본은 소유 규칙을 지키므로 결과 그래프는 늘 만들어진다.
            graph = inventory.Build();
            return true;
        }

        // 인벤토리 명령을 계산해 확정한다. 확정은 아래 제네릭 오버로드가 하고, 이 오버로드는 명령 대리자를 상태 인자로 넘긴다.
        bool ChangeInventory(Func<GameplayInventory, InventoryResult> command, out InventoryRejection rejection) =>
            ChangeInventory(command, (inventory, run) => run(inventory), out rejection);

        // 명령 인수를 state로 넘겨 호출마다 닫힘 객체를 만들지 않는다. 규칙 거절은 코드로 돌려준다.
        // 계산이 성공하면 CommitGameplay는 실패하지 않는다.
        bool ChangeInventory<TState>(TState state, Func<GameplayInventory, TState, InventoryResult> command,
            out InventoryRejection rejection)
        {
            rejection = InventoryRejection.None;
            // 인벤토리 명령은 월드 첫 초기화가 소유 그래프를 만든 뒤에만 온다.
            ItemOwnershipState source = _session.Snapshot().Campaign.Gameplay.Inventory.Items!;
            if (!TryInventory(source, state, command, out ItemOwnershipState? graph, out rejection)) return false;
            // 생존 값은 PlayerVitals가 소유하므로 인벤토리 명령은 그래프만 확정한다. 바뀐 것이 없으면 Build가 원본 그래프를
            // 돌려주므로, 기지 발사처럼 같은 값을 다시 확정해 구독자에게 알리지 않는다.
            if (!ReferenceEquals(graph, source)) CommitGameplay(graph);
            return true;
        }

        // 새 소유 그래프를 캠페인 복사 한 번으로 확정하고, 그래프 불변식에만 쓰는 세션의 플레이어 사본은 그대로 둔다.
        // 그래프는 작업 사본이 소유 규칙을 지켜 Build로 만든 값이다. 퀵슬롯은 WithItems가 맞추며 인벤토리 명령은 아이템 정의를
        // 바꾸지 않으므로, 재검증 없는 With*를 쓴다.
        void CommitGameplay(ItemOwnershipState graph)
        {
            GameSessionSnapshot snapshot = _session.Snapshot();
            CampaignState previous = snapshot.Campaign;
            _session.Commit(snapshot.WithCampaign(previous.WithGameplay(previous.Gameplay.WithItems(graph))));
        }

        // 보관함 넣기와 수령이 소유 그래프와 창고 임시 보관함을 캠페인 복사 한 번으로 확정한다. 퀵슬롯은 WithItems가 새 그래프에 맞춘 값이다.
        // 그래프는 작업 사본이 소유 규칙을 지켜 만든 값이고 보관함 항목은 루트 하나짜리 그래프라, 믿을 수 있는 입력용 생성자를 쓴다.
        // 소지품 묶음 전체를 바꾸는 With는 WithDeathRecord뿐이라 지금 사망 기록을 그대로 넘긴다.
        void CommitInventory(GameSessionSnapshot snapshot, ItemOwnershipState graph, IReadOnlyList<ItemOwnershipState> buffer)
        {
            CampaignState campaign = snapshot.Campaign;
            GameplayPersistenceState gameplay = campaign.Gameplay;
            InventoryPersistenceState reconciled = gameplay.Inventory.WithItems(graph);
            var inventory = new InventoryPersistenceState(reconciled.Items, reconciled.Shortcuts, buffer);
            _session.Commit(snapshot.WithCampaign(campaign.WithGameplay(gameplay.WithDeathRecord(inventory, gameplay.Deaths))));
        }

        // 작업 사본이 카탈로그를 모르도록 정의 조회를 InventoryRules 대리자로 감싼다. 명령마다 하나씩 만든다.
        sealed class CatalogRules
        {
            readonly OriginalGameplayCatalog _catalog;

            internal CatalogRules(OriginalGameplayCatalog catalog)
            {
                _catalog = catalog;
                // 펫 가방의 Sticky 거절도 다른 규칙과 같은 정의 조회로 판정한다.
                Rules = new InventoryRules(MaxStack, SlotsFor, SortKey, ItemSlotsFor, ItemSlotAccepts, Sticky);
            }

            internal InventoryRules Rules { get; }

            // 합칠 수 있는 수량은 원본 정의의 최대 스택이다.
            int MaxStack(ItemInstanceState item) => Definition(item).MaxStack;

            // 원작 'Sticky' 태그는 정의 플래그로 보존돼 있다.
            bool Sticky(ItemInstanceState item) => Definition(item).Sticky;

            // 받는 칸은 원작 칸 표(OriginalCharacterSlots) 한 곳에서 정한다.
            IReadOnlyList<string> SlotsFor(ItemInstanceState item) => OriginalCharacterSlots.SlotsFor(Definition(item));

            // 아이템 칸은 원본 정의의 SlotCollection 키다.
            IReadOnlyList<string> ItemSlotsFor(ItemInstanceState owner) => Definition(owner).ItemSlotKeys;

            // 원작 Slot.CanPlug의 태그 검사다. 주인 정의에 그 칸이 없으면 받지 않는다.
            bool ItemSlotAccepts(ItemInstanceState owner, string slot, ItemInstanceState item) =>
                Definition(owner).TryGetItemSlot(slot, out OriginalItemSlot? found) && found.Accepts(Definition(item));

            // 정렬 키는 원본 정의의 첫 태그와 순서다. 정의는 한 번만 찾는다.
            InventorySortKey SortKey(ItemInstanceState item)
            {
                OriginalItemDefinition definition = Definition(item);
                return new InventorySortKey(definition.FirstTag, definition.FirstTagPriority, definition.Order);
            }

            // 이어하기와 저장 지점이 그래프의 아이템 ID를 카탈로그와 대조하고, 세션은 카탈로그 정의로만 아이템을 만든다.
            // 그래서 명령이 묻는 아이템의 정의 조회는 늘 성공한다.
            OriginalItemDefinition Definition(ItemInstanceState item) => _catalog.DefinitionOf(item.OriginalItemId);
        }
    }
}
