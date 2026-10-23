#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 FadeGroup처럼 CanvasGroup 알파를 정한 시간 동안 선형으로 옮기는 공용 규칙이다. View와 제작 도구가 같은 최소 시간을 쓴다.
    internal static class UiFade
    {
        // 페이드와 반동 시간의 최소값이다. 인스펙터 Min과 제작 도구의 값 제한이 함께 써서 0으로 나누지 않게 한다.
        internal const float _minimumDuration = .01f;

        // duration초에 0에서 1까지 가는 속도로 alpha를 target 쪽으로 deltaTime만큼 옮긴다.
        internal static float Step(float alpha, float target, float deltaTime, float duration) =>
            Mathf.MoveTowards(alpha, target, deltaTime / duration);
    }
}
