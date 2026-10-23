#nullable enable

namespace Game.App
{
    // 낚시의 소지품 명령이다. 미끼는 낚싯대 아이템 칸의 내용물을 쓰는 칸 명령이고, 잡은 물고기는 TryDeliver(PlayerThenDrop, 출처 Fishing)로 준다.
    internal sealed partial class SessionInventoryCommands
    {
        // 원작 Action_FishingV2.SpawnFish의 미끼 소모다. ownerId 아이템의 slotKey 칸 내용물을 겹치면 하나, 아니면 통째로 없애고 메모리에 한 번 확정한다.
        // 관문이 닫혔거나, 주인이 그래프에 없거나, 칸이 비었으면 기록 없이 false다. 호출자: WorldInteractions(IFishingHost.TryConsumeBait, 낚싯대 Bait 칸).
        internal bool TryConsumeSlotContent(OriginalGameplayCatalog catalog, string ownerId, string slotKey)
        {
            if (!_session.AcceptsCommands) return false;
            var rules = new CatalogRules(catalog);
            return ChangeInventory((Owner: ownerId, Slot: slotKey, Rules: rules.Rules),
                static (inventory, use) => inventory.ConsumeSlotContent(use.Owner, use.Slot, use.Rules), out _);
        }
    }
}
