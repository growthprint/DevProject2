#nullable enable annotations

namespace Game.App
{
    // 인벤토리 화면이 캐릭터 행동에 묻는 일이다. 전투 월드는 전투 시뮬레이션이 맡고, 무기와 아이템 사용이 없는 프롤로그는
    // 늘 행동이 없는 구현(NoInventoryActions)을 쓴다.
    internal interface IInventoryActions
    {
        // 장전, 사용, 대시 같은 행동이 진행 중인지다. 기지에서 창고를 열기 전에 본다.
        bool ActionInProgress { get; }
        // 원본 CA_UseItem처럼 아이템 사용 중이라 인벤토리를 바꿀 수 없는지다.
        bool UsingItem { get; }
        // 고른 소지품 아이템 사용을 시작한다. 시작했으면 true다.
        bool TryBeginUse(string id);
    }

    // 프롤로그의 인벤토리 화면이 쓰는 행동 없음 구현이다. 원작 프롤로그에는 무기와 소비품이 없다.
    internal sealed class NoInventoryActions : IInventoryActions
    {
        internal static readonly NoInventoryActions _instance = new NoInventoryActions();
        public bool ActionInProgress => false;
        public bool UsingItem => false;
        public bool TryBeginUse(string id) => false;
    }
}
