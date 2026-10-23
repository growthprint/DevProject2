#nullable enable annotations
using System;
using Game.Core;
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.UI.StorageDock 창(LevelManager GameplayUICanvas/StorageDock)이다. 한 쪽의 보관함 항목, 비었을 때의 자리표시, n/m 쪽 글자를 보인다.
    // 값은 화면 컨트롤러(StorageDockScreen)가 넣고, 이 View는 항목 수령, 쪽 이동, 닫기 의도만 이벤트로 낸다.
    // 보이기와 숨기기는 원작 FadeGroup 0.2초 전환 대신 활성 여부로 한다(쪽지 창과 같은 원작 차이).
    public sealed class OriginalStorageDockView : MonoBehaviour
    {
        // 원작 entryTemplate이다. 꺼진 채 저장되고, 복제본은 템플릿과 같은 부모(줄 배치) 아래에 놓인다.
        [SerializeField] OriginalStorageDockEntryView _entryTemplate = null!;
        // 원작 placeHolder와 pageText다.
        [SerializeField] GameObject _placeholder = null!;
        [SerializeField] TMP_Text _page = null!;

        UiTemplateCollection<OriginalStorageDockEntryView>? _entries;
        Action<int>? _entryClicked;

        // 원본 보관함 번호를 넘긴다. 쪽 안의 줄 번호가 아니다.
        internal event Action<int>? DockEntryRequested;
        // 원작 PrevPage는 -1, NextPage는 +1이다. 범위 자르기는 컨트롤러가 한다.
        internal event Action<int>? PageRequested;
        internal event Action? CloseRequested;

        internal bool IsShown => gameObject.activeSelf;

        UiTemplateCollection<OriginalStorageDockEntryView> Entries =>
            _entries ??= new UiTemplateCollection<OriginalStorageDockEntryView>(_entryTemplate, entry => entry.Unbind());

        internal void Show() => gameObject.SetActive(true);

        // 화면 닫기와 공용 HUD 분리가 부른다. 이미 숨었어도 불러도 된다.
        internal void Hide()
        {
            if (_entries != null) _entries.Clear();
            gameObject.SetActive(false);
        }

        // 원작 Refresh의 EntryPool.ReleaseAll, Get().Setup, placeHolder, pageText 순서를 셋으로 나눈다. 셋은 같은 호출 안에서 짝을 이룬다.
        internal void BeginEntries() => Entries.Begin();

        internal void AddEntry(int index, Sprite? icon, int count, bool storable, string text)
        {
            OriginalStorageDockEntryView entry = Entries.Acquire(_entryTemplate.transform.parent);
            entry.Bind(index, icon, count, storable, text, _entryClicked ??= EntryClicked);
            entry.gameObject.SetActive(true);
        }

        internal void EndEntries(bool empty, string page)
        {
            Entries.End();
            _placeholder.SetActive(empty);
            _page.text = page;
        }

        // 고정 버튼(PrevPage, NextPage, ExitButton)의 저장된 Runtime Only OnClick이 부른다. 버튼은 열린 창 안에만 있다.
        public void OnPrevPageClicked()
        {
            Log.Instance.Assert(IsShown, "보관함 창이 열린 동안에만 쪽을 넘깁니다.", this);
            PageRequested?.Invoke(-1);
        }

        public void OnNextPageClicked()
        {
            Log.Instance.Assert(IsShown, "보관함 창이 열린 동안에만 쪽을 넘깁니다.", this);
            PageRequested?.Invoke(1);
        }

        public void OnExitClicked()
        {
            Log.Instance.Assert(IsShown, "보관함 창이 열린 동안에만 닫습니다.", this);
            CloseRequested?.Invoke();
        }

        void EntryClicked(int index) => DockEntryRequested?.Invoke(index);
    }
}
