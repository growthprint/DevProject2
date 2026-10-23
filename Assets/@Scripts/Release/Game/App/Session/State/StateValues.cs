#nullable enable

namespace Game.App
{
    // 저장 상태 값의 TryCreate 검사가 함께 쓰는 수치 규칙이다. 같은 판정을 한 곳에 두어 상태마다 다르게 읽지 않게 한다.
    internal static class StateValues
    {
        // 난이도 배율, 생존 수치, 내구도, 가격 배율처럼 음수가 아닌 유한값이어야 하는 수치를 본다. NaN과 무한대는 거부한다.
        internal static bool IsFiniteNonNegative(float value) => float.IsFinite(value) && value >= 0f;
    }
}
