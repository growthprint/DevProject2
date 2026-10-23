#nullable enable

namespace Game.App
{
    // 열쇠 등록기(MasterKeysManager)와 세션 소지품에 걸친 명령이다. 원작 InteractableBase.TryGetRequiredItem과 UseRequiredItem의
    // 문 열쇠 확인, 원작 MasterKeysManager.SubmitAndActivate의 등록을 한곳에 둔다. 소유자가 화면이나 상호작용 대상을 모르게 하기 위해서다.
    internal static partial class GameplayCommands
    {
        // 원작처럼 등록 열쇠를 먼저 보고, 등록된 종류면 실물 없이 있다고 답한다(원작 InteractableBase.cs:555-558).
        // 그 뒤 장비 칸, 가방(+아이템 칸), 펫 가방(+아이템 칸)을 보고 창고는 보지 않는다(ItemSourceRules.Keys). 확정 상태를 읽기만 하므로 관문을 보지 않는다.
        // 호출자: WorldInteractions.HasRequired(전투 월드와 프롤로그의 상호작용 호스트).
        internal static bool HasRequiredItem(GameSessionManager session, MasterKeysManager keys, int originalItemId) =>
            keys.IsActive(originalItemId) || session.Inventory.HasRequiredItem(originalItemId, ItemSourceRules.Keys);

        // 원작 UseRequiredItem이다. 등록된 종류는 원작처럼 찾은 실물 참조가 없어 쓰지 못하고 실패한다. 가방에 실물이 있어도 그대로다
        // (원작 InteractableBase.cs:416-424, S4 2절). 그 밖에는 찾기와 같은 출처에서 하나를 쓴다. 호출자: WorldInteractions.TryUseRequired.
        internal static bool TryUseRequiredItem(GameSessionManager session, MasterKeysManager keys, OriginalGameplayCatalog catalog,
            int originalItemId) =>
            !keys.IsActive(originalItemId) && session.Inventory.TryUseRequiredItem(catalog, originalItemId, ItemSourceRules.Keys);

        // 원작 MasterKeysManager.SubmitAndActivate다. 원작과 달리 확인을 먼저 한다. 등록할 수 있는 종류이고, 아직 등록되지 않았고,
        // 가방이나 이 씬에서 보이는 창고(기지)의 맨 위 칸에 있어야 한다. 그 아이템을 빼고 메모리에 확정한 뒤 등록한다.
        // 원작은 겹친 열쇠를 1 줄이지만 카탈로그의 열쇠는 모두 최대 1개라 빼기가 곧 1개 소모다(MasterKeysRegisterTests가 데이터로 확인한다).
        // 관문이 닫혔거나 확인에 걸리면 기록 없이 false이고 아무것도 바뀌지 않는다. 호출자: MasterKeyRegisterScreen(등록 버튼).
        internal static bool TryRegisterMasterKey(GameSessionManager session, MasterKeysManager keys, OriginalGameplayCatalog catalog, string itemId)
        {
            GameSessionSnapshot snapshot = session.Snapshot();
            ItemInstanceState? item = snapshot.Campaign.Gameplay.Inventory.Items?.Find(itemId);
            if (item == null) return false;
            // 저장 읽기의 카탈로그 검사를 지난 소유 그래프의 아이템이라 정의가 늘 있다.
            OriginalItemDefinition definition = catalog.DefinitionOf(item.OriginalItemId);
            if (!MasterKeysManager.IsRegistrable(definition) || keys.IsActive(definition.Id)) return false;
            // 원작 등록 창은 가방과 PlayerStorage(기지에만 있음) 목록만 보인다. 등록 창들이 함께 쓰는 출처 규칙이다.
            ItemSources sources = ItemSourceRules.BagAndBaseStorage(snapshot.SceneId);
            if (!session.Inventory.TryRemoveOwned(itemId, sources, out _)) return false;
            keys.Activate(definition.Id);
            return true;
        }
    }
}
