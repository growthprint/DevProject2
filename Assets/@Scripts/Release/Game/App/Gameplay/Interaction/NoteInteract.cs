#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.NoteIndexs.NoteInteract다. 읽으면 쪽지를 해금하고 쪽지 창을 연 뒤 꺼진다.
    // 원작은 이 컴포넌트가 붙은 쪽지 물체를 끄므로, 여기서는 복원한 원본 쪽지 물체(_noteObject)와 이 대상을 함께 끈다.
    [DisallowMultipleComponent]
    public sealed class NoteInteract : InteractableBase
    {
        // 원작 noteKey다. 제목과 본문 번역 키는 NoteIndex가 이 키로 만든다.
        [SerializeField] string _noteKey = "";
        [SerializeField] GameObject _noteObject = null!;

        internal string NoteKey => _noteKey;

        // 원작 Start처럼 이미 해금한 쪽지는 처음부터 꺼 둔다. 쪽지 상태는 씬을 불러오기 전에 저장 기록에서 읽혀 있다.
        void Start()
        {
            if (GameManager.HasInstance && GameManager.Notes.IsUnlocked(_noteKey)) Hide();
        }

        internal override void OnInteractFinished(IInteractionHost host)
        {
            host.ReadNote(this);
            Hide();
        }

        void Hide()
        {
            _noteObject.SetActive(false);
            gameObject.SetActive(false);
        }

        // 제작 도구가 쓰는 구성이다.
        internal void ConfigureNote(string noteKey, GameObject noteObject)
        {
            _noteKey = noteKey; _noteObject = noteObject;
        }
    }
}
