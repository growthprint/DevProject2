#nullable enable annotations
using System;

namespace Game.App
{
    // 원본 GameClockDisplay처럼 시계를 hh:mm으로 표시한다. 시각을 아직 포착하지 않은 시계의 표시 기본값은 이 위젯이 정하므로,
    // 소유자들이 그 값을 되풀이하지 않는다. 프리팹 참조는 SharedHudView의 직렬화 필드를 직접 읽는다.
    internal sealed class HudClockDisplay
    {
        readonly SharedHudView _hud;
        // 시계 문구 hh:mm을 문자열 할당 없이 쓰는 재사용 글자 배열이다.
        readonly char[] _chars = new char[5];
        // 분이 바뀐 프레임에만 문구를 쓰도록 마지막 표시 분을 기억한다. 시작값이면 다음 호출이 반드시 쓴다.
        int _shownMinutes = int.MinValue;

        internal HudClockDisplay(SharedHudView hud) => _hud = hud;

        internal void Reset() => _shownMinutes = int.MinValue;

        internal void Show(double? secondsOfDay)
        {
            // secondsOfDay는 Unity 객체가 아닌 double?이라 ??를 써도 된다.
            double seconds = secondsOfDay ?? GameplayClockState._defaultSecondsOfDay;
            int minutes = (int)(seconds / 60d);
            if (minutes == _shownMinutes) return;
            _shownMinutes = minutes;
            // 문자열을 만들지 않도록 시와 분을 두 자리씩 재사용 배열에 써서 SetCharArray로 넘긴다.
            // hh:mm 형식처럼 부호 없이 0~23시와 분을 표시하려고 절댓값을 쓴다.
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            UiDigits.Write(_chars, 0, Math.Abs(time.Hours), 2);
            _chars[2] = ':';
            UiDigits.Write(_chars, 3, Math.Abs(time.Minutes), 2);
            _hud._clock.SetCharArray(_chars, 0, _chars.Length);
        }
    }
}
