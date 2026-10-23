#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 PerkTreeView, PerkEntry, PerkDetails의 열기, 노드 상태, 상세, 연구 시작과 활성화를 맡는 화면 컨트롤러다. BuilderScreen과 같은 수명 규칙이다.
    // 판정은 PerkTreeManager 조회와 지불 확인(GameplayCommands.CanPay)으로 하고, 시작과 활성화는 GameplayCommands.TryBeginPerkResearch와 TryConfirmPerk로 보낸다.
    // 원작처럼 창을 열 때만 노드를 배치하고, 소유자 알림 때 모습, 선, 상세를 다시 그린다. 남은 시간은 GameplayScreens.Tick이 매 프레임 그린다.
    internal sealed class PerkTreeScreen : GameplayScreen
    {
        // 원작 PerkDetails.RequireLevelFormatKey와 동작 설명 형식 키(ModifyCharacterStatsBase, AddPlayerStorage, UnlockStockShopItem)다.
        const string _requireLevelFormatKey = "UI_Perk_RequireLevel";
        const string _statsFormatKey = "PerkBehaviour_ModifyCharacterStatsBase";
        const string _storageFormatKey = "PerkBehaviour_AddPlayerStorage";
        const string _shopItemFormatKey = "PerkBehaviour_UnlockStockShopItem";

        readonly GameplayWorldContext _world;
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 지난 그리기에서 연구 중이고 시간이 남았던 특성 번호다. 원작 PerkEntry.FixedUpdate처럼 시간이 다 되면 다시 그린다.
        readonly List<int> _researching = new List<int>();
        PerkTreeDefinition? _tree;
        // 원작 selectedPerkEntry다. 트리 정의의 특성 번호이고 -1은 고른 특성 없음이다. 화면 상태라 저장하지 않는다.
        int _selected = -1;

        internal PerkTreeScreen(GameplayWorldContext world) => _world = world;

        // 원작 PerkTreeView.Show와 Show_Local이다. 원작처럼 고름을 비우고 제목을 넣은 뒤 노드를 채운다. 공용 HUD가 이 월드에 붙어 있지 않거나
        // 입력 차단을 못 받으면(정리 중) 열지 않는다. 트리 정의가 없으면 저작 결함이라 기록하고 열지 않는다(원작은 null 트리에서 예외로 멈춘다).
        // 호출자: GameplayScreens.ShowPerkTree(기지 스킬 강화 기계와 특성 건물의 PerkTreeUIInvoker).
        internal void Show(string treeId)
        {
            // HUD 연결과 세션 조회는 월드 문맥이 한 번에 본다. 세션 조회가 앱 루트 유무도 함께 본다.
            if (!_world.TryPresentSession(out GameSessionManager? session)) return;
            if (!PerkTreeManager.TryGetTree(treeId, out PerkTreeDefinition? tree))
            {
                Log.Instance.Error("특성 트리 정의가 없어 특성 트리 창을 열지 않습니다: " + treeId);
                return;
            }
            Close();
            if (!TryBlockInput(_world.Runtime)) return;
            _tree = tree;
            _selected = -1;
            OriginalPerkTreeView view = _world.Hud.PerkTree;
            OriginalPerkDetailsView details = view.Details;
            Action<int> select = Select;
            Action begin = Begin, activate = Activate, close = Close, redraw = Redraw;
            // 붙이기와 떼기를 짝으로 공용 구독 도우미에 넘겨, 닫을 때 붙인 순서대로 뗀다.
            _subscriptions.Bind(() => view.EntrySelected += select, () => view.EntrySelected -= select);
            _subscriptions.Bind(() => view.CloseRequested += close, () => view.CloseRequested -= close);
            _subscriptions.Bind(() => details.BeginRequested += begin, () => details.BeginRequested -= begin);
            _subscriptions.Bind(() => details.ActivateRequested += activate, () => details.ActivateRequested -= activate);
            // 원작은 특성 상태 신호로 노드와 상세를, 트리 신호로 선을 다시 그린다. 제품은 특성 소유자 알림 하나에 모두 다시 그리고,
            // 요구 조건 표시가 돈, 아이템, 레벨을 보므로 그 변경에도 다시 그린다. 열린 동안만 듣는다.
            _subscriptions.Bind(() => PerkTreeManager.Changed += redraw, () => PerkTreeManager.Changed -= redraw);
            _subscriptions.Bind(() => EconomyManager.Changed += redraw, () => EconomyManager.Changed -= redraw);
            _subscriptions.Bind(() => ExperienceManager.Changed += redraw, () => ExperienceManager.Changed -= redraw);
            // 세션과 로캘 구독도 공용 도우미가 붙이고 뗀다. 로캘은 노드를 다시 배치해야 해서 Redraw 대신 Repopulate를 부른다.
            _subscriptions.BindSessionChanged(session, redraw);
            _subscriptions.BindLocaleChanged(Repopulate);
            view.Show();
            Populate(view, tree);
            Redraw();
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 OnClose다. 구독을 먼저 풀고 입력 차단을 돌려준 뒤 창을 숨긴다. 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다.
        internal override void Close()
        {
            if (!IsOpen) return;
            _subscriptions.Clear();
            _researching.Clear();
            _tree = null;
            _selected = -1;
            ReleaseInput();
            if (_world.PresentationAttached) _world.Hud.PerkTree.Hide();
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 PerkEntry.FixedUpdate와 PerkDetails.Update다. 연구 중인 특성의 시간이 다 되면 노드와 상세를 다시 그려 "활성화"를 보이고,
        // 아니면 고른 특성의 진행 막대와 남은 시간을 그린다. 남은 시간 조회가 미래 시작 시각을 지금으로 당길 수 있다(소유자 규칙).
        // 부르는 컨트롤러 Update가 앱 루트 유무를 먼저 거르고, 트리는 입력 차단과 함께 서고 함께 비워진다.
        internal void Tick()
        {
            if (!IsOpen || !_world.PresentationAttached) return;
            PerkTreeManager perks = GameManager.Perks;
            for (int index = 0; index < _researching.Count; index++)
            {
                if (perks.GetRemaining(_tree.Id, _tree.Perks[_researching[index]].Name) > TimeSpan.Zero) continue;
                Redraw();
                return;
            }
            OriginalPerkDetailsView details = _world.Hud.PerkTree.Details;
            if (_selected < 0 || !details.CountingDown) return;
            string name = _tree.Perks[_selected].Name;
            details.SetCountdown(perks.GetProgress01(_tree.Id, name), Countdown(perks.GetRemaining(_tree.Id, name)));
        }

        // 원작 PopulatePerks다. 관계 그래프에 노드가 있는 특성과 레벨 구분선을 넣고 배치한다. 데모 판이 없어 LockInDemo는 보지 않는다.
        static void Populate(OriginalPerkTreeView view, PerkTreeDefinition tree)
        {
            view.BeginPopulate(GameLocalization.Text(tree.DisplayNameKey), tree.Horizontal);
            IReadOnlyList<PerkDefinition> perks = tree.Perks;
            for (int index = 0; index < perks.Count; index++)
                if (perks[index].HasNode) view.AddEntry(index, perks[index].Icon, GameLocalization.Text(perks[index].DisplayNameKey), perks[index].Position);
            IReadOnlyList<PerkLevelLine> lines = tree.LevelLines;
            for (int index = 0; index < lines.Count; index++) view.AddLevelLine(lines[index].DisplayName, lines[index].Position);
            view.EndPopulate();
        }

        // 원작 PerkEntry.Refresh, RefreshConnections, PerkDetails.Refresh다. 요구 조건은 원작 PerkRequirement.AreSatisfied(레벨, 비용 넉넉함)이고,
        // 비용 출처는 지불 명령과 같아 기지에서는 창고도 센다. 씬 정리 중이라 세션이 없으면 그리지 않는다.
        void Redraw()
        {
            if (!IsOpen || _tree == null || !_world.TrySession(out GameSessionManager? session)) return;
            PerkTreeDefinition tree = _tree;
            OriginalPerkTreeView view = _world.Hud.PerkTree;
            PerkTreeManager perks = GameManager.Perks;
            EconomyManager economy = GameManager.Economy;
            int level = GameManager.Experience.Level;
            // 비용 출처는 지불 명령과 같은 세션 기본 출처 규칙으로 정한다.
            ItemSources sources = ItemSourceRules.Owned(session);
            // 노드마다 가방 전체를 다시 세지 않도록 이 다시 그리기의 소유 수를 한 번 센다(Docs/runtime-boundaries.md#main-thread-split).
            OwnedItemCounts owned = session.Inventory.CountAllOwned(sources);
            _researching.Clear();
            for (int index = 0; index < tree.Perks.Count; index++)
            {
                PerkDefinition perk = tree.Perks[index];
                if (!perk.HasNode) continue;
                bool unlocked = perks.IsUnlocked(tree.Id, perk.Name), unlocking = perks.IsUnlocking(tree.Id, perk.Name);
                bool parents = perks.AreAllParentsUnlocked(tree.Id, perk.Name);
                bool timeUp = unlocking && perks.GetRemaining(tree.Id, perk.Name) <= TimeSpan.Zero;
                bool available = !unlocked && !unlocking && parents && Satisfied(perk, level, session, economy, owned);
                // 연구 중(시간 남음)과 활성화 대기(시간 다 됨)는 노드 표시와 남은 시간 감시가 같은 판정을 쓴다.
                bool researching = !unlocked && unlocking && !timeUp;
                bool activatable = !unlocked && unlocking && timeUp;
                view.SetEntryState(index, EntryLook(unlocked, parents), available, researching, activatable);
                if (researching) _researching.Add(index);
            }
            view.SetSelection(_selected);
            view.BeginConnections();
            IReadOnlyList<PerkConnection> connections = tree.Connections;
            for (int index = 0; index < connections.Count; index++)
                view.AddConnection(connections[index].From, connections[index].To, perks.IsUnlocked(tree.Id, tree.Perks[connections[index].From].Name));
            DrawDetails(view.Details, tree, perks, economy, level, session, owned);
        }

        // 원작 PerkDetails.Refresh와 SetupActivationInfo다. 원작 트리 창은 늘 editable로 부르므로 해금 전이면 요구 레벨과 비용을 보인다.
        void DrawDetails(OriginalPerkDetailsView details, PerkTreeDefinition tree, PerkTreeManager perks, EconomyManager economy, int level,
            GameSessionManager session, OwnedItemCounts owned)
        {
            if (_selected < 0)
            {
                details.ShowPlaceholder();
                return;
            }
            PerkDefinition perk = tree.Perks[_selected];
            bool unlocked = perks.IsUnlocked(tree.Id, perk.Name), unlocking = perks.IsUnlocking(tree.Id, perk.Name);
            bool timeUp = unlocking && perks.GetRemaining(tree.Id, perk.Name) <= TimeSpan.Zero;
            bool parents = perks.AreAllParentsUnlocked(tree.Id, perk.Name);
            bool satisfied = parents && Satisfied(perk, level, session, economy, owned);
            details.Show(GameLocalization.Text(perk.DisplayNameKey), Description(perk), perk.Icon, unlocked,
                DetailsAction(unlocked, unlocking, timeUp, parents, satisfied));
            if (!unlocked)
            {
                if (perk.RequireLevel > 0)
                {
                    Color color = level >= perk.RequireLevel ? details.NormalTextColor : details.UnsatisfiedTextColor;
                    details.SetRequireLevel(GameLocalization.Text(_requireLevelFormatKey).Replace("{level}", perk.RequireLevel.ToString(CultureInfo.InvariantCulture))
                        .Replace("{color}", "#" + ColorUtility.ToHtmlStringRGBA(color)));
                }
                else details.SetRequireLevel(null);
                // 원작 CostDisplay.Setup이다. 지불 명령과 같은 출처로 노드 판정이 센 수량 묶음을 넘겨 공용 도우미가 그린다.
                CostDisplayDrawing.Draw(details.Cost, _world.Catalog, perk.Cost, owned, CostDisplayDrawing.OwnedMoney(economy, owned),
                    GameplayCommands.CanPay(session, economy, perk.Cost, owned, true));
            }
            if (details.CountingDown) details.SetCountdown(perks.GetProgress01(tree.Id, perk.Name), Countdown(perks.GetRemaining(tree.Id, perk.Name)));
        }

        // 원작 Perk.Description이다. 특성 설명(있을 때) 뒤에 동작 설명을 한 줄씩 붙인다. 원작처럼 마지막 줄바꿈도 남긴다.
        string Description(PerkDefinition perk)
        {
            var text = new StringBuilder();
            if (perk.DescriptionKey.Length > 0) AppendLine(text, GameLocalization.Text(perk.DescriptionKey));
            IReadOnlyList<PerkEffect> effects = perk.Effects;
            for (int index = 0; index < effects.Count; index++) AppendLine(text, EffectDescription(effects[index]));
            return text.ToString();
        }

        // 원작 PerkBehaviour.Description 하위 구현이다. 범위 트리의 설명이 있는 동작은 스탯, 창고 확장, 상품 해금 셋이다.
        // 제조식과 업적 동작은 원작도 설명이 없고, 범위 밖 정원·블랙마켓 동작은 화면이 없어 빈 문구다.
        string EffectDescription(PerkEffect effect)
        {
            switch (effect.Kind)
            {
                case PerkEffectKind.Stats:
                    string format = GameLocalization.Text(_statsFormatKey);
                    var lines = new StringBuilder();
                    foreach (PerkStatEntry entry in effect.Stats)
                    {
                        if (string.IsNullOrEmpty(entry.Key)) continue;
                        // 스탯 이름 키 규칙은 아이템 상세와 같은 GameLocalization 키 함수가 정한다.
                        lines.Append(format.Replace("{statDisplayName}", GameLocalization.Text(GameLocalization.StatNameKey(entry.Key.Trim())))
                            .Replace("{value}", StatValue(entry.Value, entry.Percentage))).Append('\n');
                    }
                    return lines.ToString().Trim();
                case PerkEffectKind.PlayerStorage:
                    return GameLocalization.Text(_storageFormatKey).Replace("{addCapacity}", effect.IntValue.ToString(CultureInfo.InvariantCulture));
                case PerkEffectKind.ShopItem:
                    string item = _world.Catalog.TryGet(effect.IntValue, out OriginalItemDefinition? definition) ? GameLocalization.Text(definition!.NameKey) : string.Empty;
                    return GameLocalization.Text(_shopItemFormatKey).Replace("{ItemDisplayName}", item);
                default:
                    return string.Empty;
            }
        }

        static void AppendLine(StringBuilder text, string line)
        {
            if (line.Length > 0) text.Append(line).Append('\n');
        }

        // 비용 넉넉함은 다시 그리기가 한 번 센 수량 묶음으로 본다. 묶음의 출처가 지불 명령과 같은 세션 기본 출처다.
        static bool Satisfied(PerkDefinition perk, int level, GameSessionManager session, EconomyManager economy, OwnedItemCounts owned) =>
            level >= perk.RequireLevel && GameplayCommands.CanPay(session, economy, perk.Cost, owned, true);

        // 원작 PerkEntry.SetSelection이다. 노드를 고르면 노드 고름 표시와 상세를 다시 그린다.
        void Select(int perkIndex)
        {
            if (!IsOpen) return;
            _selected = perkIndex;
            Redraw();
        }

        // 원작 OnBeginButtonClicked(SubmitItemsAndBeginUnlocking)다. 성공하면 소유자 알림이 다시 그린다. 거절은 원작처럼 알리지 않는다.
        void Begin()
        {
            if (!IsOpen || _tree == null || _selected < 0 || !_world.TrySession(out GameSessionManager? session)) return;
            GameplayCommands.TryBeginPerkResearch(session, GameManager.Economy, GameManager.Experience, GameManager.Perks, _tree.Id, _tree.Perks[_selected].Name);
        }

        // 원작 OnActivateButtonClicked(ConfirmUnlock)다. 성공하면 소유자 알림이 다시 그린다. 거절은 원작처럼 알리지 않는다.
        // 특성 확정이 원작 UnlockFormula 동작으로 제조식도 열므로 제조 소유자를 함께 넘긴다.
        void Activate()
        {
            if (!IsOpen || _tree == null || _selected < 0 || !_world.TrySession(out GameSessionManager? session)) return;
            GameplayCommands.TryConfirmPerk(session, GameManager.Economy, GameManager.Quests, GameManager.Crafting, _world.Catalog, GameManager.Perks,
                _tree.Id, _tree.Perks[_selected].Name);
        }

        // 언어가 바뀌면 제목과 노드 이름을 다시 넣는다. 원작 배치처럼 스크롤이 처음으로 돌아간다.
        void Repopulate()
        {
            if (!IsOpen || _tree == null) return;
            Populate(_world.Hud.PerkTree, _tree);
            Redraw();
        }

        // 원작 PerkEntry.Refresh의 모습 고르기다.
        internal static PerkEntryLook EntryLook(bool unlocked, bool parentsUnlocked) =>
            unlocked ? PerkEntryLook.Active : parentsUnlocked ? PerkEntryLook.Available : PerkEntryLook.Unavailable;

        // 원작 PerkDetails.Refresh의 버튼 자리 고르기다. 해금됐으면 없음, 연구 중이면 시간에 따라 활성화 또는 진행 중,
        // 부모가 잠겼으면 불가, 요구 조건이 맞으면 시작, 아니면 조건 미충족이다.
        internal static PerkDetailsAction DetailsAction(bool unlocked, bool unlocking, bool timeUp, bool parentsUnlocked, bool satisfied)
        {
            if (unlocked) return PerkDetailsAction.None;
            if (unlocking) return timeUp ? PerkDetailsAction.Activate : PerkDetailsAction.InProgress;
            if (!parentsUnlocked) return PerkDetailsAction.Unavailable;
            return satisfied ? PerkDetailsAction.Begin : PerkDetailsAction.Unsatisfied;
        }

        // 원작 UpdateCountDown 형식 "d hh:mm:ss.fff"다.
        internal static string Countdown(TimeSpan remaining) => string.Format(CultureInfo.InvariantCulture, "{0} {1:00}:{2:00}:{3:00}.{4:000}",
            remaining.Days, remaining.Hours, remaining.Minutes, remaining.Seconds, remaining.Milliseconds);

        // 원작 ModifyCharacterStatsBase.Description의 값 문구다. 양수면 +를 붙이고, percentage면 100을 곱해 %를 붙인다.
        // 원작은 현재 문화권 숫자 형식이고 제품은 고정 형식이다.
        internal static string StatValue(float value, bool percentage) =>
            (value > 0f ? "+" : string.Empty) + (percentage ? (value * 100f).ToString(CultureInfo.InvariantCulture) + "%" : value.ToString(CultureInfo.InvariantCulture));
    }
}
