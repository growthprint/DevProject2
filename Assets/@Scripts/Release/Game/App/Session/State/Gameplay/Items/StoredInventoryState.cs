#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 창고와 배송 대기함 등 독립 저장 인벤토리를 원본 키로 구분한다.
    public sealed class StoredInventoryState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public StoredInventoryState(string key, ItemContainerState inventory)
        {
            Key = key; Inventory = inventory;
        }

        // TryCreate가 쓰는 검사다. 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(string key, ItemContainerState inventory)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (inventory == null) return false;
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(string key, ItemContainerState inventory, [NotNullWhen(true)] out StoredInventoryState? value)
        {
            value = Check(key, inventory) ? new StoredInventoryState(key, inventory) : null;
            return value != null;
        }

        public string Key { get; }
        public ItemContainerState Inventory { get; }
    }
}
