#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 현재 소유 그래프와 그에 맞춘 퀵슬롯을 묶는다. 플레이어 인벤토리가 있어야 한다는 규칙과 퀵슬롯이 실제 아이템을 가리켜야 한다는
    // 규칙을 이 묶음이 지킨다. 두 필드의 null은 각각 미포착이고, 사망 당시 그래프는 DeathPersistenceState가 따로 보관한다.
    // 창고 임시 보관함(원작 PlayerStorageBuffer)도 이 묶음에 둔다. 보관함 수령이 그래프와 보관함을 한 커밋으로 바꾸기 때문이다.
    public sealed class InventoryPersistenceState
    {
        // 믿을 수 있는 입력용 생성자다. 생략한 인자는 미포착(null)이다.
        // 저장 로드는 모든 인자가 필수인 TryCreate를 쓰므로, 필드가 늘면 코덱에서 컴파일 오류가 나 누락을 막는다.
        // 보관함은 미포착 상태가 없어 생략하면 빈 목록이다. 목록은 따로 한 번 복사한다.
        public InventoryPersistenceState(ItemOwnershipState? items = null, ItemShortcutState? shortcuts = null,
            IReadOnlyList<ItemOwnershipState>? storageBuffer = null)
        {
            Items = items; Shortcuts = shortcuts; StorageBuffer = ReadOnlyLists.CopyOrEmpty(storageBuffer);
        }

        // TryCreate가 쓰는 검사다. 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(ItemOwnershipState? items, ItemShortcutState? shortcuts, IReadOnlyList<ItemOwnershipState>? storageBuffer)
        {
            if (items != null && !TryCheckPlayerInventory(items)) return false;
            if (!IsStorageBuffer(storageBuffer)) return false;
            return !ShortcutViolation(items, shortcuts?.InventoryIndexes);
        }

        // 보관함 항목은 원작 ItemTreeData 하나, 곧 루트가 정확히 하나인 그래프다. 목록 누락과 null 항목도 거부한다.
        // 각 그래프는 시체 그래프처럼 따로 검증된 기록이라 현재 소유 그래프나 다른 항목과 인스턴스 ID를 대조하지 않는다.
        static bool IsStorageBuffer(IReadOnlyList<ItemOwnershipState>? storageBuffer)
        {
            if (storageBuffer == null) return false;
            for (int index = 0; index < storageBuffer.Count; index++)
            {
                ItemOwnershipState? graph = storageBuffer[index];
                if (graph == null || graph.Roots.Count != 1) return false;
            }
            return true;
        }

        // 현재 소유 그래프에 플레이어 루트(MainCharacterItemData), 그 루트 아이템, 소지 인벤토리가 모두 있는지 본다.
        // 시체나 사망 기록 그래프에는 쓰지 않는다. GameplayInventory는 생성될 때 이 규칙이 지켜졌다고 가정한다.
        static bool TryCheckPlayerInventory(ItemOwnershipState items)
        {
            string? character = items.CharacterId;
            return character != null && items.Find(character)?.Inventory != null;
        }

        // 퀵슬롯이 메인 인벤토리의 빈 칸을 가리키거나 같은 종류 아이템을 두 칸에 등록하면 true다. 그래프나 목록이 없으면 false다.
        // TryCreate의 검사만 부르며, 그 검사가 플레이어 루트와 소지 인벤토리를 먼저 확인하므로 그래프가 있으면 루트 ID도 있다.
        static bool ShortcutViolation(ItemOwnershipState? items, IReadOnlyList<int>? inventoryIndexes)
        {
            if (items == null || inventoryIndexes == null) return false;
            string character = items.CharacterId!;
            // 퀵슬롯은 여섯 칸뿐이라 종류 중복은 앞 칸과 직접 비교해 집합과 열거자를 만들지 않는다.
            // 앞 칸은 이미 실제 아이템을 가리킨다고 확인했다.
            for (int index = 0; index < inventoryIndexes.Count; index++)
            {
                int position = inventoryIndexes[index];
                if (position < 0) continue;
                ItemInstanceState? item = Carried(items, character, position);
                if (item == null) return true;
                for (int earlier = 0; earlier < index; earlier++)
                    if (inventoryIndexes[earlier] >= 0 &&
                        Carried(items, character, inventoryIndexes[earlier])!.OriginalItemId == item.OriginalItemId) return true;
            }
            return false;
        }

        // 메인 인벤토리의 그 칸에 있는 아이템이다. 칸이 비었으면 null이다.
        static ItemInstanceState? Carried(ItemOwnershipState items, string character, int position)
        {
            string? id = items.EntryAt(character, position);
            return id == null ? null : items.Find(id);
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        // 보관함 목록도 필수 인자다. 저장에 필드가 없을 때의 빈 목록은 코덱이 정한다.
        public static bool TryCreate(ItemOwnershipState? items, ItemShortcutState? shortcuts, IReadOnlyList<ItemOwnershipState> storageBuffer,
            [NotNullWhen(true)] out InventoryPersistenceState? value)
        {
            value = Check(items, shortcuts, storageBuffer) ? new InventoryPersistenceState(items, shortcuts, storageBuffer) : null;
            return value != null;
        }

        // 소유 그래프만 바꾼 묶음을 만든다. 퀵슬롯 교차 규칙을 이 묶음이 지키므로, 그래프를 바꾸는 모든 경로가 여기서 퀵슬롯을 맞춘다.
        // 그래프는 작업 사본이 소유 규칙을 지켜 만든 값이고, 맞춘 퀵슬롯은 규칙을 항상 지킨다.
        // 그래프 변경과 사망이 보관함을 지우지 않도록 현재 보관함을 그대로 넘긴다(원작 보관함은 사망과 무관하다).
        internal InventoryPersistenceState WithItems(ItemOwnershipState items) =>
            new InventoryPersistenceState(items, ReconcileShortcuts(items), StorageBuffer);

        // 각 퀵슬롯이 가리키던 인스턴스를 새 그래프의 메인 인벤토리 위치로 따라가고, 거기 없으면 비운다. 결과가 같으면 입력
        // 객체를 그대로 돌려줘 참조 비교로 변경 없음을 알 수 있다. 입력 퀵슬롯은 검증된 값이고 서로 다른 칸은 서로 다른 인스턴스를 가리키므로
        // 새 위치도 겹치지 않아 결과는 교차 규칙을 항상 지킨다. 미포착(null) 퀵슬롯은 그대로 null이다.
        ItemShortcutState? ReconcileShortcuts(ItemOwnershipState after)
        {
            ItemShortcutState? shortcuts = Shortcuts;
            if (shortcuts == null) return null;
            ItemOwnershipState? before = Items;
            // 수량만 바꾼 그래프는 칸 색인을 공유해 모든 위치가 같으므로 퀵슬롯을 그대로 둔다. 발사마다 위치 배열을 만들지 않는다.
            if (before != null && after.SharesPositions(before)) return shortcuts;
            int[] result = ItemShortcutState.EmptyIndexes();
            string? oldCharacter = before?.CharacterId;
            string? newCharacter = after.CharacterId;
            bool unchanged = true;
            for (int index = 0; index < ItemShortcutState._slotCount; index++)
            {
                int position = shortcuts.InventoryIndexes[index];
                string? id = position < 0 || oldCharacter == null ? null : before!.EntryAt(oldCharacter, position);
                if (id != null && newCharacter != null) result[index] = after.PositionIn(newCharacter, id) ?? ItemShortcutState._emptyIndex;
                unchanged &= result[index] == position;
            }
            return unchanged ? shortcuts : new ItemShortcutState(result);
        }

        public ItemOwnershipState? Items { get; }
        public ItemShortcutState? Shortcuts { get; }
        // 원작 PlayerStorageBuffer(키 PlayerStorage_Buffer)의 아이템 트리 목록이다. 목록 순서가 수령 번호이고, 어떤 출처 마스크에도 들지 않는다.
        public IReadOnlyList<ItemOwnershipState> StorageBuffer { get; }
    }
}
