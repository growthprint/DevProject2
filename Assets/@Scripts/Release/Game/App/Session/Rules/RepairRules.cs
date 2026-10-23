#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace Game.App
{
    // 원작 ItemRepairView의 수리 판정(CanRepair, NoNeedToRepair, Broken), 가격 계산(CalculateRepairPrice), 적용(Repair)과
    // ItemUtilities.GetRepairLossRatio, ItemRepair_RepairAllPanel의 목록 규칙이다. 수리 화면과 수리 명령이 같은 함수를 써서
    // 화면의 가격과 단추 상태가 명령의 판정과 어긋나지 않게 한다. 받은 그래프와 정의를 읽기만 하고 아무것도 바꾸지 않는다.
    // 내구도는 원작 절대값이다. 저장 상태의 null 내구도는 가득 참, null 손실은 0으로 본다(InventoryItemDetails와 같은 규칙).
    internal static class RepairRules
    {
        // 원작 Item.Repairable과 GetRepairLossRatio가 보는 태그 이름이다. 카탈로그 태그는 원본 Tag 자산 이름이다.
        internal const string _repairableTag = "Repairable", _weaponTag = "Weapon";
        // 원작 CharacterMainControl.WeaponRepairLossFactor와 EquipmentRepairLossFactor다. 원작 코드가 캐릭터 스탯이 아니라
        // 상수에서 읽어 늘 1이고, 이 값을 바꾸는 특성 데이터도 없다.
        internal const float _weaponLossFactor = 1f, _equipmentLossFactor = 1f;
        // 원작 가격 식의 계수다. 가격 = 올림(아이템 가격 × 고친 양 / 최대 내구도 × 0.5).
        const float _priceFactor = .5f;

        // 저장 상태의 현재 내구도와 누적 손실이다. 모르는 값은 원작 새 아이템처럼 가득 참과 손실 0이다.
        internal static float Current(OriginalItemDefinition definition, ItemInstanceState item) => item.AbsoluteDurability ?? definition.Durability;
        internal static float Loss(ItemInstanceState item) => item.DurabilityLoss ?? 0f;

        // 원작 Item.MaxDurabilityWithLoss다.
        internal static float MaxWithLoss(float maximum, float loss) => maximum * (1f - loss);

        // 원작 Item.Repairable이다. 내구도를 쓰고 'Repairable' 태그가 있어야 한다.
        internal static bool IsRepairable(OriginalItemDefinition definition) => definition.Durability > 0f && definition.HasTag(_repairableTag);

        // 원작 GetRepairLossRatio다. 아이템 상수(없으면 카탈로그 기본 0.14)에 무기면 무기 계수, 아니면 장비 계수를 곱한다.
        internal static float LossRatio(OriginalItemDefinition definition) =>
            definition.RepairLossRatio * (definition.HasTag(_weaponTag) ? _weaponLossFactor : _equipmentLossFactor);

        // 원작 Broken과 NoNeedToRepair다. 내구도를 쓰지 않는 아이템(최대 0)은 둘 다 거짓이다.
        internal static bool IsBroken(float maximum, float loss) => maximum > 0f && MaxWithLoss(maximum, loss) < 1f;
        internal static bool NoNeedToRepair(float maximum, float current, float loss) => maximum > 0f && current >= MaxWithLoss(maximum, loss);

        // 원작 CanRepair다. 내구도를 쓰고, 손실 반영 최대가 1 이상이고(파손 아님), 'Repairable'이고, 현재가 손실 반영 최대보다 작아야 한다.
        internal static bool CanRepair(float maximum, float current, float loss, bool repairable)
        {
            if (!(maximum > 0f) || !repairable) return false;
            float limit = MaxWithLoss(maximum, loss);
            return limit >= 1f && current < limit;
        }

        // 원작 모두 수리 목록의 조건(Durability < MaxDurabilityWithLoss)이다. 태그와 파손은 보지 않는다.
        internal static bool NeedsRepair(float maximum, float current, float loss) => current < MaxWithLoss(maximum, loss);

        // 원작 CalculateRepairPrice(Item, out ...) 식 그대로다. 채울 양에서 비율만큼을 영구 손실로 떼고, 남은 양이 0 이하면 가격과
        // 손실 비율이 0이다. 내구도를 쓰지 않는 아이템은 모두 0이다. 계산 순서와 float 연산도 원작과 같게 둔다.
        internal static RepairQuote Quote(float maximum, float current, float loss, float ratio, int value)
        {
            if (!(maximum > 0f)) return default;
            float repaired = MaxWithLoss(maximum, loss) - current;
            float lost = repaired * ratio;
            repaired -= lost;
            if (repaired <= 0f) return new RepairQuote(0, lost, 0f);
            return new RepairQuote(Mathf.CeilToInt(value * (repaired / maximum) * _priceFactor), lost, lost / maximum);
        }

        // 원작 Repair의 적용이다. 손실에 손실 비율을 더하고, 내구도를 최대 × (1 − 손실)로 정한다. 원작 Item의 DurabilityLoss 읽기는
        // 0~1로 자르고 Durability 쓰기는 0~최대로 자르므로 같은 범위로 맞춰 돌려준다. 그래서 저장 상태 검사를 늘 통과한다.
        internal static (float durability, float loss) Repaired(float maximum, float loss, RepairQuote quote)
        {
            float next = Mathf.Clamp01(loss + quote.LossAdded);
            return (Mathf.Clamp(maximum * (1f - next), 0f, maximum), next);
        }

        // 원작 수리 창이 고를 수 있는 아이템이다. 원작 OnOpen은 캐릭터 장비 칸(slotDisplay)과 가방(inventoryDisplay)만 보이므로
        // 그 두 곳의 맨 위 아이템만 찾는다. 창고, 펫 가방, 다른 아이템의 칸에 꽂힌 부품은 고를 수 없다. 플레이어 루트가 없으면 없는 것이다.
        internal static bool TryShown(ItemOwnershipState items, string itemId, [NotNullWhen(true)] out ItemInstanceState? item)
        {
            item = null;
            if (!OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? player)) return false;
            bool shown = items.PositionIn(player.InstanceId, itemId) != null;
            IReadOnlyList<ItemSlotState> slots = player.Slots;
            for (int index = 0; !shown && index < slots.Count; index++) shown = slots[index].InstanceId == itemId;
            if (!shown) return false;
            item = items.Find(itemId);
            return item != null;
        }

        // 원작 RepairAllPanel.Refresh와 RepairItems다. 장비 칸을 화면 순서로 보며 listed에 원작 목록(현재 < 손실 반영 최대)을 담고,
        // 원작 RepairItems가 내는 장비 칸 전체 가격 합을 돌려준다. 목록 밖 가격은 원작 데이터(비율 0.2 이하)에서 늘 0이라 화면 합계와 같다.
        // changes에는 원작 결함까지 그대로 옮겨, 태그와 파손을 보지 않고 내구도를 쓰는 장비 칸 아이템 전부의 Repair 결과를 담는다.
        // 목록 밖 아이템은 손실이 그대로이고 내구도만 손실 반영 최대로 맞춰진다. 값이 그대로인 아이템과 내구도가 없는 아이템은 담지 않는다.
        internal static long RepairAll(ItemOwnershipState items, OriginalGameplayCatalog catalog, List<ItemInstanceState>? listed,
            List<(string id, float durability, float loss)>? changes)
        {
            if (!OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? player)) return 0;
            long total = 0;
            IReadOnlyList<string> slots = OriginalCharacterSlots.All;
            for (int index = 0; index < slots.Count; index++)
            {
                if (!OriginalInventoryQueries.TryEquippedIn(player, slots[index], out string? id) || id == null) continue;
                // 검증된 그래프의 칸이 가리키는 아이템이라 늘 있고, 그래프의 아이템은 저장 읽기 검사로 늘 카탈로그 정의가 있다.
                ItemInstanceState item = items.Find(id)!;
                OriginalItemDefinition definition = catalog.DefinitionOf(item.OriginalItemId);
                float maximum = definition.Durability;
                if (!(maximum > 0f)) continue;
                float current = Current(definition, item), loss = Loss(item);
                RepairQuote quote = Quote(maximum, current, loss, LossRatio(definition), definition.Value);
                total += quote.Price;
                if (NeedsRepair(maximum, current, loss) && listed != null) listed.Add(item);
                if (changes == null) continue;
                (float durability, float repairedLoss) = Repaired(maximum, loss, quote);
                if (durability != current || repairedLoss != loss) changes.Add((id, durability, repairedLoss));
            }
            return total;
        }
    }

    // 원작 CalculateRepairPrice의 반환값과 out 값이다. Price는 원작 가격, Lost는 최대 내구도에서 영구히 줄어드는 양(lostAmount),
    // LossAdded는 그 양의 최대 내구도 비율(lostPercentage)이다. 원작 repairAmount는 화면도 명령도 읽지 않아 두지 않는다.
    internal readonly struct RepairQuote
    {
        internal RepairQuote(int price, float lost, float lossAdded)
        {
            Price = price;
            Lost = lost;
            LossAdded = lossAdded;
        }

        internal int Price { get; }
        internal float Lost { get; }
        internal float LossAdded { get; }
    }
}
