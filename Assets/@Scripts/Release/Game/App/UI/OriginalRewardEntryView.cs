#nullable enable annotations
using System;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 RewardEntry.Refresh가 읽는 보상 값이다. 화면 컨트롤러가 소유자 조회와 번역으로 만든다.
    internal readonly struct QuestRewardValues
    {
        internal QuestRewardValues(string description, Sprite? icon, bool claimable, bool claimed)
        {
            Description = description;
            Icon = icon;
            Claimable = claimable;
            Claimed = claimed;
        }

        internal string Description { get; }
        internal Sprite? Icon { get; }
        // 원작 Reward.Claimable(= 퀘스트 완료)이다. 받은 뒤에도 참이다.
        internal bool Claimable { get; }
        internal bool Claimed { get; }
    }

    // 원작 Duckov.Quests.UI.RewardEntry다. RewardEntry.prefab과 완료 패널의 CompletePanel_RewardEntry가 쓴다.
    // 설명과 아이콘을 보이고, 받기 버튼은 Interactable일 때만 보인다. 버튼은 저장된 Runtime Only OnClick이 OnClaimClicked를 부른다.
    // 원작 받는 중 아이콘(claimingIcon)은 제품이 보상을 한 호출 안에서 주므로(2-Q1) 꺼진 채 저장하고 참조하지 않는다.
    public sealed class OriginalRewardEntryView : MonoBehaviour
    {
        [SerializeField] Image _rewardIcon = null!;
        [SerializeField] TMP_Text _rewardText = null!;
        [SerializeField] Button _claim = null!;
        // 원작 완료 패널 줄은 claimableIndicator와 buttonText를 두지 않는다(원작 참조 0). 그래서 이 둘만 비어 있을 수 있고 원작처럼 확인한다.
        [SerializeField] GameObject? _claimableIndicator = null;
        [SerializeField] Image _statusIcon = null!;
        [SerializeField] TMP_Text? _buttonText = null;
        [SerializeField] Sprite _claimIcon = null!;
        [SerializeField] Sprite _claimedIcon = null!;
        [SerializeField] string _claimTextKey = "UI_Quest_RewardClaim";
        [SerializeField] string _claimedTextKey = "UI_Quest_RewardClaimed";
        // 원작 RewardEntry.interactable이다. 완료 패널 줄은 원작 저장값 1, 별도 템플릿은 0이고 상세 창이 퀘스트마다 정한다.
        [SerializeField] bool _interactable;

        Action<int>? _claimRequested;
        int _index;

        internal bool Interactable
        {
            get => _interactable;
            set => _interactable = value;
        }

        // 원작 Setup과 Refresh다. 받을 수 있지만 상세 창이 상호작용하지 않으면 받을 수 있음 표시만 켠다.
        internal void Bind(in QuestRewardValues values, int index, Action<int> claimRequested)
        {
            _index = index;
            _claimRequested = claimRequested;
            _rewardText.text = values.Description;
            _rewardIcon.gameObject.SetActive(values.Icon != null);
            _rewardIcon.sprite = values.Icon;
            bool button = _interactable && values.Claimable;
            _claim.gameObject.SetActive(button);
            if (_claimableIndicator != null) _claimableIndicator.SetActive(!_interactable && values.Claimable && !values.Claimed);
            if (!button) return;
            if (_buttonText != null) _buttonText.text = GameLocalization.Text(values.Claimed ? _claimedTextKey : _claimTextKey);
            _statusIcon.sprite = values.Claimed ? _claimedIcon : _claimIcon;
            _claim.interactable = !values.Claimed;
        }

        // 줄 모음이 다시 채우기 전에 앞 연결을 끊는다.
        internal void Unbind() => _claimRequested = null;

        // 버튼은 Bind가 연결한 줄에서만 켜지고, 쓰지 않은 줄은 줄 모음이 숨기므로 확인 뒤 바로 부른다. 중복 받기 거절은 소유자가 한다.
        public void OnClaimClicked()
        {
            Log.Instance.Assert(_claimRequested != null, "연결되지 않은 보상 줄의 버튼이 눌렸습니다.", this);
            _claimRequested!(_index);
        }
    }
}
