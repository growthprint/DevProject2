#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원본 캐릭터 무기 슬롯 키다. OriginalItemDefinition.Slot 문자열과 같아야 하므로 enum 대신 문자열 상수로 둔다.
    // All은 원본 WeaponButtons의 표시 순서로, 주무기, 보조 무기, 근접 무기 순이다.
    internal static class GameplayWeaponSlots
    {
        internal const string _primary = "PrimaryWeapon";
        internal const string _secondary = "SecondaryWeapon";
        internal const string _melee = "MeleeWeapon";
        internal static IReadOnlyList<string> All { get; } = new[] { _primary, _secondary, _melee };

        // 슬롯 키의 표시 순서 0~2를 돌려준다. 무기 슬롯이 아니면 -1이다.
        internal static int IndexOf(string slot) => slot == _primary ? 0 : slot == _secondary ? 1 : slot == _melee ? 2 : -1;
    }

    // 불변 소유 그래프(ItemOwnershipState)에서 퀵슬롯, 탄약, 장착을 읽기만 하는 질의다. 그래프를 복사하거나 바꾸지 않는다.
    // 질의는 던지지도 기록하지도 않는 Try 형식이다. 퀵슬롯 없음, 빈 칸, 빈 탄창 같은 정상 상태는 true와 null을 돌려준다.
    // 그래프 구조 결함은 false를 돌려주고, 결함 기록은 호출자가 키마다 한 번 한다. 매 프레임 불리므로 목록이나 반복기를 새로 만들지 않는다.
    // MagazineCount(원본 GetBulletCount)와 화면 칸 수 CellCount는 실패가 없어 Try 형식 없이 int를 돌려준다.
    internal static class OriginalInventoryQueries
    {
        // 원본 MainCharacterItemData에 해당하는 플레이어 루트 아이템을 찾는다. 루트 ID나 그 아이템이 없으면 구조 결함으로 false다.
        internal static bool TryPlayerItem(ItemOwnershipState items, [NotNullWhen(true)] out ItemInstanceState? player)
        {
            player = Find(items, items.CharacterId);
            return player != null;
        }

        // 확정 그래프에서 ID로 아이템을 찾는 조회 함수다. 캡처 없는 정적 대리자라 질의마다 할당하지 않는다.
        static readonly Func<ItemOwnershipState, string, ItemInstanceState?> _graphFind = (items, id) => items.Find(id);

        // 확정 그래프의 컨테이너를 찾는다. 규칙은 작업 사본과 함께 쓰는 아래 TryContainer에 있다. 둘 다 없으면 결함으로 false다.
        internal static bool TryContainer(ItemOwnershipState items, string containerId, [NotNullWhen(true)] out ItemContainerState? container) =>
            TryContainer(items, _graphFind, items.Inventories, containerId, out container, out _, out _);

        // 원작처럼 창고(PlayerStorage)는 출처에 창고가 든 씬, 곧 기지에서만 목록으로 보인다. 상점, 등록, 분해, 개조 화면이
        // 같은 판정을 쓴다. 출처에 창고가 없으면 컨테이너를 찾지 않고 false와 null이다.
        internal static bool TryShownStorage(ItemOwnershipState items, ItemSources sources, [NotNullWhen(true)] out ItemContainerState? stored)
        {
            stored = null;
            return (sources & ItemSources.Storage) != 0 && TryContainer(items, GameplayInventory._storage, out stored);
        }

        // 등록 칸에 놓은 아이템이 아직 화면 목록(소지 가방, 보이는 창고)에 있는지다. 다른 명령으로 옮겨졌으면 칸을 비운다.
        // 두 등록 화면이 같은 판정을 쓴다. 가방을 먼저 보고, 없을 때만 창고를 본다.
        internal static bool SlotStillListed(ItemOwnershipState items, string bag, bool storage, string slotItem) =>
            items.PositionIn(bag, slotItem) != null || (storage && items.PositionIn(GameplayInventory._storage, slotItem) != null);

        // 화면이 그릴 칸 수다. 원본 InventoryDisplay처럼 용량까지 빈 칸도 그리고, 용량을 모르는 컨테이너는 차 있는 마지막 칸까지만 그린다.
        // 용량이 있으면 생성 검증이 모든 칸을 용량 안에 둔다. 인벤토리, 상점, 등록, 수리, 분해, 개조 화면이 함께 쓴다.
        internal static int CellCount(ItemContainerState container)
        {
            if (container.Capacity.HasValue) return container.Capacity.Value;
            int count = 0;
            IReadOnlyList<ItemPositionState> entries = container.Entries;
            for (int index = 0; index < entries.Count; index++) count = Math.Max(count, entries[index].Position + 1);
            return count;
        }

        // 아이템 인벤토리나 창고 같은 저장 인벤토리를 찾는다. 같은 ID의 아이템이 우선이고, 그 아이템에 인벤토리가 없으면
        // 저장 인벤토리를 보지 않고 false다. 확정 그래프와 인벤토리 작업 사본이 조회 함수만 바꿔 같은 규칙을 쓴다.
        // 소유자가 아이템이면 owner에, 저장 인벤토리면 그 색인이 stored에 담긴다. 화면 갱신과 발사마다 불리므로 인덱스로 돈다.
        internal static bool TryContainer<TState>(TState state, Func<TState, string, ItemInstanceState?> find,
            IReadOnlyList<StoredInventoryState> inventories, string containerId, [NotNullWhen(true)] out ItemContainerState? container,
            out ItemInstanceState? owner, out int stored)
        {
            container = null;
            stored = -1;
            owner = find(state, containerId);
            if (owner != null) container = owner.Inventory;
            else
                for (int index = 0; index < inventories.Count; index++)
                {
                    if (inventories[index].Key != containerId) continue;
                    stored = index;
                    container = inventories[index].Inventory;
                    break;
                }
            return container != null;
        }

        // 퀵슬롯 칸(0~5)에 등록된 소지 인벤토리 위치의 아이템을 찾는다. 퀵슬롯 표시가 갱신마다 루트를 한 번만 찾도록 player를 받는다.
        // ItemShortcutState는 늘 6칸이다. 퀵슬롯 없음, 빈 칸, 비어 있는 위치는 true와 null이고,
        // 위치가 가리키는 아이템이 없으면 구조 결함으로 false다.
        internal static bool TryShortcutEntry(ItemOwnershipState items, ItemShortcutState? shortcuts, int slot, ItemInstanceState? player,
            out ItemInstanceState? entry)
        {
            entry = null;
            if (shortcuts == null) return true;
            IReadOnlyList<int> indexes = shortcuts.InventoryIndexes;
            int position = indexes[slot];
            if (position < 0) return true;
            // 플레이어 루트는 InventoryPersistenceState 검증이 보장하고 명령이 지우지 않으므로 player는 null이 아니다.
            string? id = items.EntryAt(player!.InstanceId, position);
            if (id == null) return true;
            entry = items.Find(id);
            return entry != null;
        }

        // 소지 인벤토리를 저장된 칸 순서대로 보며, 무기와 구경이 같은 첫 탄 묶음과 그 탄종의 수량 합을 구한다.
        // 원본 장전과 탄약 HUD도 이 순서로 첫 항목을 고른다. 플레이어 루트나 소지 인벤토리가 없으면 결함으로 false와 null, 0을 돌려준다.
        // bulletType을 주면 그 원본 탄종만 센다. 장전과 탄약 HUD는 원본처럼 총의 목표 탄종으로 거른다.
        internal static bool TryCompatibleAmmo(ItemOwnershipState items, OriginalGameplayCatalog catalog, OriginalItemDefinition weapon,
            out ItemInstanceState? first, out int quantity, int? bulletType = null)
        {
            first = null;
            quantity = 0;
            if (!TryPlayerItem(items, out ItemInstanceState? player) || player.Inventory == null) return false;
            IReadOnlyList<ItemPositionState> entries = player.Inventory.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                // 컨테이너 생성 검증(CheckUnique)이 null 칸을 거절하므로 entries[i]는 null이 아니다. 그래프 검증이 끊긴 칸을 거절하고
                // 저장 읽기 검사가 모르는 ID를 막아, 칸의 아이템과 그 카탈로그 정의는 늘 있다.
                ItemInstanceState item = Find(items, entries[i].InstanceId)!;
                OriginalItemDefinition definition = catalog.DefinitionOf(item.OriginalItemId);
                // 구경이 같은 탄만 보고, 탄종 필터가 있으면 그 탄종만 본다. 수량은 첫 호환 탄과 같은 탄종만 더한다.
                if ((bulletType == null || item.OriginalItemId == bulletType) && definition.IsBullet && definition.Caliber == weapon.Caliber)
                {
                    first ??= item;
                    if (item.OriginalItemId == first.OriginalItemId) quantity += item.Quantity;
                }
            }
            return true;
        }

        // 저장 형식 검사는 카탈로그를 모르므로, 모르는 ID나 탄창 없는 총이 월드에서 결함으로 터지지 않게 이어하기 읽기와 저장 수집이
        // 이 판정을 함께 쓴다. 이어하기 정산은 레거시 출격 결과의 플레이 상태를 채택하므로 그것도 본다. 기록하지 않고 첫 위반을 reason에 담는다.
        // 규칙: Docs/game-save.md의 Gameplay state 절.
        internal static bool TryCheckSavedItems(CampaignState campaign, OriginalGameplayCatalog catalog, out string reason)
        {
            if (!TryCheckSavedItems(campaign.Gameplay, catalog, out reason)) return false;
            GameplayPersistenceState? result = campaign.Raid?.Result?.Gameplay;
            return result == null || TryCheckSavedItems(result, catalog, out reason);
        }

        // 월드가 읽는 현재 소유 그래프와 유실물 그래프만 본다. 사망 당시 그래프와 상점 ID는 읽는 런타임 코드가 없어 막지 않는다.
        // 창고 임시 보관함 그래프도 수령 때 소유 그래프로 들어오므로 같은 판정으로 막는다.
        static bool TryCheckSavedItems(GameplayPersistenceState gameplay, OriginalGameplayCatalog catalog, out string reason)
        {
            reason = "";
            ItemOwnershipState? items = gameplay.Inventory.Items;
            if (items != null && !TryCheckSavedItems(items, catalog, out reason)) return false;
            // 보관함 목록은 미포착 상태가 없어 늘 있으므로 null을 보지 않는다. 유실물 검사의 조기 반환보다 먼저 돈다.
            IReadOnlyList<ItemOwnershipState> buffer = gameplay.Inventory.StorageBuffer;
            for (int i = 0; i < buffer.Count; i++)
                if (!TryCheckSavedItems(buffer[i], catalog, out reason)) return false;
            IReadOnlyList<LostBodyState>? bodies = gameplay.Deaths.LostBodies;
            if (bodies == null) return true;
            for (int i = 0; i < bodies.Count; i++)
                if (!TryCheckSavedItems(bodies[i].Items, catalog, out reason)) return false;
            return true;
        }

        // 총의 탄창은 아이템 인벤토리라, 정의가 총인데 인벤토리가 없으면 장전과 발사와 탄약 HUD가 탄창을 찾지 못한다.
        static bool TryCheckSavedItems(ItemOwnershipState items, OriginalGameplayCatalog catalog, out string reason)
        {
            IReadOnlyList<ItemInstanceState> nodes = items.Items;
            for (int i = 0; i < nodes.Count; i++)
            {
                ItemInstanceState item = nodes[i];
                if (!catalog.TryGet(item.OriginalItemId, out OriginalItemDefinition? definition))
                {
                    reason = "카탈로그에 없는 아이템입니다: " + item.OriginalItemId + " (" + item.InstanceId + ")";
                    return false;
                }
                if (definition.IsGun && item.Inventory == null)
                {
                    reason = "탄창 컨테이너가 없는 총입니다: " + item.OriginalItemId + " (" + item.InstanceId + ")";
                    return false;
                }
            }
            reason = "";
            return true;
        }

        // 플레이어 슬롯에 장착된 아이템의 인스턴스 ID를 찾는다. 슬롯이 비어 있으면 true와 null이고,
        // 플레이어 루트가 없으면 구조 결함으로 false다.
        internal static bool TryEquippedIn(ItemOwnershipState items, string slot, out string? instanceId) =>
            TryEquippedIn(TryPlayerItem(items, out ItemInstanceState? player) ? player : null, slot, out instanceId);

        // 플레이어 루트를 이미 찾은 호출자용이다. player가 null이면 루트 누락 구조 결함으로 false다.
        internal static bool TryEquippedIn(ItemInstanceState? player, string slot, out string? instanceId)
        {
            instanceId = null;
            if (player == null) return false;
            // 장착 칸 목록은 읽기 전용 배열 래퍼라 foreach가 열거자를 할당하므로 인덱스로 돈다.
            IReadOnlyList<ItemSlotState> slots = player.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                ItemSlotState value = slots[i];
                if (value != null && value.Key == slot)
                {
                    instanceId = value.InstanceId;
                    return true;
                }
            }
            return true;
        }

        // 인스턴스에서 슬롯을 거꾸로 찾는 질의로, 인벤토리 화면과 인벤토리 작업 사본이 함께 쓴다.
        // 그 인스턴스를 꽂은 플레이어 슬롯이 없으면 null이다. 선택마다 불리므로 반복기 없이 인덱스로 돈다.
        internal static ItemSlotState? EquippedSlot(ItemInstanceState player, string instanceId)
        {
            IReadOnlyList<ItemSlotState> slots = player.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                ItemSlotState slot = slots[i];
                if (slot != null && slot.InstanceId == instanceId) return slot;
            }
            return null;
        }

        // 아이템의 칸 목록에서 그 칸 키에 꽂힌 인스턴스 ID를 찾는다. 비어 있으면 null이다. 아이템 상세와 개조 화면이 함께 쓴다.
        internal static string? SlotHolder(IReadOnlyList<ItemSlotState> slots, string key)
        {
            for (int index = 0; index < slots.Count; index++)
                if (slots[index].Key == key) return slots[index].InstanceId;
            return null;
        }

        // 원본 ItemSetting_Gun.GetBulletCount처럼 탄창에 든 탄 수량을 합한다.
        internal static int MagazineCount(ItemOwnershipState items, string weapon)
        {
            // 탄종 필터가 없으면 합계는 실패하지 않으므로 결과를 보지 않는다.
            TrySumMagazine(items, _graphFind, Magazine(items, weapon), null, out int total);
            return total;
        }

        // 탄창 칸의 탄 수량을 원본 GetBulletCount처럼 int로 합하며, 원본처럼 넘침 검사는 하지 않는다. bulletType을 주면
        // 다른 탄종이 든 칸에서 false와 0이다. 탄약 HUD와 작업 사본의 장전이 함께 쓰며, 발사마다 불리므로 반복기 없이 인덱스로 돈다.
        internal static bool TrySumMagazine<TState>(TState state, Func<TState, string, ItemInstanceState?> find, ItemContainerState magazine,
            int? bulletType, out int total)
        {
            total = 0;
            IReadOnlyList<ItemPositionState> entries = magazine.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                // 검증된 그래프와 명령은 끊긴 칸을 만들지 않으므로 칸이 가리키는 탄은 늘 있다.
                ItemInstanceState ammo = find(state, entries[i].InstanceId)!;
                if (bulletType != null && ammo.OriginalItemId != bulletType)
                {
                    total = 0;
                    return false;
                }
                total += ammo.Quantity;
            }
            return true;
        }

        // 총의 탄창에 든 첫 탄 묶음으로, 원본 발사가 소모할 탄약이다.
        // 빈 탄창이면 true와 null이고, 첫 칸의 탄이 없으면 구조 결함으로 false다.
        internal static bool TryLoadedAmmo(ItemOwnershipState items, string weapon, out ItemInstanceState? ammo)
        {
            ammo = null;
            // 그래프의 총은 늘 탄창 컨테이너를 가지므로 실패 확인 없이 바로 읽는다.
            ItemContainerState magazine = Magazine(items, weapon);
            if (magazine.Entries.Count == 0) return true;
            // 컨테이너 생성 검증이 null 칸을 거절하므로 첫 칸을 ?. 없이 읽는다.
            ammo = Find(items, magazine.Entries[0].InstanceId);
            return ammo != null;
        }

        // 총 인스턴스의 탄창 컨테이너다. 호출자는 그래프에 있는 총만 넘기고, 그래프의 총은 늘 탄창 컨테이너를 가진다.
        // 저장 읽기 검사가 탄창 없는 총을 막으므로 실패 결과를 두지 않는다.
        static ItemContainerState Magazine(ItemOwnershipState items, string weapon) => items.Find(weapon)!.Inventory!;

        // 원본 Item.TotalWeight처럼 아이템 무게에 칸에 꽂힌 아이템과 인벤토리 내용물의 무게를 재귀로 더한다.
        // 검증된 그래프가 가리키는 ID라 노드는 늘 있고, 그래프의 아이템은 저장 읽기 검사로 늘 카탈로그 정의가 있다.
        // 아이템마다 대리자를 할당하지 않도록 LINQ 없이 인덱스로 더한다.
        internal static float TotalWeight(ItemOwnershipState items, OriginalGameplayCatalog catalog, string id)
        {
            ItemInstanceState item = items.Find(id)!;
            float total = catalog.DefinitionOf(item.OriginalItemId).Weight * item.Quantity;
            IReadOnlyList<ItemSlotState> slots = item.Slots;
            for (int i = 0; i < slots.Count; i++) total += TotalWeight(items, catalog, slots[i].InstanceId);
            if (item.Inventory != null)
            {
                IReadOnlyList<ItemPositionState> entries = item.Inventory.Entries;
                for (int i = 0; i < entries.Count; i++) total += TotalWeight(items, catalog, entries[i].InstanceId);
            }
            return total;
        }

        // 원작 ItemUtilities.IsRegistered다. 활성 열쇠이거나 해금된 제조식의 설계도이면 "등록됨"이다. 퀵슬롯 등록과는 상관없다.
        // 아이템 종류로만 정하므로 상자와 상점 칸의 아이템도 같다. 호출자: InventoryItemDetails(상세 화면 표시).
        internal static bool IsRegistered(MasterKeysManager keys, CraftingManager crafting, int originalItemId) =>
            keys.IsActive(originalItemId) || crafting.IsItemRegistered(originalItemId);

        // ID가 가리키는 아이템을 찾는다. ID가 null이거나 그래프에 없으면 던지지 않고 null을 돌려준다.
        static ItemInstanceState? Find(ItemOwnershipState items, string? id) => id != null ? items.Find(id) : null;
    }
}
