#nullable enable annotations
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원작 CostTakerHUD_Entry와 그 안의 CostDisplay다. 건설 지점 이름과 비용 아이템 줄을 보인다. 원작 FadeGroup 대신 CanvasGroup 알파를
    // UiFade 규칙으로 올리고 내린다. 원작 제로존 비용은 돈이 모두 0이라 돈 줄(moneyContainer)은 끈다. 필드는 SharedHUD 저작 도구가 채운다.
    public sealed class OriginalCostTakerEntryView : MonoBehaviour
    {
        [SerializeField] CanvasGroup _group = null!;
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] GameObject _moneyContainer = null!;
        [SerializeField] GameObject _itemsContainer = null!;
        [SerializeField] OriginalCostItemView _itemTemplate = null!;
        [SerializeField, Min(UiFade._minimumDuration)] float _fadeDuration = .15f;
        readonly List<OriginalCostItemView> _items = new();
        // 마지막으로 쓴 화면 위치다. Transform 대입은 같은 값이어도 HUD 캔버스를 다시 묶으므로 바뀐 때만 쓴다.
        Vector2 _shownPoint = new(float.NaN, float.NaN);

        internal bool Visible => _group.alpha > 0f;

        // 원작 Setup이다. 숨긴 채로 시작해 표시기가 거리 판정으로 켠다.
        internal void Setup(string name)
        {
            _name.text = name;
            _moneyContainer.SetActive(false);
            _itemsContainer.SetActive(false);
            _group.alpha = 0f;
        }

        // 원작 CostDisplay.Setup이 비용 항목마다 줄 템플릿에서 하나씩 꺼내는 자리다. 템플릿은 꺼 둔 채 사본만 켠다.
        internal void AddItem(Sprite? icon, long required)
        {
            OriginalCostItemView row = Instantiate(_itemTemplate, _itemTemplate.transform.parent, false);
            row.gameObject.SetActive(true);
            row.Setup(icon, required);
            _items.Add(row);
            _itemsContainer.SetActive(true);
        }

        internal void ShowPossess(int row, long possess) => _items[row].ShowPossess(possess);

        // 원작 LateUpdate의 UpdatePosition과 UpdateFadeGroup이다. 보일 때만 화면 점으로 옮기고, 알파는 목표에 닿을 때까지만 쓴다.
        internal void Present(Vector2 screenPoint, bool shown, float deltaTime)
        {
            if (shown && screenPoint != _shownPoint)
            {
                transform.position = screenPoint;
                _shownPoint = screenPoint;
            }
            float target = shown ? 1f : 0f;
            if (_group.alpha != target) _group.alpha = UiFade.Step(_group.alpha, target, deltaTime, _fadeDuration);
        }
    }
}
