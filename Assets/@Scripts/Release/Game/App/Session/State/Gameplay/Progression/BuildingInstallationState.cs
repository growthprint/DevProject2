#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 건물 설치는 진행 레벨과 구별하여 고유 ID와 원본 격자 좌표를 보관한다.
    public sealed class BuildingInstallationState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public BuildingInstallationState(int instanceId, string originalBuildingId, string areaId, int x, int y, int rotation)
        {
            InstanceId = instanceId; OriginalBuildingId = originalBuildingId; AreaId = areaId; X = x; Y = y; Rotation = rotation;
        }

        // TryCreate가 쓰는 검사다. 회전은 원본처럼 0에서 3까지다.
        // 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(int instanceId, string originalBuildingId, string areaId, int rotation)
        {
            if (instanceId < 0) return false;
            if (string.IsNullOrWhiteSpace(originalBuildingId))
                return false;
            if (string.IsNullOrWhiteSpace(areaId)) return false;
            if (rotation < 0 || rotation > 3) return false;
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(int instanceId, string originalBuildingId, string areaId, int x, int y, int rotation,
            [NotNullWhen(true)] out BuildingInstallationState? value)
        {
            value = Check(instanceId, originalBuildingId, areaId, rotation)
                ? new BuildingInstallationState(instanceId, originalBuildingId, areaId, x, y, rotation) : null;
            return value != null;
        }
        public int InstanceId { get; }
        public string OriginalBuildingId { get; }
        public string AreaId { get; }
        public int X { get; }
        public int Y { get; }
        public int Rotation { get; }
    }
}
