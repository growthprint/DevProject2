#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 암시장 수요나 공급 한 항목의 잔량, 가격 배율, 묶음 수를 보관한다.
    public sealed class MarketEntryState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public MarketEntryState(int originalItemId, int remaining, float priceFactor, int batchCount)
        {
            OriginalItemId = originalItemId; Remaining = remaining; PriceFactor = priceFactor; BatchCount = batchCount;
        }

        // TryCreate가 쓰는 검사다. 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(int originalItemId, int remaining, float priceFactor, int batchCount)
        {
            if (originalItemId < 0 || remaining < 0 || batchCount < 1)
                return false;
            // 가격 배율은 다른 상태 수치와 같은 규칙으로 NaN, 무한대, 음수를 거부한다.
            if (!StateValues.IsFiniteNonNegative(priceFactor))
                return false;
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(int originalItemId, int remaining, float priceFactor, int batchCount,
            [NotNullWhen(true)] out MarketEntryState? value)
        {
            value = Check(originalItemId, remaining, priceFactor, batchCount)
                ? new MarketEntryState(originalItemId, remaining, priceFactor, batchCount) : null;
            return value != null;
        }
        public int OriginalItemId { get; }
        public int Remaining { get; }
        public float PriceFactor { get; }
        public int BatchCount { get; }
    }
}
