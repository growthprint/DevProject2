#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 일반 상점의 재고는 원본 아이템 정의별 잔량이다.
    public sealed class ShopStockState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public ShopStockState(int originalItemId, int stock)
        {
            OriginalItemId = originalItemId; Stock = stock;
        }

        // TryCreate가 쓰는 검사다. 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(int originalItemId, int stock) => originalItemId >= 0 && stock >= 0;

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(int originalItemId, int stock, [NotNullWhen(true)] out ShopStockState? value)
        {
            value = Check(originalItemId, stock) ? new ShopStockState(originalItemId, stock) : null;
            return value != null;
        }
        public int OriginalItemId { get; }
        public int Stock { get; }
    }
}
