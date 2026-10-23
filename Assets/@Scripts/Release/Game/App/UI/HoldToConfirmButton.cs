#nullable enable annotations
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.App
{
    // 포인터나 UI 제출 입력을 3초 동안 계속 누르고 있어야 확정한다.
    // 클릭 이벤트와 따로 판정해 실수로 지우거나 반복해서 제출하는 일을 막는다.
    [RequireComponent(typeof(Button))]
    [DisallowMultipleComponent]
    public sealed class HoldToConfirmButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IPointerExitHandler, IDeselectHandler
    {
        // 원본 진행 막대다. 누른 시간만큼 채운다.
        [SerializeField] Image? _fill;
        // 확정만 알린다. 무엇을 지울지는 구독자가 정한다.
        public event Action? Confirmed;
        // 제출 액션은 UI 소유자가 주입하며 컴포넌트가 켜거나 끄지 않는다.
        InputAction? _submit;
        // IsInteractable로 CanvasGroup 잠금까지 함께 본다.
        Button _button = null!;
        // 지금 누르고 있는 입력 종류를 기록한다.
        bool _pointerHeld, _submitHeld;
        // 일시정지 중에도 실제로 누른 시간을 잰다. 확정하면 상한에 머물러, 한 번 누르는 동안 한 번만 확정한다.
        float _heldTime;
        // 원본의 삭제 확인 시간이다. 이 컴포넌트 안에서만 쓴다.
        const float _requiredSeconds = 3f;

        // 메뉴가 연결할 때 제출 액션을, 해제할 때 null을 넘긴다. 바꿀 때마다 진행을 버린다.
        public void Configure(InputAction? submit) { CancelHold(); _submit = submit; }
        void Awake() { _button = GetComponent<Button>(); CancelHold(); }
        // 꺼질 때 진행을 버려, 다시 켜졌을 때 남은 시간으로 삭제되지 않게 한다.
        void OnDisable() => CancelHold();
        // 앱이 포커스를 잃으면 진행을 버려 그동안 삭제가 끝나지 않게 한다.
        void OnApplicationFocus(bool focused) { if (!focused) CancelHold(); }
        // 키보드와 패드 제출은 이 버튼이 선택된 채 새로 누른 것부터 잰다. 다른 버튼에서 누르던 입력은 이어받지 않는다.
        // _button은 RequireComponent로 보장되므로 null을 검사하지 않는다.
        void Update()
        {
            if (!_button.IsInteractable()) { CancelHold(); return; }
            bool selected = EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject == gameObject;
            // 포인터로 누르는 중이 아닐 때 선택된 이 버튼에서 새로 누른 제출만 유지 시작으로 본다.
            if (SubmitStartedHere(selected))
            { CancelHold(); _submitHeld = true; }
            // 제출 유지는 액션이 있을 때만 시작하고 액션을 바꾸는 Configure가 먼저 유지를 끊으므로, 유지 중에는 액션이 늘 있다.
            if (_submitHeld && (!selected || !_submit.IsPressed())) CancelHold();
            if (!_pointerHeld && !_submitHeld) return;
            // 누른 시간이 상한에 닿았으면 이미 확정한 누름이라, 취소로 0이 될 때까지 다시 알리지 않는다.
            if (_heldTime >= _requiredSeconds) return;
            _heldTime = Mathf.Min(_requiredSeconds, _heldTime + Time.unscaledDeltaTime);
            if (_fill != null) _fill.fillAmount = _heldTime / _requiredSeconds;
            if (_heldTime < _requiredSeconds) return;
            Confirmed?.Invoke();
        }

        // 제출 액션이 없으면 유지를 시작하지 않는다. 메뉴 해제 뒤 Configure(null)이 그런 상태다.
        bool SubmitStartedHere(bool selected) =>
            !_pointerHeld && selected && _submit != null && _submit.WasPressedThisFrame();
        // 왼쪽 포인터 누름만 남은 진행을 버리고 새로 재기 시작한다.
        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || !_button.IsInteractable()) return;
            CancelHold();
            _pointerHeld = true;
        }
        // 손을 떼면 다음 동작은 다시 3초가 필요하다.
        public void OnPointerUp(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) CancelHold(); }
        // 원본처럼 포인터가 버튼 밖으로 나가면 취소한다.
        public void OnPointerExit(PointerEventData data) => CancelHold();
        // 선택이 다른 곳으로 옮겨 가면 취소해, 보이지 않는 곳에서 삭제가 이어지지 않게 한다.
        public void OnDeselect(BaseEventData data) => CancelHold();
        // 모든 취소 경로가 여기서 내부 상태와 진행 막대를 함께 초기화한다.
        public void CancelHold()
        {
            _pointerHeld = _submitHeld = false;
            _heldTime = 0;
            if (_fill != null) _fill.fillAmount = 0;
        }
    }
}

