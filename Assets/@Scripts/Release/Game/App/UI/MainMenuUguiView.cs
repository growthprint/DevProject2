#nullable enable annotations
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원본 MainMenu Canvas에서 입력과 연출에 쓰는 대상이다. 직렬화 자산이 스크립트 GUID와 필드 이름으로 참조하므로 필드 이름을 바꾸지 않는다.
    // 직렬화 참조는 EditMode 저작 검사가 확인하므로 메뉴 씬을 준비할 때 다시 검사하지 않는다.
    [DisallowMultipleComponent]
    public sealed class MainMenuUguiView : MonoBehaviour
    {
        [SerializeField] GameObject _titleInput = null!;
        [SerializeField] RectTransform _menuRoot = null!;
        [SerializeField] CanvasGroup _menuCanvasGroup = null!;
        [SerializeField] TMP_Text _titlePrompt = null!;
        [SerializeField] UnityEngine.UI.Button _newGameButton = null!;
        [SerializeField] UnityEngine.UI.Button _continueButton = null!;
        [SerializeField] UnityEngine.UI.Button _deleteSaveButton = null!;
        [SerializeField] UnityEngine.UI.Button _optionsButton = null!;
        [SerializeField] UnityEngine.UI.Button _quitButton = null!;
        [SerializeField] UnityEngine.UI.Button _recoverBackupButton = null!;
        [SerializeField] TMP_Text _saveStatus = null!;

        public GameObject TitleInput => _titleInput;
        public RectTransform MenuRoot => _menuRoot;
        public CanvasGroup MenuCanvasGroup => _menuCanvasGroup;
        public TMP_Text TitlePrompt => _titlePrompt;
        public UnityEngine.UI.Button NewGameButton => _newGameButton;
        public UnityEngine.UI.Button ContinueButton => _continueButton;
        public UnityEngine.UI.Button DeleteSaveButton => _deleteSaveButton;
        public UnityEngine.UI.Button OptionsButton => _optionsButton;
        public UnityEngine.UI.Button QuitButton => _quitButton;
        public UnityEngine.UI.Button RecoverBackupButton => _recoverBackupButton;
        public TMP_Text SaveStatus => _saveStatus;
    }
}
