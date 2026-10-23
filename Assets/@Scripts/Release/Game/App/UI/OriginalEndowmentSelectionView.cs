#nullable enable annotations
using System;
using Game.Core;
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.Endowment.UI.EndowmentSelectionPanel 창(LevelManager GameplayUICanvas/EndowmentSelection)이다.
    // 재능 줄, 고른 재능의 설명과 수정치, 확인과 취소 버튼을 보인다. 값은 화면 컨트롤러(EndowmentSelectionScreen)가 넣고,
    // 이 View는 줄 고르기, 확인, 취소 의도만 이벤트로 낸다. 보이기와 숨기기는 원작 FadeGroup 전환 대신 활성 여부로 한다(보관함 창과 같은 원작 차이).
    public sealed class OriginalEndowmentSelectionView : MonoBehaviour
    {
        // 원작 entryTemplate이다. 꺼진 채 저장되고, 복제본은 템플릿과 같은 부모(줄 배치) 아래에 놓인다.
        [SerializeField] OriginalEndowmentEntryView _entryTemplate = null!;
        // 원작 descriptionText다. 고른 재능의 설명과 수정치 줄을 보인다.
        [SerializeField] TMP_Text _description = null!;

        UiTemplateCollection<OriginalEndowmentEntryView>? _entries;
        Action<EndowmentIndex>? _entryClicked;

        // 원작 OnEntryClicked다. 잠긴 줄은 줄 View가 알리지 않는다.
        internal event Action<EndowmentIndex>? EntrySelected;
        // 원작 confirmButton과 cancelButton이다. ESC 취소는 화면 연결(GameplayScreens)이 TryClose로 처리한다.
        internal event Action? ConfirmRequested;
        internal event Action? CancelRequested;

        internal bool IsShown => gameObject.activeSelf;

        UiTemplateCollection<OriginalEndowmentEntryView> Entries =>
            _entries ??= new UiTemplateCollection<OriginalEndowmentEntryView>(_entryTemplate, entry => entry.Unbind());

        internal void Show() => gameObject.SetActive(true);

        // 화면 닫기와 공용 HUD 분리가 부른다. 이미 숨었어도 불러도 된다.
        internal void Hide()
        {
            if (_entries != null) _entries.Clear();
            gameObject.SetActive(false);
        }

        // 원작 Setup의 Pool.ReleaseAll, Get().Setup과 RefreshDescription을 셋으로 나눈다. 셋은 같은 호출 안에서 짝을 이룬다.
        internal void BeginEntries() => Entries.Begin();

        // requirement는 원작처럼 "- 해금 조건 -" 형태로 컨트롤러가 번역해 넘긴다. 잠금 표시가 켜질 때만 보인다.
        internal void AddEntry(EndowmentIndex index, Sprite? icon, string displayName, string requirement, bool locked, bool selected)
        {
            OriginalEndowmentEntryView entry = Entries.Acquire(_entryTemplate.transform.parent);
            entry.Bind(index, icon, displayName, requirement, locked, selected, _entryClicked ??= EntryClicked);
            entry.gameObject.SetActive(true);
        }

        internal void EndEntries(string description)
        {
            Entries.End();
            _description.text = description;
        }

        // 고정 버튼(확인, 취소)의 저장된 Runtime Only OnClick이 부른다. 버튼은 열린 창 안에만 있다.
        public void OnConfirmClicked()
        {
            Log.Instance.Assert(IsShown, "재능 선택 창이 열린 동안에만 확인합니다.", this);
            ConfirmRequested?.Invoke();
        }

        public void OnCancelClicked()
        {
            Log.Instance.Assert(IsShown, "재능 선택 창이 열린 동안에만 취소합니다.", this);
            CancelRequested?.Invoke();
        }

        void EntryClicked(EndowmentIndex index) => EntrySelected?.Invoke(index);
    }
}
