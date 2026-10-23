#nullable enable
namespace Game.GAS
{
    // 큐 하나의 연출 정보다. 값만 담고 AbilitySystem은 담지 않아, 연출이 게임 상태를 바꾸지 못하게 한다.
    public readonly struct GameplayCueParameters
    {
        public GameplayCueParameters(GameplayTag tag, GameplayCueEvent cueEvent, float magnitude, int stackCount, in GameplayEffectContext context,
            GameplayEffectDefinition? effect)
        {
            Tag = tag;
            Event = cueEvent;
            Magnitude = magnitude;
            StackCount = stackCount;
            Context = context;
            Effect = effect;
        }

        public GameplayTag Tag { get; }
        public GameplayCueEvent Event { get; }
        // Executed 큐는 효과가 처음 적용한 수정자 크기다(예: 받은 피해). Added와 Removed는 0이다.
        public float Magnitude { get; }
        public int StackCount { get; }
        public GameplayEffectContext Context { get; }
        public GameplayEffectDefinition? Effect { get; }
    }
}
