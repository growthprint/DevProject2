#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 플레이어와 적 캐릭터가 함께 쓰는 대시와 발소리 값이다. 플레이어 모터와 적 컨트롤러가 같은 원본 값을 읽도록 한곳에 둔다.
    internal static class OriginalCharacterMotion
    {
        // 원본 대시 진행 시간이다. 시작 때 내는 스태미나와 끝난 뒤의 대기는 대시 어빌리티의 비용과 쿨다운 효과가 정하고,
        // 곡선에 곱할 기본 속도는 GAS DashSpeed 어트리뷰트에서 읽는다. 원작 적 대시도 같은 시간이다.
        internal const float _dashDuration = 0.5f;
        // 원본 CA_Dash.sfx 기본값이다. 대시를 시작한 프레임에 한 번 낸다. 적 대시도 같은 소리를 낸다.
        internal const string _dashSound = "event:/Char/Footstep/dash";
        // 원본 대시 속도 곡선이다. 키의 시간, 값, 접선이 원본과 같다. 읽기만 하므로 원작 적 대시와 한 곡선을 함께 쓴다.
        internal static readonly AnimationCurve _dashCurve = new AnimationCurve(
            new Keyframe(-0.0012207031f, 0.6135613f, 4.2667236f, 4.2667236f),
            new Keyframe(0.19268288f, 1f, 0f, 0f),
            new Keyframe(0.7638108f, 0.7851256f, -0.91723293f, -0.91723293f),
            new Keyframe(1f, 0.5459905f, -0.8117536f, -0.8117536f));
        // 원본 발소리 이벤트 경로다. 플레이어와 적 발소리가 같은 소리를 낸다.
        internal const string _footstepSound = "event:/Char/Footstep/footstep_organic_light";
        // 발소리를 내는 최소 속력이다. 플레이어와 적 발소리가 같은 값을 쓴다.
        internal const float _footstepMinSpeed = .5f;
        // 원본 이동 클립의 발소리 AnimationEvent 함수 이름이다. 수신기 메서드 이름과 어긋나지 않도록 그 이름에서 만든다.
        internal const string _footstepEventName = nameof(OriginalFootstepReceiver.PostFootStepSound);
        // 원작 CharacterSoundMaker.cs:81, :96, :103의 무거움 기준과 배율이다. 소지 무게가 최대 무게의 75% 이상이면 반경이 1.5배다.
        const float _heavyFootstepRatio = .75f, _heavyFootstepRangeMultiplier = 1.5f;

        // 원작 CharacterSoundMaker.PostFootStepSound가 AI에게 내는 발소리 반경이다. 달리면 run, 아니면 walk이고, 무거우면 1.5배다.
        // 반경이 0 이하이면 원작처럼 아무도 못 듣는 0이다. 최대 무게가 0 이하면 나눗셈 대신 무겁지 않은 것으로 본다.
        internal static float FootstepNoiseRange(bool running, float walk, float run, float weight, float maxWeight)
        {
            float range = running ? run : walk;
            if (!(range > 0f)) return 0f;
            bool heavy = maxWeight > 0f && weight / maxWeight >= _heavyFootstepRatio;
            return range * (heavy ? _heavyFootstepRangeMultiplier : 1f);
        }
    }
}
