#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 MagicBlendState처럼 손 레이어 상태에 들어오면 이 상태의 자세로 넘어가라고 혼합 컴포넌트에 알린다.
    // 혼합 컴포넌트가 없는 Animator에서는 아무것도 하지 않는다.
    public sealed class OriginalMagicBlendState : StateMachineBehaviour
    {
        [SerializeField] OriginalMagicBlendPose? _pose;
        // 행동은 Animator마다 따로 만들어지므로 찾은 컴포넌트를 기억한다. 컴포넌트가 나중에 붙을 수 있어 못 찾으면 다음에 다시 찾는다.
        OriginalMagicBlend? _blend;

        // 원본처럼 이번 전환 시간을 함께 넘긴다. 0이면 자세 에셋의 혼합 시간을 쓴다.
        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (_blend == null && !animator.TryGetComponent(out _blend)) return;
            _blend!.Request(_pose!, animator.GetAnimatorTransitionInfo(layerIndex).duration);
        }
    }
}
