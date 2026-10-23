#nullable enable annotations
using System;
using UnityEngine;

namespace Game.App
{
    // 원본 TimeOfDayDisplay의 단계 이름, 날씨 이름, 폭풍 제목과 남은 시간, 막대, Grow 지시기, 기지 전용 설명을 표시한다.
    // 값과 번역 키는 월드의 시간대 볼륨(OriginalTimeOfDay)이 0.5초마다 넘기고, 이 위젯은 바뀐 것만 쓴다. 프리팹 참조는 SharedHudView의
    // 직렬화 필드를 직접 읽으며 제작 도구(Author Shared HUD Storm Display)가 연결하고 SharedHudTests가 확인하므로 검사하지 않는다.
    internal sealed class HudStormDisplay
    {
        // 원본 지시기 Animator의 bool 이름이다.
        static readonly int _grow = Animator.StringToHash("Grow");

        readonly SharedHudView _hud;
        // 원본 "{총 시간:000}:{분:00}" 문구를 문자열 할당 없이 쓰는 재사용 글자 배열이다. 부호, 넘친 시간 자릿수, 콜론, 분이 들어간다.
        readonly char[] _chars = new char[24];
        // 마지막으로 쓴 번역 키와 값이다. 시작값이면 다음 Show가 반드시 쓴다. 키가 같으면 번역 문자열을 다시 만들지 않는다.
        string? _phaseKey, _weatherKey, _titleKey;
        long _shownHours = long.MinValue;
        int _shownMinutes = int.MinValue;
        float _shownFill = float.NaN;

        internal HudStormDisplay(SharedHudView hud) => _hud = hud;

        // HUD를 다른 소유자에게 넘기기 전 상태로 되돌린다. 시간대 볼륨이 없는 씬에 앞 월드의 폭풍 문구가 남지 않게 폭풍 루트를 끈다.
        internal void Reset()
        {
            Forget();
            _hud._stormRoot.SetActive(false);
        }

        // 원본 TimeOfDayDisplay.Start다. 폭풍이 아닌 날씨로 고정된 레벨(튜토리얼)이면 폭풍 루트를 끄고, 아니면 켠다.
        internal void Begin(bool showsStorm)
        {
            Forget();
            _hud._stormRoot.SetActive(showsStorm);
        }

        // 원본 RefreshPhase, RefreshWeather, RefreshStormText 한 번이다. 원본처럼 막대와 시간은 숨긴 폭풍 루트 안이어도 갱신한다.
        // 지시기 Animator는 꺼진 동안 값을 받지 못하고 켜질 때 기본값으로 돌아가므로, 원본처럼 켜져 있을 때마다 다시 넣는다.
        internal void Show(string phaseKey, string weatherKey, string titleKey, TimeSpan eta, float fill, bool grow, bool description)
        {
            SetKey(ref _phaseKey, phaseKey, _hud._timePhase);
            SetKey(ref _weatherKey, weatherKey, _hud._weatherName);
            SetKey(ref _titleKey, titleKey, _hud._stormTitle);
            Animator indicator = _hud._stormIndicator;
            if (indicator.isActiveAndEnabled) indicator.SetBool(_grow, grow);
            if (_hud._stormDescription.activeSelf != description) _hud._stormDescription.SetActive(description);
            if (fill != _shownFill)
            {
                _shownFill = fill;
                _hud._stormFill.fillAmount = fill;
            }
            // 원본은 시간을 Mathf.FloorToInt((float)TotalHours)로 내림하고 분은 TimeSpan.Minutes를 쓴다. 고정 폭풍 레벨처럼 시각이 단계 밖이면
            // 음수가 될 수 있어 부호를 앞에 붙이고 자릿수는 절댓값으로 쓴다.
            long hours = Mathf.FloorToInt((float)eta.TotalHours);
            int minutes = eta.Minutes;
            if (hours == _shownHours && minutes == _shownMinutes) return;
            _shownHours = hours;
            _shownMinutes = minutes;
            int index = 0;
            if (hours < 0 || minutes < 0) _chars[index++] = '-';
            index = UiDigits.Write(_chars, index, Math.Abs(hours), 3);
            _chars[index++] = ':';
            index = UiDigits.Write(_chars, index, Math.Abs(minutes), 2);
            _hud._stormTime.SetCharArray(_chars, 0, index);
        }

        void Forget()
        {
            _phaseKey = _weatherKey = _titleKey = null;
            _shownHours = long.MinValue;
            _shownMinutes = int.MinValue;
            _shownFill = float.NaN;
        }

        static void SetKey(ref string? shown, string key, TMPro.TMP_Text text)
        {
            if (string.Equals(shown, key, StringComparison.Ordinal)) return;
            shown = key;
            text.text = GameLocalization.Text(key);
        }
    }
}
