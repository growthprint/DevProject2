#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 QuestGiverView.RefreshInteractButton의 세 상태다. 받을 수 있음 탭은 수락, 진행 중 탭은 완료, 완료 탭과 선택 없음은 숨김이다.
    internal enum QuestGiverInteractMode
    {
        None,
        Accept,
        Complete,
    }

    // 원작 QuestGiverTabs.buttons 순서의 탭 번호다. 원작 상태 Avaliable, Active, Finished와 같은 순서이고 TabSelected와 SetTab이 이 값을 쓴다.
    internal enum QuestGiverTab
    {
        Available,
        Active,
        Finished,
    }

    // 원작 Duckov.Quests.UI.QuestGiverView(GameplayUICanvas/QuestGiverView)다. 탭 3개, 퀘스트 줄 목록, 빈 자리표시,
    // 수락·완료 버튼, 빨간 점 2개를 보인다. 상세(OriginalQuestDetailsView)와 완료 패널은 같은 창 안의 View다.
    // 열고 닫기, 입력 차단, 목록 내용, 명령은 화면 컨트롤러(QuestGiverScreen)가 정하고, 이 View는 값만 받아 의도만 알린다.
    // 원작 FadeGroup 전환 대신 활성 여부로 보이고 숨긴다. 수락·완료와 닫기 버튼은 저장된 Runtime Only OnClick이 부르고,
    // 번호만 다른 탭 버튼 3개만 코드로 구독한다(Docs/rules/ui.md).
    public sealed class OriginalQuestGiverView : MonoBehaviour
    {
        [SerializeField] RectTransform _entriesParent = null!;
        // 원작 entryPrefab이다. 별도 템플릿 프리팹 자산(QuestEntry)을 가리키고 _entriesParent 아래에 복제한다.
        [SerializeField] OriginalQuestEntryView _entryTemplate = null!;
        [SerializeField] GameObject _entryPlaceholder = null!;
        // 원작 탭 버튼(QuestGiverTabButton)의 button과 selectedIndicator다. 순서는 QuestGiverTab 값 순서다.
        [SerializeField] Button[] _tabButtons = Array.Empty<Button>();
        [SerializeField] GameObject[] _tabSelected = Array.Empty<GameObject>();
        [SerializeField] Button _interact = null!;
        [SerializeField] TMP_Text _interactText = null!;
        [SerializeField] Image _interactImage = null!;
        // 원작 btnText_AcceptQuest, btnText_CompleteQuest 키와 버튼 색이다. 제작 도구가 원작 값을 옮긴다.
        [SerializeField] string _acceptKey = "UI_Quest_Btn_Accept";
        [SerializeField] string _completeKey = "UI_Quest_Btn_Complete";
        [SerializeField] Color _interactableColor = new(.47770557f, .8962264f, .56048983f, 1f);
        [SerializeField] Color _uninteractableColor = new(.5f, .5f, .5f, 1f);
        // 원작 uninspectedAvaliableRedDot(받을 수 있고 본 적 없는 퀘스트)과 activeRedDot(확인이 필요한 진행 퀘스트)이다.
        [SerializeField] GameObject _availableRedDot = null!;
        [SerializeField] GameObject _activeRedDot = null!;
        [SerializeField] OriginalQuestDetailsView _details = null!;
        [SerializeField] OriginalQuestCompletePanelView _completePanel = null!;

        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        UiTemplateCollection<OriginalQuestEntryView>? _entries;
        QuestGiverInteractMode _mode;

        public event Action<int>? TabSelected;
        public event Action<int>? QuestSelected;
        public event Action? InteractRequested;
        public event Action? CloseRequested;

        internal OriginalQuestDetailsView Details => _details;
        internal OriginalQuestCompletePanelView CompletePanel => _completePanel;
        internal bool IsShown => gameObject.activeSelf;

        UiTemplateCollection<OriginalQuestEntryView> Entries => _entries ??= new UiTemplateCollection<OriginalQuestEntryView>(_entryTemplate, entry => entry.Unbind());

        // 창을 켜고 탭 버튼을 구독한다. 이미 보이면 구독을 겹치지 않는다. 처음 탭과 목록은 컨트롤러가 이어서 넣는다.
        // 번호만 다른 버튼은 다른 창과 같은 색인 구독 도우미로 묶고, 버튼 번호가 곧 탭 번호다.
        internal void Show()
        {
            if (gameObject.activeSelf) return;
            _subscriptions.BindIndexed(_tabButtons, tab => TabSelected?.Invoke(tab));
            gameObject.SetActive(true);
        }

        // 공용 HUD 초기화와 화면 닫기가 무조건 부르므로 보이지 않을 때도 안전하고 이벤트를 내지 않는다.
        internal void Hide()
        {
            _subscriptions.Clear();
            _completePanel.Hide();
            _details.Clear();
            _entries?.Clear();
            _mode = QuestGiverInteractMode.None;
            gameObject.SetActive(false);
        }

        // 원작 QuestGiverTabButton.Refresh다. 고른 탭의 선택 표시만 켠다.
        internal void SetTab(int tab) => UiVisibility.ShowOnly(_tabSelected, tab);

        // 원작 RefreshList와 RefreshEntries다. 정렬과 선택 유지는 컨트롤러가 정하고, 줄 채우기는 일지와 같은 도우미가 맡는다.
        internal void SetQuests(IReadOnlyList<QuestEntryValues> quests) =>
            OriginalQuestEntryView.Fill(Entries, _entriesParent, _entryPlaceholder, quests, OnQuestClicked);

        // 원작 RefreshRedDots다.
        internal void SetRedDots(bool available, bool active)
        {
            _availableRedDot.SetActive(available);
            _activeRedDot.SetActive(active);
        }

        // 원작 RefreshInteractButton이다. 수락은 늘 누를 수 있고, 완료는 과제가 모두 끝났을 때만 누를 수 있다.
        internal void SetInteract(QuestGiverInteractMode mode, bool interactable)
        {
            _mode = mode;
            bool shown = mode != QuestGiverInteractMode.None;
            _interact.gameObject.SetActive(shown);
            if (!shown) return;
            _interact.interactable = interactable;
            _interactImage.color = interactable ? _interactableColor : _uninteractableColor;
            _interactText.text = GameLocalization.Text(mode == QuestGiverInteractMode.Accept ? _acceptKey : _completeKey);
        }

        // 버튼은 SetInteract가 수락이나 완료로 켰을 때만 눌린다. 수락·완료 판정과 소리는 컨트롤러가 한다.
        public void OnInteractClicked()
        {
            Log.Instance.Assert(IsShown && _mode != QuestGiverInteractMode.None, "숨은 퀘스트 창구 버튼이 눌렸습니다.", this);
            InteractRequested?.Invoke();
        }

        // 원작 View.exitButton이다. 완료 패널이 열려 있을 때의 처리(원작 TryQuit)는 컨트롤러가 정한다.
        public void OnExitClicked()
        {
            Log.Instance.Assert(IsShown, "닫힌 퀘스트 창구의 닫기가 눌렸습니다.", this);
            CloseRequested?.Invoke();
        }

        void OnQuestClicked(int questId) => QuestSelected?.Invoke(questId);

        void OnDestroy() => _subscriptions.Clear();
    }
}
