#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원작 PerkTree_<id> 저장의 특성 한 줄이다. 원작 특성 GameObject 이름, 해금, 연구 중 여부, 연구 시작 UTC 틱을 보존한다.
    public sealed class PerkSaveEntry
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public PerkSaveEntry(string name, bool unlocked, bool unlocking, long beginUtcTicks)
        {
            Name = name; Unlocked = unlocked; Unlocking = unlocking; BeginUtcTicks = beginUtcTicks;
        }

        // TryCreate가 쓰는 검사다. 해금된 특성은 다시 연구하지 않으므로 둘 다 참인 값은 손상이다. 틱은 DateTime이 나타낼 수 있는 범위만 받는다.
        // 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(string name, bool unlocked, bool unlocking, long beginUtcTicks)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (unlocked && unlocking) return false;
            return beginUtcTicks >= 0L && beginUtcTicks <= DateTime.MaxValue.Ticks;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(string name, bool unlocked, bool unlocking, long beginUtcTicks,
            [NotNullWhen(true)] out PerkSaveEntry? value)
        {
            value = Check(name, unlocked, unlocking, beginUtcTicks) ? new PerkSaveEntry(name, unlocked, unlocking, beginUtcTicks) : null;
            return value != null;
        }

        public string Name { get; }
        public bool Unlocked { get; }
        public bool Unlocking { get; }
        public long BeginUtcTicks { get; }
    }
}
