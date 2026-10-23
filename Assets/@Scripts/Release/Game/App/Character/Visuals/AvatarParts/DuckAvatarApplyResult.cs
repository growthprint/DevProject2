#nullable enable

using System;
using System.Collections.Generic;
using Game.Core;

namespace DuckCharacter
{
    // 외형 적용의 불변 결과다. 거부 사유는 싣지 않는다. 결함은 감지한 곳이 Log로 한 번 보고하고, 재진입이나 해제 뒤 호출
    // 같은 규칙 거부는 기록하지 않는다. CleanupErrors는 적용 뒤 정리 경고의 읽기 전용 목록이고, 거부 결과에서는 비어 있다.
    public sealed class DuckAvatarApplyResult
    {
        public bool IsApplied { get; }
        public IReadOnlyList<Exception> CleanupErrors { get; }

        // 거부 결과에 정리 목록이 붙지 않도록 아래 팩토리 메서드로만 만든다.
        DuckAvatarApplyResult(bool applied, IReadOnlyList<Exception> cleanupErrors)
        {
            IsApplied = applied;
            CleanupErrors = cleanupErrors;
        }

        // 아무것도 바꾸기 전에 거부한 적용에 쓴다.
        internal static DuckAvatarApplyResult Reject() =>
            new DuckAvatarApplyResult(false, Array.Empty<Exception>());

        // 정리 경고가 있어도 외형은 확정됐으므로 성공으로 만든다. 받는 목록은 적용 하나가 만들고 다시 쓰지 않는 지역 목록이라
        // 복사하지 않고 읽기 전용으로 감싸며, 경고가 없으면 공유 빈 배열을 쓴다.
        internal static DuckAvatarApplyResult Commit(List<Exception> cleanupErrors) =>
            new DuckAvatarApplyResult(true, cleanupErrors.Count == 0
                ? (IReadOnlyList<Exception>)Array.Empty<Exception>() : cleanupErrors.AsReadOnly());

        // 미리보기, 게임 캐릭터, 적 같은 외형 호스트가 "label: 집계 예외" 형식으로 한 번 기록한다. 적용 뒤 정리 경고는
        // 제품 실패가 아니라 Warning이다. 적용 중 예외의 롤백 복구 실패는 외형 적용이 예외와 함께 따로 기록한다.
        public void ReportCleanupWarnings(UnityEngine.Object context, string label)
        {
            if (CleanupErrors.Count == 0) return;
            Log.Instance.Warning(label + ": " + new AggregateException(CleanupErrors), context);
        }
    }
}
