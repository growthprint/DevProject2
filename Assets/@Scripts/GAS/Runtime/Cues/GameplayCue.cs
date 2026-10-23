#nullable enable
using UnityEngine;

namespace Game.GAS
{
    // UE GameplayCueNotify처럼 태그 하나에 대한 순수 연출 에셋이다(소리, 입자, 화면 흔들림). 여러 캐릭터가 공유하므로
    // 호출별 상태를 필드에 두지 않는다. target은 큐를 받은 캐릭터의 아바타이고, 없으면 라우터의 오브젝트다.
    public abstract class GameplayCue : ScriptableObject
    {
        public abstract void Handle(in GameplayCueParameters parameters, GameObject target);
    }
}
