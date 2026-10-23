#nullable enable
using System;

namespace Game.App
{
    // 출격, 아이템 인스턴스, 드롭 루트처럼 저장 기록에 남는 식별자를 만든다. 모든 곳이 같은 형식을 쓰도록 한 곳에 둔다.
    internal static class PersistentIds
    {
        // 하이픈 없는 32자리 16진 GUID 문자열이다. 저장 파일에 남으므로 형식을 바꾸지 않는다.
        internal static string New() => Guid.NewGuid().ToString("N");
    }
}
