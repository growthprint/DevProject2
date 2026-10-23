#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 CraftViewFilterBtnEntry다. 분류 하나의 아이콘, 이름, 선택 표시를 보이고 클릭하면 그 분류 번호를 넘긴다.
    // 분류 단추는 번호만 다른 단추라 줄 모음처럼 코드로 연결한다(Docs/rules/ui.md). 분류 고르기는 화면 컨트롤러(CraftScreen)가 맡는다.
    public sealed class OriginalCraftFilterButtonView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] Image _icon = null!;
        [SerializeField] TMP_Text _displayName = null!;
        [SerializeField] GameObject _selectedIndicator = null!;

        Action<int>? _clicked;
        int _index;

        // 원작 Setup이다. index는 OriginalCraftView 분류 목록의 번호이고, 이름은 컨트롤러가 번역해 넘긴다.
        internal void Bind(Action<int> clicked, int index, string displayName, Sprite? icon, bool selected)
        {
            _clicked = clicked;
            _index = index;
            _icon.sprite = icon;
            _displayName.text = displayName;
            _selectedIndicator.SetActive(selected);
            gameObject.SetActive(true);
        }

        // 풀로 돌아간 단추나 닫힌 창의 단추가 앞 의도를 보내지 않게 연결을 끊는다.
        internal void ResetBinding() => _clicked = null;

        // 원작 OnPointerClick의 master.SetFilter(index)다. 원작처럼 마우스 단추를 가리지 않는다.
        public void OnPointerClick(PointerEventData data)
        {
            if (_clicked != null) _clicked(_index);
        }
    }
}
