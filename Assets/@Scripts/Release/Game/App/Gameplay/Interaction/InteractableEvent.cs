#nullable enable annotations
using System;
using UnityEngine;

namespace Game.App
{
    // 원작 InteractableBase의 OnInteractStartEvent와 OnInteractFinishedEvent가 부르는 호출 가운데 이 게임이 옮긴 것이다.
    // 정수로 직렬화되므로 값을 바꾸지 않고, 새 종류는 끝에 붙인다.
    // Switch는 원작이 Door.Switch로 부르지만 문이 아닌 스위치(OriginalSwitch)를 켜고 끄는 호출이다.
    // TaskEvent는 원작 TaskEventEmitter.EmitEvent 호출로, 상호작용이 끝날 때 과제 이벤트 키를 보낸다.
    // ShowRepairView와 ShowDecomposeView는 원작 ViewsProxy.ShowRepairView와 ShowDecomposeView 호출이다. 인자 없이 호스트가 창을 연다.
    public enum InteractableEventKind { SetActive = 0, DoorSwitch = 1, Bubble = 2, Notify = 3, Switch = 4, TaskEvent = 5, ShowRepairView = 6, ShowDecomposeView = 7 }

    // 원작 UnityEvent 호출 하나다. 제작 도구가 원본 m_PersistentCalls에서 종류와 인자를 옮겨 적는다.
    // SetActive는 _target과 _active, DoorSwitch는 _door, Bubble은 _target 위치와 _textKey, 높이, 시간, Notify는 _textKey,
    // Switch는 _switch, TaskEvent는 _emitter를 쓴다.
    // ShowRepairView와 ShowDecomposeView는 필드를 쓰지 않는다. 제작 도구는 아래 생성자에 종류만 넘기고 나머지는 빈 값으로 둔다.
    [Serializable]
    public struct InteractableEvent
    {
        [SerializeField] InteractableEventKind _kind;
        [SerializeField] GameObject? _target;
        [SerializeField] bool _active;
        [SerializeField] Door? _door;
        [SerializeField] string _textKey;
        [SerializeField] float _yOffset;
        [SerializeField] float _duration;
        [SerializeField] OriginalSwitch? _switch;
        // 과제 프리팹 안의 발신기다. 프리팹을 만든 쪽이 키를 과제 키로 덮으므로 키 대신 발신기를 가리킨다.
        [SerializeField] TaskEventEmitter? _emitter;

        internal InteractableEventKind Kind => _kind;
        internal GameObject? Target => _target;
        internal bool Active => _active;
        internal Door? Door => _door;
        internal string TextKey => _textKey;
        internal float YOffset => _yOffset;
        internal float Duration => _duration;
        internal OriginalSwitch? Switch => _switch;
        // TaskEvent 종류만 읽는다. 다른 종류에서는 비어 있다.
        internal TaskEventEmitter? Emitter => _emitter;

        // 제작 도구가 쓰는 구성이다.
        internal InteractableEvent(InteractableEventKind kind, GameObject? target, bool active, Door? door, string textKey, float yOffset,
            float duration, OriginalSwitch? switcher = null)
        {
            _kind = kind; _target = target; _active = active; _door = door; _textKey = textKey; _yOffset = yOffset; _duration = duration;
            // 구조체 생성자는 모든 필드를 채워야 한다. 과제 발신기는 이 생성자가 만드는 종류에서 쓰지 않는다.
            _switch = switcher; _emitter = null;
        }
    }
}
