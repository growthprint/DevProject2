#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 쪽지, 부서진 벽, 건설 지점처럼 원작 저장 키를 목록으로 담는 필드의 공통 규칙이다. 빈 키와 중복 키를 거부한다.
    internal static class SaveKeys
    {
        internal static bool AreValid([NotNullWhen(true)] IReadOnlyList<string>? keys) =>
            ReadOnlyLists.AreUnique(keys, key => key, key => !string.IsNullOrWhiteSpace(key));

        // 열쇠, 퀘스트, 보상, 해금 아이템처럼 원작 정수 ID를 목록으로 담는 필드의 공통 규칙이다. 음수 ID와 중복 ID를 거부한다.
        internal static bool AreValidIds([NotNullWhen(true)] IReadOnlyList<int>? ids) =>
            ReadOnlyLists.AreUnique(ids, id => id, id => id >= 0);
    }
}
