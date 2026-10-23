#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 Duckov.UI.StorageDockEntry다. 창고 임시 보관함 항목 하나의 아이콘과 2개 이상일 때의 개수를 보이고,
    // 창고에 넣을 수 있는지에 따라 안내 글자와 배경색을 바꾼다. 클릭은 항목 번호와 함께 보관함 창(OriginalStorageDockView)에 넘긴다.
    // 원작은 수령을 기다리는 동안 받는 중 표시를 켜지만 제품 수령은 한 호출에 끝나므로, 다시 그릴 때 원작 Setup처럼 늘 끈다.
    public sealed class OriginalStorageDockEntryView : MonoBehaviour
    {
        // 원작 itemDisplay(ItemMetaDisplay)의 icon이다. 품질 그림자는 제품 아이템 표시가 쓰지 않아 옮기지 않는다.
        [SerializeField] Image _icon = null!;
        [SerializeField] TMP_Text _text = null!;
        [SerializeField] GameObject _countDisplay = null!;
        [SerializeField] TMP_Text _countText = null!;
        [SerializeField] Image _background = null!;
        [SerializeField] Button _button = null!;
        [SerializeField] GameObject _loadingIndicator = null!;
        // 원작 colorNormal과 colorFull이다. 제작 도구가 원본 값을 옮긴다.
        [SerializeField] Color _colorNormal = Color.white;
        [SerializeField] Color _colorFull = Color.white;

        // 줄 모음의 버튼이라 코드로 한 번만 구독한다. 첫 Bind에서 구독해 Edit Mode에서도 같은 경로로 눌린다.
        readonly UiEventSubscriptions _bindings = new UiEventSubscriptions();
        Action<int>? _clicked;
        int _index = -1;
        bool _bound;

        // 원작 Setup이다. 안내 글자는 컨트롤러가 번역해 넘긴다.
        internal void Bind(int index, Sprite? icon, int count, bool storable, string text, Action<int> clicked)
        {
            if (!_bound)
            {
                _bindings.Bind(_button.onClick, OnButtonClicked);
                _bound = true;
            }
            _index = index;
            _clicked = clicked;
            _icon.sprite = icon;
            bool many = count > 1;
            if (many) _countText.text = count.ToString();
            _countDisplay.SetActive(many);
            _background.color = storable ? _colorNormal : _colorFull;
            _text.text = text;
            _text.gameObject.SetActive(true);
            _loadingIndicator.SetActive(false);
        }

        // 줄 모음이 다시 채우기 전에 부른다. 앞 목록의 번호로 수령을 보내지 않게 끊는다.
        internal void Unbind()
        {
            _clicked = null;
            _index = -1;
        }

        void OnButtonClicked()
        {
            if (_clicked != null) _clicked(_index);
        }

        void OnDestroy() => _bindings.Clear();
    }
}
