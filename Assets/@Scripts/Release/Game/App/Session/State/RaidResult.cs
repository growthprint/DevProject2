#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    public enum RaidOutcome { Extracted = 0, Died = 1 }

    // 결과만 기록되고 정산되지 않은 레거시 저장의 출격 결과다. 지금은 출격이 끝나면 정산된 기지 상태를 바로 저장하므로
    // 새로 만들지 않고, 저장 읽기와 이어하기 정산(PrepareContinueSnapshot)에만 쓴다.
    public sealed class RaidResult
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public RaidResult(long sequence, string raidId, RaidOutcome outcome, PlayerState? player, GameplayPersistenceState? gameplay = null)
        {
            Sequence = sequence;
            RaidId = raidId;
            Outcome = outcome;
            Player = player;
            Gameplay = gameplay;
        }

        // TryCreate가 쓰는 검사다. 순번은 1 이상이고 출격 ID가 있어야 하며, 결과는 정의된 값이어야 한다.
        static bool Check(long sequence, string? raidId, RaidOutcome outcome) =>
            sequence >= 1 && !string.IsNullOrWhiteSpace(raidId) && outcome >= RaidOutcome.Extracted && outcome <= RaidOutcome.Died;

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려준다.
        // 선택 인수는 out 인수 앞에 둘 수 없어 gameplay를 필수로 받는다.
        public static bool TryCreate(long sequence, string raidId, RaidOutcome outcome, PlayerState? player,
            GameplayPersistenceState? gameplay, [NotNullWhen(true)] out RaidResult? value)
        {
            value = Check(sequence, raidId, outcome) ? new RaidResult(sequence, raidId, outcome, player, gameplay) : null;
            return value != null;
        }

        public long Sequence { get; }
        public string RaidId { get; }
        public RaidOutcome Outcome { get; }
        // 정산 뒤 포착한 생존 수치다. null이면 정산할 때 캠페인의 현재 값을 그대로 둔다.
        public PlayerState? Player { get; }
        // 플레이 상태 전체를 포착하지 않은 저장 형식이면 null이다. 이때 정산은 캠페인의 현재 플레이 상태를 그대로 둔다.
        public GameplayPersistenceState? Gameplay { get; }
    }
}
