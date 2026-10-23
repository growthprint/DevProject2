#nullable enable
using System.Collections.Generic;
using Game.GAS;

namespace Game.App
{
    // PlayerVitals가 지금 아바타에 붙여 둔 장비, 재능, 폭풍 효과를 기억한다. 원작에서 이 효과들은 캐릭터에 붙어 씬과 함께 사라지므로
    // 아바타 해제가 모두 뗀다. 아바타가 연결됐는지는 PlayerVitals가 먼저 보고, 메인 스레드에서만 쓴다.
    internal sealed class PlayerAvatarEffects
    {
        // 지금 아바타에 붙여 둔 장비 효과다. 동기화가 원하는 목록과 비교해 차이만 붙이고 떼며, 아바타 해제가 모두 뗀다.
        readonly List<GameplayEffectDefinition> _equipmentEffects = new();
        // 동기화마다 원하는 장비 효과를 모으는 재사용 버퍼다. 끝나면 비워 에셋을 잡아 두지 않는다.
        readonly List<GameplayEffectDefinition> _desiredEquipmentEffects = new();
        // 지금 아바타에 붙여 둔 재능 효과다. 재능은 하나만 고르므로 하나만 둔다. 동기화가 바뀐 경우만 갈아 끼우고, 아바타 해제가 뗀다.
        GameplayEffectDefinition? _endowmentEffect;
        // 이 아바타에 건 적이 있는 폭풍 버프 정의다. 아바타 해제가 이 목록으로 모두 뗀다.
        readonly List<GameplayEffectDefinition> _stormEffects = new();

        // 플레이어 루트 칸에 꽂힌 아이템마다 그 장비 효과를 붙여 둔다. 붙이거나 뗀 효과가 있으면 true다.
        // 같은 효과를 쓰는 아이템이 둘이어도 효과는 한 번만 붙인다.
        internal bool SyncEquipment(AbilitySystem abilities, ItemOwnershipState? items, OriginalGameplayCatalog catalog)
        {
            List<GameplayEffectDefinition> desired = _desiredEquipmentEffects;
            desired.Clear();
            // 소유 그래프나 플레이어 루트가 없으면 꽂힌 장비가 없는 것으로 본다. 루트 결함은 그래프를 읽는 월드가 기록한다.
            if (items != null && OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? player))
            {
                IReadOnlyList<ItemSlotState> slots = player.Slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    // 소유 그래프 생성 검증이 끊긴 슬롯 참조를 거절하고 슬롯 목록에는 null이 없으므로, 플레이어 슬롯이 가리키는 아이템은 늘 있다.
                    ItemInstanceState item = items.Find(slots[i].InstanceId)!;
                    // 이어하기와 저장 지점이 소유 그래프의 아이템 ID를 카탈로그와 대조하므로 장착 아이템의 정의 조회는 늘 성공한다.
                    GameplayEffectDefinition? effect = catalog.DefinitionOf(item.OriginalItemId).EquipEffect;
                    if (effect != null && !desired.Contains(effect)) desired.Add(effect);
                }
            }
            bool changed = false;
            // 빠진 장비의 효과부터 떼어, 같은 칸의 장비를 바꾼 동기화에서도 두 장비의 보정이 겹치지 않게 한다.
            for (int i = _equipmentEffects.Count - 1; i >= 0; i--)
            {
                GameplayEffectDefinition applied = _equipmentEffects[i];
                if (desired.Contains(applied)) continue;
                _equipmentEffects.RemoveAt(i);
                RemoveIfActive(abilities, applied);
                changed = true;
            }
            for (int i = 0; i < desired.Count; i++)
            {
                GameplayEffectDefinition effect = desired[i];
                if (_equipmentEffects.Contains(effect)) continue;
                // 목록에 먼저 넣어, 붙이기가 Editor에서 GAS 결함 보고로 멈춰도 다음 동기화와 아바타 해제가 이 효과를 뗄 수 있게 한다.
                // 장비 효과는 적용 조건이 없어 늘 붙으므로 결과를 보지 않는다.
                _equipmentEffects.Add(effect);
                changed = true;
                abilities.ApplyEffect(new GameplayEffectSpec(effect, abilities));
            }
            desired.Clear();
            return changed;
        }

        // 같은 효과면 아무것도 하지 않고, 다르면 앞 효과를 떼고 새 효과를 붙이며 그때 true다.
        internal bool SyncEndowment(AbilitySystem abilities, GameplayEffectDefinition? effect)
        {
            if (ReferenceEquals(_endowmentEffect, effect)) return false;
            GameplayEffectDefinition? applied = _endowmentEffect;
            if (applied != null) RemoveIfActive(abilities, applied);
            // 붙이기 전에 기록해, 붙이기가 Editor에서 GAS 결함 보고로 멈춰도 다음 동기화와 아바타 해제가 이 효과를 뗄 수 있게 한다.
            // 재능 효과는 적용 조건이 없어 늘 붙으므로 결과를 보지 않는다.
            _endowmentEffect = effect;
            if (effect != null) abilities.ApplyEffect(new GameplayEffectSpec(effect, abilities));
            return true;
        }

        // 아바타 해제가 뗄 폭풍 버프 정의를 한 번씩만 기록한다. 같은 버프를 다시 걸어도 목록은 늘지 않는다.
        internal void TrackStorm(GameplayEffectDefinition buff)
        {
            if (!_stormEffects.Contains(buff)) _stormEffects.Add(buff);
        }

        // 아바타 해제가 부른다. 폭풍, 장비, 재능 효과 순서로 떼고 목록을 비운다. 다음 씬의 아바타는 남은 층 없이 시작하고,
        // 장비와 재능 효과는 연결 뒤 첫 동기화와 월드 첫 초기화가 다시 붙인다.
        internal void RemoveAll(AbilitySystem abilities)
        {
            for (int i = 0; i < _stormEffects.Count; i++) RemoveIfActive(abilities, _stormEffects[i]);
            _stormEffects.Clear();
            for (int i = 0; i < _equipmentEffects.Count; i++) RemoveIfActive(abilities, _equipmentEffects[i]);
            _equipmentEffects.Clear();
            GameplayEffectDefinition? endowment = _endowmentEffect;
            if (endowment != null) RemoveIfActive(abilities, endowment);
            _endowmentEffect = null;
        }

        // 없는 효과를 떼려고 작업 단위를 열지 않도록 먼저 확인한다. 아바타 해제와 효과 동기화가 정의 하나씩 뗄 때 쓴다.
        internal static void RemoveIfActive(AbilitySystem abilities, GameplayEffectDefinition definition)
        {
            if (abilities.HasEffect(definition)) abilities.RemoveEffect(definition);
        }
    }
}
