#nullable enable annotations
using System;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 PerkDetails.Refresh가 켜는 버튼 자리 하나다. 원작처럼 한 번에 하나만 보이고, 해금된 특성은 아무것도 보이지 않는다.
    internal enum PerkDetailsAction
    {
        None,
        // 연구 중이고 남은 시간이 0이다. 원작처럼 자동 확정하지 않고 "활성화" 버튼을 보인다.
        Activate,
        InProgress,
        Begin,
        // 부모는 모두 해금했지만 레벨이나 비용이 모자란다.
        Unsatisfied,
        // 해금하지 않은 부모가 있다.
        Unavailable,
    }

    // 원작 Duckov.UI.PerkDetails(LevelManager GameplayUICanvas/PerkTreeView/Content/NodeInspector)다. 고른 특성의 이름, 설명, 아이콘,
    // 해금 표시, 요구 레벨, 비용, 시작·활성화 버튼, 진행 막대와 남은 시간을 보인다. 값은 특성 트리 화면 컨트롤러(PerkTreeScreen)가 넣고,
    // 이 View는 시작과 활성화 의도만 낸다. 원작 FadeGroup 전환 대신 활성 여부로 보이고 숨긴다(다른 기지 창과 같은 원작 차이).
    public sealed class OriginalPerkDetailsView : MonoBehaviour
    {
        [SerializeField] GameObject _content = null!;
        [SerializeField] GameObject _placeHolder = null!;
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] TMP_Text _description = null!;
        [SerializeField] Image _icon = null!;
        [SerializeField] GameObject _unlockedIndicator = null!;
        [SerializeField] GameObject _activationInfo = null!;
        [SerializeField] TMP_Text _requireLevel = null!;
        [SerializeField] OriginalCostDisplayView _cost = null!;
        // 원작 normalTextColor, unsatisfiedTextColor다. 요구 레벨 문구의 {color}에 넣는다.
        [SerializeField] Color _normalTextColor = Color.white;
        [SerializeField] Color _unsatisfiedTextColor = Color.red;
        [SerializeField] GameObject _activate = null!;
        [SerializeField] GameObject _begin = null!;
        [SerializeField] GameObject _unsatisfied = null!;
        [SerializeField] GameObject _unavailable = null!;
        [SerializeField] GameObject _inProgress = null!;
        [SerializeField] Image _progressFill = null!;
        [SerializeField] TMP_Text _countDown = null!;

        // 원작 beginButton, activateButton 클릭이다. 고정 버튼이라 저장된 Runtime Only OnClick이 아래 공개 메서드를 부른다.
        internal event Action? BeginRequested;
        internal event Action? ActivateRequested;

        internal OriginalCostDisplayView Cost => _cost;
        internal Color NormalTextColor => _normalTextColor;
        internal Color UnsatisfiedTextColor => _unsatisfiedTextColor;
        // 원작 Update가 남은 시간을 그리는 조건(inProgressPlaceHolder.activeSelf)이다.
        internal bool CountingDown => _content.activeSelf && _inProgress.activeSelf;

        // 원작 Refresh의 showingPerk == null 경로다.
        internal void ShowPlaceholder()
        {
            _content.SetActive(false);
            _placeHolder.SetActive(true);
        }

        // 원작 Refresh 본문이다. 요구 레벨과 비용(activationInfo)은 해금 전에만 보이고, 그 내용은 SetRequireLevel과 Cost로 채운다.
        internal void Show(string displayName, string description, Sprite? icon, bool unlocked, PerkDetailsAction action)
        {
            _name.text = displayName;
            _description.text = description;
            _icon.sprite = icon;
            _unlockedIndicator.SetActive(unlocked);
            _activate.SetActive(action == PerkDetailsAction.Activate);
            _inProgress.SetActive(action == PerkDetailsAction.InProgress);
            _begin.SetActive(action == PerkDetailsAction.Begin);
            _unsatisfied.SetActive(action == PerkDetailsAction.Unsatisfied);
            _unavailable.SetActive(action == PerkDetailsAction.Unavailable);
            _activationInfo.SetActive(!unlocked);
            _content.SetActive(true);
            _placeHolder.SetActive(false);
        }

        // 원작 SetupActivationInfo의 요구 레벨 줄이다. 요구 레벨이 0이면 원작처럼 줄을 숨기므로 text는 null이다.
        internal void SetRequireLevel(string? text)
        {
            _requireLevel.gameObject.SetActive(text != null);
            if (text != null) _requireLevel.text = text;
        }

        // 원작 UpdateCountDown이다. 문구는 컨트롤러가 원작 형식(d hh:mm:ss.fff)으로 만든다.
        internal void SetCountdown(float progress01, string text)
        {
            _progressFill.fillAmount = progress01;
            _countDown.text = text;
        }

        public void OnBeginClicked()
        {
            Log.Instance.Assert(_begin.activeInHierarchy, "특성 연구 시작 버튼이 보일 때만 누릅니다.", this);
            BeginRequested?.Invoke();
        }

        public void OnActivateClicked()
        {
            Log.Instance.Assert(_activate.activeInHierarchy, "특성 활성화 버튼이 보일 때만 누릅니다.", this);
            ActivateRequested?.Invoke();
        }
    }
}
