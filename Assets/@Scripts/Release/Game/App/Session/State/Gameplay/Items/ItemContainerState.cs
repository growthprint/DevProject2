#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 인벤토리 칸 위치와 정렬 잠금 칸을 보관한다. 용량을 모르는 경우(null)와 실제로 빈 인벤토리를 구분한다.
    public sealed class ItemContainerState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        // 목록은 따로 한 번 복사하고, null 목록은 빈 목록으로 둔다.
        public ItemContainerState(int? capacity, IReadOnlyList<ItemPositionState> entries, IReadOnlyList<int> lockedIndexes)
        {
            Capacity = capacity; Entries = ReadOnlyLists.CopyOrEmpty(entries); LockedIndexes = ReadOnlyLists.CopyOrEmpty(lockedIndexes);
        }

        // TryCreate가 쓰는 검사다. 복사는 생성자가 한 번만 하므로 여기서는 판정만 한다.
        // 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(int? capacity, IReadOnlyList<ItemPositionState>? entries, IReadOnlyList<int>? lockedIndexes)
        {
            if (capacity < 0) return false;
            if (!ReadOnlyLists.AreUnique(entries, state => state.Position)) return false;
            if (!ReadOnlyLists.AreUnique(lockedIndexes, index => index)) return false;
            if (capacity.HasValue)
                foreach (ItemPositionState entry in entries)
                    if (entry.Position >= capacity.Value)
                        return false;
            foreach (int index in lockedIndexes)
                if (index < 0 || capacity.HasValue && index >= capacity.Value)
                    return false;
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(int? capacity, IReadOnlyList<ItemPositionState> entries, IReadOnlyList<int> lockedIndexes,
            [NotNullWhen(true)] out ItemContainerState? value)
        {
            value = Check(capacity, entries, lockedIndexes) ? new ItemContainerState(capacity, entries, lockedIndexes) : null;
            return value != null;
        }

        public int? Capacity { get; }
        public IReadOnlyList<ItemPositionState> Entries { get; }
        public IReadOnlyList<int> LockedIndexes { get; }
    }
}
