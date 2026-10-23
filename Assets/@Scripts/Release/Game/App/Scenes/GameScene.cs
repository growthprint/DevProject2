namespace Game.App
{
    // 게임 흐름이 쓰는 논리 씬이다. 값 순서가 SceneTable의 배열 색인이므로 순서를 바꾸지 않는다.
    // 저장에 쓰는 문자열 변환은 GameScenes가 맡는다.
    public enum GameScene { BaseCamp, MainMenu, Customization, Prepare, Intro, Prologue, Tutorial, ZeroZone }
}
