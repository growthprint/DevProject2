#nullable enable annotations
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 원작 QuestGiverView와 QuestCompletePanel의 명령 쪽이다. 창은 공용 HUD의 OriginalQuestGiverView이고 값만 받아 그린다.
    // 열린 동안 게임플레이 입력을 막고 ESC(원작 TryQuit)로 닫는다(NoteScreen 선례). 수락, 완료, 제출, 보상 받기는 소유자와
    // GameplayCommands가 확인하고 확정하며, 이 화면은 성공했을 때만 소리와 완료 패널을 보인다.
    // 과제 알림과 상점 해금 알림은 화면이 닫혀 있어도 뜨므로 화면 묶음이 만든 QuestNotices가 낸다.
    internal sealed class QuestGiverScreen : GameplayScreen
    {
        // 창의 TabSelected와 SetTab이 쓰는 원작 QuestGiverTabs 버튼 번호다. 첫 탭이 열 때의 기본값이다.
        const int _availableTab = (int)QuestGiverTab.Available;
        const int _activeTab = (int)QuestGiverTab.Active;
        const int _historyTab = (int)QuestGiverTab.Finished;
        // 원작 sfx_AcceptQuest, sfx_CompleteQuest의 FMOD 이벤트다.
        const string _acceptSound = "event:/UI/mission_accept";
        const string _completeSound = "event:/UI/mission_large";

        readonly GameplayWorldContext _world;
        // 제출과 보상 받기가 성공한 뒤 과제 알림과 해금 알림을 내는 객체다. 화면 묶음과 수명이 같다.
        readonly QuestNotices _notices;
        // 연 동안 붙인 구독을 닫을 때 한 번에 뗀다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 연 동안 구독한 창이다. 닫을 때 같은 객체에서 끊으려고 잡아 둔다.
        OriginalQuestGiverView? _view;
        QuestGiver? _giver;
        int _tab;
        // 원작 selectedQuestEntry 자리다. 목록이 바뀌어 그 퀘스트가 지금 탭에서 빠지면 원작처럼 선택을 지운다.
        int? _selected;
        // 완료 패널이 보이는 퀘스트다. 원작 QuestCompletePanel.target이고, 값이 있으면 패널이 열려 있다.
        int? _panelQuest;

        internal QuestGiverScreen(GameplayWorldContext world, QuestNotices notices)
        {
            _world = world;
            _notices = notices;
        }

        // 원작 QuestGiver.OnInteractStart의 Setup과 Open이다. 열 때 첫 탭과 선택 없음으로 시작한다.
        // 공용 HUD가 이 월드에 붙어 있지 않거나 입력 차단을 얻지 못하면 열지 않는다. 이미 열려 있으면 대상만 바꾼다.
        internal void Show(QuestGiver giver)
        {
            if (!_world.CanPresent) return;
            if (!IsOpen)
            {
                if (!TryBlockInput(_world.Runtime)) return;
                OriginalQuestGiverView view = _world.Hud.QuestGiver;
                _view = view;
                Subscribe(view);
            }
            ClosePanel();
            _giver = giver;
            _tab = _availableTab;
            _selected = null;
            _view!.Show();
            _view.SetTab(_tab);
            Refresh();
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다. 기지 밖에는 자동 저장이 없어 무시된다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 입력 차단과 구독을 돌려주고 창을 닫는다. 완료 패널이 열려 있으면 원작 패널 닫기처럼 자동 보상을 먼저 받는다.
        // 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다.
        internal override void Close()
        {
            if (!IsOpen) return;
            _subscriptions.Clear();
            ClosePanel();
            ReleaseInput();
            if (_view != null && _world.PresentationAttached) _view.Hide();
            _view = null;
            _giver = null;
            _selected = null;
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 QuestGiverView.TryQuit이다. 완료 패널이 열려 있으면 Skip만 하고 화면은 남긴다. ESC 처리가 묻는다.
        internal override bool TryClose()
        {
            if (!IsOpen) return false;
            if (_panelQuest.HasValue) ClosePanel();
            else Close();
            return true;
        }

        // 붙인 순서대로 떼도록 짝마다 해제를 등록한다. 창 이벤트는 C# 구독이라 HUD가 먼저 파괴됐어도 뗀다.
        void Subscribe(OriginalQuestGiverView view)
        {
            QuestManager.Changed += Refresh;
            _subscriptions.Add(() => QuestManager.Changed -= Refresh);
            _subscriptions.BindLocaleChanged(Refresh);
            view.TabSelected += SelectTab;
            view.QuestSelected += SelectQuest;
            view.InteractRequested += Interact;
            view.CloseRequested += Close;
            view.Details.TaskInteractRequested += InteractTask;
            view.Details.RewardClaimRequested += ClaimDetailsReward;
            view.CompletePanel.RewardClaimRequested += ClaimPanelReward;
            view.CompletePanel.TakeAllRequested += TakeAll;
            view.CompletePanel.SkipRequested += ClosePanel;
            _subscriptions.Add(() =>
            {
                view.TabSelected -= SelectTab;
                view.QuestSelected -= SelectQuest;
                view.InteractRequested -= Interact;
                view.CloseRequested -= Close;
                view.Details.TaskInteractRequested -= InteractTask;
                view.Details.RewardClaimRequested -= ClaimDetailsReward;
                view.CompletePanel.RewardClaimRequested -= ClaimPanelReward;
                view.CompletePanel.TakeAllRequested -= TakeAll;
                view.CompletePanel.SkipRequested -= ClosePanel;
            });
        }

        // 원작 OnTabChanged다. 다른 탭에는 같은 퀘스트가 없으므로 선택을 지우고 다시 그린다.
        // 번호는 창의 탭 버튼 번호뿐이라 범위를 보지 않는다. 버튼 수는 QuestAndMapScreenAuthoringTests가 본다.
        void SelectTab(int tab)
        {
            if (_view == null) return;
            _tab = tab;
            _selected = null;
            _view.SetTab(tab);
            Refresh();
        }

        // 원작 SetSelection의 SetEverInspected와 QuestViewDetails.Setup의 NeedInspection = false다. 지금 탭에 없는 줄의 늦은 클릭은 무시한다.
        // 두 명령은 관문이 닫혔으면 거절되지만 선택은 화면 상태라 그대로 바꾼다.
        void SelectQuest(int questId)
        {
            if (_giver == null || !GameManager.HasInstance) return;
            QuestManager quests = GameManager.Quests;
            // 목록 확인은 일지 화면과 같은 퀘스트 표시 도우미를 쓴다.
            if (!QuestDisplayValues.Contains(TabQuests(quests, _giver.GiverId, _tab), questId)) return;
            _selected = questId;
            quests.TryMarkInspected(questId);
            quests.TryClearNeedInspection(questId);
            Refresh();
        }

        // 원작 OnInteractButtonClicked다. 수락은 소유자가 가용성과 창구를 다시 확인한다. 완료가 성공하면 완료 패널을 연다.
        void Interact()
        {
            if (_giver == null || !_selected.HasValue || !GameManager.HasInstance) return;
            QuestManager quests = GameManager.Quests;
            int questId = _selected.Value;
            if (_tab == _availableTab)
            {
                if (quests.TryActivate(questId, _giver.GiverId)) _world.Audio.PlayOneShot(_acceptSound);
            }
            else if (_tab == _activeTab && quests.TryComplete(questId))
            {
                OpenPanel(questId);
                _world.Audio.PlayOneShot(_completeSound);
            }
        }

        // 원작 TaskEntry 버튼의 task.Interact()다. 제출 과제 두 종류만 버튼이 있다.
        // 과제 번호는 상세 창이 이 화면이 고른 퀘스트의 과제 목록에 붙인 줄 번호라 늘 범위 안이다.
        void InteractTask(int taskIndex)
        {
            if (!_selected.HasValue || !_world.TrySession(out GameSessionManager? session)) return;
            QuestManager quests = GameManager.Quests;
            int questId = _selected.Value;
            if (!quests.Catalog.TryGet(questId, out OriginalQuestDefinition? quest)) return;
            OriginalQuestTaskDefinition task = quest.Tasks[taskIndex];
            if (task.Kind == QuestTaskKind.SubmitItems)
            {
                // 원작 SubmitItems.Submit은 낸 수가 바뀌면 끝나지 않아도 Master.NotifyTaskFinished를 불러 과제 알림과 소리를 낸다.
                // 그래서 일부만 내도 알림을 띄운다(2-Q3 열린 항목의 결정). 과제가 끝나면 소유자의 TaskFinished가 한 번 더 내지만, 같은 문구는
                // 원작 NotificationText.Push처럼 대기열 맨 앞과 같으면 다시 넣지 않는다. 원작은 아이템 묶음마다 알리고 여기서는 명령마다 한 번이다.
                if (GameplayCommands.TrySubmitQuestItems(session!, quests, questId, task.Id)) _notices.NotifyTaskProgress(questId);
            }
            else if (task.Kind == QuestTaskKind.SubmitMoney)
                GameplayCommands.TrySubmitQuestMoney(session!, GameManager.Economy, quests, questId, task.Id);
        }

        // 원작 RewardEntry 클릭이다. 상세의 보상은 완료한 퀘스트에서만 버튼이 있고, 받기 판정은 소유자가 한다.
        void ClaimDetailsReward(int rewardIndex)
        {
            if (_selected.HasValue) TryClaim(_selected.Value, rewardIndex);
        }

        // 원작 완료 패널은 보상을 모두 받으면 기다림을 끝내고 닫힌다.
        void ClaimPanelReward(int rewardIndex)
        {
            if (!_panelQuest.HasValue) return;
            int questId = _panelQuest.Value;
            if (TryClaim(questId, rewardIndex) && AllClaimed(GameManager.Quests, questId)) ClosePanel();
        }

        // 원작 Take All이다. 소유자 명령은 모두 주거나 아무것도 주지 않으므로 성공하면 모두 받은 것이고 패널을 닫는다.
        void TakeAll()
        {
            if (!_panelQuest.HasValue || !_world.TrySession(out GameSessionManager? session)) return;
            int questId = _panelQuest.Value;
            // 원작 EconomyManager.Unlock 알림을 받기가 성공한 뒤에만 띄우려고 지금 잠긴 품목을 먼저 고른다. 알림은 QuestNotices가 낸다.
            List<int>? locked = QuestNotices.LockedShopItems(questId);
            if (!GameplayCommands.TryClaimAllQuestRewards(session!, GameManager.Quests, GameManager.Experience, GameManager.Economy,
                    _world.Catalog, questId)) return;
            _notices.NotifyUnlocked(locked);
            ClosePanel();
        }

        // 보상 번호는 상세 창과 완료 패널이 이 화면이 넘긴 보상 목록에 붙인 줄 번호라 늘 범위 안이다.
        bool TryClaim(int questId, int rewardIndex)
        {
            if (!_world.TrySession(out GameSessionManager? session)) return false;
            QuestManager quests = GameManager.Quests;
            if (!quests.Catalog.TryGet(questId, out OriginalQuestDefinition? quest)) return false;
            // 해금 알림은 받기 전에 고른 잠긴 품목으로 QuestNotices가 낸다.
            List<int>? locked = QuestNotices.LockedShopItems(questId);
            if (!GameplayCommands.TryClaimQuestReward(session!, quests, GameManager.Experience, GameManager.Economy, _world.Catalog,
                    questId, quest.Rewards[rewardIndex].Id)) return false;
            _notices.NotifyUnlocked(locked);
            return true;
        }

        // 원작 ShowCompleteUI다. 보상이 없는 퀘스트는 원작 WaitForEndOfInteraction처럼 바로 끝나 패널을 닫는다.
        void OpenPanel(int questId)
        {
            QuestManager quests = GameManager.Quests;
            quests.Catalog.TryGet(questId, out OriginalQuestDefinition? quest);
            _panelQuest = questId;
            // 같은 호출에서 TryComplete가 정의를 찾았으므로 늘 있다. 보상 줄 값은 일지와 같은 퀘스트 표시 도우미가 만든다.
            _view!.CompletePanel.Show(GameLocalization.TextOrEmpty(quest!.NameKey), QuestDisplayValues.Rewards(quests, _world.Catalog, quest));
            if (AllClaimed(quests, questId)) ClosePanel();
        }

        // 원작 QuestCompletePanel.Show의 끝부분이다. 받지 않은 AutoClaim 보상을 받은 뒤 숨긴다. Skip, 모두 받음, 화면 닫기가 부른다.
        // 씬 정리 중 앱이 먼저 사라졌으면 보상 명령 없이 상태만 지운다.
        void ClosePanel()
        {
            if (!_panelQuest.HasValue) return;
            int questId = _panelQuest.Value;
            _panelQuest = null;
            // 자동 보상에 상점 해금이 있으면 원작처럼 받은 뒤 QuestNotices가 해금 알림을 띄운다.
            if (GameManager.HasInstance)
            {
                List<int>? locked = QuestNotices.LockedShopItems(questId);
                if (GameplayCommands.TryClaimQuestAutoRewards(GameManager.Quests, GameManager.Experience, GameManager.Economy, questId))
                    _notices.NotifyUnlocked(locked);
            }
            if (_view != null && _world.PresentationAttached) _view.CompletePanel.Hide();
        }

        // 원작 RefreshList, RefreshRedDots, RefreshInteractButton, RefreshDetails를 한 번에 한다. 소유자는 변경 신호 하나만 내므로
        // 원작의 목록 변경과 상태 변경 둘 다 여기로 온다. 선택한 퀘스트가 지금 탭에 남아 있으면 원작처럼 선택을 유지한다.
        // 앱 루트와 공용 HUD 연결은 월드 문맥의 표시 가능 판정 하나로 본다.
        void Refresh()
        {
            if (!IsOpen || _view == null || _giver == null || !_world.CanPresent) return;
            QuestManager quests = GameManager.Quests;
            QuestGiverId giver = _giver.GiverId;
            IReadOnlyList<int> ids = TabQuests(quests, giver, _tab);
            // 목록 확인과 줄, 보상 값은 일지와 같은 퀘스트 표시 도우미가 만든다.
            if (_selected.HasValue && !QuestDisplayValues.Contains(ids, _selected.Value)) _selected = null;
            var entries = new List<QuestEntryValues>(ids.Count);
            for (int index = 0; index < ids.Count; index++)
            {
                int id = ids[index];
                // 창구별 목록은 정의가 있는 퀘스트만 담는다. 번호 0000 형식은 줄 View가 만든다.
                quests.Catalog.TryGet(id, out OriginalQuestDefinition? quest);
                entries.Add(QuestDisplayValues.Entry(quests, quest!, _selected == id));
            }
            _view.SetQuests(entries);
            _view.SetRedDots(AnyUnseenAvailable(quests, giver), quests.AnyNeedsInspection(giver));
            RefreshButton(quests);
            RefreshDetails(quests);
            if (_panelQuest.HasValue && quests.Catalog.TryGet(_panelQuest.Value, out OriginalQuestDefinition? panelQuest))
                _view.CompletePanel.Present(GameLocalization.TextOrEmpty(panelQuest.NameKey), QuestDisplayValues.Rewards(quests, _world.Catalog, panelQuest));
        }

        // 원작 RefreshInteractButton이다. 받을 수 있음 탭은 늘 누를 수 있는 수락, 진행 중 탭은 과제가 끝나야 누를 수 있는 완료, 완료 탭은 숨김이다.
        void RefreshButton(QuestManager quests)
        {
            if (!_selected.HasValue || _tab == _historyTab)
            {
                _view!.SetInteract(QuestGiverInteractMode.None, false);
                return;
            }
            bool accept = _tab == _availableTab;
            _view!.SetInteract(accept ? QuestGiverInteractMode.Accept : QuestGiverInteractMode.Complete,
                accept || quests.AreTasksFinished(_selected.Value));
        }

        // 원작 QuestViewDetails다. Interactable은 진행 중이거나 완료한 퀘스트이고 과제 줄과 보상 줄에 그대로 넘긴다.
        void RefreshDetails(QuestManager quests)
        {
            OriginalQuestDetailsView details = _view!.Details;
            if (!_selected.HasValue || !quests.Catalog.TryGet(_selected.Value, out OriginalQuestDefinition? quest))
            {
                details.Clear();
                return;
            }
            int id = quest.Id;
            OriginalGameplayCatalog catalog = _world.Catalog;
            _world.TrySession(out GameSessionManager? session);
            // 상세, 과제 설명, 보상 줄 값은 일지와 같은 퀘스트 표시 도우미가 만들고, 창구만 제출 버튼 값을 더한다.
            QuestDetailsValues values = QuestDisplayValues.Details(catalog, quest);
            IReadOnlyList<OriginalQuestTaskDefinition> tasks = quest.Tasks;
            var taskValues = new List<QuestTaskValues>(tasks.Count);
            for (int index = 0; index < tasks.Count; index++)
            {
                OriginalQuestTaskDefinition task = tasks[index];
                bool finished = quests.IsTaskFinished(id, task.Id);
                bool submit = task.Kind == QuestTaskKind.SubmitItems || task.Kind == QuestTaskKind.SubmitMoney;
                string description = QuestDisplayValues.TaskText(quests, catalog, session, id, task, finished, out List<string>? extras);
                taskValues.Add(new QuestTaskValues(description, extras, QuestDisplayValues.TaskIcon(catalog, task), finished, submit,
                    submit && CanSubmit(session, task),
                    GameLocalization.Text(task.Kind == QuestTaskKind.SubmitMoney ? "QuestTask_SubmitMoney_Interact" : "Task_SubmitItems_Interact")));
            }
            // 원작 RefreshDetails의 Interactable은 진행 중이거나 완료한 퀘스트다. 버튼과 받을 수 있음 표시는 View가 이 값으로 고른다.
            details.Show(values, taskValues, QuestDisplayValues.Rewards(quests, catalog, quest), quests.IsActive(id) || quests.IsFinished(id));
        }

        static IReadOnlyList<int> TabQuests(QuestManager quests, QuestGiverId giver, int tab) => tab switch
        {
            _availableTab => quests.AvailableFor(giver),
            _activeTab => quests.ActiveFor(giver),
            _ => quests.FinishedFor(giver)
        };

        // 원작 AnyUninspectedAvaliableQuest다. 진행 전 퀘스트의 NeedInspection은 본 적 없음이다.
        static bool AnyUnseenAvailable(QuestManager quests, QuestGiverId giver)
        {
            IReadOnlyList<int> ids = quests.AvailableFor(giver);
            for (int index = 0; index < ids.Count; index++)
                if (quests.NeedsInspection(ids[index])) return true;
            return false;
        }

        static bool AllClaimed(QuestManager quests, int questId)
        {
            if (!quests.Catalog.TryGet(questId, out OriginalQuestDefinition? quest)) return true;
            for (int index = 0; index < quest.Rewards.Count; index++)
                if (!quests.IsRewardClaimed(questId, quest.Rewards[index].Id)) return false;
            return true;
        }

        // 원작 PossibleValidInteraction이다. 아이템은 남은 수가 아니라 요구 수 전체를 지금 가진 수와 비교하고(CheckItemEnough),
        // 돈은 원작 new Cost(money).Enough처럼 계좌와 현금을 함께 본다.
        bool CanSubmit(GameSessionManager? session, OriginalQuestTaskDefinition task)
        {
            if (session == null) return false;
            // 출처는 제출 명령과 같은 세션 기본 출처 규칙으로 정한다.
            ItemSources sources = ItemSourceRules.Owned(session);
            if (task.Kind == QuestTaskKind.SubmitItems) return session.Inventory.CountOwned(task.ItemId, sources) >= task.RequiredCount;
            return GameplayCommands.CanPay(session, GameManager.Economy, new Cost(task.Money, Array.Empty<Cost.ItemEntry>()), sources, true);
        }
    }
}
