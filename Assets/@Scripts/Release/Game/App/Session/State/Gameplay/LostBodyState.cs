#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 사망 때 남긴 시체다. 아이템은 사망 당시 소유 트리 전체를 기록으로 보관하고, 첫 상호작용 여부는 따로 보존한다.
    public sealed class LostBodyState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public LostBodyState(uint raidId, string subSceneId, float x, float y, float z, bool valid,
            bool spawned, bool touched, ItemOwnershipState items)
        {
            RaidId = raidId; SubSceneId = subSceneId; X = x; Y = y; Z = z; Valid = valid; Spawned = spawned; Touched = touched;
            Items = items;
        }

        // TryCreate가 쓰는 검사다. 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(string subSceneId, float x, float y, float z, ItemOwnershipState items)
        {
            if (string.IsNullOrWhiteSpace(subSceneId)) return false;
            if (!float.IsFinite(x)) return false;
            if (!float.IsFinite(y)) return false;
            if (!float.IsFinite(z)) return false;
            if (items == null) return false;
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(uint raidId, string subSceneId, float x, float y, float z, bool valid,
            bool spawned, bool touched, ItemOwnershipState items, [NotNullWhen(true)] out LostBodyState? value)
        {
            value = Check(subSceneId, x, y, z, items)
                ? new LostBodyState(raidId, subSceneId, x, y, z, valid, spawned, touched, items) : null;
            return value != null;
        }
        public uint RaidId { get; }
        public string SubSceneId { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public bool Valid { get; }
        public bool Spawned { get; }
        public bool Touched { get; }
        public ItemOwnershipState Items { get; }
    }
}
