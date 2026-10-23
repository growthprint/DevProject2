#nullable enable annotations
using System.Globalization;
using TMPro;

namespace Game.App
{
    // 원본 MoneyDisplay(계좌 잔액)와 CashDisplay(가진 현금 아이템 수)다. 공용 HUD의 소지품, 상점, 건설, 수리, 분해, 특성, 설계도, 열쇠 창이
    // 같은 두 값을 보이므로 글자 묶음 두 개에 같은 문자열을 쓴다. 값은 소유자가 넘기고, 값이 바뀐 프레임에만 글자를 다시 만든다.
    internal sealed class HudMoneyDisplay
    {
        readonly SharedHudView _hud;
        // 마지막으로 쓴 값이다. 시작값 long.MinValue는 어떤 값과도 달라 첫 Show가 반드시 쓴다.
        long _money = long.MinValue, _cash = long.MinValue;

        internal HudMoneyDisplay(SharedHudView hud) => _hud = hud;

        // 소유자가 바뀌면 다음 소유자의 첫 값이 반드시 쓰이게 한다.
        internal void Reset() => _money = _cash = long.MinValue;

        internal void Show(long money, long cash)
        {
            if (money != _money)
            {
                _money = money;
                Write(_hud._moneyTexts, money);
            }
            if (cash != _cash)
            {
                _cash = cash;
                Write(_hud._cashTexts, cash);
            }
        }

        // 원본 형식 "n0"이다. 문화권마다 구분 기호가 달라지지 않게 고정 문화권으로 만든다.
        static void Write(TMP_Text[] texts, long value)
        {
            string text = value.ToString("n0", CultureInfo.InvariantCulture);
            for (int index = 0; index < texts.Length; index++) texts[index].text = text;
        }
    }
}
