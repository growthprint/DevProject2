#nullable enable annotations
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.App
{
    // 같은 버튼의 입력으로 원본 메뉴 버튼의 강조, 누름, 펀치 반응만 재현하고 게임 명령은 다루지 않는다.
    // 꺼지면 기준 자세로 되돌린다.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnityEngine.UI.Button))]
    public sealed class OriginalMenuButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        // 원본 Hovering 객체와, 버튼마다 다른 원본 애니메이션 값을 직렬화한다.
        [SerializeField] GameObject? _hoveringIndicator;
        [SerializeField] RectTransform? _punchTarget;
        [SerializeField] float _punchDuration = .1f;
        [SerializeField] float _punchScale = -.1f;
        [SerializeField] int _vibrato = 10;
        [SerializeField] float _elasticity = 1f;
        [SerializeField] AnimationCurve _punchCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] RectTransform? _sizeTarget;
        [SerializeField] Vector2 _idleSize;
        [SerializeField] Vector2 _pressedSize = new(-8f, -8f);
        [SerializeField] float _sizeDuration = .1f;
        [SerializeField] AnimationCurve _sizeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        // RequireComponent로 보장되는 같은 객체의 Button을 첫 OnEnable이 대입하므로 non-null로 둔다.
        UnityEngine.UI.Button _button = null!;
        Vector3 _restScale;
        Vector2 _sizeFrom;
        Vector2 _sizeTo;
        float _punchElapsed;
        float _sizeElapsed;
        bool _punching;
        bool _resizing;
        // 원본 ButtonAnimation처럼 포인터 사건이 반응을 시작한다. 반응이 남은 동안만 이 코루틴이 돌고, 대기 중인 버튼은 프레임 비용이 없다.
        Coroutine? _tickRoutine;
        // 강조나 누름 크기가 보이는 동안은 입력 잠금을 계속 확인해야 하므로, 코루틴을 멈추지 않는 조건으로 기억한다.
        bool _hovered;
        bool _pressed;

        // 클릭 구독은 첫 활성화에서 한 번만 하고 컴포넌트 수명 동안 유지한다. 활성화마다 남은 변형과 강조를 정리한다.
        void OnEnable()
        {
            if (_button == null)
            {
                _button = GetComponent<UnityEngine.UI.Button>();
                _button.onClick.AddListener(PlayClick);
            }
            // 활성화 전에 남은 변형을 먼저 복원해 기준 자세를 잘못 캐시하지 않는다.
            RestorePose();
            CachePose();
            SetHover(false);
        }

        // 모달 전환, 숨김, 파괴 때 진행 중인 펀치와 누름이 다음 표시로 넘어가지 않게 한다. 클릭 구독은 해제하지 않는다.
        // 컴포넌트만 꺼질 때는 코루틴이 남으므로 직접 멈춘다.
        void OnDisable()
        {
            RestorePose();
            SetHover(false);
            if (_tickRoutine != null) StopCoroutine(_tickRoutine);
            _tickRoutine = null;
        }

        // 꺼진 객체에서 코루틴을 시작하면 Unity가 오류를 남기므로 활성 상태에서만 시작하고, 이미 돌고 있으면 하나만 둔다.
        void StartTicking()
        {
            if (_tickRoutine != null || !isActiveAndEnabled) return;
            _tickRoutine = StartCoroutine(TickRoutine());
        }

        // 시작한 프레임에는 진행하지 않고 다음 프레임부터 Update와 같은 간격으로 진행한다.
        IEnumerator TickRoutine()
        {
            yield return null;
            while (Tick()) yield return null;
            _tickRoutine = null;
        }

        // CanvasGroup이나 Button이 입력을 막으면 버튼 객체가 활성 상태여도 애니메이션과 강조를 정리하고 멈춘다.
        // 펀치, 누름 크기 변화, 강조, 누름 중 하나라도 남아 있으면 true를 돌려 다음 프레임에도 진행한다.
        bool Tick()
        {
            if (!CanRespond())
            {
                RestorePose();
                SetHover(false);
                return false;
            }
            AdvanceAnimation(Time.unscaledDeltaTime);
            return _punching || _resizing || _hovered || _pressed;
        }

        // 반복 클릭은 진행 중인 크기를 재캐시하지 않고 복원한 기준 스케일에서 시작한다. 펀치가 끝날 때까지 프레임 진행을 켠다.
        void PlayClick()
        {
            if (!CanRespond() || _punchTarget == null) return;
            if (_punching) _punchTarget.localScale = _restScale;
            CachePose();
            _punchElapsed = 0f;
            _punching = true;
            StartTicking();
        }

        // 프레임 간격이 긴 경우에도 완료 프레임에 기준 자세를 정확히 복원한다.
        void AdvanceAnimation(float deltaTime)
        {
            if (_punching && _punchTarget != null)
            {
                _punchElapsed = Mathf.Min(_punchDuration, _punchElapsed + Mathf.Max(0f, deltaTime));
                _punchTarget.localScale = _restScale + Vector3.one * EvaluatePunch(_punchElapsed);
                if (_punchElapsed >= _punchDuration)
                {
                    _punchTarget.localScale = _restScale;
                    _punching = false;
                }
            }
            if (_resizing && _sizeTarget != null)
            {
                _sizeElapsed = Mathf.Min(_sizeDuration, _sizeElapsed + Mathf.Max(0f, deltaTime));
                _sizeTarget.sizeDelta = Vector2.LerpUnclamped(_sizeFrom, _sizeTo,
                    _sizeCurve.Evaluate(_sizeElapsed / _sizeDuration));
                if (_sizeElapsed >= _sizeDuration)
                {
                    _sizeTarget.sizeDelta = _sizeTo;
                    _resizing = false;
                }
            }
        }

        // 원본 DOTween Punch의 증가하는 구간 길이와 감쇠 진폭을 동일하게 계산한다.
        float EvaluatePunch(float elapsed)
        {
            if (elapsed >= _punchDuration) return 0f;
            int count = Mathf.Max(2, (int)(_vibrato * _punchDuration));
            float totalWeight = count * (count + 1f) * .5f;
            float segmentStart = 0f;
            float previous = 0f;
            for (int index = 0; index < count; index++)
            {
                float duration = _punchDuration * (index + 1f) / totalWeight;
                float next = index == count - 1 ? 0f : _punchScale * (1f - index / (float)count) *
                    (index % 2 == 0 ? 1f : -_elasticity);
                if (elapsed <= segmentStart + duration)
                    return Mathf.LerpUnclamped(previous, next, _punchCurve.Evaluate((elapsed - segmentStart) / duration));
                segmentStart += duration;
                previous = next;
            }
            return 0f;
        }

        // 입력이 허용된 버튼의 왼쪽 포인터만 누름 반응을 시작한다.
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && CanRespond()) SetPressed(true);
        }

        // 버튼 밖에서 놓아도 현재 누름 애니메이션은 원본 대기 크기로 돌아간다.
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) SetPressed(false);
        }

        // 원본 강조는 포인터 진입 중이며 조작 가능한 버튼에만 표시한다.
        public void OnPointerEnter(PointerEventData eventData) => SetHover(CanRespond());

        // 포인터 이탈은 강조를 숨기고 취소된 누름을 해제한다.
        public void OnPointerExit(PointerEventData eventData)
        {
            SetHover(false);
            SetPressed(false);
        }

        // 누름 반전은 현재 표시 크기부터 보간하여 빠른 입력에도 불연속 누적이 없다.
        // 누른 채로 잠기면 누름 크기를 되돌려야 하므로 누름 동안 코루틴을 이어 간다.
        void SetPressed(bool pressed)
        {
            if (_sizeTarget == null) return;
            _sizeFrom = _sizeTarget.sizeDelta;
            _sizeTo = pressed ? _pressedSize : _idleSize;
            _sizeElapsed = 0f;
            _resizing = true;
            _pressed = pressed;
            StartTicking();
        }

        // 같은 강조 상태에는 SetActive를 반복하지 않는다. 강조가 켜진 동안 잠기면 강조를 지워야 하므로 코루틴을 이어 간다.
        void SetHover(bool value)
        {
            if (_hoveringIndicator == null) return;
            _hovered = value;
            UiVisibility.Set(_hoveringIndicator, value);
            if (value) StartTicking();
        }

        // 부모 CanvasGroup의 상호작용 차단도 Button 판정으로 따른다. 클릭 구독은 꺼진 동안에도 남지만
        // Button이 꺼져 있으면 여기서 거르고, 그래도 남은 상태는 다음 OnEnable의 RestorePose가 되돌린다.
        bool CanRespond() => _button.IsActive() && _button.IsInteractable();

        // 스케일만 기억하고 되돌리므로 레이아웃이 관리하는 위치, 회전, 폭을 덮어쓰지 않는다.
        void CachePose()
        {
            if (_punchTarget == null) return;
            _restScale = _punchTarget.localScale;
        }

        // 기준 자세 복원은 여러 번 호출해도 크기나 위치를 누적 변경하지 않는다. 대기 크기로 돌렸으므로 누름도 끝난 것으로 본다.
        void RestorePose()
        {
            // 펀치는 PlayClick이 기준 스케일을 저장한 뒤에만 시작하므로, 펀치 중이면 되돌릴 기준 스케일이 있다.
            if (_punching && _punchTarget != null) _punchTarget.localScale = _restScale;
            if (_sizeTarget != null && (_resizing || _sizeTarget.sizeDelta != _idleSize))
                _sizeTarget.sizeDelta = _idleSize;
            _punching = false;
            _resizing = false;
            _pressed = false;
        }
    }
}
