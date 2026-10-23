#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 인벤토리의 빈 칸을 압축하지 않고 원래 위치를 보관한다.
    public sealed class ItemPositionState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public ItemPositionState(int position, string instanceId)
        {
            Position = position; InstanceId = instanceId;
        }

        // TryCreate가 쓰는 검사다. 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(int position, string instanceId)
        {
            if (position < 0) return false;
            if (string.IsNullOrWhiteSpace(instanceId)) return false;
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(int position, string instanceId, [NotNullWhen(true)] out ItemPositionState? value)
        {
            value = Check(position, instanceId) ? new ItemPositionState(position, instanceId) : null;
            return value != null;
        }

        public int Position { get; }
        public string InstanceId { get; }
    }
}
