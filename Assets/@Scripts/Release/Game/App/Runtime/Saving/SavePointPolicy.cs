#nullable enable
namespace Game.App
{
    // 어느 씬에서 언제 파일에 저장하는지 판정한다. 원본은 LevelConfig가 LevelManager를 만드는 메인 씬에서만
    // 씬 로드 직전에 저장한다. 프롤로그, 튜토리얼, 기지, 제로존이 해당하고 준비와 인트로 씬은 저장하지 않는다.
    // 메뉴로 가거나 종료할 때는 기지에서만 저장한다. 원본과 달리 프롤로그를 떠날 때는 튜토리얼 도착 상태를 기록해,
    // 튜토리얼에 한 번 도착하면 이어하기가 튜토리얼 처음부터 시작한다.
    internal static class SavePointPolicy
    {
        // 이 씬을 떠나는 전환이 씬 로드 직전에 확정 세션을 기록하는지 판정한다. 프롤로그는 튜토리얼 도착 저장이 대신한다.
        internal static bool SavesOnLeave(GameScene scene) =>
            scene == GameScene.Tutorial || scene == GameScene.BaseCamp || scene == GameScene.ZeroZone;

        // 이 씬에 들어가는 캠페인 진행이 떠나는 씬을 내리기 전에 도착 후보 자체를 기록하는지 판정한다. 튜토리얼만 해당하며,
        // 결과 화면을 떠나 튜토리얼을 다시 여는 경로는 떠날 때의 저장이 같은 세션을 기록하므로 호출자가 제외한다.
        internal static bool SavesOnArrival(GameScene scene) => scene == GameScene.Tutorial;

        // 이 씬에서 메뉴로 돌아가거나 종료할 때 확정 세션을 기록하는지 판정한다.
        internal static bool SavesOnMenuOrQuit(GameScene scene) => scene == GameScene.BaseCamp;
    }
}
