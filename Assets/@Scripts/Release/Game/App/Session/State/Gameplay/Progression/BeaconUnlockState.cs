#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 비콘 해금은 원본 ID와 같은 지역 안의 비콘 인덱스 쌍을 보존한다.
    public sealed class BeaconUnlockState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public BeaconUnlockState(string beaconId, int beaconIndex)
        {
            BeaconId = beaconId; BeaconIndex = beaconIndex;
        }

        // TryCreate가 쓰는 검사다. 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(string beaconId, int beaconIndex)
        {
            if (string.IsNullOrWhiteSpace(beaconId)) return false;
            if (beaconIndex < 0) return false;
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(string beaconId, int beaconIndex, [NotNullWhen(true)] out BeaconUnlockState? value)
        {
            value = Check(beaconId, beaconIndex) ? new BeaconUnlockState(beaconId, beaconIndex) : null;
            return value != null;
        }
        public string BeaconId { get; }
        public int BeaconIndex { get; }
    }
}
