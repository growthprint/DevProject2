#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원작 SavesSystem.SaveGlobal로 슬롯 밖 공통 파일에 남는 값이다(사용자 결정 D2). 재능 해금과 분노 난이도 해금을 담는다.
    // 슬롯 삭제, 새 게임, 슬롯 백업 복구와 상관없이 남는다. 불변 값이고 현재 값은 GlobalProgress가 들고 있다.
    public sealed class GlobalProgressState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다. 목록은 따로 한 번 복사한다.
        public GlobalProgressState(IReadOnlyList<EndowmentIndex> endowmentUnlocks, bool rageUnlocked)
        {
            EndowmentUnlocks = ReadOnlyLists.CopyOrEmpty(endowmentUnlocks);
            RageUnlocked = rageUnlocked;
        }

        // TryCreate가 쓰는 검사다. 목록 누락, 중복, 원작 재능 네 개 밖의 값(None 포함)을 거부한다.
        // 판정만 하고 복사하지 않으며, 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(IReadOnlyList<EndowmentIndex>? endowmentUnlocks) =>
            ReadOnlyLists.AreUnique(endowmentUnlocks, index => index,
                index => index > EndowmentIndex.None && index <= EndowmentIndex.Marksman);

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(IReadOnlyList<EndowmentIndex> endowmentUnlocks, bool rageUnlocked,
            [NotNullWhen(true)] out GlobalProgressState? value)
        {
            value = Check(endowmentUnlocks) ? new GlobalProgressState(endowmentUnlocks, rageUnlocked) : null;
            return value != null;
        }

        // 파일이 없는 첫 실행과 읽지 못한 파일의 값이다. 모두 잠겨 있다.
        public static GlobalProgressState Empty { get; } = new GlobalProgressState(Array.Empty<EndowmentIndex>(), false);

        // 원작 Endowment_Unlock_R_<이름>이 참인 재능이며 해금한 순서다. 기본 해금 재능은 정의가 정하므로 여기에 없다.
        public IReadOnlyList<EndowmentIndex> EndowmentUnlocks { get; }
        // 원작 Difficulty/RageUnlocked다.
        public bool RageUnlocked { get; }

        // 재능은 네 개뿐이라 색인 없이 훑는다.
        internal bool IsEndowmentUnlocked(EndowmentIndex index)
        {
            for (int position = 0; position < EndowmentUnlocks.Count; position++)
                if (EndowmentUnlocks[position] == index) return true;
            return false;
        }
    }
}
