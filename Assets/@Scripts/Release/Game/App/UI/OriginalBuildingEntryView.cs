#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 Duckov.Buildings.UI.BuildingBtnEntry다. 건설 목록의 건물 한 줄로 이름, 설명, 아이콘, 토큰 수, 비용, 개수, 최대 도달 표시를 보인다.
    // 원작처럼 줄을 누르면 고르기를, 회수 버튼을 길게 누르면 그 종류 전체 회수를 건설 창(OriginalBuilderView)에 넘긴다. 문구는 화면 컨트롤러가 번역해 넘긴다.
    public sealed class OriginalBuildingEntryView : MonoBehaviour
    {
        // 원작 button, icon, nameText, descriptionText, costDisplay, recycleButton, amountText, tokenText,
        // reachedAmountLimitationIndicator, backGround와 두 배경색(normalColor, avaliableColor)이다.
        [SerializeField] Button _button = null!;
        [SerializeField] Image _icon = null!;
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] TMP_Text _description = null!;
        [SerializeField] OriginalCostDisplayView _cost = null!;
        [SerializeField] OriginalLongPressButton _recycleButton = null!;
        [SerializeField] TMP_Text _amount = null!;
        [SerializeField] TMP_Text _token = null!;
        [SerializeField] GameObject _reachedAmountLimitIndicator = null!;
        [SerializeField] Image _background = null!;
        [SerializeField] Color _normalColor;
        [SerializeField] Color _availableColor;

        string _buildingId = "";
        Action<string>? _clicked;
        Action<string>? _recycle;

        // 비용 줄은 줄마다 다르므로 컨트롤러가 이 표시를 직접 채운다.
        internal OriginalCostDisplayView Cost => _cost;

        // 원작 Awake처럼 복제된 줄마다 버튼 알림을 한 번 건다. 줄 모음의 항목이라 저장된 OnClick 대신 코드로 묶는다.
        void Awake()
        {
            _button.onClick.AddListener(Clicked);
            _recycleButton.Fulfilled += RecycleFulfilled;
        }

        // 원작 Setup과 Refresh다. 비용 표시는 토큰이 없을 때만, 회수 버튼은 지은 건물이 있을 때만 보인다.
        // 배경 강조는 원작처럼 최대 개수에 닿지 않았고 토큰이나 비용이 넉넉할 때다.
        internal void Bind(string buildingId, Sprite? icon, string displayName, string description, string tokenText, bool costShown,
            string amountText, bool reachedLimit, bool available, bool recyclable, Action<string> clicked, Action<string> recycle)
        {
            _buildingId = buildingId;
            _clicked = clicked;
            _recycle = recycle;
            _name.text = displayName;
            _description.text = description;
            _token.text = tokenText;
            _icon.sprite = icon;
            _cost.gameObject.SetActive(costShown);
            _amount.text = amountText;
            _reachedAmountLimitIndicator.SetActive(reachedLimit);
            _background.color = available ? _availableColor : _normalColor;
            _recycleButton.gameObject.SetActive(recyclable);
        }

        // 줄 모음이 다시 채우기 전에 부른다. 앞 목록의 줄이 눌려도 앞 건물을 보내지 않게 끊는다.
        internal void Unbind()
        {
            _clicked = null;
            _recycle = null;
        }

        void Clicked()
        {
            if (_clicked != null) _clicked(_buildingId);
        }

        void RecycleFulfilled()
        {
            if (_recycle != null) _recycle(_buildingId);
        }
    }
}
