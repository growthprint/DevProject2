#nullable enable annotations
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 ItemAmountDisplay다. 비용 아이템 하나의 아이콘과 "가진 수 / 필요 수" 문구를 보이고, 가진 수가 넉넉하면 배경색을 바꾼다.
    // 수는 화면 컨트롤러가 공용 아이템 도우미로 세어 넘긴다. 원작의 마우스 올림 아이템 정보 알림은 옮기지 않는다.
    public sealed class OriginalItemAmountView : MonoBehaviour
    {
        // 원작 background, icon, amountText와 문구 형식, 두 배경색이다. 형식은 번역 키가 아니라 프리팹 문자열이다.
        [SerializeField] Image _background = null!;
        [SerializeField] Image _icon = null!;
        [SerializeField] TMP_Text _amountText = null!;
        [SerializeField] string _amountFormat = "( {possess} / {amount} )";
        [SerializeField] Color _normalColor;
        [SerializeField] Color _enoughColor;

        // 원작 Setup과 Refresh다. 원작처럼 가진 수가 필요 수 이상이면 넉넉하다고 본다.
        internal void Bind(Sprite? icon, long possess, long amount)
        {
            _icon.sprite = icon;
            _amountText.text = _amountFormat.Replace("{amount}", amount.ToString(CultureInfo.InvariantCulture))
                .Replace("{possess}", possess.ToString(CultureInfo.InvariantCulture));
            _background.color = possess >= amount ? _enoughColor : _normalColor;
        }
    }
}
