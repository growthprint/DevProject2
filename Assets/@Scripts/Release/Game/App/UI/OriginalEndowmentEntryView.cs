#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 Duckov.Endowment.UI.EndowmentSelectionEntry다. 재능 하나의 이름, 아이콘, 고름 표시, 잠금 표시와 해금 조건 문구를 보인다.
    // 원작처럼 줄을 누르면(IPointerClickHandler) 잠기지 않은 줄만 재능 번호를 선택 창(OriginalEndowmentSelectionView)에 넘긴다.
    // 원작 줄의 마우스 올림 표시 컴포넌트는 옮기지 않는다.
    public sealed class OriginalEndowmentEntryView : MonoBehaviour, IPointerClickHandler
    {
        // 원작 displayNameText, icon, selectedIndicator, lockedIndcator(원작 철자), requirementText다.
        [SerializeField] TMP_Text _displayName = null!;
        [SerializeField] Image _icon = null!;
        [SerializeField] GameObject _selectedIndicator = null!;
        [SerializeField] GameObject _lockedIndicator = null!;
        [SerializeField] TMP_Text _requirement = null!;

        Action<EndowmentIndex>? _clicked;
        EndowmentIndex _index;
        bool _locked;

        // 원작 Setup과 SetSelection, Refresh다. 이름과 조건 문구는 컨트롤러가 번역해 넘긴다.
        internal void Bind(EndowmentIndex index, Sprite? icon, string displayName, string requirement, bool locked, bool selected,
            Action<EndowmentIndex> clicked)
        {
            _index = index;
            _locked = locked;
            _clicked = clicked;
            _displayName.text = displayName;
            _icon.sprite = icon;
            _requirement.text = requirement;
            _selectedIndicator.SetActive(selected);
            _lockedIndicator.SetActive(locked);
        }

        // 줄 모음이 다시 채우기 전에 부른다. 앞 목록의 줄이 눌려도 선택을 보내지 않게 끊는다.
        internal void Unbind() => _clicked = null;

        // 원작 OnPointerClick이다. 잠긴 줄은 아무것도 보내지 않는다.
        public void OnPointerClick(PointerEventData eventData)
        {
            if (_locked || _clicked == null) return;
            _clicked(_index);
        }
    }
}
