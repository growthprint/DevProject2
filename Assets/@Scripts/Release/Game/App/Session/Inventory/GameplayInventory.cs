#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Game.App
{
    // 원본 아이템 소유 그래프의 이동, 소비, 장전을 Unity 객체 없이 작업 사본에서 계산하고 결과 그래프를 세션에 돌려준다.
    // 작업 사본은 소유 규칙을 지켜 그래프를 만들므로 다시 검증하지 않고, 테스트가 결과 그래프를 TryCreate로 확인한다. 규칙 위반은 기록 없이 거절 코드로 돌려준다. 구조 결함은 검출 지점에서 InventoryResult.Defect로 보고하고 Invalid를 돌려준다.
    // 수리·분해 작업대 명령이 자기 partial 파일에 계산을 두도록 partial로 연다.
    internal sealed partial class GameplayInventory
    {
        internal const string _characterRoot = "MainCharacterItemData";
        internal const string _storage = "PlayerStorage";
        // 원작 PlayerStorage.DefaultCapacity 자리인 새 게임 창고 칸 수다. 창고 확장 특성(AddPlayerStorage) 합을 이 값에 더한다.
        // 값을 바꾸면 새 게임 저장 JSON과 기존 창고 칸 수가 달라진다.
        internal const int _storageCapacity = 100;
        // 원작 PetProxy 가방의 저장 키다. 창고처럼 저장 인벤토리로 두며, 아이템 ID(GUID)와 겹치지 않는다.
        internal const string _pet = "Inventory_Safe";
        // 떨군 아이템 그래프의 루트 키 앞부분이다. 유실물 루트 키로 저장되므로 값을 바꾸지 않는다.
        const string _dropRootPrefix = "drop:";
        // 루트 하나짜리 바닥 그래프의 루트 키다. 넘친 지급, 판매로 뺀 트리, 월드에 새로 떨군 아이템이 같은 키를 쓴다.
        internal const string _dropRoot = _dropRootPrefix + "0";
        // 명령을 시작한 확정 그래프다. 모양을 바꾸기 전에는 사본 없이 이 그래프를 읽는다.
        // Build가 수량만 바꾼 그래프를 내주면 그 그래프로 바꿔, 넘긴 노드 배열을 작업 사본이 다시 쓰지 않게 한다.
        ItemOwnershipState _source;
        // 모양을 바꾸기 전에 수량을 바꾸면 원본 노드 목록을 같은 순서로 한 번 복사해 그 자리만 바꾼다. 바뀐 노드를 따로 모으면
        // 발사마다 목록 객체와 내부 배열이 생기고 Build가 노드 배열을 또 복사한다. Build가 이 배열을 새 그래프에 그대로 넘긴다.
        ItemInstanceState[]? _nodes;
        // 칸, 슬롯, 아이템 수를 바꾸는 첫 쓰기 때 원본 순서대로 만드는 전체 사본이다. 만든 뒤에는 이 사전만 읽고 쓰며,
        // Build가 이 사본으로 새 그래프와 색인을 만든다.
        Dictionary<string, ItemInstanceState>? _items;
        // 저장 인벤토리 칸을 바꿀 때만 만드는 사본이다.
        List<StoredInventoryState>? _inventories;
        // 플레이어 루트(MainCharacterItemData)의 인스턴스 ID다. 루트 아이템과 소지 인벤토리는 InventoryPersistenceState 검증이
        // 보장하므로 명령은 다시 검사하지 않는다.
        readonly string _character;

        // 명령마다 만들어지므로 그래프를 복사하지 않고 원본을 읽는다. 저장된 아이템과 부착물은 알려지지 않은 것까지 모두 남는다.
        // 세션 그래프는 로드 검증과 월드 첫 초기화가 플레이어 루트를 보장한다.
        internal GameplayInventory(ItemOwnershipState source)
        {
            _source = source;
            _character = source.CharacterId!;
        }

        // 로드 검증이 보장하고 명령이 지우지 않으므로 항상 있다. Player는 매번 다시 찾으므로 루트 아이템을 교체한 뒤에도
        // 최신 값이다.
        internal string CharacterId => _character;
        internal ItemInstanceState Player
        {
            get
            {
                TryItem(_character, out ItemInstanceState? player);
                return player!;
            }
        }
        ItemContainerState Carried => Player.Inventory!;

        // 노드를 찾는다. 명령 인수는 못 찾으면 호출자가 NotOwned로 거절하고, 그래프 안의 칸과 슬롯 참조도 같은 조회로 찾는다.
        // 전체 사본이 있으면 그것만 읽고, 없으면 원본 색인으로 찾은 위치를 수량을 바꾼 노드 배열이나 원본 그래프에서 읽는다. id는 null이 아니다.
        internal bool TryItem(string id, [NotNullWhen(true)] out ItemInstanceState? item)
        {
            if (_items != null) return _items.TryGetValue(id, out item);
            if (!_source.TryIndexOf(id, out int index))
            {
                item = null;
                return false;
            }
            item = _nodes != null ? _nodes[index] : _source.Items[index];
            return true;
        }

        // 노드의 수량만 바꾼다. 칸과 슬롯이 그대로라 전체 사본을 만들지 않는다. 다른 필드는 그대로라 목록을 공유하는 WithQuantity를 쓴다.
        void SetQuantity(ItemInstanceState item, int quantity)
        {
            ItemInstanceState changed = item.WithQuantity(quantity);
            if (_items != null)
            {
                _items[changed.InstanceId] = changed;
                return;
            }
            // item은 TryItem이 원본 색인에서 찾은 노드라 위치가 항상 있다.
            _source.TryIndexOf(changed.InstanceId, out int index);
            if (_nodes == null)
            {
                IReadOnlyList<ItemInstanceState> items = _source.Items;
                _nodes = new ItemInstanceState[items.Count];
                for (int position = 0; position < _nodes.Length; position++) _nodes[position] = items[position];
            }
            _nodes[index] = changed;
        }

        // 모양을 바꾸는 쓰기 전에 원본 순서대로 전체 사본을 만든다. 수량만 바꾼 노드는 제자리에 있으므로 노드 순서는 처음부터
        // 사본을 만든 것과 같다. 크기를 정한 사전에 인덱스 루프로 넣으며, 중복 ID면 Add가 던지지만 검증된 그래프라 생기지 않는다.
        Dictionary<string, ItemInstanceState> Reshaped()
        {
            if (_items != null) return _items;
            IReadOnlyList<ItemInstanceState> items = _nodes ?? _source.Items;
            var copy = new Dictionary<string, ItemInstanceState>(items.Count, StringComparer.Ordinal);
            for (int index = 0; index < items.Count; index++) copy.Add(items[index].InstanceId, items[index]);
            _nodes = null;
            return _items = copy;
        }

        // 저장 인벤토리 목록이다. 칸을 바꾸기 전에는 원본 목록을 그대로 읽는다.
        IReadOnlyList<StoredInventoryState> Inventories => _inventories ?? _source.Inventories;

        // 루트, 칸, 슬롯이 가리키는 아이템이다. 검증된 그래프에서 시작하고 명령이 노드와 칸을 함께 넣고 빼므로 늘 있다.
        ItemInstanceState Child(string id)
        {
            TryItem(id, out ItemInstanceState? item);
            return item!;
        }

        // 작업 사본의 현재 노드를 찾는 조회 함수다. 확정 그래프와 같은 컨테이너 규칙과 탄 합산을 OriginalInventoryQueries에서
        // 함께 쓰려고 넘긴다. 캡처 없는 정적 대리자라 발사마다 할당하지 않는다.
        static readonly Func<GameplayInventory, string, ItemInstanceState?> _find =
            (inventory, id) => inventory.TryItem(id, out ItemInstanceState? item) ? item : null;

        // 아이템 인벤토리나 저장 인벤토리를 찾는다. 규칙은 확정 그래프와 함께 쓰는 OriginalInventoryQueries.TryContainer에 있다.
        internal bool TryContainer(string id, [NotNullWhen(true)] out ItemContainerState? container) =>
            TryContainer(id, out container, out _, out _);

        // 컨테이너와 소유자를 함께 찾아 ReplaceContainer가 다시 찾지 않게 한다. 칸을 바꾸기 전에는 원본 저장 인벤토리 목록을 읽는다.
        bool TryContainer(string id, [NotNullWhen(true)] out ItemContainerState? container, out ItemInstanceState? owner,
            out int stored) =>
            OriginalInventoryQueries.TryContainer(this, _find, Inventories, id, out container, out owner, out stored);

        // 명령 인수가 소유하지 않은 아이템일 때의 규칙 거절이다. 기록하지 않는다.
        internal static InventoryResult Unowned() => InventoryResult.Reject(InventoryRejection.NotOwned);

        // 컨테이너가 없는 구조 결함을 보고하고 Invalid를 돌려준다. 릴리스 기록이 이 도우미가 아닌 실제 호출 위치를
        // 가리키도록 호출자 파일과 줄을 Defect에 넘긴다. id는 호출자가 넘긴 상수나 그래프 ID라 null이 아니다.
        InventoryResult MissingContainer(string id, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
            InventoryResult.Defect(new InvalidOperationException(TryItem(id, out _)
                ? "인벤토리 불변식 위반: 인벤토리가 없는 아이템입니다." : "인벤토리 불변식 위반: 저장 인벤토리를 찾을 수 없습니다."),
                callerFile, callerLine);

        // 첫 원본 캐릭터를 빈 소지 인벤토리와 빈 창고(PlayerStorage)로 만든다. 호출자는 그래프가 없을 때만 부른다.
        // 만드는 그래프가 늘 소유 규칙을 지켜 실패하지 않으므로 그래프를 바로 돌려준다.
        internal static ItemOwnershipState Initialize(int characterType, int capacity)
        {
            // 저장 아이템 ID 형식은 영속 ID 생성 한곳에서 정한다.
            string id = PersistentIds.New();
            // 용량은 저작 카탈로그의 플레이어 정의 값이고 ID는 새 GUID라 믿을 수 있는 입력용 생성자로 만든다.
            var main = new ItemContainerState(capacity, Array.Empty<ItemPositionState>(), Array.Empty<int>());
            ItemInstanceState character = Create(id, characterType, 1, main);
            // 새 게임 창고 칸 수는 하나뿐이라 인수로 받지 않는다. 특성 창고 확장도 같은 기본값을 읽도록 상수로 옮겼다.
            var storage = new ItemContainerState(_storageCapacity, Array.Empty<ItemPositionState>(), Array.Empty<int>());
            var storageRoot = new StoredInventoryState(_storage, storage);
            // 새 GUID 루트 하나와 빈 창고뿐인 그래프라 소유 규칙을 늘 지키므로 검증 없는 생성자로 만든다.
            return new ItemOwnershipState(new ItemInstanceState[] { character }, new[] { new ItemSlotState(_characterRoot, id) },
                new[] { storageRoot });
        }

        // 펫 가방이 없는 그래프에 빈 펫 가방을 더한다. 새 게임과 펫 가방이 없던 저장이 같은 길을 쓴다. 이미 있으면 source를 그대로
        // 돌려주므로 호출자는 참조 비교로 바뀌었는지 안다. 노드와 루트는 그대로이고 새 키는 아이템 ID와 겹치지 않아 검증 없는 생성자를 쓴다.
        internal static ItemOwnershipState WithPetBag(ItemOwnershipState source, int capacity)
        {
            IReadOnlyList<StoredInventoryState> inventories = source.Inventories;
            for (int index = 0; index < inventories.Count; index++)
                if (inventories[index].Key == _pet) return source;
            var added = new StoredInventoryState[inventories.Count + 1];
            for (int index = 0; index < inventories.Count; index++) added[index] = inventories[index];
            var bag = new ItemContainerState(capacity, Array.Empty<ItemPositionState>(), Array.Empty<int>());
            added[inventories.Count] = new StoredInventoryState(_pet, bag);
            return new ItemOwnershipState(source.Items, source.Roots, added);
        }

        // 코드가 정한 ID, 종류, 수량과 카탈로그 빌드 데이터로 새 아이템을 만든다. 카탈로그 정의는 런타임에 검증하지 않고
        // EditMode 카탈로그 테스트가 확인하므로 검증 없는 생성자를 쓴다.
        internal static ItemInstanceState Create(string id, int type, int count, ItemContainerState? inventory = null,
            float? durability = null) => new ItemInstanceState(id, type, count, durability, null, true, null,
            Array.Empty<ItemSlotState>(), inventory, Array.Empty<ItemVariableState>());

        // 확인된 용량 안의 첫 빈 칸을 AddAndMerge와 같은 FirstFree로 찾는다. 찾은 컨테이너는 contents로 돌려줘 다시 찾지 않게 한다.
        // 빈 칸이 없으면 화면에 알릴 InventoryFull 거절이고, 컨테이너나 용량이 없으면 Defect로 보고한 구조 결함이다.
        InventoryResult FreePosition(string container, out ItemContainerState? contents, out int position)
        {
            position = -1;
            InventoryResult known = KnownCapacity(container, out contents, out int capacity);
            if (!known.IsOk) return known;
            position = FirstFree(Ordered(contents!), capacity);
            return position >= 0 ? InventoryResult.Ok : InventoryResult.Reject(InventoryRejection.InventoryFull);
        }

        // FreePosition과 Recover가 함께 쓰는 컨테이너와 용량 확인이다. 둘 중 하나라도 없으면 Defect로 보고한 구조 결함이다.
        // 성공하면 contents는 null이 아니다.
        InventoryResult KnownCapacity(string container, out ItemContainerState? contents, out int capacity)
        {
            capacity = 0;
            if (!TryContainer(container, out contents)) return MissingContainer(container);
            if (!contents.Capacity.HasValue)
                return InventoryResult.Defect(new InvalidOperationException("인벤토리 불변식 위반: 인벤토리 용량이 아직 확인되지 않았습니다."));
            capacity = contents.Capacity.Value;
            return InventoryResult.Ok;
        }

        // 컨테이너의 칸 목록을 교체하며 실패하지 않는다. 칸이 바뀌면 그래프 모양이 바뀌므로 전체 사본으로 넘어가 Build가 색인을 다시 만들게 한다.
        // 용량을 바꿀 때만 capacity와 그 용량 안의 정렬 잠금 칸 locked를 함께 넘기고, 생략하면 원래 값을 둔다.
        void ReplaceContainer(string id, IReadOnlyList<ItemPositionState> entries, int? capacity = null, IReadOnlyList<int>? locked = null)
        {
            // 호출자는 이 명령에서 먼저 찾은 컨테이너와, 용량과 위치 중복 규칙을 지키도록 계산한 칸 목록만 넘긴다.
            // 그래서 믿을 수 있는 입력용 생성자로 새 컨테이너를 만든다.
            TryContainer(id, out ItemContainerState? found, out ItemInstanceState? owner, out int stored);
            ItemContainerState old = found!;
            var replacement = new ItemContainerState(capacity ?? old.Capacity, entries, locked ?? old.LockedIndexes);
            Dictionary<string, ItemInstanceState> items = Reshaped();
            // 인벤토리만 바꾼 노드는 부착물과 변수 목록을 원래 노드와 공유한다.
            if (owner != null) items[id] = owner.With(inventory: replacement);
            // 아이템이 아니면 TryContainer가 저장 인벤토리에서 찾았으므로 색인은 항상 0 이상이다.
            else (_inventories ??= new List<StoredInventoryState>(_source.Inventories))[stored] = new StoredInventoryState(id, replacement);
        }

        // 지급 ID는 호출자가 새 GUID나 방금 소비해 지운 ID로 정하므로 이미 소유한 ID가 아니다. 실패하면 작업 사본은 통째로 버려진다.
        internal InventoryResult Add(string container, ItemInstanceState item)
        {
            InventoryResult free = FreePosition(container, out ItemContainerState? contents, out int position);
            if (!free.IsOk) return free;
            var entries = new List<ItemPositionState>(contents!.Entries) { new ItemPositionState(position, item.InstanceId) };
            // 아이템 수가 바뀌므로 전체 사본에 넣고 칸을 붙인다. 칸 교체는 실패하지 않는다.
            Reshaped().Add(item.InstanceId, item);
            ReplaceContainer(container, entries);
            return InventoryResult.Ok;
        }

        // 원본의 마지막 사망 기록처럼 손실 필터를 적용하기 전의 캐릭터 전체를 포착하고 창고는 제외한다.
        // 검증된 작업 사본에서 모으므로 실패하지 않아 그래프를 바로 돌려준다.
        internal ItemOwnershipState CaptureCharacter()
        {
            string character = _character;
            var items = new List<ItemInstanceState>();
            // 사망 기록도 유실물 회수, 용량 축소와 같은 슬롯 다음 칸 순서로 캐릭터부터 모은다.
            CollectTree(this, _child, character, items);
            // 작업 사본에서 캐릭터 하위 트리만 모은 노드라 소유 규칙을 늘 지키므로 검증 없는 생성자로 만든다.
            // 모은 목록은 새 그래프가 그대로 가지며 이 뒤에 바꾸지 않는다.
            return new ItemOwnershipState(items, new[] { new ItemSlotState(_characterRoot, character) },
                Array.Empty<StoredInventoryState>());
        }

        // 사망 시 보호되지 않은 소지품의 하위 트리만 시체로 옮기고 창고와 다른 루트는 남긴다. retain은 슬롯 아이템(둘째 인수 true)을
        // 먼저, 소지 칸 아이템(false)을 다음으로 한 번씩 부른다. 떼어 낸 하위 트리는 실패 없이 유실물 그래프로 돌려준다.
        internal ItemOwnershipState DropCarried(Func<ItemInstanceState, bool, bool> retain)
        {
            string character = _character;
            // 버릴 ID는 슬롯 다음 소지 칸 순서로 모은다. retain은 작업 사본을 바꾸지 않으므로 두 루프가 Player를 바로 읽어도 된다.
            var slots = new List<ItemSlotState>();
            var drop = new List<string>();
            // 검증된 그래프에서 시작하고 명령이 끊긴 참조를 만들지 않으므로, 두 루프에서 슬롯과 칸이 가리키는 아이템은 늘 있다.
            foreach (ItemSlotState slot in Player.Slots)
            {
                if (retain(Child(slot.InstanceId), true)) slots.Add(slot);
                else drop.Add(slot.InstanceId);
            }
            var entries = new List<ItemPositionState>();
            foreach (ItemPositionState entry in Carried.Entries)
            {
                if (retain(Child(entry.InstanceId), false)) entries.Add(entry);
                else drop.Add(entry.InstanceId);
            }
            var removed = new List<ItemInstanceState>();
            // 하위 트리 제거는 용량 축소와 함께 쓰는 TakeTree가 맡고, 남길 칸으로 소지품을 바꾼다. 두 도우미는 실패하지 않는다.
            foreach (string id in drop) TakeTree(id, removed);
            ReplaceContainer(character, entries);
            // 칸 교체가 루트 아이템을 새 값으로 바꾸므로 최신 Player에 슬롯을 적용한다. 칸 교체가 이미 전체 사본을 만들었다.
            Reshaped()[character] = Player.With(slots: slots);
            // 루트 키는 유실물로 저장되므로 용량 축소와 같은 앞부분 상수로 만든다.
            var roots = new ItemSlotState[drop.Count];
            for (int index = 0; index < drop.Count; index++) roots[index] = new ItemSlotState(_dropRootPrefix + index, drop[index]);
            // 작업 사본에서 통째로 뺀 하위 트리와 겹치지 않는 루트 키라 소유 규칙을 늘 지키므로 검증 없는 생성자로 만든다.
            // 모은 목록은 새 그래프가 그대로 가지며 이 뒤에 바꾸지 않는다.
            return new ItemOwnershipState(removed, roots, Array.Empty<StoredInventoryState>());
        }

        // 원작 기지 레벨 시작의 정리(CharacterMainControl.DestroyItemsThatNeededToBeDestriedInBase, PetProxy.DestroyItemInBase)다.
        // 소지품 맨 위 칸, 캐릭터 칸, 펫 가방 맨 위 칸 순서로 destroy가 참인 아이템을 하위 트리째 지운다. 지울 것이 없으면 작업 사본을
        // 바꾸지 않아 Build가 원본 그래프를 돌려준다. 실패하지 않는다.
        internal InventoryResult DestroyInBase(Func<ItemInstanceState, bool> destroy)
        {
            DestroyTopLevel(_character, Carried, destroy);
            var slots = new List<ItemSlotState>();
            bool unplugged = false;
            // 반복 시작 때 읽은 루트의 칸 목록은 바뀌지 않고, 칸마다 다른 아이템을 가리켜 앞 칸의 제거가 뒤 칸 아이템을 지우지 않는다.
            foreach (ItemSlotState slot in Player.Slots)
            {
                if (!destroy(Child(slot.InstanceId)))
                {
                    slots.Add(slot);
                    continue;
                }
                TakeTree(slot.InstanceId, new List<ItemInstanceState>());
                unplugged = true;
            }
            // 하위 트리 제거가 전체 사본을 이미 만들었으므로 최신 Player에 남길 칸을 적용한다.
            if (unplugged) Reshaped()[_character] = Player.With(slots: slots);
            // 호출자는 펫 가방을 더하는 EnsureGraph 다음에만 부르므로 펫 가방이 늘 있다.
            TryContainer(_pet, out ItemContainerState? pet);
            DestroyTopLevel(_pet, pet!, destroy);
            return InventoryResult.Ok;
        }

        // 가방 하나의 맨 위 칸에서 destroy가 참인 아이템을 하위 트리째 지운다. 지울 것이 없으면 아무것도 바꾸지 않는다.
        void DestroyTopLevel(string container, ItemContainerState contents, Func<ItemInstanceState, bool> destroy)
        {
            var kept = new List<ItemPositionState>(contents.Entries.Count);
            var drop = new List<string>();
            foreach (ItemPositionState entry in contents.Entries)
            {
                if (destroy(Child(entry.InstanceId))) drop.Add(entry.InstanceId);
                else kept.Add(entry);
            }
            if (drop.Count == 0) return;
            var removed = new List<ItemInstanceState>();
            // 모든 하위 트리를 먼저 뺀 뒤 남길 칸으로 교체한다. 두 도우미는 실패하지 않는다.
            foreach (string id in drop) TakeTree(id, removed);
            ReplaceContainer(container, kept);
        }

        // 원본 하위 트리 순서인 슬롯 다음 칸으로 루트부터 노드를 모은다. find는 검증된 그래프에서 가리키는 노드를 늘 찾는다.
        static void CollectTree<TSource>(TSource source, Func<TSource, string, ItemInstanceState> find, string id, List<ItemInstanceState> tree)
        {
            ItemInstanceState item = find(source, id);
            tree.Add(item);
            foreach (ItemSlotState slot in item.Slots) CollectTree(source, find, slot.InstanceId, tree);
            if (item.Inventory != null)
                foreach (ItemPositionState entry in item.Inventory.Entries) CollectTree(source, find, entry.InstanceId, tree);
        }

        // 작업 사본과 유실물 사전에서 노드를 찾는 캡처 없는 조회 함수다. 하위 트리를 모을 때마다 대리자를 만들지 않는다.
        // 유실물 그래프는 사망 때 검증된 그래프에서 떼어 냈거나 불러올 때 TryCreate가 검증해 가리키는 노드가 늘 있다.
        static readonly Func<GameplayInventory, string, ItemInstanceState> _child = (inventory, id) => inventory.Child(id);
        static readonly Func<Dictionary<string, ItemInstanceState>, string, ItemInstanceState> _lost = (contents, id) =>
        {
            contents.TryGetValue(id, out ItemInstanceState? item);
            return item!;
        };

        // 아이템과 하위 트리 전체를 작업 사본에서 빼고, 뺀 노드를 루트부터 removed에 담는다.
        // 모으는 동안 작업 사본을 읽으므로 다 모은 뒤 한꺼번에 뺀다.
        void TakeTree(string id, List<ItemInstanceState> removed)
        {
            int start = removed.Count;
            CollectTree(this, _child, id, removed);
            Dictionary<string, ItemInstanceState> items = Reshaped();
            for (int index = start; index < removed.Count; index++) items.Remove(removed[index].InstanceId);
        }

        // 원본 무덤 상자의 모두 줍기(LootView.OnPickAllButtonClicked)처럼 유실물 루트를 순서대로 원래 ID, 탄약, 부착물 그대로 되찾는다.
        // 루트마다 slotsFor가 준 슬롯 중 빈 첫 슬롯을, 없으면 소지품 빈 칸을 쓰고, 둘 다 없으면 remainder 그래프에 남긴다.
        // 하나도 못 넣으면 InventoryFull 거절이다. 이미 소유한 유실물은 Defect로 보고한 구조 결함이다.
        internal InventoryResult Recover(ItemOwnershipState dropped, Func<ItemInstanceState, IReadOnlyList<string>> slotsFor,
            out ItemOwnershipState? remainder)
        {
            remainder = null;
            string character = _character;
            var contents = new Dictionary<string, ItemInstanceState>(StringComparer.Ordinal);
            foreach (ItemInstanceState item in dropped.Items) contents[item.InstanceId] = item;
            // 루트가 없는 유실물은 되찾을 것이 없으므로 빈 그래프 그대로 남은 것으로 돌려준다.
            IReadOnlyList<ItemSlotState> roots = dropped.Roots;
            if (roots.Count == 0)
            {
                remainder = dropped;
                return InventoryResult.Ok;
            }
            InventoryResult known = KnownCapacity(character, out ItemContainerState? carried, out int capacity);
            if (!known.IsOk) return known;
            // 칸 집합과 슬롯 목록은 한 번만 만들고 교체도 끝에 한 번만 한다. 루트마다 앞 루트가 차지한 칸 다음부터 빈 칸을 찾으므로
            // 루트마다 첫 빈 칸을 찾는 것과 결과가 같다. 하위 트리 복원은 소지 칸을 바꾸지 않아 처음 확인한 carried의 칸을 그대로 쓴다.
            IReadOnlyList<ItemPositionState> existing = carried!.Entries;
            var used = new HashSet<int>();
            var entries = new List<ItemPositionState>(existing.Count + roots.Count);
            for (int index = 0; index < existing.Count; index++)
            {
                used.Add(existing[index].Position);
                entries.Add(existing[index]);
            }
            var slots = new List<ItemSlotState>(Player.Slots);
            var tree = new List<ItemInstanceState>();
            var leftItems = new List<ItemInstanceState>();
            var leftRoots = new List<ItemSlotState>();
            bool plugged = false;
            int position = 0;
            for (int index = 0; index < roots.Count; index++)
            {
                ItemSlotState root = roots[index];
                tree.Clear();
                // 되찾을지 남길지 정하기 전에 루트 하나의 하위 트리를 루트부터 모아야 남는 트리를 그대로 옮길 수 있다.
                CollectTree(contents, _lost, root.InstanceId, tree);
                // 짝 토템 칸에 같은 종류가 있으면 그 칸은 고르지 않는다. 앞 루트가 꽂은 아이템도 짝 칸 검사에 들어간다.
                string? slot = EmptySlot(slots, slotsFor(tree[0]), tree[0]);
                while (position < capacity && used.Contains(position)) position++;
                // 원본처럼 들어가지 않는 아이템은 상자에 남는다. 루트 키를 그대로 두어 남은 그래프도 키가 겹치지 않는다.
                if (slot == null && position >= capacity)
                {
                    leftRoots.Add(root);
                    leftItems.AddRange(tree);
                    continue;
                }
                foreach (ItemInstanceState item in tree)
                {
                    // 저장에서 온 유실물이 이미 소유한 ID를 가지면 구조 결함으로 보고한다.
                    if (TryItem(item.InstanceId, out _))
                        return InventoryResult.Defect(new InvalidOperationException("인벤토리 불변식 위반: 이미 소유한 유실물입니다."));
                    // 아이템 수가 바뀌므로 전체 사본에 넣는다.
                    Reshaped().Add(item.InstanceId, item);
                }
                if (slot != null)
                {
                    // 슬롯 키는 원작 칸 표에서 고른 키이고 루트 ID는 검증된 그래프의 ID라, 믿을 수 있는 입력용 생성자로 만든다.
                    slots.Add(new ItemSlotState(slot, root.InstanceId));
                    plugged = true;
                }
                else entries.Add(new ItemPositionState(position++, root.InstanceId));
            }
            if (leftRoots.Count == roots.Count) return InventoryResult.Reject(InventoryRejection.InventoryFull);
            // 소지 칸에 놓은 루트가 있을 때만 칸을 바꾼다. 칸 교체는 실패하지 않는다.
            if (entries.Count != existing.Count) ReplaceContainer(character, entries);
            // 칸 교체가 루트 아이템을 새 값으로 바꾸므로 최신 Player에 슬롯을 적용한다. 되찾은 아이템을 넣으며 전체 사본을 이미 만들었다.
            if (plugged) Reshaped()[character] = Player.With(slots: slots);
            // 유실물 그래프에서 루트 키 그대로 떼어 낸 하위 트리라 소유 규칙을 늘 지키므로 검증 없는 생성자로 만든다.
            // 모은 목록은 새 그래프가 그대로 가지며 이 뒤에 바꾸지 않는다.
            remainder = new ItemOwnershipState(leftItems, leftRoots, Array.Empty<StoredInventoryState>());
            return InventoryResult.Ok;
        }

        // 원본 TryPlug(emptyOnly)처럼 후보 칸 중 비어 있는 첫 칸을 고른다. 원작 forbidItemsWithSameID처럼 짝 토템 칸에
        // 같은 종류가 꽂혀 있으면 그 칸은 건너뛴다. 되찾기가 쌓는 중간 칸 목록을 slots로 받는다. 고를 칸이 없으면 null이다.
        string? EmptySlot(IReadOnlyList<ItemSlotState> slots, IReadOnlyList<string> candidates, ItemInstanceState item)
        {
            for (int index = 0; index < candidates.Count; index++)
            {
                string key = candidates[index];
                if (Holder(slots, key) == null && !PartnerHolds(slots, key, item)) return key;
            }
            return null;
        }

        // 칸 목록에서 그 키에 꽂힌 인스턴스 ID를 찾는다. 비어 있으면 null이다.
        static string? Holder(IReadOnlyList<ItemSlotState> slots, string key)
        {
            for (int index = 0; index < slots.Count; index++)
                if (slots[index].Key == key) return slots[index].InstanceId;
            return null;
        }

        // 원작 Slot.CanPlug다. 캐릭터 칸은 그 종류를 받아야 하고 짝 칸에 같은 종류의 다른 아이템이 없어야 한다.
        // 아이템 칸은 칸 태그 규칙을 통과해야 하고, 원작처럼 자기 자신이나 자기 안에 든 아이템의 칸에는 꽂지 못한다.
        bool CanPlug(string owner, string slot, ItemInstanceState item, InventoryRules rules)
        {
            if (owner == _character) return Contains(rules.SlotsFor(item), slot) && !PartnerHolds(Player.Slots, slot, item);
            return owner != item.InstanceId && !Holds(item.InstanceId, owner) && rules.ItemSlotAccepts(Child(owner), slot, item);
        }

        // root의 칸이나 인벤토리 아래 어딘가에 id가 있는지 본다. 원작 Master.GetAllParents 검사와 같은 뜻이다.
        bool Holds(string root, string id)
        {
            ItemInstanceState node = Child(root);
            foreach (ItemSlotState slot in node.Slots)
                if (slot.InstanceId == id || Holds(slot.InstanceId, id)) return true;
            if (node.Inventory != null)
                foreach (ItemPositionState entry in node.Inventory.Entries)
                    if (entry.InstanceId == id || Holds(entry.InstanceId, id)) return true;
            return false;
        }

        // 원작 forbidItemsWithSameID 검사다. 짝 칸에 item과 같은 종류의 다른 인스턴스가 꽂혀 있으면 참이고, 짝이 없는 칸은 거짓이다.
        // 짝 칸 규칙은 OriginalCharacterSlots 한 곳에 있다. 칸 목록의 ID는 작업 사본에 넣은 아이템이라 늘 있다.
        bool PartnerHolds(IReadOnlyList<ItemSlotState> slots, string slot, ItemInstanceState item)
        {
            string? partner = OriginalCharacterSlots.SameIdPartner(slot);
            string? held = partner == null ? null : Holder(slots, partner);
            return held != null && held != item.InstanceId && Child(held).OriginalItemId == item.OriginalItemId;
        }

        // 짧은 칸 키 목록과 정렬 잠금 목록을 LINQ 없이 찾는다.
        static bool Contains<T>(IReadOnlyList<T> values, T value)
        {
            for (int index = 0; index < values.Count; index++)
                if (EqualityComparer<T>.Default.Equals(values[index], value)) return true;
            return false;
        }

        // 작업 사본 안에서 아이템이 놓인 자리다. 소지품이나 창고 칸이면 Container와 Position이, 칸이면 Owner와 Slot이 정해진다.
        // 캐릭터 칸의 Owner는 플레이어 루트이고, 총 부착 칸 같은 아이템 칸의 Owner는 그 주인 아이템이다.
        readonly struct Place
        {
            Place(string? container, int position, string? slot, string? owner)
            {
                Container = container;
                Position = position;
                Slot = slot;
                Owner = owner;
            }

            internal string? Container { get; }
            internal int Position { get; }
            internal string? Slot { get; }
            internal string? Owner { get; }

            internal static Place At(string container, int position) => new Place(container, position, null, null);
            internal static Place InSlot(string owner, string slot) => new Place(null, InventoryLocation._anyPosition, slot, owner);
        }

        // 끌어 온 아이템의 자리를 소지품, 창고, 펫 가방, 캐릭터 칸, 그 아이템들의 아이템 칸 순서로 찾는다. 다른 아이템 인벤토리에 든
        // 탄 같은 하위 소유물은 화면에서 끌 수 없으므로 찾지 않고 false다.
        bool TryLocate(string id, out Place place)
        {
            if (TryPosition(_character, id, out int position))
            {
                place = Place.At(_character, position);
                return true;
            }
            if (TryPosition(_storage, id, out position))
            {
                place = Place.At(_storage, position);
                return true;
            }
            if (TryPosition(_pet, id, out position))
            {
                place = Place.At(_pet, position);
                return true;
            }
            ItemSlotState? slot = OriginalInventoryQueries.EquippedSlot(Player, id);
            if (slot != null)
            {
                place = Place.InSlot(_character, slot.Key);
                return true;
            }
            return TryLocateInItemSlot(id, out place);
        }

        // 소지품, 창고, 펫 가방, 캐릭터 칸에 놓인 아이템부터 그 칸에 꽂힌 아이템까지 아이템 칸을 차례로 훑어 id가 꽂힌 칸을 찾는다.
        // 끌기 한 번에 한 번만 불려 대기열을 그때 만든다.
        bool TryLocateInItemSlot(string id, out Place place)
        {
            var pending = new Queue<string>();
            foreach (ItemPositionState entry in Carried.Entries) pending.Enqueue(entry.InstanceId);
            if (TryContainer(_storage, out ItemContainerState? storage))
                foreach (ItemPositionState entry in storage.Entries) pending.Enqueue(entry.InstanceId);
            if (TryContainer(_pet, out ItemContainerState? pet))
                foreach (ItemPositionState entry in pet.Entries) pending.Enqueue(entry.InstanceId);
            foreach (ItemSlotState equipped in Player.Slots) pending.Enqueue(equipped.InstanceId);
            while (pending.Count > 0)
            {
                string owner = pending.Dequeue();
                foreach (ItemSlotState slot in Child(owner).Slots)
                {
                    if (slot.InstanceId == id)
                    {
                        place = Place.InSlot(owner, slot.Key);
                        return true;
                    }
                    pending.Enqueue(slot.InstanceId);
                }
            }
            place = default;
            return false;
        }

        // 컨테이너에서 그 아이템이 놓인 칸 번호를 찾는다. 컨테이너가 없거나 그 안에 없으면 false다.
        bool TryPosition(string container, string id, out int position)
        {
            position = -1;
            if (!TryContainer(container, out ItemContainerState? contents)) return false;
            IReadOnlyList<ItemPositionState> entries = contents.Entries;
            for (int index = 0; index < entries.Count; index++)
                if (entries[index].InstanceId == id)
                {
                    position = entries[index].Position;
                    return true;
                }
            return false;
        }

        // 그 칸 번호에 놓인 인스턴스 ID다. 빈 칸이면 null이다.
        static string? EntryAt(ItemContainerState contents, int position)
        {
            IReadOnlyList<ItemPositionState> entries = contents.Entries;
            for (int index = 0; index < entries.Count; index++)
                if (entries[index].Position == position) return entries[index].InstanceId;
            return null;
        }

        // 컨테이너에서 그 아이템의 칸만 빼며 실패하지 않는다. 노드는 남기므로 호출자가 다른 자리에 놓거나 지운다.
        void RemoveEntry(string container, string id)
        {
            // 출발지나 목적지로 이미 찾은 컨테이너라 늘 있다.
            TryContainer(container, out ItemContainerState? contents);
            IReadOnlyList<ItemPositionState> entries = contents!.Entries;
            var kept = new List<ItemPositionState>(entries.Count);
            for (int index = 0; index < entries.Count; index++)
                if (entries[index].InstanceId != id) kept.Add(entries[index]);
            // 칸 교체는 실패하지 않으므로 결과 없이 적용한다.
            ReplaceContainer(container, kept);
        }

        // 빈 칸에 아이템을 놓으며 실패하지 않는다. 호출자는 이 명령에서 먼저 찾은 컨테이너의 용량 안 빈 칸 번호만 넘긴다.
        void PutEntry(string container, int position, string id)
        {
            // 출발지나 목적지로 이미 찾은 컨테이너라 늘 있다. 칸 교체는 실패하지 않는다.
            TryContainer(container, out ItemContainerState? contents);
            var entries = new List<ItemPositionState>(contents!.Entries) { new ItemPositionState(position, id) };
            ReplaceContainer(container, entries);
        }

        // owner 아이템의 칸 하나를 바꾸며, id가 null이면 비운다. 같은 키는 제자리에서 바꾸고 없던 키는 끝에 붙여 칸 순서를 지킨다.
        // owner는 플레이어 루트나 아이템 칸의 주인 아이템이다. 실패하지 않는다.
        void SetSlot(string owner, string slot, string? id)
        {
            // 슬롯 키는 CanPlug를 통과한 칸 규칙의 키나 그래프에 있던 키이고 id는 그래프의 ID라, 믿을 수 있는 입력용 생성자로 만든다.
            ItemSlotState? plugged = id != null ? new ItemSlotState(slot, id) : null;
            IReadOnlyList<ItemSlotState> current = Child(owner).Slots;
            var slots = new List<ItemSlotState>(current.Count + 1);
            bool placed = false;
            for (int index = 0; index < current.Count; index++)
            {
                if (current[index].Key != slot) slots.Add(current[index]);
                else if (plugged != null)
                {
                    slots.Add(plugged);
                    placed = true;
                }
            }
            if (plugged != null && !placed) slots.Add(plugged);
            // 칸 교체가 주인 아이템을 새 값으로 바꿀 수 있으므로 최신 주인 노드에 슬롯을 적용한다. 인벤토리와 변수 목록은 공유한다.
            Reshaped()[owner] = Child(owner).With(slots: slots);
        }

        // 옮기는 아이템을 원래 자리에서 뺀다. 노드는 남긴다. 칸 바꾸기 도우미가 실패하지 않아 결과가 없다.
        void Leave(string id, Place from)
        {
            if (from.Slot != null) SetSlot(from.Owner!, from.Slot, null);
            else RemoveEntry(from.Container!, id);
        }

        // 원본 Stackable처럼 최대 스택이 2 이상이고, 내용물도 부착물도 없는 아이템만 합친다.
        static bool Stackable(ItemInstanceState item, int maximum) => maximum > 1 && item.Inventory == null && item.Slots.Count == 0;

        // 원본 Inventory는 칸 번호 순서로 내용을 돌므로, 저장 순서와 상관없이 칸 번호 순서로 정렬한 사본을 만든다.
        static List<ItemPositionState> Ordered(ItemContainerState contents)
        {
            var ordered = new List<ItemPositionState>(contents.Entries);
            ordered.Sort(_byPosition);
            return ordered;
        }

        // 칸 번호 정렬 비교다. 캡처 없는 정적 대리자라 정렬마다 만들지 않는다.
        static readonly Comparison<ItemPositionState> _byPosition = (left, right) => left.Position.CompareTo(right.Position);

        // 원본 GetFirstEmptyPosition처럼 용량 안의 가장 앞 빈 칸이다. 정렬 잠금 칸도 빈 칸으로 본다. 없으면 -1이다.
        static int FirstFree(List<ItemPositionState> ordered, int capacity)
        {
            int next = 0;
            for (int index = 0; index < ordered.Count && ordered[index].Position == next; index++) next++;
            return next < capacity ? next : -1;
        }

        // 그 칸의 아이템이 item을 받아 합칠 수 있는 같은 종류의 덜 찬 스택인지 본다. 칸은 검증된 그래프의 참조라 아이템이 늘 있다.
        bool TryMergeTarget(string id, ItemInstanceState item, int maximum, [NotNullWhen(true)] out ItemInstanceState? target)
        {
            target = Child(id);
            return target.InstanceId != item.InstanceId && target.OriginalItemId == item.OriginalItemId &&
                target.Quantity < maximum && Stackable(target, maximum);
        }

        // 같은 종류의 덜 찬 스택에 더 넣을 수 있는 수량의 합이다. 칸이 많아도 넘치지 않게 long으로 센다.
        long Room(List<ItemPositionState> ordered, ItemInstanceState item, int maximum)
        {
            long room = 0;
            for (int index = 0; index < ordered.Count; index++)
                if (TryMergeTarget(ordered[index].InstanceId, item, maximum, out ItemInstanceState? target)) room += maximum - target.Quantity;
            return room;
        }

        // 원본 AddAndMerge의 다 들어가는지 검사다. 빈 칸(free)이 없고, 쌓을 수 없거나 같은 종류 스택의 여유가 수량보다 적으면 못 넣는다.
        // 합쳐 넣기와 끌어 놓기가 같은 판정을 써서 어느 쪽이든 InventoryFull 거절이 작업 사본을 바꾸지 않는다.
        bool CannotFit(List<ItemPositionState> ordered, int free, ItemInstanceState item, int maximum) =>
            free < 0 && (!Stackable(item, maximum) || Room(ordered, item, maximum) < item.Quantity);

        // 원본 Item.Combine의 조건이다. 같은 종류이고 두 아이템 모두 쌓을 수 있어야 item을 onto에 합친다.
        static bool CanStack(ItemInstanceState item, ItemInstanceState onto, int maximum) =>
            onto.OriginalItemId == item.OriginalItemId && Stackable(item, maximum) && Stackable(onto, maximum);

        // 원본 ItemUtilities.AddAndMerge처럼 같은 종류의 덜 찬 스택을 칸 순서대로 채우고, 남은 수량은 첫 빈 칸에 놓는다. 루팅도 이 규칙으로 쌓는다.
        // item은 어느 칸에도 놓이지 않은 노드이며, 다 합쳐지면 작업 사본에서 지운다. partial이 거짓이면 다 넣을 수 없을 때 아무것도
        // 바꾸지 않고 InventoryFull이다. 참이면 원본 용량 축소처럼 합칠 만큼 합치고, 빈 칸이 없으면 남은 수량으로 노드를 고친 채
        // placed를 거짓으로 둔다.
        internal InventoryResult AddAndMerge(string container, ItemInstanceState item, int maximum, bool partial, out bool placed)
        {
            placed = false;
            InventoryResult known = KnownCapacity(container, out ItemContainerState? contents, out int capacity);
            if (!known.IsOk) return known;
            List<ItemPositionState> ordered = Ordered(contents!);
            int free = FirstFree(ordered, capacity);
            bool stackable = Stackable(item, maximum);
            // 끌어 놓기와 같은 다 들어가는지 판정이다. partial이면 넣을 만큼만 넣으므로 보지 않는다.
            if (!partial && CannotFit(ordered, free, item, maximum)) return InventoryResult.Reject(InventoryRejection.InventoryFull);
            int remaining = item.Quantity;
            if (stackable)
                for (int index = 0; index < ordered.Count && remaining > 0; index++)
                {
                    if (!TryMergeTarget(ordered[index].InstanceId, item, maximum, out ItemInstanceState? target)) continue;
                    int amount = Math.Min(remaining, maximum - target.Quantity);
                    SetQuantity(target, target.Quantity + amount);
                    remaining -= amount;
                }
            if (remaining == 0)
            {
                // 원본 Combine처럼 다 합쳐진 인스턴스는 사라진다. 사본에 없던 새 노드면 수량만 바뀐 채 확정된다.
                if (TryItem(item.InstanceId, out _)) Reshaped().Remove(item.InstanceId);
                placed = true;
                return InventoryResult.Ok;
            }
            ItemInstanceState rest = remaining == item.Quantity ? item : item.WithQuantity(remaining);
            Reshaped()[rest.InstanceId] = rest;
            if (free < 0) return InventoryResult.Ok;
            placed = true;
            // 위에서 찾은 첫 빈 칸이라 놓기는 실패하지 않는다.
            PutEntry(container, free, rest.InstanceId);
            return InventoryResult.Ok;
        }

        // 칸 번호로 가리키는 구역의 컨테이너 ID다. 창고와 펫 가방은 저장 인벤토리 키이고, 나머지는 소지품이다.
        // 장비와 아이템 칸 목적지는 컨테이너가 아니라 이 도우미를 거치지 않는다.
        internal string ContainerOf(InventoryArea area) =>
            area == InventoryArea.Storage ? _storage : area == InventoryArea.Pet ? _pet : _character;

        // 끌어 놓기 한 번을 원본 InventoryEntry.OnDrop과 SlotDisplay.OnDrop 규칙으로 계산한다. 끌 수 있는 아이템은 소지품, 창고,
        // 펫 가방, 캐릭터 칸과 그 아이템들의 아이템 칸에 있는 것뿐이고 못 찾으면 NotOwned다. 칸 번호는 화면이 용량 안에서만 만들고,
        // 창고는 기지의 창고 화면에만 보인다. 아이템 칸 목적지의 주인 아이템이 그래프에 없어도 NotOwned다.
        internal InventoryResult Move(string id, InventoryLocation to, InventoryRules rules)
        {
            if (!TryItem(id, out ItemInstanceState? item) || !TryLocate(id, out Place from)) return Unowned();
            // 장비와 아이템 칸 목적지는 InventoryLocation.Equipment와 ItemSlot으로만 만들어 칸 키가 늘 있다.
            if (to.Area == InventoryArea.Equipment) return MoveToSlot(item, from, _character, to.Slot!, rules);
            if (to.Area == InventoryArea.ItemSlot)
                return TryItem(to.Owner!, out _) ? MoveToSlot(item, from, to.Owner!, to.Slot!, rules) : Unowned();
            // 원작 InventoryEntry.OnDrop처럼 펫 가방(acceptSticky 거짓)은 끌어 온 Sticky 아이템을 받지 않는다.
            // 원작처럼 끌어 온 아이템만 보므로 맞바꾸기로 밀려나는 아이템은 보지 않는다.
            if (to.Area == InventoryArea.Pet && rules.Sticky(item)) return InventoryResult.Reject(InventoryRejection.Sticky);
            string container = ContainerOf(to.Area);
            // 같은 컨테이너로의 AddAndMerge나 제자리 놓기는 바꿀 것이 없다.
            if (from.Container == container && (to.Position == InventoryLocation._anyPosition || to.Position == from.Position))
                return InventoryResult.Ok;
            int maximum = rules.MaxStack(item);
            if (to.Position == InventoryLocation._anyPosition)
            {
                // AddAndMerge와 같은 다 들어가는지 검사를 출발 칸을 비우기 전에 해, InventoryFull 거절이 작업 사본을 바꾸지 않게 한다.
                // 같은 컨테이너는 위에서 돌아왔으므로 출발 칸을 비워도 목적지 여유는 그대로다.
                InventoryResult targetKnown = KnownCapacity(container, out ItemContainerState? targetContents, out int targetCapacity);
                if (!targetKnown.IsOk) return targetKnown;
                List<ItemPositionState> targetOrdered = Ordered(targetContents!);
                if (CannotFit(targetOrdered, FirstFree(targetOrdered, targetCapacity), item, maximum))
                    return InventoryResult.Reject(InventoryRejection.InventoryFull);
                // 출발 칸 비우기는 실패하지 않으므로 바로 목적지에 합쳐 넣는다.
                Leave(id, from);
                return AddAndMerge(container, item, maximum, false, out _);
            }
            // 칸 번호는 화면이 확정 그래프 용량 안의 칸에서만 만들므로 용량은 받지 않는다.
            InventoryResult known = KnownCapacity(container, out ItemContainerState? contents, out _);
            if (!known.IsOk) return known;
            string? occupantId = EntryAt(contents!, to.Position);
            if (occupantId == null)
            {
                // 빈 칸으로 옮기는 두 도우미는 실패하지 않는다.
                Leave(id, from);
                PutEntry(container, to.Position, id);
                return InventoryResult.Ok;
            }
            // 검증된 그래프의 칸이 가리키는 아이템이라 늘 있다.
            ItemInstanceState occupant = Child(occupantId);
            // 원작 ItemDisplay.HandleDirectDrop처럼 놓은 칸의 아이템에 받는 아이템 칸이 있으면 먼저 꽂는다. 받는 칸이 없을 때만 아래 칸 규칙을 쓴다.
            if (PlugInto(occupant, item, from, from.Container ?? container, rules) is InventoryResult plugged) return plugged;
            // 같은 종류 스택에 붓기는 실패하지 않는다. 합칠 조건은 루팅 칸 놓기와 같은 CanStack이다.
            if (CanStack(item, occupant, maximum))
            {
                Combine(item, from, occupant, maximum);
                return InventoryResult.Ok;
            }
            // 그 밖에는 맞바꾼다. 캐릭터 칸이나 아이템 칸에서 온 아이템이면 밀려난 아이템이 그 칸 규칙을 통과해야 하고, 아니면 Occupied다.
            if (from.Slot != null)
            {
                if (!CanPlug(from.Owner!, from.Slot, occupant, rules)) return InventoryResult.Reject(InventoryRejection.Occupied);
                // 칸 규칙을 통과한 뒤의 칸 바꾸기는 실패하지 않는다.
                RemoveEntry(container, occupantId);
                PutEntry(container, to.Position, id);
                SetSlot(from.Owner!, from.Slot, occupantId);
                return InventoryResult.Ok;
            }
            // 원본 OnDrop처럼 두 칸의 아이템을 서로의 칸 번호로 옮긴다. 소지품, 창고, 펫 가방 사이에서도 같고, 칸 바꾸기는 실패하지 않는다.
            RemoveEntry(from.Container!, id);
            RemoveEntry(container, occupantId);
            PutEntry(from.Container!, from.Position, occupantId);
            PutEntry(container, to.Position, id);
            return InventoryResult.Ok;
        }

        // 원본 OnDrop의 Item.Combine이다. target 스택에 들어가는 만큼만 옮기고 나머지는 원래 자리에 남는다. 가득 찬 스택이면
        // 바꾸지 않고, 다 옮기면 원래 자리에서 빼고 인스턴스를 지운다. 수량과 칸만 바꾸므로 실패하지 않는다.
        void Combine(ItemInstanceState item, Place from, ItemInstanceState target, int maximum)
        {
            int amount = Math.Min(item.Quantity, maximum - target.Quantity);
            if (amount <= 0) return;
            SetQuantity(target, target.Quantity + amount);
            if (amount < item.Quantity)
            {
                SetQuantity(item, item.Quantity - amount);
                return;
            }
            Leave(item.InstanceId, from);
            Reshaped().Remove(item.InstanceId);
        }

        // 원본 SlotDisplay.OnDrop처럼 owner 아이템의 칸(캐릭터 칸이나 아이템 칸)에 꽂는다. 칸이 받지 않으면 SlotMismatch다.
        // 원작처럼 칸에 든 아이템이 받는 아이템 칸을 가졌으면 그 아이템에 먼저 꽂는다(ItemDisplay.HandleDirectDrop).
        // 밀려난 아이템은 소지품, 창고, 펫 가방 칸에서 왔으면 그 칸으로, 다른 칸에서 왔으면 그 칸이 받을 때 그 칸으로 간다.
        // 둘 다 아니면 소지품에 AddAndMerge로 들어가고, 거기도 자리가 없으면 InventoryFull이다.
        InventoryResult MoveToSlot(ItemInstanceState item, Place from, string owner, string slot, InventoryRules rules)
        {
            if (from.Owner == owner && from.Slot == slot) return InventoryResult.Ok;
            string? occupantId = Holder(Child(owner).Slots, slot);
            // 검증된 그래프의 칸이 가리키는 아이템이라 늘 있다. 밀려난 아이템의 백업은 원작 TryPlug처럼 출발 컨테이너, 없으면 소지품이다.
            if (occupantId != null && PlugInto(Child(occupantId), item, from, from.Container ?? _character, rules) is InventoryResult plugged)
                return plugged;
            if (!CanPlug(owner, slot, item, rules)) return InventoryResult.Reject(InventoryRejection.SlotMismatch);
            // 칸 규칙을 통과한 뒤의 칸 바꾸기는 실패하지 않는다. 밀려난 아이템이 없으면 끝난다.
            Leave(item.InstanceId, from);
            SetSlot(owner, slot, item.InstanceId);
            if (occupantId == null) return InventoryResult.Ok;
            ItemInstanceState occupant = Child(occupantId);
            // 밀려난 아이템을 출발 칸이나 출발 칸에 놓는 것은 실패하지 않고, 소지품에 합쳐 넣을 때만 InventoryFull이 날 수 있다.
            if (from.Slot == null)
            {
                PutEntry(from.Container!, from.Position, occupantId);
                return InventoryResult.Ok;
            }
            if (CanPlug(from.Owner!, from.Slot, occupant, rules))
            {
                SetSlot(from.Owner!, from.Slot, occupantId);
                return InventoryResult.Ok;
            }
            return AddAndMerge(_character, occupant, rules.MaxStack(occupant), false, out _);
        }

        // 원작 ItemUtilities.TryPlug(emptyOnly 거짓)다. main의 아이템 칸 중 part를 받는 칸이 없으면 null을 돌려 호출자가 칸 규칙을 잇는다.
        // part가 이미 그 후보 칸 하나에 꽂혀 있어도 원작처럼 꽂지 않고 null이다. 받는 빈 칸이 있으면 첫 빈 칸에, 없으면 첫 받는 칸에 꽂는다.
        // 밀려난 아이템은 part가 떠난 칸이 받으면 그 칸으로, 아니면 backup 컨테이너에 AddAndMerge로 간다. 자리가 없으면 InventoryFull이다.
        // 원작은 그때 땅에 떨어뜨리지만 이 명령은 아무것도 바꾸지 않는다. from이 null이면 아직 어디에도 놓이지 않은 새 아이템이다.
        InventoryResult? PlugInto(ItemInstanceState main, ItemInstanceState part, Place? from, string backup, InventoryRules rules)
        {
            IReadOnlyList<string> keys = rules.ItemSlotsFor(main);
            string? empty = null, first = null;
            for (int index = 0; index < keys.Count; index++)
            {
                string key = keys[index];
                if (!CanPlug(main.InstanceId, key, part, rules)) continue;
                if (from is Place source && source.Owner == main.InstanceId && source.Slot == key) return null;
                first ??= key;
                if (empty == null && Holder(main.Slots, key) == null) empty = key;
            }
            string? slot = empty ?? first;
            if (slot == null) return null;
            string? displaced = Holder(main.Slots, slot);
            // 받는 칸 규칙을 통과한 뒤의 칸 바꾸기는 실패하지 않는다.
            if (from is Place left) Leave(part.InstanceId, left);
            SetSlot(main.InstanceId, slot, part.InstanceId);
            if (displaced == null) return InventoryResult.Ok;
            ItemInstanceState occupant = Child(displaced);
            if (from is Place origin && origin.Slot != null && CanPlug(origin.Owner!, origin.Slot, occupant, rules))
            {
                SetSlot(origin.Owner!, origin.Slot, displaced);
                return InventoryResult.Ok;
            }
            return AddAndMerge(backup, occupant, rules.MaxStack(occupant), false, out _);
        }

        // 루팅으로 새로 만든 아이템을 소지품, 창고, 펫 가방의 정한 칸에 놓는다. 빈 칸이면 그 자리에 넣고, 같은 종류의 스택이면 수량이
        // 전부 들어갈 때만 합친다. 그 밖에는 Occupied다. 아이템은 새 GUID로 만든 것이라 이미 소유한 ID가 아니다.
        internal InventoryResult AddAt(string container, int position, ItemInstanceState item, int maximum, InventoryRules rules)
        {
            // 칸 번호는 화면이 확정 그래프 용량 안의 칸에서만 만들므로 용량은 받지 않는다.
            InventoryResult known = KnownCapacity(container, out ItemContainerState? contents, out _);
            if (!known.IsOk) return known;
            string? occupantId = EntryAt(contents!, position);
            if (occupantId == null)
            {
                // 빈 칸에 새 노드를 놓는 것은 실패하지 않는다.
                Reshaped().Add(item.InstanceId, item);
                PutEntry(container, position, item.InstanceId);
                return InventoryResult.Ok;
            }
            // 검증된 그래프의 칸이 가리키는 아이템이라 늘 있다.
            ItemInstanceState occupant = Child(occupantId);
            // 원작 ItemDisplay.HandleDirectDrop처럼 놓은 칸의 아이템이 받는 아이템 칸을 가졌으면 새 아이템을 거기 꽂는다.
            // 받는 칸이 없으면 null이라 아래 합치기 규칙으로 간다. 새 노드는 꽂기 전에 작업 사본에 넣는다.
            if (rules.ItemSlotsFor(occupant).Count > 0)
            {
                Reshaped().Add(item.InstanceId, item);
                if (PlugInto(occupant, item, null, container, rules) is InventoryResult plugged) return plugged;
                Reshaped().Remove(item.InstanceId);
            }
            // 끌어 놓기와 같은 합칠 조건에 더해, 루팅 칸은 수량이 전부 들어갈 때만 합친다.
            if (!CanStack(item, occupant, maximum) || item.Quantity > maximum - occupant.Quantity)
                return InventoryResult.Reject(InventoryRejection.Occupied);
            // 놓인 스택에 더하는 것은 수량만 바뀌므로 모양을 유지한 채 확정된다.
            SetQuantity(occupant, occupant.Quantity + item.Quantity);
            return InventoryResult.Ok;
        }

        // 루팅으로 새로 만든 아이템을 owner의 칸(캐릭터 칸이나 아이템 칸)에 꽂는다. 칸 규칙은 MoveToSlot과 같고,
        // 칸에 든 아이템이 받는 아이템 칸을 가졌으면 거기 먼저 꽂는다. 밀려난 아이템은 소지품에 AddAndMerge로 넣는다.
        // 자리가 없으면 InventoryFull이다. 아이템은 새 GUID로 만든 것이라 이미 소유한 ID가 아니다.
        internal InventoryResult Plug(string owner, string slot, ItemInstanceState item, InventoryRules rules)
        {
            if (!TryItem(owner, out ItemInstanceState? master)) return Unowned();
            string? occupantId = Holder(master.Slots, slot);
            // 새 노드를 먼저 넣어야 칸 규칙과 꽂기가 그 노드를 찾는다. 거절되면 작업 사본째 버려지므로 다시 빼지 않는다.
            Reshaped().Add(item.InstanceId, item);
            if (occupantId != null && PlugInto(Child(occupantId), item, null, _character, rules) is InventoryResult plugged) return plugged;
            if (!CanPlug(owner, slot, item, rules)) return InventoryResult.Reject(InventoryRejection.SlotMismatch);
            // 칸 규칙을 통과한 뒤 꽂기는 실패하지 않는다. 밀려난 아이템이 없으면 끝난다.
            SetSlot(owner, slot, item.InstanceId);
            if (occupantId == null) return InventoryResult.Ok;
            // 검증된 그래프의 칸이 가리키는 아이템이라 늘 있다.
            ItemInstanceState occupant = Child(occupantId);
            return AddAndMerge(_character, occupant, rules.MaxStack(occupant), false, out _);
        }

        // 원본 LootView.OnStoreAllButtonClicked처럼 소지품을 칸 순서대로 창고에 AddAndMerge로 넣는다. 정렬 잠금 칸은 건너뛰고,
        // 들어가지 않는 첫 아이템에서 멈춘다. 각 아이템은 전부 넣거나 그대로 둔다. 하나도 못 넣고 멈추면 InventoryFull이다.
        internal InventoryResult StoreAll(InventoryRules rules, out int moved, out bool stopped)
        {
            moved = 0;
            stopped = false;
            ItemContainerState carried = Carried;
            List<ItemPositionState> ordered = Ordered(carried);
            IReadOnlyList<int> locked = carried.LockedIndexes;
            for (int index = 0; index < ordered.Count; index++)
            {
                ItemPositionState entry = ordered[index];
                if (Contains(locked, entry.Position)) continue;
                // 시작 시점 칸 목록이지만 반복 안에서는 지금 아이템만 빠지므로 뒤 칸의 아이템은 늘 있다.
                ItemInstanceState item = Child(entry.InstanceId);
                // 소지 칸을 빼기 전에 창고에 넣어 본다. 들어가지 않으면 AddAndMerge가 아무것도 바꾸지 않으므로 그대로 멈춘다.
                InventoryResult stored = AddAndMerge(_storage, item, rules.MaxStack(item), false, out _);
                if (stored.Rejection == InventoryRejection.InventoryFull)
                {
                    stopped = true;
                    break;
                }
                if (!stored.IsOk) return stored;
                // 창고에 넣은 아이템의 소지 칸 빼기는 실패하지 않는다.
                RemoveEntry(_character, item.InstanceId);
                moved++;
            }
            return moved == 0 && stopped ? InventoryResult.Reject(InventoryRejection.InventoryFull) : InventoryResult.Ok;
        }

        // 원작 Inventory.Sort다. 정렬 잠금 칸의 아이템은 제자리에 둔다. 나머지는 첫 태그 묶음 순서(태그 priority, 같으면 태그 이름을
        // 대소문자 없이 비교)로, 묶음 안은 종류 순서(아이템 order, 같으면 원본 ID)로 늘어놓는다. 쌓이는 종류는 원작 TryMerge처럼 합친 뒤
        // 원작 AddItem처럼 앞 빈 칸부터 놓는다. 인스턴스 ID는 그대로이고 다 부어진 인스턴스만 사라진다.
        internal InventoryResult Sort(string container, InventoryRules rules)
        {
            InventoryResult known = KnownCapacity(container, out ItemContainerState? contents, out _);
            if (!known.IsOk) return known;
            List<ItemPositionState> ordered = Ordered(contents!);
            IReadOnlyList<int> locked = contents!.LockedIndexes;
            var entries = new List<ItemPositionState>(ordered.Count);
            var used = new HashSet<int>();
            var loose = new List<ItemInstanceState>(ordered.Count);
            for (int index = 0; index < ordered.Count; index++)
            {
                ItemPositionState entry = ordered[index];
                if (Contains(locked, entry.Position))
                {
                    entries.Add(entry);
                    used.Add(entry.Position);
                }
                // 검증된 그래프의 칸이라 가리키는 아이템이 늘 있다.
                else loose.Add(Child(entry.InstanceId));
            }
            // GroupBy는 처음 나온 순서를 지켜 원작 LINQ 묶음과 같다. 묶음 키가 서로 달라 정렬 결과는 하나로 정해진다.
            List<IGrouping<string, ItemInstanceState>> tags = loose.GroupBy(item => rules.SortKey(item).Tag, StringComparer.Ordinal).ToList();
            tags.Sort((left, right) => CompareTags(rules.SortKey(left.First()), rules.SortKey(right.First())));
            var sorted = new List<string>(loose.Count);
            foreach (IGrouping<string, ItemInstanceState> tag in tags)
            {
                List<IGrouping<int, ItemInstanceState>> types = tag.GroupBy(item => item.OriginalItemId).ToList();
                types.Sort((left, right) =>
                {
                    int leftOrder = rules.SortKey(left.First()).Order, rightOrder = rules.SortKey(right.First()).Order;
                    return leftOrder != rightOrder ? leftOrder.CompareTo(rightOrder) : left.Key.CompareTo(right.Key);
                });
                foreach (IGrouping<int, ItemInstanceState> type in types)
                {
                    List<ItemInstanceState> group = type.ToList();
                    int maximum = rules.MaxStack(group[0]);
                    // 원작은 첫 아이템의 Stackable만 본다. 여기서는 내용물이나 부착물이 있는 인스턴스를 부어 하위 트리를 잃지 않도록
                    // 모두 합칠 수 있을 때만 합친다. 그런 인스턴스는 최대 스택이 1이라 실제 데이터에서는 결과가 같다.
                    if (group.TrueForAll(item => Stackable(item, maximum))) Merge(group, maximum, sorted);
                    else foreach (ItemInstanceState item in group) sorted.Add(item.InstanceId);
                }
            }
            int position = 0;
            foreach (string id in sorted)
            {
                // 원작 GetFirstEmptyPosition처럼 비어 있는 잠금 칸에도 놓는다. 놓을 아이템이 원래 칸 수보다 많지 않아 용량 안에 든다.
                while (used.Contains(position)) position++;
                entries.Add(new ItemPositionState(position++, id));
            }
            // 다른 경로가 넣는 순서와 상관없이 저장 칸 목록도 칸 번호 순서로 둔다. 칸 교체는 실패하지 않는다.
            entries.Sort(_byPosition);
            ReplaceContainer(container, entries);
            return InventoryResult.Ok;
        }

        // 원작 Sort의 태그 묶음 비교다. priority가 같으면 태그 이름을 대소문자 없이 비교한다. 빈 이름은 원작 null 태그로 priority -1이다.
        static int CompareTags(InventorySortKey left, InventorySortKey right) => left.Priority != right.Priority
            ? left.Priority.CompareTo(right.Priority) : string.Compare(left.Tag, right.Tag, StringComparison.OrdinalIgnoreCase);

        // 원작 TryMerge다. 칸 순서 목록을 스택에 넣어 마지막 스택부터 꺼내고, 꺼낸 스택이 찰 때까지 그 앞 스택들을 붓는다.
        // 붓다 남은 스택이 다음에 채울 스택이 되므로, 남는 인스턴스와 그 순서가 원작과 같다. 남은 ID를 sorted에 붙이고 다 부은 스택은 지운다.
        void Merge(List<ItemInstanceState> group, int maximum, List<string> sorted)
        {
            var counts = new int[group.Count];
            for (int index = 0; index < counts.Length; index++) counts[index] = group[index].Quantity;
            int top = group.Count, held = -1;
            while (top > 0)
            {
                if (held < 0) held = --top;
                if (top == 0)
                {
                    sorted.Add(group[held].InstanceId);
                    break;
                }
                int poured = -1;
                while (counts[held] < maximum && top > 0)
                {
                    poured = --top;
                    int amount = Math.Min(counts[poured], maximum - counts[held]);
                    counts[held] += amount;
                    counts[poured] -= amount;
                }
                sorted.Add(group[held].InstanceId);
                if (poured >= 0 && counts[poured] > 0)
                {
                    if (top == 0)
                    {
                        sorted.Add(group[poured].InstanceId);
                        break;
                    }
                    held = poured;
                }
                else held = -1;
            }
            for (int index = 0; index < group.Count; index++)
            {
                if (counts[index] == group[index].Quantity) continue;
                if (counts[index] == 0) Reshaped().Remove(group[index].InstanceId);
                else SetQuantity(group[index], counts[index]);
            }
        }

        // 원본 CharacterMainControl.UpdateInventoryCapacity처럼 컨테이너 용량을 바꾼다. 소지품과 펫 가방이 같은 규칙을 쓴다.
        // 줄면 새 용량 밖의 아이템을 칸 순서대로 안쪽에 AddAndMerge로 다시 넣는다. 원본처럼 합칠 만큼 합친 뒤 남은 것은 하위 트리째
        // 그래프에서 빼고, 루트 하나짜리 그래프로 dropped에 담는다. 새 용량 밖의 정렬 잠금 칸은 버린다. 컨테이너가 없으면 Defect다.
        // 원작 PetProxy는 넘친 칸을 그대로 두지만, 이 컨테이너는 용량 밖 칸을 가질 수 없어 소지품처럼 떨군다.
        internal InventoryResult SetCapacity(string container, int capacity, InventoryRules rules, out List<ItemOwnershipState> dropped)
        {
            dropped = new List<ItemOwnershipState>();
            if (!TryContainer(container, out ItemContainerState? contents)) return MissingContainer(container);
            var kept = new List<ItemPositionState>(contents.Entries.Count);
            var outside = new List<ItemPositionState>();
            foreach (ItemPositionState entry in contents.Entries) (entry.Position < capacity ? kept : outside).Add(entry);
            outside.Sort(_byPosition);
            var locked = new List<int>(contents.LockedIndexes.Count);
            foreach (int index in contents.LockedIndexes)
                if (index < capacity) locked.Add(index);
            // 세션 명령이 용량을 0 이상으로 자르고, 칸과 정렬 잠금은 검증된 컨테이너에서 새 용량 안의 것만 남기므로
            // 칸 교체가 믿을 수 있는 입력용 생성자로 새 컨테이너를 만든다.
            ReplaceContainer(container, kept, capacity, locked);
            for (int index = 0; index < outside.Count; index++)
            {
                // 검증된 그래프에서 시작하고 명령이 끊긴 참조를 만들지 않으므로 칸이 가리키는 아이템은 늘 있다.
                ItemInstanceState item = Child(outside[index].InstanceId);
                InventoryResult added = AddAndMerge(container, item, rules.MaxStack(item), true, out bool placed);
                if (!added.IsOk) return added;
                // 탄창 비우기와 같은 바닥 그래프 규칙을 쓴다.
                if (!placed) dropped.Add(TakeDropped(item.InstanceId));
            }
            return InventoryResult.Ok;
        }

        // 원본 ItemSetting_Gun.TakeOutAllBullets처럼 탄창의 탄을 칸 순서대로 꺼내 원본 PickupItem처럼 소지품에 합칠 만큼 합친다.
        // 빈 칸이 없어 남은 묶음은 그래프에서 빼 dropped에 담고, 호출자가 원본처럼 플레이어 주변 바닥에 떨군다.
        internal InventoryResult Unload(string weapon, InventoryRules rules, out List<ItemOwnershipState> dropped)
        {
            dropped = new List<ItemOwnershipState>();
            // 호출자는 같은 프레임에 그래프에서 찾은 총만 넘기고, 그래프의 총은 늘 탄창 컨테이너를 가진다.
            TryContainer(weapon, out ItemContainerState? magazine);
            List<ItemPositionState> bullets = Ordered(magazine!);
            if (bullets.Count == 0) return InventoryResult.Ok;
            // 탄 노드는 남긴 채 탄창 칸만 비운다. 칸이 없는 노드는 아래 AddAndMerge가 소지품에 놓거나 합쳐 지운다.
            ReplaceContainer(weapon, Array.Empty<ItemPositionState>());
            for (int index = 0; index < bullets.Count; index++)
            {
                ItemInstanceState bullet = Child(bullets[index].InstanceId);
                InventoryResult added = AddAndMerge(_character, bullet, rules.MaxStack(bullet), true, out bool placed);
                if (!added.IsOk) return added;
                if (!placed) dropped.Add(TakeDropped(bullet.InstanceId));
            }
            return InventoryResult.Ok;
        }

        // 어느 칸에도 놓이지 못한 아이템의 하위 트리를 작업 사본에서 빼 루트 하나짜리 바닥 그래프로 만든다.
        // 통째로 뺀 하위 트리라 소유 규칙을 늘 지키므로 검증 없는 생성자로 만든다. 루트 키는 월드에 새로 떨군 아이템과 같은 _dropRoot라
        // 줍기와 보관함이 같은 모양으로 읽는다.
        ItemOwnershipState TakeDropped(string id)
        {
            var tree = new List<ItemInstanceState>();
            TakeTree(id, tree);
            return new ItemOwnershipState(tree, new[] { new ItemSlotState(_dropRoot, id) }, Array.Empty<StoredInventoryState>());
        }

        // 땅에 떨어진 아이템 하나를 원래 ID와 하위 트리 그대로 소지품에 AddAndMerge로 줍는다. 원작 PickupItem은 빈 캐릭터 칸에
        // 먼저 꽂지만 이 명령은 소지품에만 넣는다. 자리가 없으면 InventoryFull이다.
        internal InventoryResult PickUp(ItemOwnershipState dropped, InventoryRules rules)
        {
            IReadOnlyList<ItemSlotState> roots = dropped.Roots;
            // 바닥 아이템 그래프는 용량 축소가 그래프에서 뺀 하위 트리로 만든 루트 하나짜리 그래프라 루트 아이템이 늘 있고,
            // 저장되지 않으며 주우면 사라지므로 이미 소유한 ID도 없다.
            ItemInstanceState root = dropped.Find(roots[0].InstanceId)!;
            IReadOnlyList<ItemInstanceState> items = dropped.Items;
            // 하위 트리를 먼저 넣고 루트만 AddAndMerge에 맡긴다. 합칠 수 있는 루트는 하위 트리가 없어 사라져도 고립 노드가 남지 않는다.
            for (int index = 0; index < items.Count; index++)
                if (!ReferenceEquals(items[index], root)) Reshaped().Add(items[index].InstanceId, items[index]);
            return AddAndMerge(_character, root, rules.MaxStack(root), false, out _);
        }

        // 원작 Cost.Return과 PlayerStorage.Push처럼 새 아이템 하나(최대 스택 이하)를 고른 곳에 차례로 넣는다. 소지품, 그다음 창고에
        // 같은 종류 스택을 합칠 만큼 합치고 남으면 첫 빈 칸에 놓는다. 펫 가방은 원작처럼 목적지가 아니다. 다 들어가지 않으면 남은 수량을
        // 바닥 그래프와 같은 루트 하나짜리 그래프로 left에 담아, 호출자가 창고 임시 보관함에 넣거나 바닥에 떨군다.
        // item은 작업 사본에 없는 새 노드다. 컨테이너가 없으면 Defect로 보고한 구조 결함이다.
        internal InventoryResult Deliver(ItemInstanceState item, int maximum, bool toCarried, bool toStorage, out ItemOwnershipState? left)
        {
            left = null;
            // 들어가지 못한 수량은 AddAndMerge가 칸 없는 노드로 작업 사본에 남긴다. 그 뒤 단계는 그 노드를 다시 읽는다.
            bool entered = false;
            if (toCarried)
            {
                InventoryResult added = AddAndMerge(_character, item, maximum, true, out bool placed);
                if (!added.IsOk) return added;
                if (placed) return InventoryResult.Ok;
                entered = true;
            }
            if (toStorage)
            {
                InventoryResult stored = AddAndMerge(_storage, entered ? Child(item.InstanceId) : item, maximum, true, out bool placed);
                if (!stored.IsOk) return stored;
                if (placed) return InventoryResult.Ok;
                entered = true;
            }
            // 새 노드라 하위 트리가 없다. 작업 사본에 들어간 적 없으면 TakeDropped와 같은 루트 키로 같은 모양의 그래프를 바로 만든다.
            left = entered ? TakeDropped(item.InstanceId)
                : new ItemOwnershipState(new[] { item }, new[] { new ItemSlotState(_dropRoot, item.InstanceId) },
                    Array.Empty<StoredInventoryState>());
            return InventoryResult.Ok;
        }

        // 원작 ItemUtilities.SendToPlayer와 개조 창 HandleUnpluggledItem의 돌려주기다. 소유 아이템 id를 놓인 자리(가방·창고·펫 가방 칸,
        // 캐릭터 칸, 아이템 칸, 아이템 안 인벤토리)에서 하위 트리째 떼어 소지품, 그다음 창고에 넣는다. 펫 가방은 원작처럼 목적지가 아니다.
        // 합치기는 원작 SendToPlayer(dontMerge: 창고 있음)와 PlayerStorage.Push를 따른다. 창고로도 보낼 때(기지) 소지품에는 합치지 않고 첫 빈 칸에만 놓고,
        // 소지품만 보낼 때는 같은 종류 스택에 합친다. 창고는 늘 합친 뒤 첫 빈 칸이다. 원작 SendToPlayerCharacter의 빈 캐릭터 칸 먼저 꽂기는 하지 않는다.
        // 다 들어가지 않으면 남은 수량과 하위 트리를 루트 하나짜리 그래프로 left에 담아 호출자가 창고 임시 보관함에 넣는다.
        // 그래프에 없거나 플레이어 루트면 NotOwned다. 호출자: CustomizePlug·CustomizeUnplug(4-U 개조), 4-D 분해의 부품·내용물 돌려주기.
        internal InventoryResult DeliverOwned(string id, bool toCarried, bool toStorage, InventoryRules rules, out ItemOwnershipState? left)
        {
            left = null;
            if (id == _character || !TryItem(id, out ItemInstanceState? item)) return Unowned();
            Detach(id);
            int maximum = rules.MaxStack(item);
            if (toCarried && toStorage)
            {
                // 원작 SendToPlayerCharacterInventory(dontMerge 참)의 Inventory.AddItem이다. 빈 칸이 없으면 창고로 넘어간다.
                InventoryResult free = FreePosition(_character, out _, out int position);
                if (free.IsOk)
                {
                    PutEntry(_character, position, id);
                    return InventoryResult.Ok;
                }
                if (free.Rejection != InventoryRejection.InventoryFull) return free;
            }
            else if (toCarried)
            {
                InventoryResult added = AddAndMerge(_character, item, maximum, true, out bool placed);
                if (!added.IsOk) return added;
                if (placed) return InventoryResult.Ok;
            }
            if (toStorage)
            {
                // 소지품에 일부만 합쳐졌으면 AddAndMerge가 남은 수량으로 노드를 고쳐 두었으므로 최신 노드를 읽는다.
                InventoryResult stored = AddAndMerge(_storage, Child(id), maximum, true, out bool placed);
                if (!stored.IsOk) return stored;
                if (placed) return InventoryResult.Ok;
            }
            left = TakeDropped(id);
            return InventoryResult.Ok;
        }

        // 원작 Item.Detach다. 아이템이 놓인 칸이나 슬롯 하나를 비우고 노드와 하위 트리는 남긴다. 저장 인벤토리(창고, 펫 가방)와 모든 노드의
        // 슬롯·인벤토리에서 찾는다. 검증된 그래프라 루트가 아닌 노드는 한 자리에만 놓여 있다. 다 찾은 뒤 바꾸므로 순회 중에 사본을 바꾸지 않는다.
        void Detach(string id)
        {
            string? container = null, owner = null, slot = null;
            IReadOnlyList<StoredInventoryState> inventories = Inventories;
            for (int index = 0; index < inventories.Count && container == null; index++)
                if (EntryOf(inventories[index].Inventory, id)) container = inventories[index].Key;
            if (container == null)
            {
                // 전체 사본이 있으면 그것만, 없으면 수량만 바꾼 노드 배열이나 원본 노드를 읽는다(TryItem과 같은 규칙).
                IEnumerable<ItemInstanceState> nodes = _items != null ? _items.Values : (IEnumerable<ItemInstanceState>)(_nodes ?? _source.Items);
                foreach (ItemInstanceState node in nodes)
                {
                    slot = SlotOf(node.Slots, id);
                    if (slot != null)
                    {
                        owner = node.InstanceId;
                        break;
                    }
                    if (node.Inventory != null && EntryOf(node.Inventory, id))
                    {
                        container = node.InstanceId;
                        break;
                    }
                }
            }
            // 칸 바꾸기 두 도우미는 실패하지 않는다. 어디에도 없는 노드는 이미 떨어져 있어 바꿀 것이 없다.
            if (slot != null) SetSlot(owner!, slot, null);
            else if (container != null) RemoveEntry(container, id);
        }

        // 슬롯 목록에서 그 아이템이 꽂힌 칸 키를 찾는다. 키로 아이템을 찾는 Holder의 반대 방향이다. 없으면 null이다.
        static string? SlotOf(IReadOnlyList<ItemSlotState> slots, string id)
        {
            for (int index = 0; index < slots.Count; index++)
                if (slots[index].InstanceId == id) return slots[index].Key;
            return null;
        }

        // 컨테이너 칸에 그 아이템이 있는지 본다.
        static bool EntryOf(ItemContainerState contents, string id)
        {
            IReadOnlyList<ItemPositionState> entries = contents.Entries;
            for (int index = 0; index < entries.Count; index++)
                if (entries[index].InstanceId == id) return true;
            return false;
        }

        // 원작 개조 선택 창이 보이는 아이템이다. 캐릭터 장비 칸이나 소지품 맨 위 칸에 있어야 개조 대상이 된다.
        bool IsCustomizeTarget(string id) => TryPosition(_character, id, out _) || OriginalInventoryQueries.EquippedSlot(Player, id) != null;

        // 원작 개조 창의 부품 후보다. 소지품 맨 위 칸이나, 기지면 창고(PlayerStorage) 맨 위 칸에 있어야 한다.
        bool IsCustomizePart(string id, bool atBase) => TryPosition(_character, id, out _) || (atBase && TryPosition(_storage, id, out _));

        // 원작 ItemCustomizeView.OnEquipButtonClicked다. 대상 target의 아이템 칸 slot에 부품 part를 꽂는다. 부품 후보는 원작처럼
        // 소지품과 창고(기지)의 맨 위 칸이고, 원작 Slot.CanPlug(태그, 자기 자신·조상 금지)를 통과해야 한다. 칸에 있던 부품은 먼저 빼서
        // DeliverOwned로 기지면 창고(합치기 → 빈 칸), 아니면 소지품에 돌려주고 넘친 것은 left에 더한다. 그 뒤 새 부품을 떼어 꽂는다.
        // 대상이나 부품이 그 자리에 없으면 NotOwned, 칸이 없거나 받지 않으면 SlotMismatch다. 거절은 사본을 바꾸기 전에 한다.
        internal InventoryResult CustomizePlug(string target, string slot, string part, bool atBase, InventoryRules rules,
            List<ItemOwnershipState> left)
        {
            if (!TryItem(target, out ItemInstanceState? master) || !IsCustomizeTarget(target)) return Unowned();
            if (!Contains(rules.ItemSlotsFor(master), slot)) return InventoryResult.Reject(InventoryRejection.SlotMismatch);
            // 개조 창이 부품으로 보여 주지 않는 자리의 아이템은 꽂지 않는다.
            if (!TryItem(part, out ItemInstanceState? item) || !IsCustomizePart(part, atBase)) return Unowned();
            if (!CanPlug(target, slot, item, rules)) return InventoryResult.Reject(InventoryRejection.SlotMismatch);
            string? occupant = Holder(master.Slots, slot);
            if (occupant != null)
            {
                InventoryResult returned = DeliverOwned(occupant, !atBase, atBase, rules, out ItemOwnershipState? rest);
                if (!returned.IsOk) return returned;
                if (rest != null) left.Add(rest);
            }
            // 원작처럼 뺀 부품을 먼저 보낸 뒤 새 부품을 떼므로, 뺀 부품은 새 부품이 비운 칸을 쓰지 않는다. 칸 규칙을 통과해 꽂기는 실패하지 않는다.
            Detach(part);
            SetSlot(target, slot, part);
            return InventoryResult.Ok;
        }

        // 원작 ItemCustomizeView.OnUnequipButtonClicked다. 대상의 칸 slot에 꽂힌 부품을 빼서 CustomizePlug와 같은 순서로 돌려준다.
        // 대상이 없거나 개조 대상 자리가 아니면 NotOwned, 칸이 없으면 SlotMismatch, 빈 칸이면 원작처럼 뺄 것이 없어 NotOwned다.
        internal InventoryResult CustomizeUnplug(string target, string slot, bool atBase, InventoryRules rules, out ItemOwnershipState? left)
        {
            left = null;
            if (!TryItem(target, out ItemInstanceState? master) || !IsCustomizeTarget(target)) return Unowned();
            if (!Contains(rules.ItemSlotsFor(master), slot)) return InventoryResult.Reject(InventoryRejection.SlotMismatch);
            string? occupant = Holder(master.Slots, slot);
            if (occupant == null) return Unowned();
            return DeliverOwned(occupant, !atBase, atBase, rules, out left);
        }

        // 원작 PlayerStorage.TakeBufferItem처럼 창고 임시 보관함 그래프 하나를 원래 ID와 하위 트리 그대로 창고에 AddAndMerge로 넣는다.
        // 원작 IsAccessableAndNotFull처럼 합칠 스택만 있어도 빈 칸이 하나 있어야 하고, 없으면 InventoryFull이다. 빈 칸이 있으면 늘 다 들어간다.
        // 보관함 그래프는 소유 그래프와 따로 검증된 저장 기록이라, 이미 소유한 ID를 가지면 Defect로 보고한 구조 결함이다.
        internal InventoryResult StoreBuffered(ItemOwnershipState buffered, InventoryRules rules)
        {
            InventoryResult free = FreePosition(_storage, out _, out _);
            if (!free.IsOk) return free;
            IReadOnlyList<ItemInstanceState> items = buffered.Items;
            for (int index = 0; index < items.Count; index++)
                if (TryItem(items[index].InstanceId, out _))
                    return InventoryResult.Defect(new InvalidOperationException("인벤토리 불변식 위반: 이미 소유한 보관함 아이템입니다."));
            // 저장 검증이 루트 하나짜리 그래프만 받으므로 루트 아이템이 늘 있다. PickUp처럼 하위 트리를 먼저 넣고 루트만 AddAndMerge에 맡긴다.
            ItemInstanceState root = buffered.Find(buffered.Roots[0].InstanceId)!;
            for (int index = 0; index < items.Count; index++)
                if (!ReferenceEquals(items[index], root)) Reshaped().Add(items[index].InstanceId, items[index]);
            return AddAndMerge(_storage, root, rules.MaxStack(root), false, out _);
        }

        // 탄약과 회복제를 정해진 칸에서 소비하며 하위 소유물은 지우지 않는다. 내용물이 남은 아이템은 NotUsable로 거절한다.
        internal InventoryResult Consume(string container, string id, int count)
        {
            // 장전, 발사, 아이템 사용 세 호출자 모두 이 컨테이너 칸에서 이미 찾은 아이템과, 1 이상 소지 수량 이하의 수량을 넘긴다.
            // 아이템 사용 중에는 인벤토리 편집이 막혀 그 칸을 떠나지 않는다.
            ItemInstanceState item = Child(id);
            TryContainer(container, out ItemContainerState? contents);
            // 발사마다 불리므로 할당을 피하려고 람다 없이 인덱스 루프로 일치하는 칸 수를 센다.
            IReadOnlyList<ItemPositionState> entries = contents!.Entries;
            int matches = 0;
            for (int index = 0; index < entries.Count; index++)
                if (entries[index].InstanceId == id) matches++;
            // 일부만 쓰면 칸은 그대로라 수량만 바꾼다. 발사와 회복제 한 개 사용이 이 경로로 전체 그래프를 복사하지 않는다.
            if (count < item.Quantity)
            {
                SetQuantity(item, item.Quantity - count);
                return InventoryResult.Ok;
            }
            if (item.Slots.Count != 0 || item.Inventory?.Entries.Count > 0)
                return InventoryResult.Reject(InventoryRejection.NotUsable);
            var remaining = new ItemPositionState[entries.Count - matches];
            for (int index = 0, next = 0; index < entries.Count; index++)
                if (entries[index].InstanceId != id) remaining[next++] = entries[index];
            // 칸 교체는 실패하지 않고 전체 사본을 이미 만들므로 그 사본에서 노드를 지운다.
            ReplaceContainer(container, remaining);
            Reshaped().Remove(id);
            return InventoryResult.Ok;
        }

        // 원작처럼 소지품과 펫 가방의 맨 위 칸에 놓인 아이템만 바로 쓸 수 있다. 그 아이템이 놓인 가방의 컨테이너 ID를 돌려주고,
        // 창고나 장비 칸처럼 다른 곳이면 false다. 펫 가방이 아직 없는 그래프에서는 소지품만 본다.
        internal bool TryUsableHolder(string id, [NotNullWhen(true)] out string? container)
        {
            container = TryPosition(_character, id, out _) ? _character : TryPosition(_pet, id, out _) ? _pet : null;
            return container != null;
        }

        // 원작 InteractableBase.TryGetRequiredItem처럼 sources가 고른 캐릭터 칸, 가방들을 출처 순서로 보며 그 원본 아이템을 찾는다.
        // 가방은 칸 번호 순서로 보며 아이템마다 그 아이템의 칸도 본다. 찾은 자리가 가방 아이템의 칸이면 parentSlot에 그 칸의 주인 ID가 담긴다.
        // 원작이 먼저 보는 등록 열쇠는 그래프 밖의 소유자라 호출자가 이 찾기 앞에서 본다.
        internal bool TryFindRequired(int originalItemId, ItemSources sources, [NotNullWhen(true)] out string? id, out string? parentSlot)
        {
            parentSlot = null;
            if ((sources & ItemSources.Equipment) != 0)
                foreach (ItemSlotState slot in Player.Slots)
                    if (Child(slot.InstanceId).OriginalItemId == originalItemId) { id = slot.InstanceId; return true; }
            for (int bag = 0; bag < _bagCount; bag++)
            {
                // 고르지 않았거나 아직 없는 가방은 TryBag이 거짓이라 건너뛴다.
                if (TryBag(bag, sources, out _, out ItemContainerState? contents)
                    && TryFindRequiredIn(contents, originalItemId, out id, out parentSlot)) return true;
            }
            id = null;
            return false;
        }

        // 출처 순서의 index번째 가방이 sources에 골라져 있고 작업 사본에 있으면 그 컨테이너 ID와 내용을 준다. 찾기, 빼기, 모으기가 함께 쓴다.
        // BagAt은 확정 그래프 세기도 쓰는 정적 규칙이라 소지품 컨테이너 ID를 넘긴다. 펫 가방은 월드 첫 초기화가 더하므로 그 전 그래프에서는 거짓이다.
        bool TryBag(int index, ItemSources sources, [NotNullWhen(true)] out string? container,
            [NotNullWhen(true)] out ItemContainerState? contents)
        {
            contents = null;
            container = BagAt(index, sources, _character);
            return container != null && TryContainer(container, out contents);
        }

        // 원작 FindAllBelongsToPlayer의 출처 순서(창고, 가방, 펫 가방)에서 index번째 가방의 컨테이너 ID다. sources가 고르지 않았으면 null이다.
        // 매 프레임 비용 확인이 할당 없이 돌도록 순서를 번호로 정한다. 장비 칸은 컨테이너가 아니라 열쇠 찾기가 따로 본다.
        // 작업 사본 없이 확정 그래프에서 바로 세는 경로도 같은 순서를 쓰도록 정적으로 두고 소지품 컨테이너 ID(character)를 받는다.
        static string? BagAt(int index, ItemSources sources, string character) => index switch
        {
            0 => (sources & ItemSources.Storage) != 0 ? _storage : null,
            1 => (sources & ItemSources.Bag) != 0 ? character : null,
            2 => (sources & ItemSources.Pet) != 0 ? _pet : null,
            _ => null
        };

        // BagAt이 아는 가방 수다.
        const int _bagCount = 3;

        // 가방 하나를 칸 번호 순서로 보며 아이템과 그 아이템의 칸에서 그 원본 아이템을 찾는다. 칸에서 찾으면 parentSlot에 주인 ID가 담긴다.
        bool TryFindRequiredIn(ItemContainerState contents, int originalItemId, [NotNullWhen(true)] out string? id, out string? parentSlot)
        {
            parentSlot = null;
            foreach (ItemPositionState entry in Ordered(contents))
            {
                ItemInstanceState item = Child(entry.InstanceId);
                if (item.OriginalItemId == originalItemId) { id = item.InstanceId; return true; }
                foreach (ItemSlotState slot in item.Slots)
                    if (Child(slot.InstanceId).OriginalItemId == originalItemId) { id = slot.InstanceId; parentSlot = item.InstanceId; return true; }
            }
            id = null;
            return false;
        }

        // 원작 UseRequiredItem이다. 내구도를 쓰는 아이템은 1을 깎아 반올림 0 이하면 없애고, 쌓이지 않는 아이템은 바로 없애고,
        // 쌓이는 아이템은 하나를 줄인다. 다른 아이템 칸에 꽂힌 아이템을 없애야 하면 원작과 달리 NotUsable로 거절한다.
        internal InventoryResult UseRequired(string id, string? parentSlot, bool usesDurability, int maximum)
        {
            ItemInstanceState item = Child(id);
            if (usesDurability)
            {
                float remaining = (item.AbsoluteDurability ?? 0f) - 1f;
                if ((int)Math.Round(remaining, MidpointRounding.ToEven) > 0)
                {
                    Reshaped()[id] = item.WithDurability(remaining);
                    return InventoryResult.Ok;
                }
            }
            else if (Stackable(item, maximum) && item.Quantity > 1)
            {
                SetQuantity(item, item.Quantity - 1);
                return InventoryResult.Ok;
            }
            if (parentSlot != null || !TryLocate(id, out Place place)) return InventoryResult.Reject(InventoryRejection.NotUsable);
            Leave(id, place);
            TakeTree(id, new List<ItemInstanceState>());
            return InventoryResult.Ok;
        }

        // 원작 ItemUtilities.GetItemCount(FindAllBelongsToPlayer)처럼 sources가 고른 가방의 맨 위 칸 수량 합이다. 장비 칸과 아이템 칸에
        // 꽂힌 아이템은 원작처럼 세지 않는다. 매 프레임 비용 확인이 부르므로 할당 없이 돌고, 칸이 많아도 넘치지 않게 long으로 센다.
        // 작업 사본의 현재 노드로 센다. 규칙은 확정 그래프 세기와 함께 쓰는 CountIn에 있다.
        internal long Count(int originalItemId, ItemSources sources) =>
            CountIn(this, _find, Inventories, _character, originalItemId, sources);

        // 매 프레임 비용 확인(SessionInventoryCommands.CountOwned, HasOwned)이 작업 사본을 만들지 않도록 확정 그래프에서 바로 센다.
        // 세션 그래프는 로드 검증과 월드 첫 초기화가 플레이어 루트를 보장하므로 CharacterId가 늘 있다.
        internal static long Count(ItemOwnershipState graph, int originalItemId, ItemSources sources) =>
            CountIn(graph, _graphFind, graph.Inventories, graph.CharacterId!, originalItemId, sources);

        // 확정 그래프에서 ID로 노드를 찾는 조회 함수다. 캡처 없는 정적 대리자라 세기마다 할당하지 않는다.
        static readonly Func<ItemOwnershipState, string, ItemInstanceState?> _graphFind = (items, id) => items.Find(id);

        // 작업 사본과 확정 그래프가 조회 함수만 바꿔 같은 세기 규칙을 쓴다. 가방은 BagAt 순서로 보고, 펫 가방은 월드 첫 초기화가
        // 더하므로 그 전 그래프에서는 건너뛴다. 칸이 가리키는 노드는 검증된 그래프라 늘 있다.
        static long CountIn<TState>(TState state, Func<TState, string, ItemInstanceState?> find, IReadOnlyList<StoredInventoryState> inventories,
            string character, int originalItemId, ItemSources sources)
        {
            long count = 0;
            for (int bag = 0; bag < _bagCount; bag++)
            {
                string? container = BagAt(bag, sources, character);
                if (container == null || !OriginalInventoryQueries.TryContainer(state, find, inventories, container,
                        out ItemContainerState? contents, out _, out _)) continue;
                IReadOnlyList<ItemPositionState> entries = contents.Entries;
                for (int index = 0; index < entries.Count; index++)
                {
                    ItemInstanceState item = find(state, entries[index].InstanceId)!;
                    if (item.OriginalItemId == originalItemId) count += item.Quantity;
                }
            }
            return count;
        }

        // 다시 그리기 한 번이 비용 여러 개를 볼 때 쓰는 원본 ID별 수량이다. CountIn과 같은 가방과 출처 순서로 확정 그래프를 한 번만 훑어,
        // 화면이 행마다 가방 전체를 다시 세던 비용을 없앤다(Docs/runtime-boundaries.md#main-thread-split). 호출자: SessionInventoryCommands.CountAllOwned.
        internal static Dictionary<int, long> CountAll(ItemOwnershipState graph, ItemSources sources)
        {
            var counts = new Dictionary<int, long>();
            string character = graph.CharacterId!;
            for (int bag = 0; bag < _bagCount; bag++)
            {
                string? container = BagAt(bag, sources, character);
                if (container == null || !OriginalInventoryQueries.TryContainer(graph, _graphFind, graph.Inventories, container,
                        out ItemContainerState? contents, out _, out _)) continue;
                IReadOnlyList<ItemPositionState> entries = contents.Entries;
                for (int index = 0; index < entries.Count; index++)
                {
                    ItemInstanceState item = graph.Find(entries[index].InstanceId)!;
                    counts.TryGetValue(item.OriginalItemId, out long count);
                    counts[item.OriginalItemId] = count + item.Quantity;
                }
            }
            return counts;
        }

        // 작업 사본으로 보는 비용 확인이다. Consume이 쓰며, 규칙은 확정 그래프 확인과 함께 쓰는 HasIn에 있다.
        internal bool Has(IReadOnlyList<(int id, int amount)> costs, ItemSources sources) => HasIn(this, _count, costs, sources);

        // 매 프레임 비용 확인(SessionInventoryCommands.HasOwned)이 작업 사본을 만들지 않도록 확정 그래프에서 바로 본다.
        internal static bool Has(ItemOwnershipState graph, IReadOnlyList<(int id, int amount)> costs, ItemSources sources) =>
            HasIn(graph, _graphCount, costs, sources);

        // 두 비용 확인이 세는 곳만 바꾸는 캡처 없는 정적 대리자다. 확인마다 할당하지 않는다.
        static readonly Func<GameplayInventory, int, ItemSources, long> _count = (inventory, id, sources) => inventory.Count(id, sources);
        static readonly Func<ItemOwnershipState, int, ItemSources, long> _graphCount = (graph, id, sources) => Count(graph, id, sources);

        // 원작 Cost.Enough의 아이템 부분이다. 같은 ID 항목은 합쳐서 보므로 비용 아이템과 현금 몫에 같은 현금이 있어도 한 번에 센다.
        // 매 프레임 불릴 수 있어 목록을 만들지 않고, 앞 항목과 같은 ID는 그 항목에서 이미 셌으므로 건너뛴다.
        static bool HasIn<TState>(TState state, Func<TState, int, ItemSources, long> count, IReadOnlyList<(int id, int amount)> costs,
            ItemSources sources)
        {
            for (int index = 0; index < costs.Count; index++)
            {
                int id = costs[index].id;
                bool counted = false;
                for (int earlier = 0; earlier < index && !counted; earlier++) counted = costs[earlier].id == id;
                if (counted) continue;
                long needed = 0;
                for (int other = index; other < costs.Count; other++)
                    if (costs[other].id == id) needed += costs[other].amount;
                if (needed > 0 && count(state, id, sources) < needed) return false;
            }
            return true;
        }

        // 지불 확인(GameplayCommands.CanPay)이 현금 몫을 붙인 배열을 만들지 않도록, 저작 비용 항목 끝에 extra 하나가 붙은 목록처럼 본다.
        // 합치는 규칙은 HasIn과 같다. extra 수량이 0 이하면 붙이지 않은 것이다. 비용 항목 모양이 달라 HasIn을 그대로 쓰지 못한다.
        internal static bool Has(ItemOwnershipState graph, IReadOnlyList<Cost.ItemEntry> costs, (int id, int amount) extra,
            ItemSources sources) =>
            HasIn(graph, _graphCount, costs, extra, sources);

        // 위 Has의 규칙이다. 세는 곳만 바꿔 미리 센 수량 묶음(OwnedItemCounts)도 같은 판정을 쓴다.
        internal static bool HasIn<TState>(TState state, Func<TState, int, ItemSources, long> count, IReadOnlyList<Cost.ItemEntry> costs,
            (int id, int amount) extra, ItemSources sources)
        {
            int length = costs.Count + (extra.amount > 0 ? 1 : 0);
            for (int index = 0; index < length; index++)
            {
                int id = index < costs.Count ? costs[index].Id : extra.id;
                bool counted = false;
                for (int earlier = 0; earlier < index && !counted; earlier++) counted = costs[earlier].Id == id;
                if (counted) continue;
                // extra는 그 ID가 처음 나온 자리에서 한 번만 더한다. 비용 항목에 없으면 목록 끝 자리가 처음이다.
                long needed = extra.amount > 0 && id == extra.id ? extra.amount : 0;
                for (int other = index; other < costs.Count; other++)
                    if (costs[other].Id == id) needed += costs[other].Amount;
                if (needed > 0 && count(state, id, sources) < needed) return false;
            }
            return true;
        }

        // 원작 ConsumeItems(Cost)다. 모든 항목이 충분할 때만 항목마다 같은 ID 아이템을 출처 순서로 모아 내구도가 낮은 것부터 쓴다.
        // 원작은 없앨 아이템의 칸 내용물을 떼어 플레이어에게 돌려주지만, 여기서는 칸 내용물이 있는 아이템을 없애야 하면 NotUsable로 거절한다.
        internal InventoryResult Consume(IReadOnlyList<(int id, int amount)> costs, ItemSources sources)
        {
            if (!Has(costs, sources)) return InventoryResult.Reject(InventoryRejection.NotOwned);
            for (int index = 0; index < costs.Count; index++)
            {
                // 앞 항목이 같은 ID를 줄였을 수 있어 항목마다 지금 작업 사본에서 다시 모은다.
                List<(string container, ItemInstanceState item)> found = Found(costs[index].id, sources);
                SortByDurability(found);
                InventoryResult spent = Spend(found, costs[index].amount);
                if (!spent.IsOk) return spent;
            }
            return InventoryResult.Ok;
        }

        // 원작 SubmitItems.Interact처럼 정렬 없이 출처 순서로 최대 maximum개를 쓴다. 일부만 낼 수 있고 낸 수는 submitted다.
        // 낼 것이 하나도 없으면 NotOwned 거절이다.
        internal InventoryResult Submit(int originalItemId, int maximum, ItemSources sources, out int submitted)
        {
            submitted = (int)Math.Max(0, Math.Min(Count(originalItemId, sources), maximum));
            if (submitted == 0) return InventoryResult.Reject(InventoryRejection.NotOwned);
            return Spend(Found(originalItemId, sources), submitted);
        }

        // 원작 StockShop.Sell의 Detach와 DestroyTree다. sources가 고른 가방을 출처 순서(창고, 가방, 펫 가방)로 보며 그 ID가 놓인 맨 위 칸을 비우고,
        // 아이템을 하위 트리(부착물, 내용물)째 작업 사본에서 빼 루트 하나짜리 그래프로 removed에 담는다. 장비 칸과 아이템 안의 칸은 보지 않는다.
        // 고른 가방의 맨 위 칸에 없으면 NotOwned 거절이다.
        internal InventoryResult Remove(string id, ItemSources sources, out ItemOwnershipState? removed)
        {
            removed = null;
            for (int bag = 0; bag < _bagCount; bag++)
            {
                // 고르지 않았거나 아직 없는 가방은 TryBag이 거짓이라 건너뛴다.
                if (!TryBag(bag, sources, out string? container, out ItemContainerState? contents)) continue;
                IReadOnlyList<ItemPositionState> entries = contents.Entries;
                var kept = new List<ItemPositionState>(entries.Count);
                for (int index = 0; index < entries.Count; index++)
                    if (entries[index].InstanceId != id) kept.Add(entries[index]);
                if (kept.Count == entries.Count) continue;
                // 칸을 비운 뒤 하위 트리를 뺀다. 바닥 그래프와 같은 루트 하나짜리 모양이라 TakeDropped를 쓰며, 두 도우미는 실패하지 않는다.
                ReplaceContainer(container, kept);
                removed = TakeDropped(id);
                return InventoryResult.Ok;
            }
            return Unowned();
        }

        // 원작 FindAllBelongsToPlayer처럼 고른 가방을 출처 순서로, 가방마다 칸 번호 순서로 보며 그 원본 아이템이 놓인 맨 위 칸을 모은다.
        List<(string container, ItemInstanceState item)> Found(int originalItemId, ItemSources sources)
        {
            var found = new List<(string container, ItemInstanceState item)>();
            for (int bag = 0; bag < _bagCount; bag++)
            {
                // 고르지 않았거나 아직 없는 가방은 TryBag이 거짓이라 건너뛴다.
                if (!TryBag(bag, sources, out string? container, out ItemContainerState? contents)) continue;
                foreach (ItemPositionState entry in Ordered(contents))
                {
                    ItemInstanceState item = Child(entry.InstanceId);
                    if (item.OriginalItemId == originalItemId) found.Add((container, item));
                }
            }
            return found;
        }

        // 원작 ItemDurabilityComparer처럼 내구도가 낮은 것을 앞에 둔다. 내구도가 없는 아이템은 비교값 0이라 현금처럼 출처 순서가 남는다.
        // 원작 List.Sort와 달리 안정 정렬이라 같은 내구도끼리도 출처 순서를 지킨다. 모은 수가 적어 삽입 정렬로 충분하다.
        static void SortByDurability(List<(string container, ItemInstanceState item)> found)
        {
            for (int index = 1; index < found.Count; index++)
                for (int at = index; at > 0 && LessDurable(found[at].item, found[at - 1].item); at--)
                    (found[at - 1], found[at]) = (found[at], found[at - 1]);
        }

        // 두 아이템 모두 내구도가 있고 item 쪽이 더 낮을 때만 참이다. 하나라도 없으면 원작 비교값 0처럼 순서를 바꾸지 않는다.
        static bool LessDurable(ItemInstanceState item, ItemInstanceState than) =>
            item.AbsoluteDurability is float durability && than.AbsoluteDurability is float other && durability < other;

        // 모은 칸을 앞에서부터 remaining만큼 쓰고, 다 쓴 아이템은 하위 트리째 없앤다. 모은 칸마다 아이템이 달라 한 번씩만 줄인다.
        // 아이템 칸에 다른 아이템이 꽂혀 있으면 원작처럼 떼어 돌려주지 않고 NotUsable로 거절하며, 작업 사본은 호출자가 버린다.
        InventoryResult Spend(List<(string container, ItemInstanceState item)> found, int remaining)
        {
            for (int index = 0; index < found.Count && remaining > 0; index++)
            {
                (string container, ItemInstanceState item) = found[index];
                if (item.Quantity > remaining)
                {
                    SetQuantity(item, item.Quantity - remaining);
                    return InventoryResult.Ok;
                }
                if (item.Slots.Count != 0) return InventoryResult.Reject(InventoryRejection.NotUsable);
                remaining -= item.Quantity;
                RemoveEntry(container, item.InstanceId);
                TakeTree(item.InstanceId, new List<ItemInstanceState>());
            }
            return InventoryResult.Ok;
        }

        // 무기는 전투 시뮬레이션이 장착 칸에서 읽고, 장착이 바뀌면 장전을 취소한다. 다른 탄종이 남은 탄창은 CaliberMismatch
        // 규칙 거절이다. 옮긴 탄 수는 loaded로 돌려준다.
        internal InventoryResult Reload(string weapon, int bulletType, int capacity, out int loaded)
        {
            loaded = 0;
            string character = _character;
            // 장전하는 무기는 총이다. 이어하기와 저장 지점이 탄창 없는 총을 막고 카탈로그가 만드는 총은 탄창을 가지므로 탄창은 늘 있다.
            TryContainer(weapon, out ItemContainerState? magazine);
            // 탄약 HUD의 MagazineCount와 같은 합산으로 현재 장탄수를 세고, 다른 탄종이 남은 탄창은 거절한다.
            if (!OriginalInventoryQueries.TrySumMagazine(this, _find, magazine!, bulletType, out int current))
                return InventoryResult.Reject(InventoryRejection.CaliberMismatch);
            // 카탈로그 용량이 int 하한 근처여도 뺄셈이 넘치지 않도록 long으로 계산한다. current가 0 이상이라 결과는 capacity 이하이므로
            // 아래에서 int로 되돌려도 안전하다.
            long shortfall = (long)capacity - current;
            if (shortfall <= 0) return InventoryResult.Ok;
            int needed = (int)shortfall;
            ItemContainerState carried = Carried;
            // carried는 불변 값이라 소비로 칸이 바뀌어도 시작 시점 칸 목록을 그대로 순회한다.
            foreach (ItemPositionState entry in carried.Entries)
            {
                // 시작 시점 칸 목록이지만 소비는 지금 탄만 지우므로 뒤 칸의 아이템은 늘 있다.
                ItemInstanceState ammunition = Child(entry.InstanceId);
                if (ammunition.OriginalItemId != bulletType) continue;
                int amount = Math.Min(needed - loaded, ammunition.Quantity);
                // 일부 스택 이동은 새 ID 하나만 만들고 양쪽 수량의 합을 보존한다. 탄창은 위에서 찾은 같은 무기 컨테이너라 늘 있다.
                TryContainer(weapon, out ItemContainerState? chamber);
                ItemPositionState? existing = chamber!.Entries.FirstOrDefault();
                if (existing == null)
                {
                    // 나눈 묶음의 새 ID도 저장 아이템 ID 형식을 정하는 영속 ID 생성에서 받는다.
                    string id = amount == ammunition.Quantity ? ammunition.InstanceId : PersistentIds.New();
                    InventoryResult consumed = Consume(character, ammunition.InstanceId, amount);
                    if (!consumed.IsOk) return consumed;
                    InventoryResult added = Add(weapon, Create(id, bulletType, amount));
                    if (!added.IsOk) return added;
                }
                else
                {
                    // 검증된 그래프에서 시작하고 명령이 끊긴 참조를 만들지 않으므로 탄창 칸의 탄은 늘 있다.
                    ItemInstanceState bullet = Child(existing.InstanceId);
                    // 탄창 수량이 int 범위를 넘으면 구조 결함이므로 덧셈 전에 검사해 Defect로 보고한다.
                    if (amount > int.MaxValue - bullet.Quantity)
                        return InventoryResult.Defect(new InvalidOperationException("인벤토리 불변식 위반: 탄창 탄약 수량이 정수 범위를 넘습니다."));
                    InventoryResult consumed = Consume(character, ammunition.InstanceId, amount);
                    if (!consumed.IsOk) return consumed;
                    // 탄창의 기존 탄 묶음에 더하는 것은 수량만 바뀐다. 소지 묶음도 남으면 장전 전체가 모양을 유지한다.
                    SetQuantity(bullet, bullet.Quantity + amount);
                }
                loaded += amount;
                if (loaded == needed) break;
            }
            return InventoryResult.Ok;
        }

        // 빈 탄창으로 쏘면 fired=false인 성공이다. consume이 거짓이면 원본 기지 레벨처럼 같은 판정만 하고 탄을 쓰지 않는다.
        internal InventoryResult Shoot(string weapon, out bool fired, bool consume = true)
        {
            fired = false;
            // 전투 시뮬레이션이 커밋마다 장착 칸에서 다시 읽고 발사 전에 탄창을 확인한 총만 넘기므로 탄창 컨테이너가 늘 있다.
            TryContainer(weapon, out ItemContainerState? magazine);
            IReadOnlyList<ItemPositionState> entries = magazine!.Entries;
            if (entries.Count == 0) return InventoryResult.Ok;
            if (consume)
            {
                InventoryResult consumed = Consume(weapon, entries[0].InstanceId, 1);
                if (!consumed.IsOk) return consumed;
            }
            fired = true;
            return InventoryResult.Ok;
        }

        // 모양이 그대로면 바꾼 수량만 원본 그래프에 넣어 색인을 다시 쓰고, 바뀐 것이 없으면 원본을 그대로 돌려준다.
        // 모양이 바뀌었으면 전체 사본으로 새 그래프를 만든다. 작업 사본은 소유 규칙을 지키므로 실패하지 않고 다시 검증하지 않는다.
        // 손상 저장의 유실물 ID가 창고 키와 겹치는 교차 충돌은 런타임에서 잡지 않고 테스트가 아이템 ID 형식으로 막는다.
        internal ItemOwnershipState Build()
        {
            if (_items == null)
            {
                // 새 그래프가 노드 배열을 그대로 가지므로, 작업 사본은 그 그래프를 새 원본으로 읽는다. 이 뒤에 수량을 또 바꿔도
                // 배열을 새로 복사해 내준 그래프는 바뀌지 않는다.
                if (_nodes != null)
                {
                    _source = _source.WithQuantities(_nodes);
                    _nodes = null;
                }
                return _source;
            }
            // 새 그래프는 받은 목록을 복사하지 않고 가지므로, 이 뒤에도 작업 사본이 고칠 수 있는 노드 사전과 저장 인벤토리 목록은
            // 배열로 떠서 넘긴다. 원본 루트 목록은 바뀌지 않아 그대로 넘긴다.
            IReadOnlyList<StoredInventoryState> inventories = _inventories != null ? _inventories.ToArray() : _source.Inventories;
            return new ItemOwnershipState(_items.Values.ToArray(), _source.Roots, inventories);
        }
    }

    // 작업 사본이 카탈로그와 Unity 형식을 모르도록 세션 명령이 넘기는 아이템 규칙이다. 정의 조회와 누락 기록은 대리자 안에서 한다.
    internal readonly struct InventoryRules
    {
        // sticky를 생략하면 어떤 아이템도 Sticky가 아니다. 이동 규칙만 시험하는 규칙은 Sticky 정의 없이 만든다.
        internal InventoryRules(Func<ItemInstanceState, int> maxStack, Func<ItemInstanceState, IReadOnlyList<string>> slotsFor,
            Func<ItemInstanceState, InventorySortKey> sortKey, Func<ItemInstanceState, IReadOnlyList<string>> itemSlotsFor,
            Func<ItemInstanceState, string, ItemInstanceState, bool> itemSlotAccepts, Func<ItemInstanceState, bool>? sticky = null)
        {
            MaxStack = maxStack;
            SlotsFor = slotsFor;
            SortKey = sortKey;
            ItemSlotsFor = itemSlotsFor;
            ItemSlotAccepts = itemSlotAccepts;
            Sticky = sticky ?? _notSticky;
        }

        // sticky를 생략한 규칙이 쓰는 캡처 없는 정적 대리자라 규칙마다 만들지 않는다.
        static readonly Func<ItemInstanceState, bool> _notSticky = _ => false;

        // 원본 최대 스택 수다. 1 이하면 합치지 않는다.
        internal Func<ItemInstanceState, int> MaxStack { get; }
        // 그 아이템을 받는 캐릭터 칸 키를 원작 칸 순서로 준다(OriginalCharacterSlots.SlotsFor). 장착할 수 없으면 빈 목록이다.
        internal Func<ItemInstanceState, IReadOnlyList<string>> SlotsFor { get; }
        // 원작 Inventory.Sort가 읽는 첫 태그와 order다.
        internal Func<ItemInstanceState, InventorySortKey> SortKey { get; }
        // 그 아이템이 가진 원작 아이템 칸(SlotCollection) 키를 원본 순서로 준다. 총 부착 칸 같은 칸이 없으면 빈 목록이다.
        internal Func<ItemInstanceState, IReadOnlyList<string>> ItemSlotsFor { get; }
        // (주인 아이템, 칸 키, 꽂을 아이템)으로 원작 칸 태그 규칙을 본다. 주인에게 그 칸이 없으면 거짓이다.
        internal Func<ItemInstanceState, string, ItemInstanceState, bool> ItemSlotAccepts { get; }
        // 원작 Item.Sticky('Sticky' 태그)다. acceptSticky가 거짓인 펫 가방이 끌어 온 아이템을 받을지 정한다.
        internal Func<ItemInstanceState, bool> Sticky { get; }
    }

    // 원작 Inventory.Sort의 정렬 키다. Tag가 빈 문자열이면 원작의 null 태그이며 Priority는 -1이다.
    internal readonly struct InventorySortKey
    {
        internal InventorySortKey(string tag, int priority, int order)
        {
            Tag = tag;
            Priority = priority;
            Order = order;
        }

        internal string Tag { get; }
        internal int Priority { get; }
        internal int Order { get; }
    }
}
