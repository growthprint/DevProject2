#nullable enable
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 GameManager.NoteIndex처럼 쪽지 목록과 해금, 읽음 상태를 모든 씬에 걸쳐 소유한다. GameManager 프리팹 자식의 컴포넌트이고
    // GameManager.Notes로 찾는다. 저장할 때 쪽지 구역을 써 넣고, 기록을 불러오면 읽어 간다. 메인 스레드에서만 쓴다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed class NoteIndex : SessionSectionOwner
    {
        // 해금이나 읽음이 바뀌었다는 신호만 보낸다. 원작 onNoteStatusChanged처럼 쪽지 창이 목록을 다시 그린다.
        public static event Action? Changed;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => Changed = null;

        // 원작 GameManager.prefab NoteIndex.notes의 키 66개를 원작 순서로 둔다. 원작 목록에는 그림이나 숨긴 쪽지가 없다.
        // 제목과 본문은 원작처럼 Note_<키>_Title, Note_<키>_Content 번역 키다.
        [SerializeField] string[] _notes = Array.Empty<string>();
        // 해금 순서를 지키는 키 기록이다. 저장은 원작처럼 해금한 순서로 쓴다.
        readonly OrderedKeySet _unlocked = new OrderedKeySet();
        readonly HashSet<string> _read = new HashSet<string>(StringComparer.Ordinal);

        // 쪽지 창이 원작 순서대로 그리는 전체 키 목록이다.
        internal IReadOnlyList<string> Notes => _notes;
        // 원작 GetUnlockedNoteCount처럼 해금한 키의 수다.
        internal int UnlockedCount => _unlocked.Count;

        internal static string TitleKey(string key) => "Note_" + key + "_Title";
        internal static string ContentKey(string key) => "Note_" + key + "_Content";

        internal bool IsUnlocked(string key) => _unlocked.Contains(key);
        internal bool IsRead(string key) => _read.Contains(key);

        // 원작 SetNoteUnlocked다. 이미 해금한 키도 원작처럼 알림만 다시 보낸다.
        internal bool TryUnlock(string key)
        {
            if (!AcceptsCommands) return false;
            _unlocked.Add(key);
            ObserverDispatch.InvokeEach(Changed);
            return true;
        }

        // 원작 SetNoteRead다. 쪽지 창이 본문을 보일 때 부른다.
        internal bool TryMarkRead(string key)
        {
            if (!AcceptsCommands) return false;
            _read.Add(key);
            ObserverDispatch.InvokeEach(Changed);
            return true;
        }

        // 원작 SaveData 생성자는 읽음 목록에도 해금 목록을 복사한다. 그래서 다시 불러오면 해금한 쪽지는 모두 읽은 것이 된다.
        private protected override void WriteSection(GameSaveData data)
        {
            IReadOnlyList<string> unlocked = _unlocked.ToArray();
            data.SetNotes(new NoteIndexState(unlocked, unlocked));
        }

        // 불러온 기록의 두 목록으로 바꾼 뒤 알린다. 기록의 목록은 검증된 불변 값이다.
        private protected override void ReadSection(GameSaveData data)
        {
            NoteIndexState notes = data.Notes;
            _unlocked.Load(notes.Unlocked);
            _read.Clear();
            foreach (string key in notes.Read) _read.Add(key);
            ObserverDispatch.InvokeEach(Changed);
        }
    }
}
