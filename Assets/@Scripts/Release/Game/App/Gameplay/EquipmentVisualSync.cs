#nullable enable annotations
using System;
using System.Collections.Generic;
using DuckCharacter;

namespace Game.App
{
    // 원작 CharacterEquipmentController처럼 머리, 얼굴, 몸, 가방 칸에 꽂힌 장비의 외형을 몸에 붙이고, 빈 칸이나 외형 없는 장비는 뗀다.
    // 전투 월드 컨트롤러가 준비 때 만든다. 호출은 첫 월드 초기화와 세션 변경 알림뿐이고, 정리할 구독은 없다.
    internal sealed class EquipmentVisualSync
    {
        readonly GameplayWorldContext _world;
        // 부위마다 몸에 마지막으로 요청한 장비 외형 ID다. Reset이 어느 ID와도 다른 값으로 채워 다음 Apply가 모든 부위를 한 번 적용하게 한다.
        readonly int[] _appliedEquipment = new int[(int)EquipPartType.EquipCount];

        internal EquipmentVisualSync(GameplayWorldContext world)
        {
            _world = world;
        }

        // 새로 만든 캐릭터에 저장된 장비 외형을 처음 입히기 전에 부른다.
        internal void Reset() => Array.Fill(_appliedEquipment, int.MinValue);

        // 부위마다 마지막으로 요청한 ID를 결과와 관계없이 기억해 같은 ID를 다시 시도하지 않는다. 바인딩 실패는 장비 표시가 보고한다.
        // 기록 없는 거절(캐릭터 없음, 외형 변경 도중, 정리 뒤)은 내부 순서 결함이다.
        internal void Apply(ItemOwnershipState? items)
        {
            GameplayWorldContext world = _world;
            // 그래프나 플레이어 루트가 없으면 장착한 것이 없다고 보고 모두 뗀다. 루트 누락 결함은 인벤토리 화면이 기록한다.
            ItemInstanceState? player = null;
            if (items != null) OriginalInventoryQueries.TryPlayerItem(items, out player);
            IReadOnlyList<string> slots = OriginalCharacterSlots.All;
            for (int index = 0; index < slots.Count; index++)
            {
                if (!OriginalCharacterSlots.TryVisualPart(slots[index], out EquipPartType part)) continue;
                int id = -1;
                if (OriginalInventoryQueries.TryEquippedIn(player, slots[index], out string? instanceId) && instanceId != null)
                {
                    // 장착 칸이 가리키는 아이템은 같은 검증된 그래프에 늘 있다. 정의가 없는 장비는 외형을 알 수 없어 뗀다.
                    ItemInstanceState item = items!.Find(instanceId)!;
                    if (world.Catalog.TryGet(item.OriginalItemId, out OriginalItemDefinition? definition) &&
                        definition!.EquipmentId >= 0) id = definition.EquipmentId;
                }
                if (_appliedEquipment[(int)part] == id) continue;
                _appliedEquipment[(int)part] = id;
                world.Character.TrySetEquipment(part, id);
            }
        }
    }
}
