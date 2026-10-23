#nullable enable annotations
using UnityEngine;
using System;
using TMPro;

namespace Game.App
{
    [DisallowMultipleComponent]
    // 원본 옵션 계층은 그대로 두고 표시와 입력에 필요한 참조만 저장한다. 프리팹이 스크립트 GUID와 필드 이름으로 연결되므로
    // 직렬화 필드 이름을 바꾸지 않는다.
    public sealed class GameOptionsUguiView : MonoBehaviour
    {
        // 원본 OptionsPanel에서 복원한 컨트롤 참조다. _fields는 프리팹에 저장된 참조를 테스트만 읽으므로,
        // 제품 코드가 읽지 않아 생기는 CS0414 경고를 이 필드에서만 끈다.
#pragma warning disable CS0414
        [SerializeField] GameObject _fields = null!;
#pragma warning restore CS0414
        [SerializeField] TMP_Text _status = null!;
        [SerializeField] UnityEngine.UI.Button _applyButton = null!;
        [SerializeField] UnityEngine.UI.Button _defaultsButton = null!;
        [SerializeField] UnityEngine.UI.Button _closeButton = null!;
        [SerializeField] TMP_Dropdown _language = null!;
        [SerializeField] TMP_Dropdown _resolution = null!;
        [SerializeField] TMP_Dropdown _windowMode = null!;
        // 프레임 제한 같은 그래픽 컨트롤은 범위 밖이라 음량과 감도 슬라이더만 연결한다.
        [SerializeField] UnityEngine.UI.Slider _master = null!;
        [SerializeField] UnityEngine.UI.Slider _music = null!;
        [SerializeField] UnityEngine.UI.Slider _sfx = null!;
        [SerializeField] UnityEngine.UI.Slider _sensitivity = null!;
        // 일반과 오디오 두 탭만 연결한다. 범위 밖 스위치는 참조하지 않는다.
        [SerializeField] UnityEngine.UI.Button[] _tabs = Array.Empty<UnityEngine.UI.Button>();
        [SerializeField] GameObject[] _pages = Array.Empty<GameObject>();

        public TMP_Text Status => _status;
        public UnityEngine.UI.Button ApplyButton => _applyButton;
        public UnityEngine.UI.Button DefaultsButton => _defaultsButton;
        public UnityEngine.UI.Button CloseButton => _closeButton;
        public TMP_Dropdown Language => _language;
        public TMP_Dropdown Resolution => _resolution;
        public TMP_Dropdown WindowMode => _windowMode;
        public UnityEngine.UI.Slider Master => _master;
        public UnityEngine.UI.Slider Music => _music;
        public UnityEngine.UI.Slider Sfx => _sfx;
        public UnityEngine.UI.Slider Sensitivity => _sensitivity;
        public UnityEngine.UI.Button[] Tabs => _tabs;
        // 런타임에서 참조를 검사해 던지면 메뉴 연결이 끊기므로 검사는 ProductUiLocalizationTests.AssertOptionsConfigured가 맡는다.
        // 모달의 첫 포커스는 이 View가 아니라 GameOptionsScreen.InitialFocus가 정한다.
        public GameObject[] Pages => _pages;

        // 음량 수치 표시 칸이다.
        [SerializeField] TMP_Text[] _volumeValues = Array.Empty<TMP_Text>();
        // 감도 입력은 원본 Value_Input 컴포넌트를 재사용한다.
        [SerializeField] TMP_InputField _sensitivityInput = null!;
        // 선택된 탭의 원본 강조 표시를 버튼 활성 상태와 분리한다.
        [SerializeField] GameObject[] _tabIndicators = Array.Empty<GameObject>();
        // 탭을 바꿀 때 같은 원본 ScrollRect를 맨 위로 되돌린다.
        [SerializeField] UnityEngine.UI.ScrollRect _scroll = null!;
        // 원본 난이도 배지는 확정된 난이도만 보인다. 키 편집은 범위 밖이다.
        [SerializeField] GameObject _difficultyBadge = null!;
        // 난이도 제목은 고정 번역, 선택값은 화면의 동적 번역 수명에 연결한다.
        [SerializeField] TMP_Text _difficultyLabel = null!;

        public TMP_Text[] VolumeValues => _volumeValues;
        public TMP_InputField SensitivityInput => _sensitivityInput;
        public GameObject[] TabIndicators => _tabIndicators;
        public UnityEngine.UI.ScrollRect Scroll => _scroll;
        public GameObject DifficultyBadge => _difficultyBadge;
        public TMP_Text DifficultyLabel => _difficultyLabel;
    }
}
