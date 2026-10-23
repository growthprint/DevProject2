#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 영역에 들어서면 동작하는 지점의 역할이다. F로 고르는 대상은 InteractableBase 하위 클래스가 맡는다.
    // 정수로 직렬화되므로 값을 바꾸지 않고, 새 종류는 끝에 붙인다.
    public enum OriginalPointKind { Dialogue = 1, Hint = 2, Extraction = 4 }

    // 원본 Trigger 영역 지점이다. 대사, 안내 말풍선, 철수 대기, 적 생성 Trigger로 쓴다. 위치와 조건과 표시 참조만 갖고,
    // 반복되는 Trigger 이름 대신 원본 fileID로 만든 SourceId로 구분한다.
    [DisallowMultipleComponent]
    public sealed class OriginalGameplayPoint : MonoBehaviour
    {
        [SerializeField] string _sourceId = "";
        [SerializeField] OriginalPointKind _kind;
        [SerializeField] string _labelKey = "";
        [SerializeField] Collider? _area;
        // 대사 지점이 재생할 원본 대사 트리다. 프롤로그 대사와 같은 데이터 형식이라 줄 키, 화자, 줄별 카메라 지시를 함께 갖는다.
        // 제작 때 Editor의 RestoreTutorialDialogues가 원본 트리에서 채우며, 대사가 아닌 지점은 비어 있다.
        [SerializeField] OriginalDialogueScript _dialogue = new OriginalDialogueScript();
        // 지점 종류마다 뜻이 다른 시간(초)이다. 안내는 말풍선 유지 시간, 철수는 대기 시간이다. 대사 지점은 이 값을 읽지 않는다.
        [SerializeField] float _duration;
        // 이 사건이 확정돼야 철수 영역이 동작한다. 비어 있으면 조건이 없다.
        [SerializeField] string _requiredEvent = "";
        public string SourceId => _sourceId;
        public OriginalPointKind Kind => _kind;
        public string LabelKey => _labelKey;
        // 전투 월드가 대사 지점에 처음 들어선 플레이어에게 이 데이터로 대사를 시작한다.
        internal OriginalDialogueScript Dialogue => _dialogue;
        // 지점 종류마다 뜻이 다른 시간(초)이다. 자세한 뜻은 _duration 주석에 있다.
        public float Duration => _duration;
        public string RequiredEvent => _requiredEvent;

        // 회전된 원본 상자도 맞게 판정하도록 AABB가 아니라 실제 Collider 안에 있는지 본다.
        public bool Contains(Vector3 position) => _area != null && _area.enabled && _area.gameObject.activeInHierarchy &&
            (_area.ClosestPoint(position) - position).sqrMagnitude < .0001f;
        // 원본 CharacterSpawnerRoot가 Trigger 진입 표시만 읽듯, 같은 생성 Trigger를 기다리는 적이 여럿이어도
        // Collider 판정은 프레임마다 처음 묻는 적이 한 번만 한다. 플레이어 위치만 넘겨야 하며, 같은 프레임의 다른 위치는 첫 결과를 받는다.
        internal bool ContainsPlayerThisFrame(Vector3 playerPosition)
        {
            int frame = Time.frameCount;
            if (_playerInsideFrame != frame) { _playerInsideFrame = frame; _playerInside = Contains(playerPosition); }
            return _playerInside;
        }
        // ContainsPlayerThisFrame이 판정한 프레임과 결과다. 처음에는 어떤 프레임과도 다르도록 -1로 둔다.
        int _playerInsideFrame = -1;
        bool _playerInside;

        // 제작 도구가 쓰는 구성이다. 조건 사건은 ConfigureRequiredEvent로, 대사 데이터는 ComponentAuthoring.ConfigureDialogue로 따로 쓴다.
        internal void Configure(string sourceId, OriginalPointKind kind, string labelKey, Collider? area, float duration)
        {
            _sourceId = sourceId; _kind = kind; _labelKey = labelKey; _area = area; _duration = duration; _requiredEvent = "";
        }

        internal void ConfigureRequiredEvent(string requiredEvent) => _requiredEvent = requiredEvent;
    }
}
