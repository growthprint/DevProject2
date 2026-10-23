#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 CraftView_ListEntry다. 제조식 하나의 결과 아이콘, "이름 x수량", 비용이 넉넉한지의 배경색, 선택 표시를 보인다.
    // 값만 받고 클릭 의도만 넘긴다. 선택, 지불 가능 판정, 다시 그리기는 화면 컨트롤러(CraftScreen)가 맡는다.
    // 원작 OnPlayerItemOperation 구독 대신 컨트롤러가 세션과 계좌 변경 때 목록을 다시 그린다.
    public sealed class OriginalCraftListEntryView : MonoBehaviour, IPointerClickHandler
    {
        // 원작 normalColor와 normalInsufficientColor다. 비용을 낼 수 있으면 앞 색, 아니면 뒤 색으로 배경을 칠한다.
        [SerializeField] Color _normalColor;
        [SerializeField] Color _normalInsufficientColor;
        [SerializeField] Image _icon = null!;
        [SerializeField] Image _background = null!;
        [SerializeField] TMP_Text _name = null!;
        // 원작 selectedIndicator다. 원작처럼 비어 있을 수 있어 != null로 거른다.
        [SerializeField] GameObject? _selectedIndicator;

        Action<string>? _clicked;
        string? _formulaId;

        // 원작 Setup과 Refresh다. label은 컨트롤러가 번역한 "이름 x수량"이다. 컨트롤러는 화면이 열릴 때 한 번 만든 대리자를 넘긴다.
        internal void Bind(Action<string> clicked, string formulaId, Sprite? icon, string label, bool enough, bool selected)
        {
            _clicked = clicked;
            _formulaId = formulaId;
            _icon.sprite = icon;
            _name.text = label;
            if (_selectedIndicator != null) _selectedIndicator.SetActive(selected);
            _background.color = enough ? _normalColor : _normalInsufficientColor;
            gameObject.SetActive(true);
        }

        // 풀로 돌아간 칸이나 닫힌 창의 칸이 앞 의도를 보내지 않게 연결을 끊는다. 표시는 다음 Bind가 덮거나 줄 모음이 숨긴다.
        internal void ResetBinding()
        {
            _clicked = null;
            _formulaId = null;
        }

        // 원작 OnPointerClick의 Master.SetSelection이다. 원작처럼 마우스 단추를 가리지 않는다.
        public void OnPointerClick(PointerEventData data)
        {
            if (_clicked == null || _formulaId == null) return;
            _clicked(_formulaId);
        }
    }
}
