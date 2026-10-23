#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원본 슬롯 키와 자식 아이템의 영속 식별자를 연결한다.
    public sealed class ItemSlotState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public ItemSlotState(string key, string instanceId)
        {
            Key = key; InstanceId = instanceId;
        }

        // TryCreate가 쓰는 검사다. 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(string key, string instanceId)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (string.IsNullOrWhiteSpace(instanceId)) return false;
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(string key, string instanceId, [NotNullWhen(true)] out ItemSlotState? value)
        {
            value = Check(key, instanceId) ? new ItemSlotState(key, instanceId) : null;
            return value != null;
        }

        public string Key { get; }
        public string InstanceId { get; }
    }
}
