#nullable enable annotations
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Game.App
{
    // 원작 QuestView(플레이어 퀘스트 일지)의 명령 쪽이다. 창은 공용 HUD의 OriginalQuestView이고 값만 받아 그린다.
    // 열린 동안 게임플레이 입력을 막고 ESC와 닫기 버튼으로 닫는다(NoteScreen 선례). 원작 QuestViewDetails.interactable이 0이라
    // 제출과 보상 받기 버튼이 없고, 보내는 명령은 상세를 보일 때의 "확인함"(원작 NeedInspection = false) 하나다.
    internal sealed class QuestJournalScreen : GameplayScreen
    {
        const int _activeContent = (int)QuestJournalContent.Active;

        readonly GameplayWorldContext _world;
        // 연 동안 붙인 구독을 닫을 때 한 번에 뗀다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 연 동안 구독한 창이다. 닫을 때 같은 객체에서 끊으려고 잡아 둔다.
        OriginalQuestView? _view;
        // 원작 showingContentType(LevelManager 저장값 1 = Active)과 selectedQuestEntry다. 원작 창은 레벨 동안 남아
        // 닫았다 열어도 둘을 유지하므로, 이 화면도 월드 하나의 수명 동안 유지한다.
        int _content = _activeContent;
        int? _selected;

        internal QuestJournalScreen(GameplayWorldContext world)
        {
            _world = world;
        }

        // 원작 CharacterInputControl.OnUIQuestViewInput의 열기와 닫기다. 원작은 다른 창이 열려 있으면 아무것도 하지 않는데,
        // 그 판정은 입력을 잇는 쪽(GameplayScreens)이 한다.
        internal void Toggle()
        {
            if (IsOpen) Close();
            else Show();
        }

        // 입력 차단과 구독을 돌려주고 창을 닫는다. 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다.
        // ESC는 원작 QuestView가 TryQuit을 바꾸지 않으므로 기본 TryClose처럼 열려 있으면 닫고 true를 돌려준다.
        internal override void Close()
        {
            if (!IsOpen) return;
            _subscriptions.Clear();
            ReleaseInput();
            if (_view != null && _world.PresentationAttached) _view.Hide();
            _view = null;
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 QuestView.Show의 OnOpen과 Setup이다. 앞 목록 종류와 선택을 유지하고, 선택한 퀘스트가 목록에 없으면 선택을 지운다.
        // 공용 HUD가 이 월드에 붙어 있지 않거나 입력 차단을 얻지 못하면 열지 않는다.
        void Show()
        {
            if (!_world.CanPresent) return;
            if (!TryBlockInput(_world.Runtime)) return;
            OriginalQuestView view = _world.Hud.QuestView;
            _view = view;
            Subscribe(view);
            view.Show();
            view.SetContent(_content);
            Refresh();
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다. 기지 밖에는 자동 저장이 없어 무시된다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 붙인 순서대로 떼도록 짝마다 해제를 등록한다. 창 이벤트는 C# 구독이라 HUD가 먼저 파괴됐어도 뗀다.
        void Subscribe(OriginalQuestView view)
        {
            QuestManager.Changed += Refresh;
            _subscriptions.Add(() => QuestManager.Changed -= Refresh);
            _subscriptions.BindLocaleChanged(Refresh);
            view.ContentSelected += SelectContent;
            view.QuestSelected += SelectQuest;
            view.CloseRequested += Close;
            _subscriptions.Add(() =>
            {
                view.ContentSelected -= SelectContent;
                view.QuestSelected -= SelectQuest;
                view.CloseRequested -= Close;
            });
        }

        // 원작 SetShowingContent다. 목록을 바꾸면 첫 줄을 고르고, 비었으면 선택을 지운다.
        // 번호는 창의 목록 버튼 번호뿐이라 범위를 보지 않는다. 버튼 수는 QuestAndMapScreenAuthoringTests가 본다.
        void SelectContent(int content)
        {
            if (_view == null || !GameManager.HasInstance) return;
            _content = content;
            IReadOnlyList<int> ids = ContentQuests(GameManager.Quests, content);
            _selected = ids.Count > 0 ? ids[0] : null;
            _view.SetContent(content);
            Refresh();
        }

        // 원작 SetSelection이다. 창구와 달리 SetEverInspected는 부르지 않는다. 지금 목록에 없는 줄의 늦은 클릭은 무시한다.
        void SelectQuest(int questId)
        {
            // 목록 확인은 창구 화면과 같은 퀘스트 표시 도우미를 쓴다.
            if (_view == null || !GameManager.HasInstance || !QuestDisplayValues.Contains(ContentQuests(GameManager.Quests, _content), questId)) return;
            _selected = questId;
            Refresh();
        }

        // 원작 RefreshEntryList와 RefreshDetails를 한 번에 한다. 소유자는 변경 신호 하나만 내므로 원작의 목록 변경과 상태 변경 둘 다 여기로 온다.
        // 앱 루트와 공용 HUD 연결은 월드 문맥의 표시 가능 판정 하나로 본다.
        void Refresh()
        {
            if (!IsOpen || _view == null || !_world.CanPresent) return;
            QuestManager quests = GameManager.Quests;
            IReadOnlyList<int> ids = ContentQuests(quests, _content);
            // 목록 확인은 창구 화면과 같은 퀘스트 표시 도우미를 쓴다.
            if (_selected.HasValue && !QuestDisplayValues.Contains(ids, _selected.Value)) _selected = null;
            // 원작 QuestViewDetails.RefreshAsync는 상세를 그릴 때마다 NeedInspection = false로 둔다. 저장 값이 지워지면 소유자의 Changed가
            // 이 메서드를 한 번 더 부르고, 그 호출도 같은 값을 그린다. 지울 값이 없으면 소유자는 신호를 내지 않는다.
            if (_selected.HasValue) quests.TryClearNeedInspection(_selected.Value);
            var entries = new List<QuestEntryValues>(ids.Count);
            for (int index = 0; index < ids.Count; index++)
            {
                // 목록은 정의가 있는 퀘스트만 담는다.
                quests.Catalog.TryGet(ids[index], out OriginalQuestDefinition? quest);
                entries.Add(QuestDisplayValues.Entry(quests, quest!, _selected == ids[index]));
            }
            _view.SetQuests(entries);
            RefreshDetails(quests);
        }

        // 원작 QuestViewDetails.Setup(SelectedQuest)이다. 일지의 상세는 원작 interactable 0이라 과제 줄과 보상 줄에 거짓을 넘긴다.
        void RefreshDetails(QuestManager quests)
        {
            OriginalQuestDetailsView details = _view!.Details;
            if (!_selected.HasValue || !quests.Catalog.TryGet(_selected.Value, out OriginalQuestDefinition? quest))
            {
                details.Clear();
                return;
            }
            OriginalGameplayCatalog catalog = _world.Catalog;
            _world.TrySession(out GameSessionManager? session);
            IReadOnlyList<OriginalQuestTaskDefinition> tasks = quest.Tasks;
            var taskValues = new List<QuestTaskValues>(tasks.Count);
            for (int index = 0; index < tasks.Count; index++)
            {
                OriginalQuestTaskDefinition task = tasks[index];
                bool finished = quests.IsTaskFinished(quest.Id, task.Id);
                bool submit = task.Kind == QuestTaskKind.SubmitItems || task.Kind == QuestTaskKind.SubmitMoney;
                string description = QuestDisplayValues.TaskText(quests, catalog, session, quest.Id, task, finished, out List<string>? extras);
                // 버튼은 줄이 상호작용 가능할 때만 보이므로 지금 유효 여부와 버튼 글자는 계산하지 않는다.
                taskValues.Add(new QuestTaskValues(description, extras, QuestDisplayValues.TaskIcon(catalog, task), finished, submit, false, ""));
            }
            details.Show(QuestDisplayValues.Details(catalog, quest), taskValues, QuestDisplayValues.Rewards(quests, catalog, quest), false);
        }

        // 원작 ActiveQuests와 HistoryQuests다. 소유자 기록은 수락 순서로 붙고 완료하면 끝으로 가므로, 상태로 거르면 원작의
        // 두 목록 순서(수락 순, 완료 순)와 같다. 정의 없는 저장 퀘스트는 창구처럼 보이지 않는다. 호출마다 새 목록이다.
        static IReadOnlyList<int> ContentQuests(QuestManager quests, int content)
        {
            bool active = content == _activeContent;
            var ids = new List<int>();
            IReadOnlyList<QuestProgress> records = quests.Records;
            for (int index = 0; index < records.Count; index++)
            {
                QuestProgress record = records[index];
                if ((record.Status == QuestStatus.Active) == active && quests.Catalog.TryGet(record.OriginalQuestId, out _)) ids.Add(record.OriginalQuestId);
            }
            return ids;
        }
    }

    // 퀘스트 정의와 소유자 조회를 퀘스트 줄, 상세, 과제 설명, 보상 줄 값으로 바꾼다. 일지와 창구 화면이 같은 원작 문구를 쓰므로 한곳에 둔다.
    internal static class QuestDisplayValues
    {
        // 두 퀘스트 화면이 고른 퀘스트가 지금 목록에 있는지 본다. 목록은 호출마다 새로 만드는 작은 ID 목록이다.
        internal static bool Contains(IReadOnlyList<int> ids, int id)
        {
            for (int index = 0; index < ids.Count; index++)
                if (ids[index] == id) return true;
            return false;
        }

        // 원작 QuestEntry.Refresh다. 받을 보상 표시는 원작 Complete || AreTasksFinished다.
        internal static QuestEntryValues Entry(QuestManager quests, OriginalQuestDefinition quest, bool selected)
        {
            int id = quest.Id;
            return new QuestEntryValues(id, Text(quest.NameKey), Location(quest), quests.NeedsInspection(id),
                quests.IsFinished(id) || quests.AreTasksFinished(id), selected);
        }

        // 원작 QuestViewDetails.RefreshAsync의 이름, 창구 이름, 설명, 필요 아이템이다. 아이템이 목록에 없으면 필요 아이템 줄을 숨긴다.
        internal static QuestDetailsValues Details(OriginalGameplayCatalog catalog, OriginalQuestDefinition quest)
        {
            bool hasItem = quest.RequiredItemId > 0 && catalog.TryGet(quest.RequiredItemId, out _);
            return new QuestDetailsValues(Text(quest.NameKey), Text("Character_" + quest.Giver), Text(quest.DescriptionKey),
                hasItem ? ItemName(catalog, quest.RequiredItemId) : null, hasItem ? ItemIcon(catalog, quest.RequiredItemId) : null,
                hasItem ? quest.RequiredItemCount : 0);
        }

        // 원작 Reward.Claimable은 퀘스트 완료(Master.Complete)다. 받음 여부와 함께 넘기고, 받는 중 표시는 한 호출에 끝나 늘 끈다.
        internal static List<QuestRewardValues> Rewards(QuestManager quests, OriginalGameplayCatalog catalog, OriginalQuestDefinition quest)
        {
            IReadOnlyList<OriginalQuestRewardDefinition> rewards = quest.Rewards;
            bool complete = quests.IsFinished(quest.Id);
            var values = new List<QuestRewardValues>(rewards.Count);
            for (int index = 0; index < rewards.Count; index++)
                values.Add(new QuestRewardValues(RewardText(catalog, rewards[index]), RewardIcon(catalog, rewards[index]), complete,
                    quests.IsRewardClaimed(quest.Id, rewards[index].Id)));
            return values;
        }

        // 원작 Task.Description과 ExtraDescriptsions다. 장면, 위치, 특성, 건물, 상태 이상 표시 이름은 아직 옮긴 조회가 없어 빈 글자로 둔다.
        // 추가 설명은 처치 과제만 있고, 줄 접두는 과제 줄 View가 붙인다.
        internal static string TaskText(QuestManager quests, OriginalGameplayCatalog catalog, GameSessionManager? session, int questId,
            OriginalQuestTaskDefinition task, bool finished, out List<string>? extras)
        {
            extras = null;
            int count = quests.TaskCount(questId, task.Id);
            switch (task.Kind)
            {
                case QuestTaskKind.SubmitItems:
                {
                    string text = Text("Task_SubmitItems").Replace("{ItemDisplayName}", ItemName(catalog, task.ItemId))
                        .Replace("{submittedAmount}", Number(count)).Replace("{requiredAmount}", Number(task.RequiredCount));
                    if (finished || session == null) return text;
                    // 가진 수는 제출 명령과 같은 세션 기본 출처 규칙으로 센다.
                    long owned = session.Inventory.CountOwned(task.ItemId, ItemSourceRules.Owned(session));
                    return text + " " + Text("Task_SubmitItems_HavingAmount").Replace("{amount}", owned.ToString(CultureInfo.InvariantCulture));
                }
                case QuestTaskKind.UseItem:
                    return Text("Task_UseItem").Replace("{ItemDisplayName}", ItemName(catalog, task.ItemId)).Replace("{amount}", Number(count))
                        .Replace("{requireAmount}", Number(task.RequiredCount));
                case QuestTaskKind.KillCount:
                    return KillText(catalog, task, count, out extras);
                case QuestTaskKind.SubmitMoney:
                    return Text("QuestTask_SubmitMoney").Replace("{money}", Number(task.Money));
                case QuestTaskKind.Evacuate:
                    return Text("Task_Evacuate").Replace("{TargetDisplayName}", "");
                case QuestTaskKind.ReachLocation:
                    return Text("Task_ReachLocation").Replace("{TargetLocationDisplayName}", "");
                case QuestTaskKind.UnlockPerk:
                    return Text("Task_UnlockPerk").Replace("{PerkDisplayName}", "");
                case QuestTaskKind.ConstructBuilding:
                    return Text("Task_ConstructBuilding").Replace("{BuildingName}", "");
                case QuestTaskKind.UnlockBeacon:
                    // 원작 비콘 키는 Task_Beacon_<비콘 ID> 꼴이다(예: Task_Beacon_Level_GroundZero_Main). 확인하지 않은 대응이다.
                    return Text("Task_Beacon_" + task.BeaconId);
                default:
                    // 원작 QuestTask_TaskEvent와 저장 값 확인 과제는 저작한 설명 키를 그대로 쓴다.
                    return Text(task.DescriptionKey);
            }
        }

        // 원작 Task.Icon은 아이템 과제만 아이템 아이콘이다. 다른 과제는 아이콘을 숨긴다.
        internal static Sprite? TaskIcon(OriginalGameplayCatalog catalog, OriginalQuestTaskDefinition task) =>
            task.Kind == QuestTaskKind.SubmitItems || task.Kind == QuestTaskKind.UseItem ? ItemIcon(catalog, task.ItemId) : null;

        // 원작 QuestTask_KillCount다. 적과 무기가 없으면 "임의의" 문구이고, 추가 설명은 무기, 장소, 헤드샷, 헤드샷 금지, 상태 순서다.
        static string KillText(OriginalGameplayCatalog catalog, OriginalQuestTaskDefinition task, int count, out List<string>? extras)
        {
            string weapon = task.RequireWeapon && task.WeaponItemId > 0 ? ItemName(catalog, task.WeaponItemId) : Text("Task_Desc_AnyWeapon");
            string enemy = task.EnemyNameKey.Length > 0 ? Text(task.EnemyNameKey) : Text("Task_Desc_AnyEnemy");
            string scene = task.SceneId.Length > 0 ? "" : Text("Task_Desc_AnyScene");
            var lines = new List<string>();
            if (task.RequireWeapon) lines.Add(Text("Task_Desc_WithWeapon").Replace("{weaponName}", weapon));
            if (task.SceneId.Length > 0) lines.Add(Text("Task_Desc_RequireScene").Replace("{requireSceneName}", scene));
            if (task.RequireCrit) lines.Add(Text("Task_Desc_RequireHeadShot"));
            if (task.ForbidCrit) lines.Add(Text("Task_Desc_WithoutHeadShot"));
            if (task.RequireBuff) lines.Add(Text("Task_Desc_WithBuff").Replace("{buffName}", ""));
            extras = lines;
            return Text("Task_KillCount").Replace("{weaponName}", weapon).Replace("{enemyName}", enemy)
                .Replace("{requireAmount}", Number(task.RequiredCount)).Replace("{amount}", Number(count)).Replace("{requireSceneName}", scene);
        }

        // 원작 QuestReward_EXP, QuestReward_Money, RewardItem("이름 x수"), QuestReward_UnlockStockItem의 Description이다.
        static string RewardText(OriginalGameplayCatalog catalog, OriginalQuestRewardDefinition reward) => reward.Kind switch
        {
            QuestRewardKind.Experience => Text("Reward_Exp").Replace("{amount}", Number(reward.Amount)),
            QuestRewardKind.Money => Text("Reward_Money").Replace("{amount}", Number(reward.Amount)),
            QuestRewardKind.Item => ItemName(catalog, reward.ItemId) + " x" + Number(reward.Amount),
            QuestRewardKind.UnlockStockItem => Text("Reward_UnlockStockItem").Replace("{ItemDisplayName}", ItemName(catalog, reward.ItemId)),
            _ => ""
        };

        // 원작 Reward.Icon 기본값은 없음이고 아이템 두 종류만 아이템 아이콘이다.
        static Sprite? RewardIcon(OriginalGameplayCatalog catalog, OriginalQuestRewardDefinition reward) =>
            reward.Kind == QuestRewardKind.Item || reward.Kind == QuestRewardKind.UnlockStockItem ? ItemIcon(catalog, reward.ItemId) : null;

        // 원작 QuestEntry 장소 글자다. 요구 장면이 없으면 null을 넘겨 줄 View가 anyLocationKey(UI_Location_Any)를 보인다.
        // 장면 표시 이름(원작 SceneInfoCollection)은 아직 옮긴 조회가 없어 빈 글자로 둔다.
        static string? Location(OriginalQuestDefinition quest) => quest.RequireSceneId.Length == 0 ? null : "";

        static string ItemName(OriginalGameplayCatalog catalog, int itemId) =>
            catalog.TryGet(itemId, out OriginalItemDefinition? item) ? Text(item.NameKey) : "";

        static Sprite? ItemIcon(OriginalGameplayCatalog catalog, int itemId) =>
            catalog.TryGet(itemId, out OriginalItemDefinition? item) ? item.Icon : null;

        // 원작 ToPlainText다. 빈 키 규칙은 GameLocalization이 맡고, 문구가 많아 짧은 이름으로 부른다.
        static string Text(string key) => GameLocalization.TextOrEmpty(key);

        // 과제 알림(QuestNotices)의 숫자도 같은 고정 숫자 형식을 쓴다.
        internal static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
