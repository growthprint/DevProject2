#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.Core;
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.Quests.UI.QuestCompletePanel(QuestGiverView/CompletePanel)이다. 완료한 퀘스트 이름과 보상 줄을 보인다.
    // 원작 Show의 "모두 받거나 건너뛸 때까지 기다림"과 닫을 때 AutoClaim 받기는 화면 컨트롤러가 명령으로 한다. View는 의도만 알린다.
    // 원작 FadeGroup 전환 대신 활성 여부로 보이고 숨긴다. 모두 받기와 건너뛰기는 저장된 Runtime Only OnClick이 부른다.
    public sealed class OriginalQuestCompletePanelView : MonoBehaviour
    {
        [SerializeField] TMP_Text _questName = null!;
        // 원작 rewardEntryTemplate(CompletePanel_RewardEntry)이다. 패널 안에 꺼진 채 저장하고, 그 부모 아래에 복제한다.
        [SerializeField] OriginalRewardEntryView _rewardTemplate = null!;

        UiTemplateCollection<OriginalRewardEntryView>? _rewards;

        public event Action? TakeAllRequested;
        public event Action? SkipRequested;
        public event Action<int>? RewardClaimRequested;

        internal bool IsShown => gameObject.activeSelf;

        UiTemplateCollection<OriginalRewardEntryView> Rewards => _rewards ??= new UiTemplateCollection<OriginalRewardEntryView>(_rewardTemplate, entry => entry.Unbind());

        // 원작 Show의 SetupContent와 표시다. 보상 받기 뒤 다시 그릴 때는 Present만 부른다.
        internal void Show(string questName, IReadOnlyList<QuestRewardValues> rewards)
        {
            gameObject.SetActive(true);
            Present(questName, rewards);
        }

        internal void Present(string questName, IReadOnlyList<QuestRewardValues> rewards)
        {
            _questName.text = questName;
            UiTemplateCollection<OriginalRewardEntryView> entries = Rewards;
            Transform parent = _rewardTemplate.transform.parent;
            entries.Begin();
            for (int index = 0; index < rewards.Count; index++)
            {
                OriginalRewardEntryView entry = entries.Acquire(parent);
                entry.gameObject.SetActive(true);
                entry.Bind(rewards[index], index, OnRewardClaimRequested);
            }
            entries.End();
        }

        // 공용 HUD 초기화와 창 닫기가 무조건 부르므로 보이지 않을 때도 안전하고 이벤트를 내지 않는다.
        internal void Hide()
        {
            _rewards?.Clear();
            gameObject.SetActive(false);
        }

        public void OnTakeAllClicked()
        {
            Log.Instance.Assert(IsShown, "닫힌 완료 패널의 모두 받기가 눌렸습니다.", this);
            TakeAllRequested?.Invoke();
        }

        public void OnSkipClicked()
        {
            Log.Instance.Assert(IsShown, "닫힌 완료 패널의 건너뛰기가 눌렸습니다.", this);
            SkipRequested?.Invoke();
        }

        void OnRewardClaimRequested(int index) => RewardClaimRequested?.Invoke(index);
    }
}
