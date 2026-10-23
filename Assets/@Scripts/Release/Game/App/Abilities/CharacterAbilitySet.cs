#nullable enable
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 캐릭터 상태 소유자가 역할별로 거는 GAS 에셋 묶음이다. 플레이어용은 GameManager 프리팹의 PlayerVitals가 참조한다.
    // 적용(AS_Enemy)은 원본 전투 카탈로그가 참조하고 피해 칸만 채운다. 역할이 고정된 효과라 이름 있는 칸으로 둔다.
    // 소유자가 쓰는 칸이 비어 있으면 연결 결함이고, GameManager 프리팹 테스트와 적 GAS 테스트가 확인한다.
    [CreateAssetMenu(menuName = "GAS/Character Ability Set", fileName = "AS_New")]
    internal sealed class CharacterAbilitySet : ScriptableObject
    {
        // 원본 TickVariables 1초 틱이다. SurvivalTickExecution을 쓰는 즉시 효과다.
        [SerializeField] GameplayEffectDefinition? _survivalTick;
        // 원본 UsageUtilities.Use의 소비품 효과다. ItemUseExecution을 쓰는 즉시 효과다.
        [SerializeField] GameplayEffectDefinition? _itemUse;
        // 원본 Health.Hurt 피해다. HurtExecution을 쓰는 즉시 효과이고 피격 큐를 낸다.
        [SerializeField] GameplayEffectDefinition? _hurt;
        // 원본 LevelManager 진입 회복과 튜토리얼 재시작의 최대 체력 채우기다. CurrentHealth를 MaxHealth로 대입한다.
        [SerializeField] GameplayEffectDefinition? _restoreFullHealth;
        // 원작 CA_UseItem, CA_Reload, CA_Interact, CA_Dash 행동이다. 원작 ActionPriorities(장전 1 < 사용 3 < 대시 4 < 상호작용 7)를
        // 각 GA 에셋의 차단과 취소 태그로 둔다. 아바타를 연결할 때 주고 뗄 때 회수한다.
        [SerializeField] CharacterActionAbility? _useItem;
        [SerializeField] CharacterActionAbility? _reload;
        [SerializeField] CharacterActionAbility? _interact;
        [SerializeField] CharacterActionAbility? _dash;
        // 원작 CA_Skill이다. 우선순위 5라 장전과 사용을 끊고, 조준하는 동안 장전, 사용, 대시를 막는다.
        [SerializeField] CharacterActionAbility? _skill;

        internal GameplayEffectDefinition? SurvivalTick => _survivalTick;
        internal GameplayEffectDefinition? ItemUse => _itemUse;
        internal GameplayEffectDefinition? Hurt => _hurt;
        internal GameplayEffectDefinition? RestoreFullHealth => _restoreFullHealth;
        internal CharacterActionAbility? UseItem => _useItem;
        internal CharacterActionAbility? Reload => _reload;
        internal CharacterActionAbility? Interact => _interact;
        internal CharacterActionAbility? Dash => _dash;
        internal CharacterActionAbility? Skill => _skill;
    }
}
