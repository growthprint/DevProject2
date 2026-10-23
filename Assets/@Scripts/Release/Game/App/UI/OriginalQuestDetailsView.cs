#nullable enable annotations
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 QuestViewDetails.RefreshAsync가 읽는 퀘스트 값이다. 화면 컨트롤러가 소유자 조회와 번역으로 만든다.
    internal readonly struct QuestDetailsValues
    {
        internal QuestDetailsValues(string name, string giverName, string description, string? requiredItemName, Sprite? requiredItemIcon, int requiredItemCount)
        {
            Name = name;
            GiverName = giverName;
            Description = description;
            RequiredItemName = requiredItemName;
            RequiredItemIcon = requiredItemIcon;
            RequiredItemCount = requiredItemCount;
        }

        internal string Name { get; }
        internal string GiverName { get; }
        internal string Description { get; }
        // 원작 QuestRequiredItem.Set의 아이템 표시 이름이다. 아이템이 없거나 목록에 없으면 null이고, 그러면 줄을 숨긴다.
        internal string? RequiredItemName { get; }
        internal Sprite? RequiredItemIcon { get; }
        internal int RequiredItemCount { get; }
    }

    // 원작 Duckov.Quests.UI.QuestViewDetails다. 원작처럼 QuestGiverView 루트에 붙고, 고른 퀘스트의 이름, 창구 이름, 설명,
    // 필요 아이템, 과제 줄, 보상 줄을 보인다. 퀘스트가 없으면 자리표시만 보인다. 원작 FadeGroup 전환 대신 활성 여부로 바꾼다.
    // "확인함" 기록(원작 NeedInspection = false)은 화면 컨트롤러가 명령으로 보낸다.
    public sealed class OriginalQuestDetailsView : MonoBehaviour
    {
        // 원작 taskEntryPrefab과 rewardEntry다. 둘 다 별도 템플릿 프리팹 자산(TaskEntry, RewardEntry)을 가리킨다.
        [SerializeField] OriginalTaskEntryView _taskTemplate = null!;
        [SerializeField] OriginalRewardEntryView _rewardTemplate = null!;
        [SerializeField] GameObject _placeholder = null!;
        // 원작 contentFadeGroup이 붙은 내용 영역이다.
        [SerializeField] GameObject _content = null!;
        [SerializeField] TMP_Text _displayName = null!;
        [SerializeField] TMP_Text _description = null!;
        [SerializeField] TMP_Text _giverName = null!;
        [SerializeField] Transform _tasksParent = null!;
        [SerializeField] Transform _rewardsParent = null!;
        // 원작 QuestRequiredItem의 객체, icon, text다.
        [SerializeField] GameObject _requiredItem = null!;
        [SerializeField] Image _requiredIcon = null!;
        [SerializeField] TMP_Text _requiredText = null!;

        UiTemplateCollection<OriginalTaskEntryView>? _tasks;
        UiTemplateCollection<OriginalRewardEntryView>? _rewards;

        public event Action<int>? TaskInteractRequested;
        public event Action<int>? RewardClaimRequested;

        UiTemplateCollection<OriginalTaskEntryView> Tasks => _tasks ??= new UiTemplateCollection<OriginalTaskEntryView>(_taskTemplate, entry => entry.Unbind());
        UiTemplateCollection<OriginalRewardEntryView> Rewards => _rewards ??= new UiTemplateCollection<OriginalRewardEntryView>(_rewardTemplate, entry => entry.Unbind());

        // 원작 target이 null일 때다. 공용 HUD 초기화도 부르므로 이벤트를 내지 않는다.
        internal void Clear()
        {
            _placeholder.SetActive(true);
            _content.SetActive(false);
            _tasks?.Clear();
            _rewards?.Clear();
        }

        // interactable은 원작 QuestGiverView.RefreshDetails의 값(진행 중이거나 완료한 퀘스트)이다. 과제 줄과 보상 줄에 그대로 넘긴다.
        internal void Show(in QuestDetailsValues values, IReadOnlyList<QuestTaskValues> tasks, IReadOnlyList<QuestRewardValues> rewards, bool interactable)
        {
            _placeholder.SetActive(false);
            _content.SetActive(true);
            _displayName.text = values.Name;
            _giverName.text = values.GiverName;
            _description.text = values.Description;
            // 원작 QuestRequiredItem.Set이다. 수가 0 이하이거나 아이템이 없으면 숨기고, 있으면 "이름 x수"를 보인다.
            bool required = values.RequiredItemName != null && values.RequiredItemCount > 0;
            if (required)
            {
                _requiredIcon.sprite = values.RequiredItemIcon;
                _requiredText.text = values.RequiredItemName + " x" + values.RequiredItemCount;
            }
            _requiredItem.SetActive(required);
            UiTemplateCollection<OriginalTaskEntryView> taskEntries = Tasks;
            taskEntries.Begin();
            for (int index = 0; index < tasks.Count; index++)
            {
                OriginalTaskEntryView entry = taskEntries.Acquire(_tasksParent);
                entry.gameObject.SetActive(true);
                entry.Interactable = interactable;
                entry.Bind(tasks[index], index, OnTaskInteractRequested);
            }
            taskEntries.End();
            UiTemplateCollection<OriginalRewardEntryView> rewardEntries = Rewards;
            rewardEntries.Begin();
            for (int index = 0; index < rewards.Count; index++)
            {
                OriginalRewardEntryView entry = rewardEntries.Acquire(_rewardsParent);
                entry.gameObject.SetActive(true);
                entry.Interactable = interactable;
                entry.Bind(rewards[index], index, OnRewardClaimRequested);
            }
            rewardEntries.End();
        }

        void OnTaskInteractRequested(int index) => TaskInteractRequested?.Invoke(index);
        void OnRewardClaimRequested(int index) => RewardClaimRequested?.Invoke(index);
    }
}
