#nullable enable
namespace Game.GAS
{
    // UE GameplayEventData처럼 어빌리티를 깨우거나 실행 중인 어빌리티에 전하는 사건이다(예: 피격, 처치, 애니메이션 신호).
    // Payload는 참조 형식만 담아 박싱을 피한다.
    public readonly struct GameplayEventData
    {
        public GameplayEventData(GameplayTag eventTag, AbilitySystem? instigator = null, AbilitySystem? target = null, float magnitude = 0f,
            object? payload = null, GameplayEffectContext context = default)
        {
            EventTag = eventTag;
            Instigator = instigator;
            Target = target;
            Magnitude = magnitude;
            Payload = payload;
            Context = context;
        }

        public GameplayTag EventTag { get; }
        public AbilitySystem? Instigator { get; }
        public AbilitySystem? Target { get; }
        public float Magnitude { get; }
        public object? Payload { get; }
        public GameplayEffectContext Context { get; }
    }
}
