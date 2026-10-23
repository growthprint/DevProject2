#nullable enable
namespace Game.GAS
{
    // UE GameplayEffectSpec처럼 "이 효과를 누가, 어떤 값으로" 적용할지 담은 요청이다. 피격마다 만들어지므로 값 형식이고,
    // SetByCaller 값은 칸 4개에 바로 담아 할당이 없다. 바꾸는 메서드는 새 값을 돌려준다.
    public readonly struct GameplayEffectSpec
    {
        public const int MaxSetByCallerValues = 4;

        readonly int _tag0, _tag1, _tag2, _tag3;
        readonly float _value0, _value1, _value2, _value3;
        readonly int _count;

        public GameplayEffectSpec(GameplayEffectDefinition definition, AbilitySystem? source = null)
            : this(definition, source, default, 0, 0, 0, 0, 0f, 0f, 0f, 0f, 0)
        {
        }

        GameplayEffectSpec(GameplayEffectDefinition? definition, AbilitySystem? source, in GameplayEffectContext context, int tag0, int tag1,
            int tag2, int tag3, float value0, float value1, float value2, float value3, int count)
        {
            Definition = definition;
            Source = source;
            Context = context;
            _tag0 = tag0;
            _tag1 = tag1;
            _tag2 = tag2;
            _tag3 = tag3;
            _value0 = value0;
            _value1 = value1;
            _value2 = value2;
            _value3 = value3;
            _count = count;
        }

        // default 값이면 null이다. 적용 진입점에는 정의가 있는 spec만 넘긴다.
        public GameplayEffectDefinition? Definition { get; }
        public AbilitySystem? Source { get; }
        public GameplayEffectContext Context { get; }
        public bool IsValid => Definition != null;

        public GameplayEffectSpec WithContext(in GameplayEffectContext context) => With(Definition, Source, context);

        // 같은 태그가 있으면 값을 바꾸고, 없으면 빈 칸에 넣는다. 호출 코드는 유효한 태그를 MaxSetByCallerValues개까지만 넣는다.
        public GameplayEffectSpec WithSetByCaller(GameplayTag tag, float magnitude)
        {
            int slot = SlotOf(tag.Id);
            if (slot < 0) slot = _count;
            int count = slot == _count ? _count + 1 : _count;
            return new GameplayEffectSpec(Definition, Source, Context,
                slot == 0 ? tag.Id : _tag0, slot == 1 ? tag.Id : _tag1, slot == 2 ? tag.Id : _tag2, slot == 3 ? tag.Id : _tag3,
                slot == 0 ? magnitude : _value0, slot == 1 ? magnitude : _value1, slot == 2 ? magnitude : _value2,
                slot == 3 ? magnitude : _value3, count);
        }

        // 호출자 값이 늘 이기고, 없을 때만 정의의 에셋 기본값을 읽는다. 기본값은 spec 칸을 쓰지 않아 칸 4개 제한과 무관하다.
        // 칸에는 유효한 태그만 들어 있고 None 질의는 어느 기본값과도 맞지 않아 따로 거르지 않는다. 정의 없는 default spec은 읽지 않는다.
        public bool TryGetSetByCaller(GameplayTag tag, out float magnitude)
        {
            int slot = SlotOf(tag.Id);
            magnitude = slot switch { 0 => _value0, 1 => _value1, 2 => _value2, 3 => _value3, _ => 0f };
            if (slot >= 0) return true;
            return Definition!.TryGetSetByCallerDefault(tag, out magnitude);
        }

        // 거는 코드나 에셋 기본값이 늘 주는 값을 읽는다. 없으면 0이다(UE GetSetByCallerMagnitude의 DefaultIfNotFound).
        public float GetSetByCaller(GameplayTag tag)
        {
            TryGetSetByCaller(tag, out float magnitude);
            return magnitude;
        }

        // 원작 AddBuff의 "기존 버프 유지, 시전자만 갱신" 규칙이 쓴다.
        internal GameplayEffectSpec WithSource(AbilitySystem? source) => With(Definition, source, Context);

        // 이어 거는 효과의 spec이다. 시전자와 문맥은 늘 잇고, SetByCaller 값은 copySetByCallers일 때만 옮긴다.
        // UE FGameplayEffectSpec::InitFromLinkedSpec과 CopySetByCallerMagnitudes에 해당한다.
        internal GameplayEffectSpec ForAdditional(GameplayEffectDefinition definition, bool copySetByCallers) =>
            copySetByCallers
                ? With(definition, Source, Context)
                : new GameplayEffectSpec(definition, Source, Context, 0, 0, 0, 0, 0f, 0f, 0f, 0f, 0);

        // 정의, 시전자, 문맥만 바꾸고 SetByCaller 칸 4개는 그대로 옮기는 복사다. 칸을 늘릴 때 고칠 곳을 하나로 둔다.
        GameplayEffectSpec With(GameplayEffectDefinition? definition, AbilitySystem? source, in GameplayEffectContext context) =>
            new GameplayEffectSpec(definition, source, context, _tag0, _tag1, _tag2, _tag3, _value0, _value1, _value2, _value3, _count);

        int SlotOf(int id)
        {
            if (_count > 0 && _tag0 == id) return 0;
            if (_count > 1 && _tag1 == id) return 1;
            if (_count > 2 && _tag2 == id) return 2;
            if (_count > 3 && _tag3 == id) return 3;
            return -1;
        }
    }
}
