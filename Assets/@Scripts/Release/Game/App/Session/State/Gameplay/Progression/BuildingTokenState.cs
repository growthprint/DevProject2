#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 반환 건물 토큰은 원본 정의별 수량이며 설치된 건물과 별도로 보존한다.
    public sealed class BuildingTokenState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public BuildingTokenState(string originalBuildingId, int amount)
        {
            OriginalBuildingId = originalBuildingId; Amount = amount;
        }

        // TryCreate가 쓰는 검사다. 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(string originalBuildingId, int amount)
        {
            if (string.IsNullOrWhiteSpace(originalBuildingId))
                return false;
            if (amount < 0) return false;
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(string originalBuildingId, int amount, [NotNullWhen(true)] out BuildingTokenState? value)
        {
            value = Check(originalBuildingId, amount) ? new BuildingTokenState(originalBuildingId, amount) : null;
            return value != null;
        }
        public string OriginalBuildingId { get; }
        public int Amount { get; }
    }
}
