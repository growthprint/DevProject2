#nullable enable annotations

namespace Game.App
{
    // 앱 입력 자산에서 여러 곳이 FindAction으로 찾는 "맵/액션" 경로다. 자산의 맵이나 액션 이름을 바꾸면 이 값도 함께 고친다.
    // 한 곳에서만 찾는 경로는 그 호출부에 문자열로 둔다.
    internal static class GameInputPaths
    {
        internal const string _playerMove = "Player/Move";
        internal const string _playerSprint = "Player/Sprint";
        internal const string _playerJump = "Player/Jump";
        internal const string _playerInteract = "Player/Interact";
        internal const string _playerAttack = "Player/Attack";
        internal const string _playerAim = "Player/Aim";
        // 게임 조준과 UI가 함께 읽는 화면 좌표 입력이다.
        internal const string _uiPoint = "UI/Point";
        internal const string _uiDialogueConfirm = "UI/DialogueConfirm";
        internal const string _uiToggleInventory = "UI/ToggleInventory";
    }
}
