#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원작 PerkTree_<id> 저장 하나다. 원작 perkTreeID와 그 트리의 특성 줄 목록을 담는다.
    // 정의에 없는 트리나 특성도 지우지 않고 그대로 보존한다. Why: 데이터가 바뀌어도 기록을 잃지 않는다.
    public sealed class PerkTreeSaveState
    {
        // 믿을 수 있는 입력용 생성자다. 목록은 한 번 복사한다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public PerkTreeSaveState(string treeId, IReadOnlyList<PerkSaveEntry> perks)
        {
            TreeId = treeId; Perks = ReadOnlyLists.CopyOrEmpty(perks);
        }

        // 믿을 수 없는 입력용 생성 경로다. 빈 트리 ID, null 항목, 트리 안의 특성 이름 중복을 거부한다.
        // 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(string treeId, IReadOnlyList<PerkSaveEntry> perks, [NotNullWhen(true)] out PerkTreeSaveState? value)
        {
            value = !string.IsNullOrWhiteSpace(treeId) && ReadOnlyLists.AreUnique(perks, perk => perk.Name)
                ? new PerkTreeSaveState(treeId, perks) : null;
            return value != null;
        }

        public string TreeId { get; }
        public IReadOnlyList<PerkSaveEntry> Perks { get; }
    }
}
