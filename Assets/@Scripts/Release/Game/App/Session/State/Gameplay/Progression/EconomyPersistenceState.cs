#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 은행 잔고는 현금 아이템(원본 ID 451)과 합치지 않고 따로 보관한다. 아이템 해금 목록과 해금 확인 대기 목록도 보존한다.
    public sealed class EconomyPersistenceState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓰고, 새 게임의 0원 경제는 GameplayPersistenceState.NewGame이 만든다.
        // 두 ID 목록은 따로 한 번 복사하며 null 목록은 빈 목록으로 둔다.
        public EconomyPersistenceState(long money, IReadOnlyList<int> unlockedItems, IReadOnlyList<int> awaitingConfirmation)
        {
            Money = money; UnlockedItems = ReadOnlyLists.CopyOrEmpty(unlockedItems);
            AwaitingConfirmation = ReadOnlyLists.CopyOrEmpty(awaitingConfirmation);
        }

        // TryCreate가 쓰는 검사다. 해금 완료와 확인 대기 목록은 각각 음수와 중복이 없고, 서로 겹치지 않아야 한다.
        // 판정만 하고 복사하지 않으며, 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(long money, IReadOnlyList<int>? unlockedItems, IReadOnlyList<int>? awaitingConfirmation)
        {
            if (money < 0) return false;
            // 겹침 검사에 쓸 집합을 만들면서 음수와 중복도 거른다. 집합을 두 번 만들지 않으려고 직접 돌며, 판정은 SaveKeys.AreValidIds와 같다.
            if (unlockedItems == null) return false;
            var known = new HashSet<int>();
            for (int index = 0; index < unlockedItems.Count; index++)
            {
                int unlocked = unlockedItems[index];
                if (!known.Add(unlocked) || unlocked < 0) return false;
            }
            if (!SaveKeys.AreValidIds(awaitingConfirmation))
                return false;
            foreach (int id in awaitingConfirmation)
                if (known.Contains(id))
                    return false;
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(long money, IReadOnlyList<int> unlockedItems, IReadOnlyList<int> awaitingConfirmation,
            [NotNullWhen(true)] out EconomyPersistenceState? value)
        {
            value = Check(money, unlockedItems, awaitingConfirmation)
                ? new EconomyPersistenceState(money, unlockedItems, awaitingConfirmation) : null;
            return value != null;
        }
        public long Money { get; }
        public IReadOnlyList<int> UnlockedItems { get; }
        public IReadOnlyList<int> AwaitingConfirmation { get; }
    }
}
