#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 HealthHUD처럼 HP 바의 체력 문구와 게이지를 같은 값으로 갱신한다.
    // 프리팹 참조는 생성자로 다시 받지 않고 SharedHudView의 직렬화 필드를 직접 읽는다.
    internal sealed class HudHealthDisplay
    {
        readonly SharedHudView _hud;
        // 최대 체력은 캐릭터 연결 때 한 번 받아 두고, 매 프레임 갱신은 현재 값만 받는다. 연결 전에는 상태 표시의 시작 최대치다.
        float _maximum = HudStatusDisplay._unboundMaximum;
        // 같은 값으로 문자열을 매 프레임 다시 만들지 않도록 마지막 표시값을 기억한다. 체력은 0.1 단위다.
        int _shownTenths = int.MinValue, _shownMaximum = int.MinValue;
        // 체력 문구의 " /최대" 조각이다. 최대 체력이 바뀌면 null로 비우고, 다음 체력 표시 때 한 번만 만든다.
        string? _suffix;

        internal HudHealthDisplay(SharedHudView hud) => _hud = hud;

        // 최대 체력이 바뀌므로 체력 문구의 최대 조각을 다시 만들게 한다.
        internal void SetMaximum(float maximum)
        {
            _maximum = maximum;
            _suffix = null;
        }

        // 소유자가 바뀔 때 최대값과 표시 캐시를 시작값으로 돌려 다음 소유자의 첫 갱신이 반드시 적용되게 한다.
        internal void Reset()
        {
            SetMaximum(HudStatusDisplay._unboundMaximum);
            _shownTenths = int.MinValue;
            _shownMaximum = int.MinValue;
        }

        // 매 프레임 불린다. 체력 문자열은 0.1 단위 값이나 최대 체력이 바뀐 프레임에만 만든다.
        internal void Show(float health)
        {
            int tenths = Mathf.RoundToInt(health * 10f), maximum = Mathf.RoundToInt(_maximum);
            if (tenths != _shownTenths || maximum != _shownMaximum)
            {
                _shownTenths = tenths;
                _shownMaximum = maximum;
                // 숫자는 위의 0.1 단위 키에서 만들어, 반올림 경계에서도 다시 쓸지 판정한 값과 보이는 값이 같게 한다.
                // _suffix는 Unity 객체가 아닌 string이라 ??=를 써도 된다.
                _hud._health.text = (tenths / 10f).ToString("F1") + (_suffix ??= " /" + _maximum.ToString("0"));
            }
            // 다른 HUD 게이지와 같은 채움 비율 규칙을 쓴다.
            _hud._healthFill.fillAmount = HudStatusDisplay.GaugeFill(health, _maximum);
        }
    }
}
