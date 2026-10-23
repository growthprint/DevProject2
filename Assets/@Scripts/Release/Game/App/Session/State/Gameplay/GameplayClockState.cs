#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 플레이 시간과 원본 월드 시계를 보관한다. 기록되지 않은 과거 플레이 시간은 추정하지 않고 null로 둔다.
    public sealed class GameplayClockState
    {
        // 원본 GameClock 하루 길이(초)다. 정확히 이 값인 시각은 다음 날로 넘기지 않는다.
        internal const double _dayLengthSeconds = 86300;
        // 시각을 아직 포착하지 않은 새 게임의 원본 시작 시각(오전 8시, 초)이다.
        internal const double _defaultSecondsOfDay = 28800;
        // 원본 월드 시계는 실제 시간의 60배로 흐른다.
        internal const double _worldTimeScale = 60;
        // 시각(초)과 시(時)를 바꿀 때 쓰는 한 시간의 초다.
        internal const double _secondsPerHour = 3600d;

        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓰고, 새 게임 값은 GameplayPersistenceState.NewGame이 정한다.
        public GameplayClockState(long recordedPlayTicks, long? historicalPlayTicks, long? days, double? secondsOfDay)
        {
            RecordedPlayTicks = recordedPlayTicks;
            HistoricalPlayTicks = historicalPlayTicks;
            Days = days;
            SecondsOfDay = secondsOfDay;
        }

        // TryCreate가 쓰는 검사다. 날짜와 시각은 함께 있거나 함께 없어야 한다.
        // 어떤 입력에도 던지거나 기록하지 않고 false만 돌려준다.
        static bool Check(long recordedPlayTicks, long? historicalPlayTicks, long? days, double? secondsOfDay)
        {
            if (recordedPlayTicks < 0) return false;
            if (historicalPlayTicks < 0)
                return false;
            if (days < 0) return false;
            if (days.HasValue != secondsOfDay.HasValue)
                return false;
            // 무한대는 범위 비교에서 걸리므로 NaN만 따로 검사한다.
            if (secondsOfDay is double seconds && (double.IsNaN(seconds) || seconds < 0 || seconds > _dayLengthSeconds))
                return false;
            // ElapsedSeconds가 두 시간을 더하므로 합이 long 범위를 넘는 값을 거절한다. 두 값 모두 음수가 아니라 이 비교로 충분하다.
            if (historicalPlayTicks.HasValue && historicalPlayTicks.Value > long.MaxValue - recordedPlayTicks)
                return false;
            return true;
        }

        // 믿을 수 없는 입력용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(long recordedPlayTicks, long? historicalPlayTicks, long? days, double? secondsOfDay,
            [NotNullWhen(true)] out GameplayClockState? value)
        {
            value = Check(recordedPlayTicks, historicalPlayTicks, days, secondsOfDay)
                ? new GameplayClockState(recordedPlayTicks, historicalPlayTicks, days, secondsOfDay) : null;
            return value != null;
        }
        public long RecordedPlayTicks { get; }
        public long? HistoricalPlayTicks { get; }
        public long? Days { get; }
        public double? SecondsOfDay { get; }
        // 시각을 아직 포착하지 않은 새 게임은 원본 시작 시각으로 읽는다. 시계, 날씨, 수면 화면이 같은 기본값을 쓰게 한다.
        internal double SecondsOfDayOrDefault() => SecondsOfDay ?? _defaultSecondsOfDay;
        public double? ElapsedSeconds => HistoricalPlayTicks.HasValue ?
            (RecordedPlayTicks + HistoricalPlayTicks.Value) / (double)TimeSpan.TicksPerSecond : (double?)null;
    }
}
