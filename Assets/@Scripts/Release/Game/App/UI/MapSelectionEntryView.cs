#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.App
{
    // 원작 Duckov.UI.MapSelectionEntry(지도 선택 항목 하나)다. 원작처럼 항목 루트의 IPointerClickHandler로 눌리고, 잠금 표시와 비용 표시를 켜고 끈다.
    // 이름 문구는 원작 번역 키(예: Level_GroundZero)에 묶인 고정 문구라 이 View가 쓰지 않는다. 원작 클릭 흔들림과 호버 표시는 옮기지 않았다.
    // 눌러도 되는지(조건, 비용)는 지도 선택 화면 컨트롤러가 정한다. 원작은 조건이 맞지 않으면 항목 안에서 클릭을 버린다.
    public sealed class MapSelectionEntryView : MonoBehaviour, IPointerClickHandler
    {
        // 원작 항목 GameObject 이름이다. 컨트롤러가 지도 선택 데이터(MapSelectionEntryData.SourceName)와 짝을 맞춘다.
        [SerializeField] string _sourceName = "";
        // 원작 displayNameText, lockedIndicator, costDisplay다.
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] GameObject _lockedIndicator = null!;
        [SerializeField] OriginalCostDisplayView _cost = null!;

        Action<int>? _clicked;
        int _index = -1;

        internal string SourceName => _sourceName;
        internal OriginalCostDisplayView Cost => _cost;
        // 원작 확인 표시의 목적지 이름 자리에 지금 보이는 항목 이름을 그대로 쓴다.
        internal string DisplayName => _name.text;

        // 원작 Refresh의 lockedIndicator.SetActive(!ConditionsSatisfied)와 costDisplay.gameObject.SetActive(!cost.IsFree)다.
        // 비용 내용은 컨트롤러가 이어서 Cost로 채운다. index는 컨트롤러의 데이터 번호이고, 짝이 없는 항목은 -1이라 눌러도 아무것도 하지 않는다.
        internal void Bind(Action<int> clicked, int index, bool locked, bool free)
        {
            _clicked = clicked;
            _index = index;
            _lockedIndicator.SetActive(locked);
            _cost.gameObject.SetActive(!free);
        }

        // 창을 닫을 때 앞 클릭 대상을 끊는다.
        internal void ResetBinding()
        {
            _clicked = null;
            _index = -1;
        }

        // Bind가 번호와 클릭 대상을 함께 넣고 ResetBinding이 함께 지우므로, 번호가 있으면 클릭 대상도 있다.
        public void OnPointerClick(PointerEventData eventData)
        {
            if (_index >= 0) _clicked!(_index);
        }
    }
}
