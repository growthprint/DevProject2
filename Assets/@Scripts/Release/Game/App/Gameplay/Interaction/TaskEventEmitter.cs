#nullable enable annotations
using System;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 TaskEventEmitter와 TaskEvent다. 과제 프리팹이나 상호작용 동작(InteractableEventKind.TaskEvent)이 키 하나를 전역 알림으로 보낸다.
    // 원작 QuestTask_TaskEvent 과제는 같은 키를 받으면 끝난다. 받는 쪽은 알림만 구독하고 이 컴포넌트를 모른다.
    public sealed class TaskEventEmitter : MonoBehaviour
    {
        // 원작 eventKey다. 과제 프리팹을 만든 쪽이 SetKey로 과제 키를 덮어써 프리팹 기본 키는 쓰지 않게 된다.
        [SerializeField] string _eventKey = "";
        // 원작 emitOnAwake다. 원작처럼 레벨 초기화가 끝난 뒤 한 번 보낸다. 이미 끝났으면 Awake에서 바로 보낸다.
        [SerializeField] bool _emitOnAwake;
        bool _waitingForLevel;

        // 원작 TaskEvent.OnTaskEvent다. 인자는 과제 이벤트 키이고, 과제 진행 소유자가 구독한다.
        internal static event Action<string>? TaskEventRaised;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => TaskEventRaised = null;

        // 원작 SetKey다. 과제 프리팹을 만든 쪽이 만든 직후 과제 키로 바꾼다.
        internal void SetKey(string key) => _eventKey = key;

        void Awake()
        {
            if (!_emitOnAwake) return;
            if (SceneContext.LevelInited) { EmitEvent(); return; }
            _waitingForLevel = true;
            SceneContext.LevelInitialized += OnLevelInitialized;
        }

        // 레벨 초기화 전에 파괴되면 원작처럼 보내지 않고 구독만 푼다.
        void OnDestroy() => StopWaiting();

        void OnLevelInitialized()
        {
            StopWaiting();
            EmitEvent();
        }

        void StopWaiting()
        {
            if (!_waitingForLevel) return;
            _waitingForLevel = false;
            SceneContext.LevelInitialized -= OnLevelInitialized;
        }

        // 원작 TaskEvent.EmitTaskEvent다. 구독자 하나가 실패해도 나머지 과제는 키를 받도록 구독자별로 격리한다.
        internal void EmitEvent() => ObserverDispatch.InvokeEachValue(TaskEventRaised, _eventKey);
    }
}
