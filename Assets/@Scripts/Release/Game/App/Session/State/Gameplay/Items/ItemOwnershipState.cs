#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 아이템 소유 그래프다. 현재 소유와 시체 기록은 따로 검증하므로 보호 아이템이 양쪽에 함께 기록될 수 있다.
    public sealed class ItemOwnershipState
    {
        // 코드가 만든 그래프와 TryCreate가 함께 쓰는 생성자다. 소유 규칙은 검사하지 않는다. 작업 사본은 규칙을 지켜 그래프를 만들고,
        // 테스트가 그 결과를 TryCreate로 확인한다. 발사와 사망마다 목록을 또 복사하지 않도록 받은 목록을 그대로 가지므로, 호출자는 넘긴 목록을
        // 다시 바꾸지 않는다. ID 위치 색인과 칸 색인은 여기서 만들며, 목록은 열거자를 만들지 않도록 인덱스 루프로 돈다.
        internal ItemOwnershipState(IReadOnlyList<ItemInstanceState> items, IReadOnlyList<ItemSlotState> roots,
            IReadOnlyList<StoredInventoryState> inventories)
        {
            Items = items; Roots = roots; Inventories = inventories;
            _positions = new Dictionary<string, int>(items.Count, StringComparer.Ordinal);
            _entries = new Dictionary<(string Owner, int Position), string>();
            _locations = new Dictionary<string, (string Owner, int Position)>(StringComparer.Ordinal);
            for (int index = 0; index < items.Count; index++)
            {
                ItemInstanceState item = items[index];
                // 노드 대신 목록 위치를 담아 수량만 바꾼 그래프가 이 사전을 그대로 공유하게 한다. 중복 ID가 와도 던지지 않도록
                // TryAdd를 쓴다.
                _positions.TryAdd(item.InstanceId, index);
                if (item.Inventory != null) IndexEntries(item.InstanceId, item.Inventory);
            }
            // 저장 인벤토리 키와 아이템 ID는 겹치지 않는다고 보고 모든 저장 인벤토리 칸을 그대로 색인한다. 저장 읽기는 Check가
            // 겹치는 값을 거절하고, 코드는 아이템 ID를 GUID로 만든다.
            for (int index = 0; index < inventories.Count; index++) IndexEntries(inventories[index].Key, inventories[index].Inventory);
            for (int index = 0; index < roots.Count; index++)
                if (roots[index].Key == GameplayInventory._characterRoot) CharacterId = roots[index].InstanceId;
        }

        // WithQuantities 전용 생성자다. 칸, 슬롯, 루트가 그대로라 shape의 검증 결과와 색인을 공유하고 노드 목록만 새로 받는다.
        ItemOwnershipState(ItemOwnershipState shape, IReadOnlyList<ItemInstanceState> items)
        {
            Items = items; Roots = shape.Roots; Inventories = shape.Inventories; CharacterId = shape.CharacterId;
            _positions = shape._positions; _entries = shape._entries; _locations = shape._locations;
        }

        // TryCreate가 쓰는 검사다. 루트와 저장 인벤토리에서 출발해 모든 아이템에 정확히 한 번씩 닿아야 한다.
        // 없는 참조, 중복 소유, 순환, 고립된 아이템을 거절한다. 하위 값의 ID, 키, 칸은 저장 읽기의 각 TryCreate와 내부 생성 코드가
        // 늘 채우므로 null을 보지 않는다. 실패하면 기록 없이 false만, 성공하면 검증한 복사본을 돌려준다.
        static bool Check(IReadOnlyList<ItemInstanceState>? items, IReadOnlyList<ItemSlotState>? roots,
            IReadOnlyList<StoredInventoryState>? inventories, [NotNullWhen(true)] out IReadOnlyList<ItemInstanceState>? itemsCopy,
            [NotNullWhen(true)] out IReadOnlyList<ItemSlotState>? rootsCopy,
            [NotNullWhen(true)] out IReadOnlyList<StoredInventoryState>? inventoriesCopy)
        {
            rootsCopy = null;
            inventoriesCopy = null;
            if (!ReadOnlyLists.TryCopyUnique(items, state => state.InstanceId, out itemsCopy)) return false;
            if (!ReadOnlyLists.TryCopyUnique(roots, state => state.Key, out rootsCopy)) return false;
            if (!ReadOnlyLists.TryCopyUnique(inventories, state => state.Key, out inventoriesCopy))
                return false;
            // 도달 검사에만 쓰는 ID 위치 사전이다. 열거자를 힙에 만들지 않도록 인덱스 루프를 쓴다.
            var byId = new Dictionary<string, int>(itemsCopy.Count, StringComparer.Ordinal);
            for (int position = 0; position < itemsCopy.Count; position++)
            {
                ItemInstanceState item = itemsCopy[position];
                byId.TryAdd(item.InstanceId, position);
            }
            // 저장 인벤토리 키가 아이템 ID와 같으면 컨테이너 조회에서 그 아이템의 인벤토리가 저장 인벤토리를 가리므로 거절한다.
            for (int position = 0; position < inventoriesCopy.Count; position++)
                if (byId.ContainsKey(inventoriesCopy[position].Key))
                    return false;
            var pending = new Queue<string>();
            for (int position = 0; position < rootsCopy.Count; position++) pending.Enqueue(rootsCopy[position].InstanceId);
            for (int position = 0; position < inventoriesCopy.Count; position++) EnqueueEntries(pending, inventoriesCopy[position].Inventory);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                // 없는 참조는 바로 거절하고, 이미 방문한 아이템에 다시 닿으면 중복 소유나 순환으로 보고 거절한다.
                if (!byId.TryGetValue(id, out int found))
                    return false;
                if (!visited.Add(id))
                    return false;
                ItemInstanceState node = itemsCopy[found];
                IReadOnlyList<ItemSlotState> slots = node.Slots;
                for (int position = 0; position < slots.Count; position++) pending.Enqueue(slots[position].InstanceId);
                EnqueueEntries(pending, node.Inventory);
            }
            // 방문하지 못한 아이템이 있으면 어느 루트에도 연결되지 않은 고립 아이템이다.
            return visited.Count == itemsCopy.Count;
        }

        // 컨테이너 칸의 인스턴스 ID를 도달 검사 대기열에 넣는다.
        static void EnqueueEntries(Queue<string> pending, ItemContainerState? container)
        {
            if (container == null) return;
            // 열거자를 만들지 않도록 인덱스 루프를 쓴다.
            IReadOnlyList<ItemPositionState> entries = container.Entries;
            for (int position = 0; position < entries.Count; position++) pending.Enqueue(entries[position].InstanceId);
        }

        // 저장 파일에서 읽은 그래프의 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        // 검증한 복사본을 내부 생성자에 넘기며, 코드가 만든 그래프는 내부 생성자를 바로 쓴다.
        public static bool TryCreate(IReadOnlyList<ItemInstanceState> items, IReadOnlyList<ItemSlotState> roots,
            IReadOnlyList<StoredInventoryState> inventories, [NotNullWhen(true)] out ItemOwnershipState? value)
        {
            value = Check(items, roots, inventories, out var itemsCopy, out var rootsCopy, out var inventoriesCopy)
                ? new ItemOwnershipState(itemsCopy, rootsCopy, inventoriesCopy) : null;
            return value != null;
        }

        // 발사, 일부 소비, 스택 합치기처럼 수량만 바꾼 노드로 새 그래프를 만든다. 칸, 슬롯, 루트가 그대로라 색인을
        // 다시 만들지 않고 공유한다. 결과는 같은 노드 목록으로 내부 생성자가 만든 그래프와 같고, 이 그래프는 바뀌지 않는다.
        // items는 작업 사본이 Items를 같은 순서로 복사해 수량만 바꾼 배열이다. 발사마다 배열을 한 번 더 복사하지 않도록 새 그래프가
        // 그대로 가지므로, 호출자는 넘긴 배열을 다시 바꾸지 않는다.
        internal ItemOwnershipState WithQuantities(ItemInstanceState[] items)
        {
            return new ItemOwnershipState(this, Array.AsReadOnly(items));
        }

        // WithQuantities로 이어진 그래프끼리만 칸 색인을 공유하므로, 참조가 같으면 모든 아이템의 칸 위치가 같다.
        internal bool SharesPositions(ItemOwnershipState other) => ReferenceEquals(_entries, other._entries);

        // ID로 Items 목록 위치를 찾는 색인이다. 수량만 바꾼 그래프는 노드 순서가 같아 이 색인을 공유한다.
        readonly Dictionary<string, int> _positions;
        // 소유자와 칸 위치로 인스턴스 ID를 찾는 색인이다. 수량만 바꾼 그래프와 공유하며, 만든 뒤에는 바꾸지 않는다.
        readonly Dictionary<(string Owner, int Position), string> _entries;
        // 인스턴스 ID로 소유자와 칸 위치를 찾는 역색인이다. 슬롯에 장착된 아이템은 들어 있지 않다. _entries처럼 공유한다.
        readonly Dictionary<string, (string Owner, int Position)> _locations;

        public IReadOnlyList<ItemInstanceState> Items { get; }
        public IReadOnlyList<ItemSlotState> Roots { get; }
        public IReadOnlyList<StoredInventoryState> Inventories { get; }
        // 플레이어 루트(MainCharacterItemData)의 인스턴스 ID다. 루트가 없으면 null이다.
        // 매 프레임 인벤토리 질의가 루트 목록을 다시 훑지 않도록 생성할 때 Roots에서 한 번 찾아 둔다.
        internal string? CharacterId { get; }

        // 노드는 Items 목록에만 두고 위치 색인으로 찾는다. 수량만 바꾼 그래프도 자기 목록에서 바뀐 노드를 읽는다.
        internal ItemInstanceState? Find(string id) => _positions.TryGetValue(id, out int position) ? Items[position] : null;

        // ID의 Items 목록 위치를 찾는다. 작업 사본이 수량만 바꾼 노드를 같은 위치에 두고 읽을 때 쓴다.
        internal bool TryIndexOf(string id, out int index) => _positions.TryGetValue(id, out index);

        // 아이템이 소유자 컨테이너의 몇 번 칸에 있는지 돌려준다. 다른 곳에 있거나 슬롯에 장착되어 있으면 null이다.
        internal int? PositionIn(string ownerId, string id) =>
            _locations.TryGetValue(id, out (string Owner, int Position) location) && location.Owner == ownerId
                ? location.Position : (int?)null;

        // 소유자 컨테이너의 해당 칸에 있는 인스턴스 ID를 반환한다(빈 칸이면 null).
        internal string? EntryAt(string ownerId, int position) =>
            _entries.TryGetValue((ownerId, position), out string id) ? id : null;

        // 한 컨테이너의 칸을 양방향 색인에 넣는다. 검증된 값에서는 칸이 겹치지 않으며, 겹치더라도 인덱서 대입이라 던지지 않는다.
        void IndexEntries(string owner, ItemContainerState container)
        {
            // 생성자가 컨테이너마다 부르므로 열거자를 만들지 않도록 인덱스 루프를 쓴다.
            IReadOnlyList<ItemPositionState> entries = container.Entries;
            for (int index = 0; index < entries.Count; index++)
            {
                ItemPositionState entry = entries[index];
                _entries[(owner, entry.Position)] = entry.InstanceId;
                _locations[entry.InstanceId] = (owner, entry.Position);
            }
        }
    }
}
