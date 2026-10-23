#nullable enable
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 원작 DecomposeDatabase.Decompose를 세션 소지품 명령 하나로 옮긴다. 기지 작업대 분해 항목과 제로존 분해 스테이션의 분해 화면이 부른다.
    // 원작은 나누기, 부품 돌려주기, 결과 지급, 파괴를 따로 하지만 여기서는 한 작업 사본에서 계산해 그래프와 창고 임시 보관함을 한 번에 확정한다.
    internal sealed partial class SessionInventoryCommands
    {
        // 원작이 분해 결과 아이템의 FromInfoKey에 넣는 출처 키다. 아이템 저장 값(fromInfo)이라 바꾸지 않는다.
        internal const string _decomposeSource = "UI_ItemDecompose";

        // 원작 DecomposeDatabase.Decompose(item, count)다. itemId는 소지품이나 기지 창고의 맨 위 칸 아이템이고, count는 1 이상 겹친 수 이하다.
        // 일부면 그만큼 나누고, 묶음 전체면 칸 부품과 내부 가방 내용물을 돌려준 뒤 결과를 겹친 수 배로 지급하고 원래 아이템을 지운다.
        // 결과와 부품은 소지품, 기지면 창고 순서로 넣고 남으면 창고 임시 보관함 끝에 붙는다(사용자 결정 2026-10-20). 결과에는 출처 키를 남긴다.
        // 관문이 닫혔거나, 그 자리에 없거나, 분해식이 없거나, 수량이 범위 밖이거나, 결과 수량이 int를 넘으면 기록 없이 false이고 아무것도 바뀌지 않는다.
        // 원작 결과 돈은 가져오기 도구가 원작 분해식 전부 0인지 확인하므로 보지 않는다. 호출자: DecomposeScreen(분해 버튼).
        internal bool TryDecompose(OriginalGameplayCatalog catalog, DecomposeDatabase database, string itemId, int count)
        {
            if (!_session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = _session.Snapshot();
            InventoryPersistenceState state = snapshot.Campaign.Gameplay.Inventory;
            // 분해 화면은 월드 첫 초기화가 소유 그래프를 만든 뒤에만 열린다.
            ItemOwnershipState source = state.Items!;
            // 원작 ItemDecomposeView가 보이는 소지품과 기지 창고다. 분해 화면도 같은 출처 규칙을 읽는다.
            ItemSources sources = ItemSourceRules.BagAndBaseStorage(snapshot.SceneId);
            if (!IsDecomposeTarget(source, sources, itemId)) return false;
            // 맨 위 칸이 가리키는 ID라 검증된 그래프에서 늘 찾는다.
            ItemInstanceState target = source.Find(itemId)!;
            if (!database.TryGet(target.OriginalItemId, out Cost result)) return false;
            if (count < 1 || count > target.Quantity) return false;
            if (!TryDecomposeResults(catalog, result, count, out List<ItemInstanceState> results)) return false;
            var inventory = new GameplayInventory(source);
            var left = new List<ItemOwnershipState>();
            bool toStorage = snapshot.SceneId == GameScene.BaseCamp;
            if (!inventory.Decompose(itemId, count, sources, toStorage, results, new CatalogRules(catalog).Rules, left).IsOk) return false;
            // 개조 명령과 같은 확정(4-U CommitWithBuffer)으로 그래프와 넘친 그래프를 한 번에 확정한다.
            CommitWithBuffer(snapshot, inventory.Build(), left);
            return true;
        }

        // 원작 목록 두 개(소지품, 기지 창고)의 맨 위 칸에 놓인 아이템인지 본다. 소지품 컨테이너 ID는 플레이어 루트 ID다.
        static bool IsDecomposeTarget(ItemOwnershipState items, ItemSources sources, string itemId) =>
            items.PositionIn(items.CharacterId!, itemId) != null
            || ((sources & ItemSources.Storage) != 0 && items.PositionIn(GameplayInventory._storage, itemId) != null);

        // 원작 Cost.Return(배수 = 나눈 묶음의 겹친 수)처럼 결과 항목마다 수량 × count를 원본 최대 스택 단위의 새 노드로 만든다.
        // 곱한 수량이 int를 넘으면 지급할 수 없어 false다.
        static bool TryDecomposeResults(OriginalGameplayCatalog catalog, Cost result, int count, out List<ItemInstanceState> results)
        {
            IReadOnlyList<Cost.ItemEntry> entries = result.Items;
            results = new List<ItemInstanceState>(entries.Count);
            for (int index = 0; index < entries.Count; index++)
            {
                long total = (long)entries[index].Amount * count;
                if (total > int.MaxValue) return false;
                // 결과 아이템 ID는 가져오기 도구가 원작에서 옮긴 빌드 데이터라 정의가 늘 있다(ItemDecomposeTests가 확인한다).
                OriginalItemDefinition definition = catalog.DefinitionOf(entries[index].Id);
                for (int remaining = (int)total; remaining > 0;)
                {
                    int amount = Math.Min(remaining, definition.MaxStack);
                    // 출처 키를 넣는 새 아이템 생성은 지급 경로와 같은 NewItem 하나로 둔다.
                    results.Add(NewItem(definition, amount, _decomposeSource));
                    remaining -= amount;
                }
            }
            return true;
        }
    }
}
