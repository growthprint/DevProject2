#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원본 여섯 퀵슬롯은 메인 인벤토리 위치를 저장하고 -1은 빈 칸으로 보존한다.
    public sealed class ItemShortcutState
    {
        // 원본 퀵슬롯 칸 수다.
        internal const int _slotCount = 6;
        // 빈 퀵슬롯 칸 값이다. 저장 파일에 들어가는 값이라 바꾸면 안 된다.
        internal const int _emptyIndex = -1;

        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        // 목록은 따로 한 번 복사하고, null 목록은 빈 목록으로 둔다.
        public ItemShortcutState(IReadOnlyList<int> inventoryIndexes)
        {
            InventoryIndexes = ReadOnlyLists.CopyOrEmpty(inventoryIndexes);
        }

        // TryCreate가 쓰는 검사다. 복사는 생성자가 한 번만 하므로 여기서는 판정만 한다.
        // 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(IReadOnlyList<int>? inventoryIndexes)
        {
            if (inventoryIndexes == null || inventoryIndexes.Count != _slotCount)
                return false;
            var occupied = new HashSet<int>();
            for (int index = 0; index < _slotCount; index++)
            {
                int position = inventoryIndexes[index];
                // 빈 칸은 여러 개여도 되므로 0 이상의 위치만 중복을 검사한다.
                if (position < _emptyIndex || position >= 0 && !occupied.Add(position))
                    return false;
            }
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(IReadOnlyList<int> inventoryIndexes, [NotNullWhen(true)] out ItemShortcutState? value)
        {
            value = Check(inventoryIndexes) ? new ItemShortcutState(inventoryIndexes) : null;
            return value != null;
        }
        public IReadOnlyList<int> InventoryIndexes { get; }
        // 모든 칸이 빈(-1) 퀵슬롯이다.
        public static ItemShortcutState Empty() => new ItemShortcutState(EmptyIndexes());
        // 빈 퀵슬롯 인덱스 배열을 새로 만든다(호출자가 채워 넣는 작업 배열로도 쓴다).
        internal static int[] EmptyIndexes()
        {
            var indexes = new int[_slotCount];
            for (int index = 0; index < _slotCount; index++) indexes[index] = _emptyIndex;
            return indexes;
        }
    }
}
