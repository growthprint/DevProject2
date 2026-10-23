#nullable enable annotations
using System;
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 공통 앱 모달과 버튼의 uGUI 참조를 한 곳에 모아 노출한다. 참조가 모두 채워졌는지는 런타임이 아니라 EditMode 테스트가 확인한다.
    // 프리팹과 테스트가 직렬화 필드 이름으로 값을 찾으므로 필드 이름을 바꾸지 않는다.
    [DisallowMultipleComponent]
    public sealed class GameUguiView : MonoBehaviour
    {
        // 화면 최상위 오브젝트다. 프리팹과 테스트가 채우지만 런타임은 읽지 않으므로 CS0414 경고만 이 필드에서 끈다.
#pragma warning disable CS0414
        [SerializeField] GameObject _root = null!;
#pragma warning restore CS0414
        [SerializeField] GameObject _pauseModal = null!;
        [SerializeField] GameObject _overwriteModal = null!;
        [SerializeField] GameObject _errorModal = null!;
        [SerializeField] GameObject _loadingModal = null!;
        [SerializeField] GameOptionsUguiView _options = null!;
        [SerializeField] UnityEngine.UI.Button _resumeButton = null!;
        // 슬롯이 자동 저장되므로 저장 버튼은 두지 않는다.
        [SerializeField] GameMapView _mapView = null!;
        [SerializeField] GameMapView _miniMapView = null!;
        [SerializeField] UnityEngine.UI.Button _customizeButton = null!;
        [SerializeField] UnityEngine.UI.Button _retryAppearanceButton = null!;
        [SerializeField] UnityEngine.UI.Button _mainMenuButton = null!;
        [SerializeField] UnityEngine.UI.Button _pauseQuitButton = null!;
        [SerializeField] UnityEngine.UI.Button _pauseOptionsButton = null!;
        [SerializeField] UnityEngine.UI.Button _confirmNewGameButton = null!;
        [SerializeField] UnityEngine.UI.Button _cancelNewGameButton = null!;
        [SerializeField] UnityEngine.UI.Button _retryButton = null!;
        [SerializeField] UnityEngine.UI.Button _cancelErrorButton = null!;
        [SerializeField] TMP_Text _errorMessage = null!;
        [SerializeField] TMP_Text _loadingMessage = null!;
        [SerializeField] TMP_Text _pauseStatus = null!;
        // 확인 모달(원본 ConfirmDialogue)의 제목과 본문이다. 새 게임 덮어쓰기와 기지 밖 이탈 확인이 번역 키를 바꿔 가며 함께 쓴다.
        [SerializeField] TMP_Text _confirmTitle = null!;
        [SerializeField] TMP_Text _confirmMessage = null!;

        // 컨트롤러는 이름 검색 없이 이 속성으로만 화면 참조를 얻는다.
        public GameObject PauseModal => _pauseModal;
        public GameObject OverwriteModal => _overwriteModal;
        public GameObject ErrorModal => _errorModal;
        public GameObject LoadingModal => _loadingModal;
        public GameOptionsUguiView Options => _options;
        public UnityEngine.UI.Button ResumeButton => _resumeButton;
        public GameMapView MapView => _mapView;
        public GameMapView MiniMapView => _miniMapView;
        public UnityEngine.UI.Button CustomizeButton => _customizeButton;
        public UnityEngine.UI.Button RetryAppearanceButton => _retryAppearanceButton;
        public UnityEngine.UI.Button MainMenuButton => _mainMenuButton;
        public UnityEngine.UI.Button PauseQuitButton => _pauseQuitButton;
        public UnityEngine.UI.Button PauseOptionsButton => _pauseOptionsButton;
        public UnityEngine.UI.Button ConfirmNewGameButton => _confirmNewGameButton;
        public UnityEngine.UI.Button CancelNewGameButton => _cancelNewGameButton;
        public UnityEngine.UI.Button RetryButton => _retryButton;
        public UnityEngine.UI.Button CancelErrorButton => _cancelErrorButton;
        public TMP_Text ErrorMessage => _errorMessage;
        public TMP_Text LoadingMessage => _loadingMessage;
        public TMP_Text PauseStatus => _pauseStatus;
        public TMP_Text ConfirmTitle => _confirmTitle;
        public TMP_Text ConfirmMessage => _confirmMessage;

        // 확인 모달 [취소]가 실행할 컨트롤러 명령이다. 컨트롤러가 바인딩된 동안만 연결된다.
        Action? _confirmCancel;

        // 컨트롤러가 명령을 연결한다. null을 넘기면 연결을 끊는다.
        internal void BindConfirmCancel(Action? command) => _confirmCancel = command;

        // 프리팹 확인 모달의 Cancel 버튼 OnClick 전용 진입점이다. 버튼은 확인 모달이 맨 위에 보일 때만 눌리므로
        // 상태를 다시 보지 않고 연결된 공용 취소 명령을 실행한다. 연결된 명령이 없으면 무시한다.
        public void OnConfirmCancelClicked() => _confirmCancel?.Invoke();
    }
}
