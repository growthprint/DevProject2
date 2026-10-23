#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 PerkEntry.Refresh의 세 모습(활성, 연구 가능, 불가)이다. 화면 컨트롤러가 해금, 부모 해금 여부로 고른다.
    internal enum PerkEntryLook
    {
        Active,
        Available,
        Unavailable,
    }

    // 원작 PerkEntry.Look이다. 원작 프리팹의 세 모습은 모두 재질이 비어 있어 재질이 null이면 기본 UI 재질로 그린다.
    [Serializable]
    public struct OriginalPerkEntryLook
    {
        [SerializeField] internal Color _iconColor;
        [SerializeField] internal Material? _material;
        [SerializeField] internal Color _frameColor;
        [SerializeField] internal Color _frameGlowColor;
        [SerializeField] internal Color _backgroundColor;
    }

    // 원작 Duckov.UI.PerkEntry 노드 하나다(원작 별도 프리팹 GameObject/PerkEntry.prefab). 아이콘, 이름, 모습, 연구 가능·진행 중·확인 대기
    // 표시, 고름 표시를 보인다. 값은 특성 트리 화면 컨트롤러(PerkTreeScreen)가 트리 View를 거쳐 넣고, 이 View는 클릭 의도만 낸다.
    // 원작 PunchReceiver 흔들림과 아이콘 품질 그림자는 옮기지 않는다(원작 차이).
    public sealed class OriginalPerkEntryView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] Image _icon = null!;
        [SerializeField] GameObject _selectionIndicator = null!;
        [SerializeField] Image _frame = null!;
        // 원작 frameGlow(TrueShadow)다. 재구성에서 그림자 효과가 없으면 비어 있고, 그때는 발광을 켜고 끄지 않는다.
        // 제품 그림자 효과는 색을 바꾸는 멤버가 없어 원작 Look의 발광 색 대신 켜짐 여부만 따른다(원작 차이).
        [SerializeField] OriginalTrueShadow? _frameGlow;
        [SerializeField] Image _background = null!;
        [SerializeField] TMP_Text _displayName = null!;
        [SerializeField] GameObject _inProgressIndicator = null!;
        [SerializeField] GameObject _timeUpIndicator = null!;
        [SerializeField] GameObject _availableForResearchIndicator = null!;
        [SerializeField] OriginalPerkEntryLook _activeLook;
        [SerializeField] OriginalPerkEntryLook _availableLook;
        [SerializeField] OriginalPerkEntryLook _unavailableLook;

        Action<int>? _clicked;

        // 트리 정의의 특성 번호다. 연결선과 상세가 이 번호로 특성을 찾는다.
        internal int PerkIndex { get; private set; } = -1;
        // 원작 GetLayoutPosition(관계 그래프 cachedPosition)이다. 트리 View가 배치에 쓴다.
        internal Vector2 LayoutPosition { get; private set; }
        internal RectTransform RectTransform => (RectTransform)transform;

        // 원작 Setup이다. 아이콘과 이름을 넣고, 클릭 의도를 받을 트리 View 처리기를 기억한다.
        internal void Bind(int perkIndex, Sprite? icon, string displayName, Vector2 layoutPosition, Action<int> clicked)
        {
            PerkIndex = perkIndex;
            LayoutPosition = layoutPosition;
            _clicked = clicked;
            _icon.sprite = icon;
            _displayName.text = displayName;
        }

        // 템플릿 모음이 줄을 돌려받을 때 부른다. 꺼진 줄이 앞 트리의 클릭 처리기를 붙잡지 않게 한다.
        internal void Unbind()
        {
            _clicked = null;
            PerkIndex = -1;
        }

        // 원작 Refresh의 모습 전환과 세 표시다. 판정은 컨트롤러가 소유자 조회로 하고 이 View는 그대로 보인다.
        internal void SetState(PerkEntryLook look, bool availableForResearch, bool inProgress, bool timeUp)
        {
            ApplyLook(look == PerkEntryLook.Active ? _activeLook : look == PerkEntryLook.Available ? _availableLook : _unavailableLook);
            _availableForResearchIndicator.SetActive(availableForResearch);
            _inProgressIndicator.SetActive(inProgress);
            _timeUpIndicator.SetActive(timeUp);
        }

        internal void SetSelected(bool selected) => _selectionIndicator.SetActive(selected);

        // 원작 OnPointerClick이다. 고르기는 트리 View가 컨트롤러에 알리고, 컨트롤러가 고른 특성을 정한다.
        public void OnPointerClick(PointerEventData eventData)
        {
            if (_clicked == null) return;
            _clicked(PerkIndex);
        }

        // 원작 ApplyLook이다. 원작처럼 발광 색의 알파가 0보다 클 때만 발광을 켠다.
        void ApplyLook(OriginalPerkEntryLook look)
        {
            _icon.material = look._material;
            _icon.color = look._iconColor;
            _frame.color = look._frameColor;
            if (_frameGlow != null) _frameGlow.enabled = look._frameGlowColor.a > 0f;
            _background.color = look._backgroundColor;
        }
    }
}
