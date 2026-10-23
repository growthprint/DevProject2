#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.App
{
    // 원작 NoteIndexView_Entry다. 쪽지 하나의 번호와 제목을 보이고, 읽지 않은 쪽지 표시와 선택 표시를 켜고 끈다.
    // 해금하지 않은 쪽지는 원작처럼 제목 대신 ???를 보인다. 클릭은 쪽지 창(OriginalNoteIndexView)에 넘긴다.
    public sealed class OriginalNoteEntryView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] GameObject _highlight = null!;
        [SerializeField] TMP_Text _title = null!;
        [SerializeField] TMP_Text _index = null!;
        [SerializeField] GameObject _notRead = null!;

        Action<string>? _clicked;
        internal string Key { get; private set; } = "";
        // 쪽지 창은 클릭과 읽음 알림마다 모든 줄을 다시 묶는다. 줄은 보통 같은 쪽지와 번호를 계속 보이므로,
        // 제목 키와 번호 글자는 바뀔 때만 다시 만든다(Docs/runtime-boundaries.md#main-thread-split).
        string _titleKey = "";
        int _shownIndex = -1;

        // 원작 Setup이다. 번호는 1부터 세 자리로 쓴다.
        internal void Bind(string key, int index, bool unlocked, bool read, bool selected, Action<string> clicked)
        {
            if (_titleKey.Length == 0 || Key != key) _titleKey = NoteIndex.TitleKey(key);
            Key = key; _clicked = clicked;
            _title.text = unlocked ? GameLocalization.Text(_titleKey) : "???";
            if (_shownIndex != index)
            {
                _shownIndex = index;
                _index.text = index.ToString("000");
            }
            _notRead.SetActive(unlocked && !read);
            _highlight.SetActive(selected);
        }

        public void OnPointerClick(PointerEventData eventData) => _clicked?.Invoke(Key);
    }
}
