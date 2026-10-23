#nullable enable annotations
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 MapSelectionView(순간이동기 지도 선택 창)의 컨트롤러다. 창은 공용 HUD의 MapSelectionView이고 값만 받아 그린다.
    // 항목은 원작처럼 조건(비콘 해금)이 맞고 비용이 넉넉할 때만 눌리고, 누르면 목적지 확인 표시를 연다. 출발을 누르면 비용을 내고
    // 실제 0.5초 뒤 출격 전환(전투 월드의 출격 루트)에 지도 선택 출격(RaidEntry.MapSelection)을 넘긴다. 확인 중과 출발 대기 중에는 원작 TryQuit처럼 닫지 않는다.
    // 승인 범위의 출격 지도는 제로존뿐이라, 다른 지도 항목은 비콘 해금 여부와 상관없이 원작 잠금 모습으로 보이고 눌리지 않는다(원작 차이).
    // 열린 동안 게임플레이 입력을 막는다(FormulasIndexScreen 선례).
    internal sealed class MapSelectionScreen : GameplayScreen
    {
        // 원작 LoadTask의 UniTask.WaitForSeconds(0.5f, ignoreTimeScale: true)다.
        const float _departDelay = 0.5f;
        readonly GameplayWorldContext _world;
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        Func<RaidEntry, UniTask>? _depart;
        Func<string, int, bool>? _beaconUnlocked;
        // 원작 loading 자리다. 확인 표시를 연 데이터 항목 번호이고 없으면 -1이다. 화면 상태라 저장하지 않는다.
        int _pending = -1;
        // 비용을 내고 출발을 기다리는 중이다. 닫기가 세대를 올리면 기다리던 출발을 멈춘다.
        bool _departing;
        int _generation;

        internal MapSelectionScreen(GameplayWorldContext world)
        {
            _world = world;
        }

        // 열려 있고 비용을 내고 출발을 기다리는 중이 아닐 때만 항목 고르기, 확인, 취소를 받는다.
        bool AcceptsChoice => IsOpen && !_departing;

        // 원작 MapSelectionView.Show와 OnOpen이다. 다른 화면 닫기는 부르는 쪽(GameplayScreens)이 먼저 한다.
        // 공용 HUD가 이 월드에 붙어 있지 않거나 입력 차단을 못 받으면(정리 중) 열지 않는다. 호출자: GameplayScreens.ShowMapSelection.
        internal void Show(Func<RaidEntry, UniTask> depart)
        {
            // HUD 연결과 세션 확인, 열린 창 닫기, 입력 차단은 다른 화면과 같은 순서의 공통 열기 앞부분이 한다.
            if (!TryBeginShow(_world, out GameSessionManager? session)) return;
            _depart = depart;
            BeaconManager beacons = GameManager.Beacons;
            _beaconUnlocked = beacons.IsUnlocked;
            MapSelectionView view = _world.Hud.MapSelection;
            Action<int> click = EntryClicked;
            Action confirm = Confirm, cancel = Cancel, close = RequestClose, redraw = Redraw;
            Action<string, int> beaconUnlocked = BeaconUnlocked;
            view.EntryClicked += click;
            _subscriptions.Add(() => view.EntryClicked -= click);
            view.ConfirmRequested += confirm;
            _subscriptions.Add(() => view.ConfirmRequested -= confirm);
            view.CancelRequested += cancel;
            _subscriptions.Add(() => view.CancelRequested -= cancel);
            view.CloseRequested += close;
            _subscriptions.Add(() => view.CloseRequested -= close);
            // 원작 CostDisplay는 돈과 소지품이 바뀌면 다시 칠한다. 항목 잠금도 비콘 해금과 맞도록 같은 때 모두 다시 그린다.
            EconomyManager.Changed += redraw;
            _subscriptions.Add(() => EconomyManager.Changed -= redraw);
            // 세션 다시 그리기는 공용 구독 도우미가 붙이고 뗀다.
            _subscriptions.BindSessionChanged(session, redraw);
            BeaconManager.Unlocked += beaconUnlocked;
            _subscriptions.Add(() => BeaconManager.Unlocked -= beaconUnlocked);
            view.Show();
            Redraw();
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 OnClose다. 씬 정리와 출격 전환이 무조건 불러도 되고, 여러 번 불러도 된다. 기다리던 출발은 세대를 올려 멈춘다.
        internal override void Close()
        {
            if (!IsOpen) return;
            _generation++;
            _subscriptions.Clear();
            _pending = -1;
            _departing = false;
            _depart = null;
            _beaconUnlocked = null;
            ReleaseInput();
            if (_world.PresentationAttached) _world.Hud.MapSelection.Hide();
            BaseAutoSaver.NotifyUiActivity();
        }

        // ESC 처리가 묻는다. 원작 TryQuit처럼 확인 중이거나 출발을 기다리면 닫지 않지만 ESC는 이 창이 받는다.
        internal override bool TryClose()
        {
            if (!IsOpen) return false;
            RequestClose();
            return true;
        }

        // 원작 MapSelectionEntry.ConditionsSatisfied에 승인 범위를 더한 판정이다. 범위 밖 지도는 늘 잠긴다.
        // 조건(원작 RequireBeaconUnlocked)은 모두 맞아야 하고, 비어 있으면 늘 맞다.
        internal static bool IsSelectable(MapSelectionEntryData entry, Func<string, int, bool> beaconUnlocked)
        {
            if (!string.Equals(entry.SceneId, QuestScenes.GroundZero, StringComparison.Ordinal)) return false;
            foreach (MapSelectionBeaconRequirement required in entry.RequiredBeacons)
                if (!beaconUnlocked(required.SceneId, required.BeaconIndex)) return false;
            return true;
        }

        // 원작 TryQuit이다. 확인 표시가 열렸거나 출발을 기다리면 닫지 않는다.
        void RequestClose()
        {
            if (_pending >= 0 || _departing) return;
            Close();
        }

        // 원작 NotifyEntryClicked다. 잠긴 항목(원작 OnPointerClick의 조건 검사)과 비용이 모자란 항목은 아무것도 하지 않는다.
        // 원작은 확인 중에 다른 항목을 누르면 두 번째 대기를 겹쳐 시작해 출발 때 두 번 낸다. 제품은 확인 대상을 새 항목으로 바꾼다(원작 차이).
        void EntryClicked(int index)
        {
            if (!AcceptsChoice || !TryEntry(index, out MapSelectionEntryData? entry, out GameSessionManager? session)) return;
            if (!IsSelectable(entry!, _beaconUnlocked!) || !CanPay(session!, entry!.Cost)) return;
            MapSelectionView view = _world.Hud.MapSelection;
            MapSelectionEntryView? shown = ViewOf(view, entry!.SourceName);
            if (shown == null) return;
            _pending = index;
            view.ShowConfirm(shown, entry.Cost.IsFree);
            DrawCost(view.ConfirmCost, session!, entry.Cost);
        }

        // 원작 WaitForConfirm 뒤의 cost.Enough 확인, cost.Pay, 실제 0.5초 대기, 장면 로드다. 지불은 S1 공용 도우미가 맡고,
        // 모자라거나 거절되면 원작처럼 확인 표시를 닫는다. 대기 중 예외는 기다리는 쪽이 없어 상태를 먼저 되돌린 뒤 한 번 기록한다.
        void Confirm()
        {
            if (!AcceptsChoice || _pending < 0 || !TryEntry(_pending, out MapSelectionEntryData? entry, out GameSessionManager? session)) return;
            MapSelectionView view = _world.Hud.MapSelection;
            view.HideConfirmButtons();
            // 지불 출처는 세션 기본 출처 규칙으로 정한다(기지는 창고 포함).
            ItemSources sources = ItemSourceRules.Owned(session!);
            if (!IsSelectable(entry!, _beaconUnlocked!) || !GameplayCommands.TryPay(session, GameManager.Economy, entry!.Cost, sources, true))
            {
                Cancel();
                return;
            }
            _departing = true;
            int generation = _generation;
            RaidEntry raid = RaidEntry.MapSelection(entry.BeaconIndex);
            DepartAfterDelayAsync(raid, generation).Forget(error =>
            {
                if (generation == _generation) Close();
                Log.Instance.Exception(error, _world.LogContext);
            });
        }

        async UniTask DepartAfterDelayAsync(RaidEntry raid, int generation)
        {
            float departAt = Time.unscaledTime + _departDelay;
            while (Time.unscaledTime < departAt)
            {
                await UniTask.Yield();
                if (generation != _generation) return;
            }
            Func<RaidEntry, UniTask>? depart = _depart;
            if (depart == null) return;
            // 출격 전환이 화면 묶음을 닫으며 이 화면도 닫는다. 거절과 실패의 기록은 전환 루트가 맡는다.
            await depart(raid);
        }

        // 원작 btnCancel과 비용 부족 갈래의 confirmIndicatorFadeGroup.Hide다.
        void Cancel()
        {
            if (!AcceptsChoice) return;
            _pending = -1;
            _world.Hud.MapSelection.HideConfirm();
        }

        void BeaconUnlocked(string beaconId, int beaconIndex) => Redraw();

        // 원작 OnEnable의 항목 Refresh다. 데이터 항목마다 같은 원작 이름의 항목 View를 찾아 잠금과 비용을 그린다.
        // 데이터가 없는 항목 View는 원작 잠금 모습으로 둔다.
        void Redraw()
        {
            if (!IsOpen || !_world.TrySession(out GameSessionManager? session)) return;
            MapSelectionView view = _world.Hud.MapSelection;
            IReadOnlyList<MapSelectionEntryView> views = view.Entries;
            for (int at = 0; at < views.Count; at++) view.BindEntry(views[at], -1, true, true);
            // 데이터 에셋은 제품 게임플레이 데이터에 늘 연결돼 있어 확인 없이 읽는다. 연결은 QuestAndMapScreenAuthoringTests가 본다.
            // 설정 속성이 이제 null 아님을 계약으로 두어 null 용서 연산자를 쓰지 않는다.
            IReadOnlyList<MapSelectionEntryData> entries = GameplayDataSettings.Progression.MapSelection.Entries;
            for (int index = 0; index < entries.Count; index++)
            {
                MapSelectionEntryData entry = entries[index];
                MapSelectionEntryView? shown = ViewOf(view, entry.SourceName);
                if (shown == null) continue;
                view.BindEntry(shown, index, !IsSelectable(entry, _beaconUnlocked!), entry.Cost.IsFree);
                if (!entry.Cost.IsFree) DrawCost(shown.Cost, session!, entry.Cost);
            }
            if (_pending >= 0 && _pending < entries.Count)
            {
                MapSelectionEntryData pending = entries[_pending];
                if (!pending.Cost.IsFree) DrawCost(view.ConfirmCost, session!, pending.Cost);
            }
        }

        // 데이터 에셋은 Redraw와 같이 늘 연결돼 있어 확인 없이 읽는다. 세션은 앱 루트가 있을 때만 있으므로,
        // 앱 루트가 사라져 설정 등록이 풀린 뒤에는 읽지 않는다.
        bool TryEntry(int index, out MapSelectionEntryData? entry, out GameSessionManager? session)
        {
            entry = null;
            if (!_world.TrySession(out session)) return false;
            // 설정 속성이 이제 null 아님을 계약으로 두어 null 용서 연산자를 쓰지 않는다.
            IReadOnlyList<MapSelectionEntryData> entries = GameplayDataSettings.Progression.MapSelection.Entries;
            if (index < 0 || index >= entries.Count) return false;
            entry = entries[index];
            return true;
        }

        static MapSelectionEntryView? ViewOf(MapSelectionView view, string sourceName)
        {
            IReadOnlyList<MapSelectionEntryView> views = view.Entries;
            for (int at = 0; at < views.Count; at++)
                if (string.Equals(views[at].SourceName, sourceName, StringComparison.Ordinal)) return views[at];
            return null;
        }

        // 원작 Cost.Enough다. 계좌를 쓰고 출처는 지불과 같은 세션 기본 출처다(기지는 창고 포함).
        static bool CanPay(GameSessionManager session, Cost cost) =>
            GameplayCommands.CanPay(session, GameManager.Economy, cost, ItemSourceRules.Owned(session), true);

        // 원작 CostDisplay.Setup이다. 넉넉한지와 아이템 줄은 지불과 같은 세션 기본 출처로 공용 도우미가 센다.
        void DrawCost(OriginalCostDisplayView display, GameSessionManager session, Cost cost) =>
            CostDisplayDrawing.Draw(display, session, GameManager.Economy, _world.Catalog, cost, ItemSourceRules.Owned(session));
    }
}
