#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 StorageDock.Show·Refresh·GotoPage와 StorageDockEntry.OnButtonClick이다. 공용 HUD의 보관함 창에 창고 임시 보관함을 한 쪽 24개씩 보이고,
    // 항목을 누르면 세션 소지품 명령 TryTakeBufferItem으로 그 항목 하나를 창고로 받는다. 쪽은 화면 상태라 저장하지 않고 원작처럼 다시 열어도 유지된다.
    // 열린 동안 게임플레이 입력을 막고, 원작 OnTakeBufferItem 대신 세션 변경 알림마다 다시 그린다. 여닫기 규칙은 NoteScreen과 같다.
    // 공용 HUD, 카탈로그, 입력 차단은 월드 문맥에서 읽어 다른 화면과 같은 HUD 연결 판정을 쓴다.
    internal sealed class StorageDockScreen : GameplayScreen
    {
        // 원작 StorageDock.itemsPerPage다.
        internal const int ItemsPerPage = 24;
        // 원작 StorageDockEntry의 textKeyNormal과 textKeyInventoryFull 값이다(LevelManager.prefab). 제작 도구가 번역 테이블에 둔다.
        const string _sendToStorageKey = "UI_StorageDock_SendToInventory";
        const string _storageFullKey = "UI_StorageDock_InventoryFull";

        readonly GameplayWorldContext _world;
        // 연 동안 붙인 세션, 창, 로캘 구독을 닫을 때 한 번에 뗀다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        GameSessionManager? _session;
        OriginalStorageDockView? _view;
        int _page;

        internal StorageDockScreen(GameplayWorldContext world)
        {
            _world = world;
        }

        // 원작 ViewsProxy.ShowStorageDock → StorageDock.Show다. 원작 OnOpen은 PlayerStorage.Instance가 없으면 바로 닫는데,
        // 창고는 기지에만 있으므로 기지가 아니면 열지 않는다. 공용 HUD가 이 월드에 붙지 않았거나 입력 차단을 못 얻으면 열지 않는다.
        internal void Show()
        {
            if (!_world.CanPresent) return;
            if (!_world.TrySession(out GameSessionManager? session) || session!.Snapshot().SceneId != GameScene.BaseCamp) return;
            if (!IsOpen)
            {
                if (!TryBlockInput(_world.Runtime)) return;
                _session = session;
                OriginalStorageDockView view = _world.Hud.StorageDock;
                _view = view;
                Subscribe(session, view);
                view.Show();
            }
            Refresh(_session!.Snapshot());
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 입력 차단과 구독을 돌려주고 창을 닫는다. 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다.
        // 창 이벤트는 C# 구독이라 HUD가 먼저 파괴됐어도 끊고, 창 숨기기만 HUD가 이 월드에 붙어 있을 때 한다.
        internal override void Close()
        {
            if (!IsOpen) return;
            ReleaseInput();
            _subscriptions.Clear();
            _session = null;
            OriginalStorageDockView? view = _view;
            _view = null;
            if (_world.PresentationAttached && view != null) view.Hide();
            BaseAutoSaver.NotifyUiActivity();
        }

        // 세션 변경은 스냅샷을 받아 그리므로 공용 세션 구독 대신 직접 붙인다. 해제는 세션, 로캘, 창 순서로 등록한다.
        void Subscribe(GameSessionManager session, OriginalStorageDockView view)
        {
            session.Changed += SessionChanged;
            _subscriptions.Add(() => session.Changed -= SessionChanged);
            view.DockEntryRequested += Take;
            view.PageRequested += Turn;
            view.CloseRequested += Close;
            _subscriptions.BindLocaleChanged(RefreshSession);
            _subscriptions.Add(() =>
            {
                view.DockEntryRequested -= Take;
                view.PageRequested -= Turn;
                view.CloseRequested -= Close;
            });
        }

        // 원작 마지막 쪽 번호(MaxPage = Count / 24)다. 24의 배수면 원작처럼 빈 마지막 쪽이 생긴다.
        internal static int LastPage(int count) => count / ItemsPerPage;

        // 원작 Refresh의 쪽 범위다. 원작처럼 쪽을 개수에 맞춰 자르지 않아, 받은 뒤 쪽이 줄면 빈 쪽이 보일 수 있다.
        internal static (int First, int End) PageRange(int page, int count)
        {
            int first = page * ItemsPerPage;
            return (first, Math.Min(first + ItemsPerPage, count));
        }

        // 원작 pageText 형식 "{page + 1}/{MaxPage + 1}"이다.
        internal static string PageText(int page, int count) => (page + 1) + "/" + (LastPage(count) + 1);

        // 원작 StorageDockEntry.OnButtonClick이다. 기지가 아니거나, 창고에 빈 칸이 없거나, 관문이 닫혔으면 소유자가 기록 없이 거절하고
        // 화면은 그대로다. 성공하면 세션 변경 알림이 다시 그린다.
        void Take(int index)
        {
            if (_session != null) _session.Inventory.TryTakeBufferItem(_world.Catalog, index);
        }

        // 원작 GotoPage다. 0과 마지막 쪽 사이로 자른 뒤 다시 그린다.
        void Turn(int delta)
        {
            if (_session == null) return;
            GameSessionSnapshot snapshot = _session.Snapshot();
            _page = Mathf.Clamp(_page + delta, 0, LastPage(snapshot.Campaign.Gameplay.Inventory.StorageBuffer.Count));
            Refresh(snapshot);
        }

        void SessionChanged(GameSessionSnapshot snapshot) => Refresh(snapshot);

        // 언어가 바뀌면 지금 세션 스냅샷으로 안내 글자를 다시 그린다.
        void RefreshSession()
        {
            if (_session != null) Refresh(_session.Snapshot());
        }

        // 원작 Refresh다. 지금 쪽의 항목마다 원본 아이템 아이콘과 수량을 보이고, 창고에 넣을 수 있는지로 안내 글자와 색을 고른다.
        void Refresh(GameSessionSnapshot snapshot)
        {
            OriginalStorageDockView? view = _view;
            if (view == null) return;
            IReadOnlyList<ItemOwnershipState> buffer = snapshot.Campaign.Gameplay.Inventory.StorageBuffer;
            bool storable = CanStore(snapshot);
            string text = GameLocalization.Text(storable ? _sendToStorageKey : _storageFullKey);
            (int first, int end) = PageRange(_page, buffer.Count);
            view.BeginEntries();
            for (int index = first; index < end; index++)
            {
                ItemOwnershipState graph = buffer[index];
                // 저장 검증이 루트 하나짜리 그래프만 받으므로 루트 아이템이 늘 있다.
                ItemInstanceState root = graph.Find(graph.Roots[0].InstanceId)!;
                Sprite? icon = _world.Catalog.TryGet(root.OriginalItemId, out OriginalItemDefinition? definition) ? definition.Icon : null;
                view.AddEntry(index, icon, root.Quantity, storable, text);
            }
            view.EndEntries(buffer.Count <= 0, PageText(_page, buffer.Count));
        }

        // 원작 PlayerStorage.IsAccessableAndNotFull이다. 창고는 기지에만 있고, 합칠 스택이 있어도 빈 칸이 하나 있어야 받는다.
        // 줄의 안내 글자와 색만 정한다. 실제 수령은 TryTakeBufferItem이 같은 규칙(GameplayInventory.StoreBuffered)으로 다시 판정한다.
        static bool CanStore(GameSessionSnapshot snapshot)
        {
            ItemOwnershipState? items = snapshot.Campaign.Gameplay.Inventory.Items;
            return snapshot.SceneId == GameScene.BaseCamp && items != null
                && OriginalInventoryQueries.TryContainer(items, GameplayInventory._storage, out ItemContainerState? storage)
                && storage.Capacity.HasValue && storage.Entries.Count < storage.Capacity.Value;
        }
    }
}
