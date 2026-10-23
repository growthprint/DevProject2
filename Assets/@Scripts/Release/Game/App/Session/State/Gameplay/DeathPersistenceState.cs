#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 출격 중 남긴 시체 목록과 마지막 사망 캐릭터의 기록, 그 토큰을 묶는다. 각 필드의 null은 미포착이다.
    // 사망 기록 그래프는 현재 소유 그래프와 따로 검증하는 독립된 그래프다.
    public sealed class DeathPersistenceState
    {
        // 믿을 수 있는 입력용 생성자다. 생략한 인자는 미포착(null)이다.
        // null이 아닌 시체 목록은 따로 한 번 복사한다. 저장 로드는 모든 인자가 필수인 TryCreate를 쓴다.
        public DeathPersistenceState(IReadOnlyList<LostBodyState>? lostBodies = null, ItemOwnershipState? lastDeadCharacter = null,
            uint? deadCharacterToken = null)
        {
            LostBodies = ReadOnlyLists.CopyOrNull(lostBodies); LastDeadCharacter = lastDeadCharacter; DeadCharacterToken = deadCharacterToken;
        }

        // TryCreate가 쓰는 검사다. null 시체 목록은 미포착이라 통과하고, 사망 기록이 있으면 토큰도 있어야 한다.
        // 판정만 하고 복사하지 않으며, 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(IReadOnlyList<LostBodyState>? lostBodies, ItemOwnershipState? lastDeadCharacter, uint? deadCharacterToken)
        {
            if (lostBodies != null && !ReadOnlyLists.AreUnique(lostBodies, body => body.RaidId)) return false;
            if (lastDeadCharacter != null && !deadCharacterToken.HasValue)
                return false;
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(IReadOnlyList<LostBodyState>? lostBodies, ItemOwnershipState? lastDeadCharacter,
            uint? deadCharacterToken, [NotNullWhen(true)] out DeathPersistenceState? value)
        {
            value = Check(lostBodies, lastDeadCharacter, deadCharacterToken)
                ? new DeathPersistenceState(lostBodies, lastDeadCharacter, deadCharacterToken) : null;
            return value != null;
        }

        public IReadOnlyList<LostBodyState>? LostBodies { get; }
        public ItemOwnershipState? LastDeadCharacter { get; }
        public uint? DeadCharacterToken { get; }
    }
}
