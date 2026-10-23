#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 건물 설치와 반환 토큰을 묶는다. 빈 목록은 설치한 건물이 없다고 확인된 상태이고, 포착하지 못한 경우는 이 묶음 자체를 null로 둔다.
    public sealed class BuildingPersistenceState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        // 목록은 따로 한 번 복사하고, null 목록은 빈 목록으로 둔다.
        public BuildingPersistenceState(IReadOnlyList<BuildingInstallationState> installations, IReadOnlyList<BuildingTokenState> tokens)
        {
            Installations = ReadOnlyLists.CopyOrEmpty(installations); Tokens = ReadOnlyLists.CopyOrEmpty(tokens);
        }

        // TryCreate가 쓰는 검사다. 복사는 생성자가 한 번만 하므로 여기서는 판정만 한다.
        // 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(IReadOnlyList<BuildingInstallationState>? installations, IReadOnlyList<BuildingTokenState>? tokens)
        {
            if (!ReadOnlyLists.AreUnique(installations, state => state.InstanceId)) return false;
            return ReadOnlyLists.AreUnique(tokens, state => state.OriginalBuildingId);
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(IReadOnlyList<BuildingInstallationState> installations, IReadOnlyList<BuildingTokenState> tokens,
            [NotNullWhen(true)] out BuildingPersistenceState? value)
        {
            value = Check(installations, tokens) ? new BuildingPersistenceState(installations, tokens) : null;
            return value != null;
        }
        public IReadOnlyList<BuildingInstallationState> Installations { get; }
        public IReadOnlyList<BuildingTokenState> Tokens { get; }
    }
}
