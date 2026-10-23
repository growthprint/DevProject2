#nullable enable
using UnityEngine;

namespace Game.GAS
{
    // 캐릭터 하나의 큐 알림을 받아 큐 세트의 연출 에셋에 넘기는 연출 컴포넌트다. 게임 상태는 바꾸지 않는다.
    // 코드로 처리하는 큐(HUD, 월드 효과 풀 등)는 AbilitySystem.CueNotified를 직접 구독해도 된다.
    [DisallowMultipleComponent]
    public sealed class GameplayCueRouter : MonoBehaviour
    {
        [SerializeField] GameplayCueSet? _cues;
        AbilitySystem? _system;

        // 다른 시스템에 묶여 있으면 먼저 푼다. 같은 시스템을 다시 묶어도 구독이 겹치지 않는다.
        public void Bind(AbilitySystem system)
        {
            Unbind();
            _system = system;
            system.CueNotified += Handle;
        }

        public void Unbind()
        {
            AbilitySystem? system = _system;
            _system = null;
            if (system != null) system.CueNotified -= Handle;
        }

        void Handle(AbilitySystem system, GameplayCueParameters parameters)
        {
            GameplayCueSet? cues = _cues;
            if (cues == null || !cues.TryFind(parameters.Tag, out GameplayCue? cue) || cue == null) return;
            GameObject? avatar = system.Avatar;
            cue.Handle(parameters, avatar != null ? avatar : gameObject);
        }

        // 씬 정리를 거치지 않고 파괴될 때의 백업 해제다.
        void OnDestroy() => Unbind();
    }
}
