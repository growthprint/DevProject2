#nullable enable

namespace Game.App
{
    // 새 아이템을 플레이어에게 보낼 곳이다. 원작 Cost.Return, PlayerStorage.Push, 상점 구매가 고르는 경로를 호출마다 고른다.
    // 펫 가방은 원작처럼 목적지가 아니다. 창고는 원작 PlayerStorage.Instance가 있는 기지에서만 쓰고, 레이드 중에는 건너뛴다. 저장하지 않는 값이다.
    internal enum ItemDelivery
    {
        // 소지품, 기지면 창고, 그래도 남으면 창고 임시 보관함이다(원작 Return(toPlayerInventory: true), 제조·분해 결과).
        PlayerThenStorage,
        // 기지면 창고, 남으면 보관함이다(원작 SendToPlayerStorage).
        Storage,
        // 늘 보관함이다(원작 Push(toBufferDirectly: true), 퀘스트 보상 아이템).
        Buffer,
        // 소지품, 남으면 바닥이다(원작 레이드 상점 구매). 넘친 아이템은 호출자가 떨군다.
        PlayerThenDrop
    }
}
