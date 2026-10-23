#nullable enable annotations
using UnityEngine;
using System;

namespace Game.App
{
    // 원본 이동 클립의 발소리 AnimationEvent를 Animator가 있는 객체에서 받는다.
    // 플레이어 모터와 적이 같은 수신기를 쓰고, 각자 Configure로 콜백을 넘긴다.
    [DisallowMultipleComponent]
    public sealed class OriginalFootstepReceiver : MonoBehaviour
    {
        Action? _post;
        internal void Configure(Action post)
        {
            _post = post;
        }
        // 원본 클립의 AnimationEvent가 이 메서드를 부르도록 원본 CharacterModelAudioPoster와 같은 이름을 쓴다.
        public void PostFootStepSound() => _post?.Invoke();
    }
}
