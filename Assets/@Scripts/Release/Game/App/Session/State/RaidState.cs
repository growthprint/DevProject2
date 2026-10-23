#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 진행 중인 출격 하나의 상태다.
    public sealed class RaidState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public RaidState(long sequence, string raidId, RaidResult? result)
        {
            Sequence = sequence;
            RaidId = raidId;
            Result = result;
        }

        // TryCreate가 쓰는 검사다. 순번은 1 이상이고, 결과가 있으면 순번과 출격 ID가 이 출격과 같아야 한다.
        static bool Check(long sequence, string? raidId, RaidResult? result) =>
            sequence >= 1 && !string.IsNullOrWhiteSpace(raidId) &&
            (result == null || (result.Sequence == sequence && StringComparer.Ordinal.Equals(result.RaidId, raidId)));

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려준다.
        public static bool TryCreate(long sequence, string raidId, RaidResult? result, [NotNullWhen(true)] out RaidState? value)
        {
            value = Check(sequence, raidId, result) ? new RaidState(sequence, raidId, result) : null;
            return value != null;
        }

        public long Sequence { get; }
        public string RaidId { get; }
        // 레거시 저장에서 읽은 정산 대기 결과다. 새 출격은 늘 null이고, 이어하기가 세션을 만들기 전에 정산한다.
        public RaidResult? Result { get; }
    }
}
