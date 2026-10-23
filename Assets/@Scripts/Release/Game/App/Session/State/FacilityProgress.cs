#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 시설 정의 ID와 건설 단계를 저장한다.
    public sealed class FacilityProgress
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public FacilityProgress(string originalFacilityId, int level)
        {
            OriginalFacilityId = originalFacilityId;
            Level = level;
        }

        // TryCreate가 쓰는 검사다. 시설 ID가 있고 단계는 0 이상이어야 한다.
        static bool Check(string? originalFacilityId, int level) => !string.IsNullOrWhiteSpace(originalFacilityId) && level >= 0;

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려준다.
        public static bool TryCreate(string originalFacilityId, int level, [NotNullWhen(true)] out FacilityProgress? value)
        {
            value = Check(originalFacilityId, level) ? new FacilityProgress(originalFacilityId, level) : null;
            return value != null;
        }

        public string OriginalFacilityId { get; }
        public int Level { get; }
    }
}
