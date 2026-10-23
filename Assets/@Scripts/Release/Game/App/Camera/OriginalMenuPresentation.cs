#nullable enable annotations
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 메인 메뉴 연출을 재현한다. 로고 도입, 타이틀 클릭, 메뉴 공개 순서로 진행하고 포인터에 따라 카메라가 움직인다.
    [DisallowMultipleComponent]
    public sealed class OriginalMenuPresentation : MonoBehaviour
    {
        // 아래 객체와 곡선은 원본 메뉴에서 복원한 값을 씬에 직렬화해 쓴다.
        [SerializeField] Camera _camera = null!;
        [SerializeField] Transform _yaw = null!;
        [SerializeField] Transform _pitch = null!;
        // 원본 BackgroundCenter는 소품과 다른 범위로 포인터에 반응한다.
        [SerializeField] Transform _background = null!;
        [SerializeField] SpriteRenderer _logo = null!;
        // 원본 Canvas에 연결된 메뉴 View가 모든 uGUI 표시 대상을 소유한다.
        [SerializeField] MainMenuUguiView _view = null!;
        [SerializeField] AnimationCurve _logoIn = new();
        [SerializeField] AnimationCurve _logoOut = new();
        [SerializeField] AnimationCurve _promptIn = new();
        [SerializeField] AnimationCurve _menuIn = new();
        [SerializeField] AnimationCurve _cameraX = new();
        [SerializeField] AnimationCurve _cameraY = new();
        [SerializeField] AnimationCurve _cameraZ = new();
        [SerializeField] float _introDuration;
        // 아래 일곱 필드는 타이틀에서 메뉴로 넘어가는 원본 전환 값이다.
        // 에디터의 ComponentAuthoring.ConfigureMotion이 값을 검사한 뒤 함께 쓴다.
        [SerializeField] AnimationCurve _menuSlide = new();
        [SerializeField] AnimationCurve _promptOut = new();
        [SerializeField] AnimationCurve _promptPulse = new();
        [SerializeField] Vector2 _menuOffset;
        [SerializeField] float _menuSlideDuration;
        [SerializeField] Vector3 _menuCameraOffset;
        [SerializeField] float _promptSourceFontSize;
        // 포인터 반응 범위는 씬에 저장된 원본 값만 쓴다. 코드 기본값을 함께 두면 씬 값과 따로 어긋날 수 있다.
        [SerializeField] Vector2 _yawRange;
        [SerializeField] Vector2 _pitchRange;
        [SerializeField] Vector2 _positionX;
        [SerializeField] Vector2 _positionY;
        // 기준 위치는 첫 활성화 때 한 번만 저장한다. 연출 시간도 초기화하지 않아 다시 활성화돼도 도입을 재생하지 않는다.
        bool _started;
        float _time;
        float _transitionTime = -1;
        Vector3 _cameraRest;
        // OnDisable에서 같은 Button의 리스너를 해제하려고 활성화 동안 보관한다.
        Button? _titleButton;
        // 메뉴 RectTransform의 기준 위치다. 전환 중 이동 오프셋은 여기에 더해 적용한다.
        Vector2 _menuRest;
        // 프리팹에 저장된 안내문 글자 크기다. 클릭 펄스는 이 크기에 원본 글자 크기 비율을 곱한다.
        float _promptFontSize;
        // 타이틀을 클릭한 순간 한 번 알린다. 메뉴 음향은 이 신호로 시작한다.
        public event Action? MenuOpened;
        // 메뉴가 화면에 공개된 뒤 한 번 알린다. UI는 이 신호로 화면을 갱신해 메뉴 버튼 입력을 연다.
        public event Action? MenuReady;
        bool _readyReported;
        // 모달이 열렸을 때 메뉴를 숨기거나 입력을 막는 값이다. 애니메이션이 매 프레임 메뉴를 되살리지 않도록 따로 둔다.
        bool _hideMenu, _blockMenuInput;
        // 원본 트윈이 끝나면 멈추듯, 도입이나 전환 곡선이 모두 끝난 뒤에는 Update가 곡선 단계를 건너뛴다.
        // 끝 시각은 첫 활성화 때 곡선 마지막 키에서 한 번 구한다. 반복 곡선은 끝나지 않는 것으로 본다.
        float _introEnd, _transitionEnd;
        bool _curvesSettled;
        // 저장에 성공한 카메라 움직임 줄이기 옵션이다. 켜면 포인터 연출을 멈추고 메뉴 이동도 생략한다.
        // 메뉴 이동 위치가 이 값에 따라 달라지므로 바뀌면 곡선 단계를 한 번 다시 적용한다.
        bool _reduceMotion;
        public bool ReduceMotion
        {
            get => _reduceMotion;
            set
            {
                if (_reduceMotion == value) return;
                _reduceMotion = value;
                _curvesSettled = false;
            }
        }
        // 원본 배경 MainMenuCamera의 반응 범위다. 포인터가 한쪽 끝에서 반대쪽 끝으로 가면 +0.5에서 -0.5로 움직인다.
        const float _backgroundSwing = .5f;
        // 타이틀 클릭 뒤 메뉴를 공개하기까지의 원본 지연 시간이다.
        const float _menuRevealDelay = .35f;
        internal bool IsMenuOpen => _transitionTime >= 0 && _time - _transitionTime >= _menuRevealDelay;

        // UI 컨트롤러가 설정 화면, 저장 슬롯 화면, 로딩 중에 메뉴를 숨기거나 입력을 막는다.
        internal void SetOverlayState(bool hideMenu, bool blockInput)
        {
            _hideMenu = hideMenu;
            _blockMenuInput = blockInput;
            // 기준 위치를 저장하기 전에는 프레임을 적용할 수 없다. 값은 남겨 두고 첫 활성화 때 반영한다.
            if (_started) ApplyFrame();
        }

        // 타이틀 Button에 클릭 콜백을 연결한다. 연출 참조와 타이틀 입력의 Button은 제작기가 연결하고
        // ProductUiLocalizationTests가 메뉴 씬에서 확인한다.
        void OnEnable()
        {
            Button titleButton = _view.TitleInput.GetComponent<Button>();
            if (!_started)
            {
                _cameraRest = _camera.transform.localPosition;
                _menuRest = _view.MenuRoot.anchoredPosition;
                // 펄스가 글자 크기를 바꾸기 전에 프리팹 값을 기준으로 남긴다.
                _promptFontSize = _view.TitlePrompt.fontSize;
                // 도입은 메뉴 시간, 전환은 클릭 뒤 시간으로 곡선을 평가하므로 각각의 끝 시각을 따로 구한다.
                // 메뉴 공개 곡선과 이동은 공개 지연 뒤에 시작하고, 이동은 0에서 1로 자른 진행도라 이동 시간에 끝난다.
                _introEnd = Mathf.Max(Mathf.Max(EndTime(_logoIn), EndTime(_promptIn)),
                    Mathf.Max(EndTime(_cameraX), Mathf.Max(EndTime(_cameraY), EndTime(_cameraZ))));
                _transitionEnd = Mathf.Max(Mathf.Max(EndTime(_logoOut), EndTime(_promptOut)),
                    Mathf.Max(EndTime(_promptPulse), _menuRevealDelay + Mathf.Max(EndTime(_menuIn), _menuSlideDuration)));
                _started = true;
            }
            _titleButton = titleButton;
            _titleButton.onClick.AddListener(ContinueFromTitle);
            ApplyFrame();
        }

        // 리스너만 해제하고 연출 시간은 남겨, 다시 활성화되면 이어서 재생한다.
        void OnDisable()
        {
            if (_titleButton != null) _titleButton.onClick.RemoveListener(ContinueFromTitle);
            _titleButton = null;
        }

        // 도입이 끝난 뒤의 첫 클릭만 메뉴 전환을 시작하고 MenuOpened를 한 번 알린다.
        void ContinueFromTitle()
        {
            if (_time < _introDuration || _transitionTime >= 0)
                return;

            _transitionTime = _time;
            // 전환 곡선이 새로 시작되므로 도입이 끝나 멈춘 곡선 단계를 다시 돌린다.
            _curvesSettled = false;
            MenuOpened?.Invoke();
        }

        // timeScale과 무관한 메뉴 시간으로 원본 곡선을 평가하고, 화면 대비 포인터 위치로 카메라를 움직인다.
        // 활성 상태에서만 돌므로 OnEnable의 기준 위치 저장이 항상 먼저 끝나 있다.
        // 원본처럼 포인터 연출은 계속 돌고, 곡선이 끝난 뒤에는 같은 값을 다시 쓰지 않도록 곡선 단계를 건너뛴다.
        void Update()
        {
            _time += Time.unscaledDeltaTime;
            // 움직임 줄이기가 켜지면 포인터가 화면 정중앙에 있는 원본 기본 구도로 고정한다.
            if (ReduceMotion) ApplyPointer(Vector2.one * .5f, Vector2.one);
            else if (Mouse.current != null && Application.isFocused)
                ApplyPointer(Mouse.current.position.ReadValue(), new Vector2(Screen.width, Screen.height));
            if (!_curvesSettled) ApplyFrame();
        }

        // 곡선이 마지막 키 뒤로 같은 값을 유지하는 시각이다. 반복하거나 왕복하는 곡선은 끝나지 않으므로 무한대를 돌려준다.
        static float EndTime(AnimationCurve curve)
        {
            if (curve.postWrapMode == WrapMode.Loop || curve.postWrapMode == WrapMode.PingPong) return float.PositiveInfinity;
            return curve.length > 0 ? curve[curve.length - 1].time : 0f;
        }

        // 원본 MainMenuCamera처럼 clamp되는 Lerp를 쓰고, yaw와 pitch 관절도 원본과 같은 축으로 움직인다.
        internal void ApplyPointer(Vector2 pointer, Vector2 size)
        {
            if (size.x <= 0 || size.y <= 0) return;
            float x = pointer.x / size.x, y = pointer.y / size.y;
            _yaw.localRotation = Quaternion.Euler(0, Mathf.Lerp(_yawRange.x, _yawRange.y, x), 0);
            _pitch.localRotation = Quaternion.Euler(Mathf.Lerp(_pitchRange.x, _pitchRange.y, y), 0, 0);
            _yaw.localPosition = new Vector3(Mathf.Lerp(_positionX.x, _positionX.y, x), Mathf.Lerp(_positionY.x, _positionY.y, y), 0);
            // 원본에서 두 번째 MainMenuCamera 스크립트가 배경에 주는 이동과 회전이다. 두 값 모두 같은 원본 범위를 쓴다.
            _background.localPosition = new Vector3(Mathf.Lerp(_backgroundSwing, -_backgroundSwing, x),
                Mathf.Lerp(_backgroundSwing, -_backgroundSwing, y), 0);
            _background.localRotation = Quaternion.Euler(0, Mathf.Lerp(_backgroundSwing, -_backgroundSwing, x), 0);
        }

        // 원본 곡선 값과 메뉴 공개 지연을 uGUI 표시 상태에 적용한다. 기준 위치를 저장한 첫 활성화 뒤에만 불린다.
        void ApplyFrame()
        {
            bool transitioning = _transitionTime >= 0;
            float transition = transitioning ? _time - _transitionTime : 0;
            UnityEngine.Color color = _logo.color;
            color.a = Mathf.Clamp01(transitioning ? _logoOut.Evaluate(transition) : _logoIn.Evaluate(_time));
            _logo.color = color;
            // 원본 ToMenu 전환에도 있는 작은 카메라 위치 오프셋을 적용한다.
            _camera.transform.localPosition = _cameraRest + (transitioning ? _menuCameraOffset :
                new Vector3(_cameraX.Evaluate(_time), _cameraY.Evaluate(_time), _cameraZ.Evaluate(_time)));
            // 안내문 알파와 클릭 때 글자 크기 펄스를 같은 전환 시간으로 평가한다. 안내문은 메뉴 CanvasGroup 밖에 있어
            // 알파를 TMP 색상에 직접 넣는다.
            Color promptColor = _view.TitlePrompt.color;
            // _promptOut은 제작기가 비어 있지 않은 곡선으로만 저장한다.
            promptColor.a = Mathf.Clamp01(transitioning ? _promptOut.Evaluate(transition) : _promptIn.Evaluate(_time));
            _view.TitlePrompt.color = promptColor;
            // 전환 전에는 프리팹 글자 크기를 그대로 두고, 클릭 뒤에만 원본 펄스 비율을 곱한다.
            // 펄스 곡선과 0보다 큰 원본 글자 크기는 제작기가 보장하므로 나누기 전에 검사하지 않는다.
            if (transitioning) _view.TitlePrompt.fontSize = _promptFontSize * _promptPulse.Evaluate(transition) / _promptSourceFontSize;
            // 메뉴가 공개되기 전에는 전체 화면 타이틀 입력만 받고, 메뉴 입력과 레이캐스트는 막는다.
            bool menuOpen = IsMenuOpen;
            // 모달이 열려 있으면 애니메이션이 메뉴를 다시 보이거나 입력 가능하게 만들지 않는다.
            bool menuVisible = menuOpen && !_hideMenu;
            _view.TitleInput.SetActive(!menuOpen && !_blockMenuInput);
            _view.MenuCanvasGroup.alpha = menuVisible ? Mathf.Clamp01(_menuIn.Evaluate(transition - _menuRevealDelay)) : 0;
            bool menuInteractive = menuVisible && !_blockMenuInput;
            _view.MenuCanvasGroup.interactable = menuInteractive;
            _view.MenuCanvasGroup.blocksRaycasts = menuInteractive;
            // 메뉴 공개 뒤 원본 RectTransformFade의 0.4초 이동을 재현한다. 이동 곡선과 0보다 큰 시간은 제작기가 보장하므로
            // 바로 나눈다.
            float remaining = ReduceMotion ? 0 : 1 - _menuSlide.Evaluate(Mathf.Clamp01((transition - _menuRevealDelay) / _menuSlideDuration));
            // uGUI도 위쪽이 양수 Y라서 원본 RectTransform 오프셋을 변환 없이 더한다.
            _view.MenuRoot.anchoredPosition = _menuRest + _menuOffset * remaining;
            // 메뉴가 공개되면 MenuReady를 한 번만 알려 UI가 버튼 입력을 열게 한다. 시간 값은 이 메서드에서 바뀌지 않아
            // 앞에서 구한 menuOpen을 그대로 쓴다.
            if (menuOpen && !_readyReported) { _readyReported = true; MenuReady?.Invoke(); }
            // 지금 단계의 곡선이 모두 끝났으면 이번에 쓴 값이 마지막 값이다. 전환의 끝 시각은 공개 지연보다 늦어 이때 메뉴 공개와
            // MenuReady 알림도 끝나 있다. 모달 상태는 SetOverlayState가 이 메서드를 직접 불러 반영한다.
            _curvesSettled = transitioning ? transition >= _transitionEnd : _time >= _introEnd;
        }
    }
}
