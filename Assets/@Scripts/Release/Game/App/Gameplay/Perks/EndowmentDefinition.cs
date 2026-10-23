#nullable enable
using System;
using System.Collections.Generic;
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.Endowment.EndowmentIndex와 같은 값이다. 슬롯 저장과 공통 파일 키가 이 값과 이름을 쓰므로 바꾸지 않는다.
    // 공통 파일 키는 원작 철자 그대로 Endowment_Unlock_R_<이름>이다.
    public enum EndowmentIndex
    {
        None = 0,
        Surviver = 1,
        Porter = 2,
        Berserker = 3,
        Marksman = 4,
    }

    // 원작 EndowmentEntry.ModifierDescription이다. 설명 문구가 이 값을 쓰고, 실제 적용은 같은 값을 담은 효과 에셋이 한다.
    [Serializable]
    public sealed class EndowmentModifier
    {
        [SerializeField] string _statKey = string.Empty;
        [SerializeField] AttributeModifierOp _type;
        [SerializeField] float _value;

        internal string StatKey => _statKey;
        internal AttributeModifierOp Type => _type;
        internal float Value => _value;
    }

    // 원작 GameManager 프리팹의 EndowmentEntry 하나다. 가져오기 도구가 만들고 실행 중에는 읽기만 한다.
    public sealed class EndowmentDefinition : ScriptableObject
    {
        [SerializeField] EndowmentIndex _index;
        [SerializeField] Sprite? _icon;
        // 잠긴 항목에 보이는 문구 키다. 원작은 해금 퀘스트 이름 키(Quest_<ID>)를 넣어 둔다.
        [SerializeField] string _requirementTextKey = string.Empty;
        [SerializeField] bool _unlockedByDefault;
        [SerializeField] EndowmentModifier[] _modifiers = Array.Empty<EndowmentModifier>();
        // 원작 UnlockEndowmentWhenQuestComplete가 붙은 퀘스트 ID다. 0이면 해금 퀘스트가 없다.
        [SerializeField] int _unlockQuestId;
        // 수정치를 담은 무한 효과다. 수정치가 없는 재능은 붙일 것이 없어 비어 있다.
        [SerializeField] GameplayEffectDefinition? _effect;

        internal EndowmentIndex Index => _index;
        internal Sprite? Icon => _icon;
        // 원작 키는 m이 두 번 들어간 Endowmment_다. 지역화 표가 이 철자를 쓰고, 설명 키는 원작 설명 키 형식을 따른다.
        internal string DisplayNameKey => "Endowmment_" + _index;
        internal string DescriptionKey => GameLocalization.DescriptionKey(DisplayNameKey);
        internal string RequirementTextKey => _requirementTextKey;
        internal bool UnlockedByDefault => _unlockedByDefault;
        internal IReadOnlyList<EndowmentModifier> Modifiers => _modifiers;
        internal int UnlockQuestId => _unlockQuestId;
        internal GameplayEffectDefinition? Effect => _effect;
    }
}
