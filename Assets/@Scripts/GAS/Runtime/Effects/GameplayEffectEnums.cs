#nullable enable
namespace Game.GAS
{
    // 원작 Buff의 limitedLifeTime이 true면 HasDuration, false면 Infinite에 해당한다. Instant는 Base를 한 번 바꾸고 남지 않는다.
    public enum GameplayEffectDurationPolicy
    {
        Instant,
        HasDuration,
        Infinite
    }

    // 효과 적용 결과다. 규칙에 따른 거절도 결과 값으로만 알리고 로그를 남기지 않는다.
    public enum EffectApplyResult
    {
        // 시스템이 해제됐거나 작업 중첩 한도를 넘었다.
        Invalid,
        // 즉시 효과가 실행됐다.
        Executed,
        // 새 지속 효과가 붙었다.
        Added,
        // 같은 효과가 이미 있어 시간을 새로 하고 한도 안에서 층을 더했다.
        Stacked,
        // 같은 배타 태그의 기존 효과를 남기고 시전자만 바꿨다(원작 AddBuff 규칙).
        KeptExisting,
        // 대상이 이 효과의 태그에 면역이다(원작 buffResist).
        Immune,
        // 적용 태그 요구조건을 만족하지 않는다.
        RequirementsNotMet,
        // 같은 배타 태그의 우선순위 높은 효과가 이미 있다.
        ExclusiveRejected
    }

    // 수정자 크기를 정하는 방법이다. UE의 ScalableFloat, AttributeBased, SetByCaller, CustomCalculationClass에 해당한다.
    public enum GameplayMagnitudeKind
    {
        ScalableFloat,
        AttributeBased,
        SetByCaller,
        CustomCalculation
    }

    // AttributeBased 크기가 어느 쪽 어트리뷰트를 읽는지다.
    public enum AttributeCaptureSource
    {
        Source,
        Target
    }

    // 큐가 나가는 시점이다. Executed는 즉시 효과와 주기 실행, Added와 Removed는 지속 효과가 붙고 떨어질 때다.
    public enum GameplayCueEvent
    {
        Executed,
        Added,
        Removed
    }

    // 지속 효과의 상태 변화 종류다. HUD 버프 아이콘 같은 표시가 받는다.
    public enum GameplayEffectChangeKind
    {
        Added,
        StackChanged,
        Removed
    }
}
