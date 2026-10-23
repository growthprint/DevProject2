#nullable enable
namespace Game.App
{
    // 원작 SceneLoader가 로딩 중에 띄우는 커튼 씬이다. 값이 SceneTable 커튼 배열의 색인이다.
    public enum SceneCurtain
    {
        // 원작 SceneLoader.defaultCurtainScene인 LoadingScreen_Black이다. 검은 배경 위에 로딩 표시만 보인다.
        Black = 0,
        // 원작 LoadingScreen Wakeup이다. 프롤로그에서 튜토리얼로, 기지에서 난이도 재설정으로 갈 때 쓴다.
        WakeUp = 1,
        // 원작 LoadingScreen_Getout이다. 기지에서 출격할 때 쓴다.
        GetOut = 2,
        // 원작 GameplayDataSettings.SceneManagement.evacuateScreenScene인 LoadingScreen Evacuate다.
        Evacuate = 3,
        // 원작 failLoadingScreenScene인 LoadingScreen 1이다. 사망 뒤에 쓴다.
        Fail = 4,
    }

    // 원작 SceneLoader.LoadScene 인수 중 화면 처리를 정하는 값이다. 기본값은 로딩 화면 없이 씬만 바꾸는 전환이다.
    // 전환별 값은 아래 규칙이 원작 호출 지점에서 그대로 옮긴다. 근거 표: Docs/game-runtime.md#loading-screen
    public readonly struct SceneTransition
    {
        SceneTransition(SceneCurtain curtain, bool clickToContinue, bool circleFade)
        {
            ShowsLoadingScreen = true;
            Curtain = curtain;
            ClickToContinue = clickToContinue;
            CircleFade = circleFade;
        }

        // 거짓이면 원작 Startup의 StaticLoadSingle처럼 로딩 화면 없이 씬만 바꾼다. 앱 시작의 메뉴 로드가 쓴다.
        public bool ShowsLoadingScreen { get; }
        public SceneCurtain Curtain { get; }
        // 원작 clickToConinue다. 참이면 로딩이 끝난 뒤 화면을 클릭해야 다음 씬이 켜진다.
        public bool ClickToContinue { get; }
        // 원작 doCircleFade다. 참이면 처음 덮을 때와 마지막에 걷을 때 원형으로 닫히고 열린다.
        public bool CircleFade { get; }

        // 로딩 화면은 팁을 보이지 않으므로 원작 hideTips 인수는 옮기지 않는다.
        static SceneTransition Of(SceneCurtain curtain, bool clickToContinue = false, bool circleFade = true) =>
            new SceneTransition(curtain, clickToContinue, circleFade);

        // 원작 ContinueButton의 새 게임이다. 메뉴에서 준비 씬을 검은 커튼으로 연다.
        internal static SceneTransition NewGame => Of(SceneCurtain.Black);

        // 원작 이어하기는 기지만 LoadBaseScene(클릭해서 계속)으로 연다. 기지 전 단계 이어하기는 원작에 없어 새 게임과 같게 둔다.
        internal static SceneTransition Continue(GameScene target) =>
            target == GameScene.BaseCamp ? Of(SceneCurtain.Black, clickToContinue: true) : NewGame;

        // 원작 Button_LoadMainMenu의 SceneLoader.LoadMainMenu다.
        internal static SceneTransition MainMenu => Of(SceneCurtain.Black);

        // 원작 GamePrepareProcess의 인트로와 기지 로드다. 둘 다 검은 커튼으로 연다.
        internal static SceneTransition FromPrepare => Of(SceneCurtain.Black);

        // 원작 기지 SelectDifficulty 프록시다. 기지에서 준비 씬을 Wakeup 커튼으로 연다.
        internal static SceneTransition DifficultyReselect => Of(SceneCurtain.WakeUp);

        // 도입부 진행이다. 준비에서 인트로는 원작 GamePrepareProcess(검은 커튼), 인트로에서 프롤로그는 원작 CutScene_EggPlanet
        // 프록시(검은 커튼, 원형 없음), 프롤로그에서 튜토리얼은 원작 Prologue_1 ToPrison 프록시(Wakeup 커튼)다.
        internal static SceneTransition Advance(GameScene target) =>
            target == GameScene.Prologue ? Of(SceneCurtain.Black, circleFade: false)
                : target == GameScene.Tutorial ? Of(SceneCurtain.WakeUp) : FromPrepare;

        // 기지 출격이다. 벙커 문은 원작 GoToGrtoundZero 프록시, 지도 선택은 원작 MapSelectionView(클릭해서 계속)다.
        internal static SceneTransition Raid(RaidEntry entry) =>
            Of(SceneCurtain.GetOut, clickToContinue: entry.Kind == RaidEntryKind.MapSelection);

        // 결과 화면의 계속이다. 철수와 튜토리얼 완료는 원작 BackToBunker와 Exit 프록시처럼 철수 커튼을 쓴다.
        // 사망은 원작 LevelManager 사망 처리처럼 실패 커튼을 쓰고, 기지로 갈 때만 LoadBaseScene처럼 클릭을 기다린다.
        internal static SceneTransition LeaveClosure(RaidOutcome outcome, GameScene target) =>
            outcome == RaidOutcome.Extracted ? Of(SceneCurtain.Evacuate)
                : Of(SceneCurtain.Fail, clickToContinue: target == GameScene.BaseCamp);
    }
}
