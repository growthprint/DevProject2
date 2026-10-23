#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 QuestView.ShowContent 순서(Active 1, History 2)의 목록 번호다. 원작 QuestViewFlagButton 두 개의 순서이고
    // ContentSelected와 SetContent가 이 값을 쓴다.
    internal enum QuestJournalContent
    {
        Active,
        History,
    }

    // 원작 Duckov.Quests.UI.QuestView(GameplayUICanvas/Tabs/ViewArea/QuestVieｗ)다. 플레이어의 퀘스트 일지로, 진행 중·완료 목록 버튼 두 개,
    // 퀘스트 줄 목록, 빈 자리표시를 보인다. 상세(OriginalQuestDetailsView)는 원작처럼 같은 루트의 View다.
    // 열고 닫기, 입력 차단, 목록 내용과 선택은 화면 컨트롤러(QuestJournalScreen)가 정하고, 이 View는 값만 받아 의도만 알린다.
    // 원작 FadeGroup 전환 대신 활성 여부로 보이고 숨긴다. 닫기 버튼은 저장된 Runtime Only OnClick이 부르고,
    // 번호만 다른 목록 버튼 두 개만 코드로 구독한다(Docs/rules/ui.md). 원작 정렬 버튼은 동작 없이 복사돼 있다.
    public sealed class OriginalQuestView : MonoBehaviour
    {
        [SerializeField] RectTransform _entriesParent = null!;
        // 원작 questEntry다. 퀘스트 창구와 같은 별도 템플릿 프리팹 자산(QuestEntry)을 가리키고 _entriesParent 아래에 복제한다.
        [SerializeField] OriginalQuestEntryView _entryTemplate = null!;
        [SerializeField] GameObject _entryPlaceholder = null!;
        // 원작 QuestViewFlagButton의 button과 selectionIndicator다. 순서는 QuestJournalContent 값 순서다.
        [SerializeField] Button[] _contentButtons = Array.Empty<Button>();
        [SerializeField] GameObject[] _contentSelected = Array.Empty<GameObject>();
        [SerializeField] OriginalQuestDetailsView _details = null!;
        // 원작처럼 이 창은 Tabs/ViewArea 안에서 LootView의 형제로 열린다(쪽지 창 OriginalNoteIndexView 선례). 공용 HUD는 Tabs 활성을
        // 인벤토리 열림으로 쓰므로, 보이는 동안 Tabs를 켜고 LootView를 꺼서 인벤토리 칸이 함께 그려지지 않게 한다.
        [SerializeField] GameObject _tabs = null!;
        [SerializeField] GameObject _lootView = null!;

        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        UiTemplateCollection<OriginalQuestEntryView>? _entries;

        public event Action<int>? ContentSelected;
        public event Action<int>? QuestSelected;
        public event Action? CloseRequested;

        internal OriginalQuestDetailsView Details => _details;
        internal bool IsShown => gameObject.activeSelf;

        UiTemplateCollection<OriginalQuestEntryView> Entries => _entries ??= new UiTemplateCollection<OriginalQuestEntryView>(_entryTemplate, entry => entry.Unbind());

        // 창을 켜고 목록 버튼을 구독한다. 이미 보이면 구독을 겹치지 않는다. 목록 종류와 줄은 컨트롤러가 이어서 넣는다.
        // 번호만 다른 버튼은 다른 창과 같은 색인 구독 도우미로 묶고, 버튼 번호가 곧 목록 번호다.
        internal void Show()
        {
            if (gameObject.activeSelf) return;
            _subscriptions.BindIndexed(_contentButtons, content => ContentSelected?.Invoke(content));
            // 쪽지 창, 열쇠 도감과 같은 탭 창 공통 전환을 쓴다.
            UiVisibility.ShowTabView(gameObject, _tabs, _lootView);
        }

        // 공용 HUD 초기화와 화면 닫기가 무조건 부르므로 이벤트를 내지 않는다. Tabs와 LootView는 공통 전환이 보이고 있었을 때만 되돌린다.
        internal void Hide()
        {
            _subscriptions.Clear();
            _details.Clear();
            _entries?.Clear();
            UiVisibility.HideTabView(gameObject, _tabs, _lootView);
        }

        // 원작 QuestViewFlagButton.Refresh다. 고른 목록의 선택 표시만 켠다.
        internal void SetContent(int content) => UiVisibility.ShowOnly(_contentSelected, content);

        // 원작 RefreshEntryList다. 순서와 선택은 컨트롤러가 정하고, 줄 채우기는 퀘스트 창구와 같은 도우미가 맡는다.
        internal void SetQuests(IReadOnlyList<QuestEntryValues> quests) =>
            OriginalQuestEntryView.Fill(Entries, _entriesParent, _entryPlaceholder, quests, OnQuestClicked);

        // 원작 View.exitButton이다. 닫기는 컨트롤러가 입력 차단과 함께 정한다.
        public void OnExitClicked()
        {
            Log.Instance.Assert(IsShown, "닫힌 퀘스트 일지의 닫기가 눌렸습니다.", this);
            CloseRequested?.Invoke();
        }

        void OnQuestClicked(int questId) => QuestSelected?.Invoke(questId);

        void OnDestroy() => _subscriptions.Clear();
    }
}
