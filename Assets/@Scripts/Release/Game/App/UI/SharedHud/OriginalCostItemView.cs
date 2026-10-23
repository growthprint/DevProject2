#nullable enable annotations
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 ItemAmountDisplay다. 비용 아이템 하나의 아이콘과 "( 가진 수 / 필요 수 )"를 보이고, 모자라면 normalColor(붉은색),
    // 넉넉하면 enoughColor(투명) 배경이다. 값은 건설 지점 표시기(CostTakerHud)가 넘긴다. 필드는 SharedHUD 저작 도구가 원본 값으로 채운다.
    public sealed class OriginalCostItemView : MonoBehaviour
    {
        [SerializeField] Image _background = null!;
        [SerializeField] Image _icon = null!;
        [SerializeField] TMP_Text _amount = null!;
        [SerializeField] string _format = "( {possess} / {amount} )";
        [SerializeField] Color _normalColor;
        [SerializeField] Color _enoughColor;
        long _required;
        // 마지막으로 쓴 가진 수다. long.MinValue는 아직 쓰지 않은 상태라 첫 표시는 늘 쓴다.
        long _shown = long.MinValue;

        internal void Setup(Sprite? icon, long required)
        {
            _icon.sprite = icon;
            _required = required;
            _shown = long.MinValue;
        }

        // 가진 수가 바뀐 때만 글자와 배경을 다시 쓴다. 원작 형식 문자열의 {possess}와 {amount}를 채운다.
        internal void ShowPossess(long possess)
        {
            if (possess == _shown) return;
            _shown = possess;
            _amount.text = _format.Replace("{possess}", possess.ToString(CultureInfo.InvariantCulture))
                .Replace("{amount}", _required.ToString(CultureInfo.InvariantCulture));
            _background.color = possess >= _required ? _enoughColor : _normalColor;
        }
    }
}
