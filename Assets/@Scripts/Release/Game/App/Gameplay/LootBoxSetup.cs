#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 LootBoxLoader의 레벨 시작 배치다. 컨트롤러가 첫 월드 초기화에서 상호작용 객체를 거쳐 한 번 부른다.
    internal static class LootBoxSetup
    {
        // 꺼진 하위 씬 상자에 못 넣은 현금이다. 원작 상자는 하위 씬을 불러올 때 Start에서 현금을 떨구므로 그 하위 씬을 켤 때까지 떨구지 않는다.
        internal readonly struct DeferredCash
        {
            internal DeferredCash(InteractableLootbox box, int cash, int subscene)
            {
                Box = box;
                Cash = cash;
                Subscene = subscene;
            }

            internal InteractableLootbox Box { get; }
            internal int Cash { get; }
            internal int Subscene { get; }
        }

        // 무작위 상자의 생성 여부와 내용을 정한다. 시드는 출격과 상자마다 같아 다시 불러도 같은 칸이다.
        // 꺼진 하위 씬의 상자도 내용과 생성 여부는 지금 정한다. 상자가 그 값을 들고 있다가 하위 씬이 켜지면 그대로 보인다.
        internal static void Run(GameplayWorldContext world, DroppedItemSpawner drops, Func<string, int> seed, bool advancedDebuffMode,
            List<DeferredCash> deferred)
        {
            foreach (InteractableBase interactable in world.Interactables)
                if (interactable is InteractableLootbox lootbox && lootbox.TryGetComponent(out LootBoxLoader loader))
                {
                    int droppedCash = loader.Setup(seed(lootbox.SourceId), world.Catalog, advancedDebuffMode);
                    if (droppedCash <= 0) continue;
                    // 꺼진 하위 씬은 바닥 충돌도 꺼져 있어 지금 떨구면 현금이 공중에 남는다. 그 하위 씬을 켤 때 떨군다.
                    int subscene = world.SubsceneOf(lootbox.transform);
                    if (world.IsSubsceneActive(subscene)) DropCash(drops, lootbox, droppedCash);
                    else deferred.Add(new DeferredCash(lootbox, droppedCash, subscene));
                }
        }

        // 하위 씬을 바꾼 직후 상호작용 객체가 부른다. 지금 켠 하위 씬 상자의 미룬 현금만 떨구고 목록에서 뺀다.
        internal static void DropDeferred(GameplayWorldContext world, DroppedItemSpawner drops, List<DeferredCash> deferred)
        {
            for (int i = deferred.Count - 1; i >= 0; i--)
            {
                DeferredCash pending = deferred[i];
                if (!world.IsSubsceneActive(pending.Subscene)) continue;
                deferred.RemoveAt(i);
                DropCash(drops, pending.Box, pending.Cash);
            }
        }

        // 원작 CreateCash는 빈 칸 없는 상자의 현금을 상자 위 1m에서 (insideUnitSphere + up) * 2 방향, 45도로 던진다.
        // 그 방향은 수평으로 고르게 무작위라 다른 떨굼처럼 forward를 360도 흔든 것과 같다.
        static void DropCash(DroppedItemSpawner drops, InteractableLootbox box, int cash) =>
            drops.SpawnNewItem(GameplayDataSettings.ItemAssets.CashItemTypeID, cash, box.transform.position + Vector3.up);
    }
}
