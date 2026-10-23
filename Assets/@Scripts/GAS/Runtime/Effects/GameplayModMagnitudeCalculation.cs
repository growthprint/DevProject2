#nullable enable
using UnityEngine;

namespace Game.GAS
{
    // UE ModMagnitudeCalculation처럼 수정자 크기를 코드로 계산하는 공유 에셋이다. 여러 효과가 같은 에셋을 쓰므로
    // 호출별 상태를 필드에 두지 않는다. 결과에는 수정자의 계수가 곱해진다.
    public abstract class GameplayModMagnitudeCalculation : ScriptableObject
    {
        public abstract float Calculate(in GameplayEffectSpec spec, AbilitySystem target);
    }
}
