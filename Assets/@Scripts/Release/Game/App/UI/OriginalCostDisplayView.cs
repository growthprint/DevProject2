#nullable enable annotations
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 CostDisplay다. 비용의 돈 줄과 아이템 줄을 보이고, 돈과 비용 전체가 넉넉한지를 배경색으로 보인다.
    // 넉넉한지는 화면 컨트롤러가 지불 확인(GameplayCommands.CanPay)과 같은 출처로 판정해 넘기고, 이 View는 표시만 한다.
    public sealed class OriginalCostDisplayView : MonoBehaviour
    {
        // 원작 moneyContainer, itemsContainer, moneyBackground, money, itemAmountTemplate과 네 색이다.
        [SerializeField] GameObject _moneyContainer = null!;
        [SerializeField] GameObject _itemsContainer = null!;
        // 원작 background다. 건설 목록의 비용 표시처럼 원작 프리팹이 비워 둔 곳이 있어 비어 있으면 칠하지 않는다.
        [SerializeField] Image? _background;
        [SerializeField] Image _moneyBackground = null!;
        [SerializeField] TMP_Text _money = null!;
        [SerializeField] OriginalItemAmountView _itemTemplate = null!;
        [SerializeField] Color _normalColor;
        [SerializeField] Color _enoughColor;
        [SerializeField] Color _moneyNormalColor;
        [SerializeField] Color _moneyEnoughColor;

        UiTemplateCollection<OriginalItemAmountView>? _items;

        UiTemplateCollection<OriginalItemAmountView> Items => _items ??= new UiTemplateCollection<OriginalItemAmountView>(_itemTemplate);

        // 원작 Setup의 돈 줄과 RefreshMoneyBackground, RefreshBackground다. 아이템 줄은 이어서 AddItem과 EndItems로 채운다.
        // 셋은 같은 호출 안에서 짝을 이룬다. 돈 문구는 원작처럼 천 단위 구분 기호를 넣는다.
        internal void BeginItems(long money, bool moneyEnough, bool enough, bool hasItems)
        {
            _moneyContainer.SetActive(money > 0);
            _money.text = money.ToString("n0", CultureInfo.InvariantCulture);
            _itemsContainer.SetActive(hasItems);
            _moneyBackground.color = moneyEnough ? _moneyEnoughColor : _moneyNormalColor;
            if (_background != null) _background.color = enough ? _enoughColor : _normalColor;
            Items.Begin();
        }

        internal void AddItem(Sprite? icon, long possess, long amount)
        {
            OriginalItemAmountView item = Items.Acquire(_itemTemplate.transform.parent);
            item.Bind(icon, possess, amount);
            item.gameObject.SetActive(true);
        }

        internal void EndItems() => Items.End();
    }
}
