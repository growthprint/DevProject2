#nullable enable annotations
using System;
using System.Collections.Generic;
// 연결 누락 보고에 프로젝트 Log를 쓴다.
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 Duckov.MasterKeys.UI.MasterKeysView(GameplayUICanvas/Tabs/ViewArea/MasterKeysIndex)다. 왼쪽에 등록할 수 있는 열쇠 전체 목록
    // (원작 MasterKeysIndexList), 오른쪽에 고른 열쇠의 이름, 설명, 아이콘(원작 MasterKeysIndexInspector)을 보인다.
    // 열고 닫기, 입력 차단, 목록 내용과 선택은 화면 컨트롤러(MasterKeysIndexScreen)가 정하고, 이 View는 값만 받아 의도만 알린다.
    // 원작 FadeGroup 전환 대신 활성 여부로 보이고 숨긴다. 닫기 버튼은 저장된 Runtime Only OnClick이 부르고, 가변 목록의 줄 클릭만 코드로 받는다(Docs/rules/ui.md).
    public sealed class OriginalMasterKeysIndexView : MonoBehaviour
    {
        // 원작 MasterKeysIndexList의 entryContainer와 entryPrefab(목록 안의 KeyEntry)이다. 템플릿은 꺼 둔 채 복제 원본으로 쓴다.
        [SerializeField] RectTransform _entriesParent = null!;
        [SerializeField] OriginalMasterKeysIndexEntryView _entryTemplate = null!;
        // 원작 MasterKeysIndexInspector의 placeHolder, content, nameText, descriptionText, icon이다.
        [SerializeField] GameObject _placeholder = null!;
        [SerializeField] GameObject _content = null!;
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] TMP_Text _description = null!;
        [SerializeField] Image _icon = null!;
        // 원작처럼 이 창은 Tabs/ViewArea 안에서 LootView의 형제로 열린다(쪽지 창, 퀘스트 일지 선례). 공용 HUD는 Tabs 활성을
        // 인벤토리 열림으로 쓰므로, 보이는 동안 Tabs를 켜고 LootView를 꺼서 인벤토리 칸이 함께 그려지지 않게 한다.
        [SerializeField] GameObject _tabs = null!;
        [SerializeField] GameObject _lootView = null!;

        UiTemplateCollection<OriginalMasterKeysIndexEntryView>? _entries;

        public event Action<int>? EntrySelected;
        public event Action? CloseRequested;

        internal bool IsShown => gameObject.activeSelf;

        UiTemplateCollection<OriginalMasterKeysIndexEntryView> Entries =>
            _entries ??= new UiTemplateCollection<OriginalMasterKeysIndexEntryView>(_entryTemplate, entry => entry.Unbind());

        // 탭 창 공통 전환으로 창을 켠다. 이미 보이면 Tabs와 LootView를 다시 바꾸지 않는다. 목록과 상세는 컨트롤러가 이어서 넣는다.
        internal void Show()
        {
            if (gameObject.activeSelf) return;
            UiVisibility.ShowTabView(gameObject, _tabs, _lootView);
        }

        // 공용 HUD 초기화와 화면 닫기가 무조건 부르므로 이벤트를 내지 않는다. Tabs와 LootView는 공통 전환이 보이고 있었을 때만 되돌린다.
        internal void Hide()
        {
            _entries?.Clear();
            UiVisibility.HideTabView(gameObject, _tabs, _lootView);
        }

        // 원작 MasterKeysIndexList.Refresh다. 줄 순서는 컨트롤러가 넘긴 원작 AllPossibleKeys 순서다.
        internal void SetEntries(IReadOnlyList<MasterKeyIndexEntryValues> entries)
        {
            UiTemplateCollection<OriginalMasterKeysIndexEntryView> collection = Entries;
            collection.Begin();
            for (int index = 0; index < entries.Count; index++)
            {
                OriginalMasterKeysIndexEntryView entry = collection.Acquire(_entriesParent);
                entry.gameObject.SetActive(true);
                entry.Bind(entries[index], OnEntryClicked);
            }
            collection.End();
        }

        // 원작 MasterKeysIndexInspector.Setup이다. 선택이 없으면 자리표시를, 있으면 이름, 설명, 아이콘을 보인다.
        // 등록하지 않은 열쇠는 원작 Entry.DisplayName, Description, Icon처럼 줄과 같은 미발견 글자와 미발견 그림이다. 아이콘 색은 원작처럼 바꾸지 않는다.
        internal void SetInspector(MasterKeyIndexEntryValues? selection)
        {
            bool shown = selection.HasValue;
            _placeholder.SetActive(!shown);
            _content.SetActive(shown);
            if (!shown) return;
            MasterKeyIndexEntryValues values = selection.Value;
            _name.text = values.Active ? values.Name : OriginalMasterKeysIndexEntryView._undiscoveredText;
            _description.text = values.Active ? values.Description : OriginalMasterKeysIndexEntryView._undiscoveredText;
            _icon.sprite = values.Active ? values.Icon : _entryTemplate.UndiscoveredIcon;
        }

        // 원작 View.exitButton이다. 닫기는 컨트롤러가 입력 차단과 함께 정한다.
        public void OnExitClicked()
        {
            Log.Instance.Assert(IsShown, "닫힌 열쇠 도감의 닫기가 눌렸습니다.", this);
            CloseRequested?.Invoke();
        }

        void OnEntryClicked(int itemId) => EntrySelected?.Invoke(itemId);
    }
}
