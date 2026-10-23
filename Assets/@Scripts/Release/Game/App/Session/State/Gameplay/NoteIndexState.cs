#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원작 NoteIndex의 저장 값처럼 해금한 쪽지와 읽은 쪽지의 키 목록이다. 슬롯의 캠페인과 함께 저장되는 불변 값이다.
    public sealed class NoteIndexState
    {
        // 새 게임과 이 필드가 없는 저장의 값이다.
        public static NoteIndexState Empty { get; } = new NoteIndexState(Array.Empty<string>(), Array.Empty<string>());

        public IReadOnlyList<string> Unlocked { get; }
        public IReadOnlyList<string> Read { get; }

        // 신뢰할 수 있는 입력용 생성자이며 유일한 대입 경로다. 목록은 여기서 한 번 복사한다. 신뢰할 수 없는 입력은 TryCreate를 쓴다.
        public NoteIndexState(IReadOnlyList<string> unlocked, IReadOnlyList<string> read)
        {
            Unlocked = ReadOnlyLists.CopyOrEmpty(unlocked);
            Read = ReadOnlyLists.CopyOrEmpty(read);
        }

        // 저장 파일처럼 신뢰할 수 없는 입력용 생성 경로다. 두 목록 모두 빈 키와 중복을 거부하고, 실패해도 던지지 않는다.
        public static bool TryCreate(IReadOnlyList<string> unlocked, IReadOnlyList<string> read, [NotNullWhen(true)] out NoteIndexState? value)
        {
            value = SaveKeys.AreValid(unlocked) && SaveKeys.AreValid(read) ? new NoteIndexState(unlocked, read) : null;
            return value != null;
        }
    }
}
