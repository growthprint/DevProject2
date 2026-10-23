#nullable enable
using System;

namespace Game.App
{
    // 아이템을 찾고 세고 빼는 명령이 볼 출처다. 호출마다 고르며, 보는 순서는 고른 값과 상관없이 원작대로 장비, 창고, 가방, 펫 가방이다.
    // 장비 칸은 열쇠 찾기만 본다. 저장하지 않는 값이다.
    [Flags]
    internal enum ItemSources
    {
        None = 0,
        Equipment = 1,
        Storage = 2,
        Bag = 4,
        Pet = 8
    }

    // 호출자가 쓰는 출처 묶음이다. 열거형에는 메서드를 둘 수 없어 정적 클래스에 둔다.
    internal static class ItemSourceRules
    {
        // 원작 InteractableBase.TryGetRequiredItem의 열쇠 찾기 출처다. 창고는 보지 않는다.
        internal const ItemSources Keys = ItemSources.Equipment | ItemSources.Bag | ItemSources.Pet;

        // 원작 FindAllBelongsToPlayer의 출처다. 원작 창고(PlayerStorage)는 기지에만 있어, 레이드 중에는 창고 컨테이너가 그래프에 있어도 빼고 본다.
        internal static ItemSources Owned(GameScene scene) => scene == GameScene.BaseCamp
            ? ItemSources.Storage | ItemSources.Bag | ItemSources.Pet
            : ItemSources.Bag | ItemSources.Pet;

        // 명령과 화면은 확정 세션의 지금 씬으로 출처를 고른다. 그 조회를 한곳에 둔다.
        internal static ItemSources Owned(GameSessionManager session) => Owned(session.Snapshot().SceneId);

        // 원작 분해, 설계도 등록, 열쇠 등록 창이 보이는 목록이다. 소지품과, 기지면 창고(PlayerStorage)이고 펫 가방은 아니다.
        // 세 창과 그 명령이 같은 출처를 쓰도록 한곳에 둔다.
        internal static ItemSources BagAndBaseStorage(GameScene scene) => ItemSources.Bag | (Owned(scene) & ItemSources.Storage);
    }
}
