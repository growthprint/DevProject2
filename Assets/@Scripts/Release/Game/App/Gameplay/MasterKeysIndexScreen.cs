#nullable enable annotations
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 원작 MasterKeysView(열쇠 도감 탭)의 컨트롤러다. 창은 공용 HUD의 OriginalMasterKeysIndexView이고 값만 받아 그린다.
    // 원작 탭은 등록 상태를 읽기만 하므로 보내는 명령이 없다. 열린 동안 게임플레이 입력을 막고 ESC와 닫기 버튼으로 닫는다(QuestJournalScreen 선례).
    // 원작은 인벤토리 위 탭 단추(GameplayUICanvas/Tabs/ViewButtons/KeyButton → ViewsProxy.ShowKeyView → MasterKeysView.Show)로만 열고 키보드 키는 없다.
    // 입력 차단 핸들은 화면 공통 바탕(GameplayScreen)이 쥐고, 열린 동안의 구독은 구독 묶음이 한 번에 푼다.
    internal sealed class MasterKeysIndexScreen : GameplayScreen
    {
        readonly GameplayWorldContext _world;
        // 열린 동안의 등록, 로캘, 창 의도 구독이다. 닫을 때 한 번에 푼다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 연 동안 그리는 창이다. 닫을 때 연 창과 같은 객체를 숨기려고 잡아 둔다.
        OriginalMasterKeysIndexView? _view;
        // 원작 MasterKeysIndexList.selection이다. 원작 OnOpen의 SetSelection(null)은 아무것도 바꾸지 않아 닫았다 열어도 선택이 남는다.
        // 원작 창은 레벨 동안 남으므로 이 화면도 월드 하나의 수명 동안 유지한다.
        int? _selected;
        // 원작 AllPossibleKeys 캐시(_cachedKeyItemIds)다. 카탈로그는 실행 중 바뀌지 않으므로 처음 그릴 때 한 번 만든다.
        IReadOnlyList<int>? _keys;

        internal MasterKeysIndexScreen(GameplayWorldContext world)
        {
            _world = world;
        }

        // 원작 MasterKeysView.Show와 OnOpen이다. 이미 열려 있으면 원작 Open처럼 목록과 상세만 다시 그린다. 다른 화면 닫기는 부르는 쪽이 먼저 한다.
        // 앱 루트가 없거나 공용 HUD가 이 월드에 붙어 있지 않거나 입력 차단을 얻지 못하면(정리 중) 열지 않는다.
        internal void Show()
        {
            if (IsOpen)
            {
                Refresh();
                return;
            }
            if (!_world.CanPresent) return;
            if (!TryBlockInput(_world.Runtime)) return;
            _view = _world.Hud.MasterKeysIndex;
            Subscribe(_view);
            _view.Show();
            Refresh();
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다. 기지 밖에는 자동 저장이 없어 무시된다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 입력 차단과 구독을 돌려주고 창을 닫는다. 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다.
        // 원작 MasterKeysView는 TryQuit을 바꾸지 않아 ESC도 공통 바탕의 TryClose로 이 닫기를 부른다.
        internal override void Close()
        {
            if (!IsOpen) return;
            _subscriptions.Clear();
            ReleaseInput();
            if (_view != null && _world.PresentationAttached) _view.Hide();
            _view = null;
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 탭은 등록 신호를 듣지 않지만, 저장 불러오기와 로캘 변경 뒤에도 보이는 값이 소유자와 같도록 열린 동안 다시 그린다.
        // 붙이는 순서는 등록, 로캘, 줄 클릭, 닫기이고 구독 묶음이 같은 순서로 뗀다.
        void Subscribe(OriginalMasterKeysIndexView view)
        {
            Action refresh = Refresh, close = Close;
            Action<int> select = Select;
            MasterKeysManager.Changed += refresh;
            _subscriptions.Add(() => MasterKeysManager.Changed -= refresh);
            _subscriptions.BindLocaleChanged(refresh);
            view.EntrySelected += select;
            _subscriptions.Add(() => view.EntrySelected -= select);
            view.CloseRequested += close;
            _subscriptions.Add(() => view.CloseRequested -= close);
        }

        // 원작 MasterKeysIndexEntry.OnPointerClick과 MasterKeysView.OnEntryClicked다. 줄은 Refresh가 다시 비우지 않는 열쇠 목록으로만
        // 만들므로 받은 ID는 늘 그 목록에 있다. 닫힌 뒤 늦게 온 클릭만 무시한다.
        void Select(int itemId)
        {
            if (_view == null) return;
            _selected = itemId;
            Refresh();
        }

        // 원작 RefreshListDisplay와 RefreshInspectorDisplay다. 줄마다 원작 Entry.Refresh처럼 그때의 등록 상태를 읽는다.
        // 정리 중이라 앱 루트나 공용 HUD 연결이 없으면 그리지 않는다.
        void Refresh()
        {
            if (!IsOpen || _view == null || !_world.CanPresent) return;
            OriginalGameplayCatalog catalog = _world.Catalog;
            MasterKeysManager manager = GameManager.MasterKeys;
            IReadOnlyList<int> keys = Keys();
            var entries = new List<MasterKeyIndexEntryValues>(keys.Count);
            MasterKeyIndexEntryValues? selection = null;
            for (int index = 0; index < keys.Count; index++)
            {
                MasterKeyIndexEntryValues values = Values(catalog, manager, keys[index]);
                entries.Add(values);
                if (keys[index] == _selected) selection = values;
            }
            _view.SetEntries(entries);
            _view.SetInspector(selection);
        }

        IReadOnlyList<int> Keys() => _keys ??= AllPossibleKeys(_world.Catalog);

        // 원작 MasterKeysManager.AllPossibleKeys다. 카탈로그 원본 순서로 Key 태그가 있고 SpecialKey 태그가 없는 아이템이다.
        // 판정은 등록 명령과 같은 MasterKeysManager.IsRegistrable이며, 원작 체험판 LockInDemo 제외는 그 판정처럼 옮기지 않는다. 호출마다 새 목록이다.
        internal static IReadOnlyList<int> AllPossibleKeys(OriginalGameplayCatalog catalog)
        {
            IReadOnlyList<OriginalItemDefinition> items = catalog.Items;
            var keys = new List<int>();
            for (int index = 0; index < items.Count; index++)
                if (MasterKeysManager.IsRegistrable(items[index])) keys.Add(items[index].Id);
            return keys;
        }

        // 원작 Entry의 DisplayName, Description, Icon 원천 값이다. 등록하지 않은 열쇠는 이름과 설명을 찾지 않는다(View가 ???를 보인다).
        // ID는 AllPossibleKeys가 카탈로그에서 고른 것이라 정의가 늘 있다.
        internal static MasterKeyIndexEntryValues Values(OriginalGameplayCatalog catalog, MasterKeysManager manager, int itemId)
        {
            OriginalItemDefinition item = catalog.DefinitionOf(itemId);
            bool active = manager.IsActive(itemId);
            // 원작 ToPlainText처럼 비어 있는 키는 번역을 찾지 않는다.
            return new MasterKeyIndexEntryValues(itemId, active, active ? GameLocalization.TextOrEmpty(item.NameKey) : "",
                active ? GameLocalization.TextOrEmpty(item.DescriptionKey) : "", item.Icon);
        }
    }
}
