#nullable enable annotations
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 Duckov.UI.NoteIndexView다. 왼쪽에 쪽지 목록과 해금 수, 오른쪽에 고른 쪽지의 제목과 본문을 보인다.
    // 본문을 보이면 원작 Inspector.Setup처럼 그 쪽지를 읽음으로 기록한다. 열고 닫기와 입력 차단은 화면 소유자(NoteScreen)가 정한다.
    public sealed class OriginalNoteIndexView : MonoBehaviour
    {
        [SerializeField] GameObject _noEntry = null!;
        // 원작 entryTemplate이다. 목록 Content 아래의 첫 줄이며 복제 원본으로 쓰고, 줄이 모자랄 때만 복제한다.
        [SerializeField] OriginalNoteEntryView _entryTemplate = null!;
        [SerializeField] TMP_Text _count = null!;
        [SerializeField] ScrollRect _scroll = null!;
        // 원작 NoteIndexView_Inspector의 자리표시, 본문 영역, 제목, 본문이다.
        [SerializeField] GameObject _placeholder = null!;
        [SerializeField] GameObject _content = null!;
        [SerializeField] TMP_Text _title = null!;
        [SerializeField] TMP_Text _body = null!;
        // 원작처럼 이 창은 Tabs/ViewArea 안에서 LootView의 형제로 열린다. 공용 HUD는 Tabs 활성을 인벤토리 열림으로 쓰므로,
        // 보이는 동안 Tabs를 켜고 LootView를 꺼서 인벤토리 칸이 함께 그려지지 않게 한다. HUD 상태 막대와 상호작용 안내도 그동안 숨는다.
        [SerializeField] GameObject _tabs = null!;
        [SerializeField] GameObject _lootView = null!;

        readonly List<OriginalNoteEntryView> _entries = new List<OriginalNoteEntryView>();
        NoteIndex? _notes;
        string? _displaying;
        bool _focus;

        internal bool IsShown => gameObject.activeSelf;

        // 원작 ShowNote다. 창을 켜고 목록을 다시 그린 뒤 그 쪽지를 보이고, 다음 프레임에 그 줄로 스크롤한다. 키가 null이면 자리표시만 보인다.
        internal void Show(NoteIndex notes, string? key)
        {
            _notes = notes;
            // 탭 창 공통 전환을 쓴다. 원작처럼 이미 보여도 목록을 다시 그리므로 조기 반환이 없다.
            UiVisibility.ShowTabView(gameObject, _tabs, _lootView);
            Display(key);
            _focus = true;
        }

        // 공용 HUD 초기화도 무조건 부르므로, 보이고 있을 때만 Tabs와 LootView를 되돌리는 공통 전환을 쓴다.
        // 인벤토리는 쪽지가 열린 동안 입력 차단으로 열리지 않는다.
        internal void Hide()
        {
            _notes = null;
            UiVisibility.HideTabView(gameObject, _tabs, _lootView);
        }

        // 원작 OnNoteStatusChanged처럼 해금이나 읽음이 바뀌거나 로캘이 바뀌면 보이는 동안 다시 그린다.
        internal void Refresh()
        {
            if (_notes != null) Display(_displaying);
        }

        void Display(string? key)
        {
            NoteIndex notes = _notes!;
            // 원작 OnEntryClicked처럼 해금하지 않은 쪽지는 고르지 않고 자리표시를 보인다.
            _displaying = key != null && notes.IsUnlocked(key) ? key : null;
            IReadOnlyList<string> keys = notes.Notes;
            _count.text = notes.UnlockedCount + " / " + keys.Count;
            for (int index = 0; index < keys.Count; index++)
            {
                if (index >= _entries.Count)
                    _entries.Add(index == 0 ? _entryTemplate : Instantiate(_entryTemplate, _entryTemplate.transform.parent));
                OriginalNoteEntryView entry = _entries[index];
                entry.gameObject.SetActive(true);
                string note = keys[index];
                entry.Bind(note, index + 1, notes.IsUnlocked(note), notes.IsRead(note), note == _displaying, Select);
            }
            for (int index = keys.Count; index < _entries.Count; index++) _entries[index].gameObject.SetActive(false);
            _noEntry.SetActive(keys.Count == 0);
            bool shown = _displaying != null;
            _placeholder.SetActive(!shown);
            _content.SetActive(shown);
            if (!shown) return;
            _title.text = GameLocalization.Text(NoteIndex.TitleKey(_displaying!));
            _body.text = GameLocalization.Text(NoteIndex.ContentKey(_displaying!));
            // 원작 Inspector.Setup의 SetNoteRead다. 읽음 알림이 이 창을 다시 그리므로 이미 읽은 쪽지는 다시 기록하지 않는다.
            if (!notes.IsRead(_displaying!)) notes.TryMarkRead(_displaying!);
        }

        void Select(string key) => Display(key);

        // 원작 MoveScrollViewToActiveEntry다. 줄 배치가 끝난 다음 프레임에 보이는 쪽지 줄의 높이로 스크롤 위치를 맞춘다.
        void LateUpdate()
        {
            if (!_focus) return;
            _focus = false;
            foreach (OriginalNoteEntryView entry in _entries)
            {
                if (!entry.gameObject.activeSelf || entry.Key != _displaying) continue;
                var rect = (RectTransform)entry.transform;
                float height = _scroll.content.rect.height;
                if (height > 0f) _scroll.verticalNormalizedPosition = 1f + rect.anchoredPosition.y / height;
                return;
            }
        }
    }
}
