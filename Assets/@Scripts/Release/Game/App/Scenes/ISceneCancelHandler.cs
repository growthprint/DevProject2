#nullable enable annotations

namespace Game.App
{
    // UI가 ESC를 받으면 일시정지보다 먼저 현재 게임 씬에 묻는 패널 닫기 계약이다.
    // SceneContext가 게임플레이 루트 아래 구현을 한 번 찾아 두고 TryCancelCurrentPanel에서 묻는다.
    internal interface ISceneCancelHandler
    {
        // 인벤토리 같은 월드 패널이 열려 있으면 닫고 true를 돌려준다.
        // 인트로 재생처럼 일시정지를 열지 않는 씬은 패널이 없어도 ESC를 소비하고 true를 돌려준다.
        bool TryCancelPanel();
    }
}
