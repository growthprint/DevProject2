#nullable enable annotations
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 SlotDisplay인 캐릭터 장비 칸 하나다. 빈 칸은 저작된 칸 아이콘을, 찬 칸은 아이템 아이콘과 이름, 수량을 보인다.
    // 클릭과 끌어 놓기는 칸 자리만 인벤토리 패널에 넘기고 장착 규칙은 화면 소유자가 정한다. 애니메이션은 원본 DOTween 값을 시간 배율과 무관하게 재현한다.
    public sealed class OriginalSlotCell : HudItemCell, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        // 원본 SlotDisplay의 punchDuration, slotIconPunchScale, denialPunchDuration, slotIconDenialColor와
        // 장착 가능 표시 CanvasGroupFade의 fadeDuration이다.
        const float _punchDuration = .1f, _slotIconPunchScale = -.1f, _denialPunchDuration = .2f, _pluggableFadeDuration = .2f;
        static readonly Color _slotIconDenialColor = Color.red;

        [SerializeField] TMP_Text _label = null!;
        // 칸마다 프리팹에 저작한 원본 Slot.SlotIcon이다. 원본처럼 빈 칸에서만 보인다.
        [SerializeField] Image _slotIcon = null!;
        // 원본 PluggableIndicator FadeGroup과 HoveringIndicator다.
        [SerializeField] CanvasGroup _pluggable = null!;
        [SerializeField] GameObject _hovering = null!;

        // 원본 iconInitialColor다. 거절 깜박임이 끝나면 이 색으로 돌아간다. 깜박이는 중의 빨간색을 읽지 않도록 처음 한 번만 기록한다.
        Color _slotIconColor;
        bool _slotIconColorKnown;
        // 원본 SlotDisplay.defaultSlotIcon이다. 아이템 칸 셀은 저작된 칸 아이콘을 이 값으로 기억하고, 칸 아이콘이 없는 칸에 쓴다.
        Sprite? _defaultSlotIcon;
        bool _defaultSlotIconKnown;
        // 진행 중인 튕김과 거절 깜박임의 경과 시간이다. 음수면 멈춘 상태다.
        float _punchElapsed = -1f, _denialElapsed = -1f;
        // 장착 가능 표시의 목표 알파와 페이드 진행 여부다. 매 프레임 CanvasGroup을 읽지 않으려고 둔다.
        float _pluggableTarget;
        bool _fading;

        // 재사용하는 장비 칸에 새 내용을 표시한다. 원본 OnTargetContentChanged처럼 이미 표시하던 칸의 아이템이 바뀔 때만 칸 아이콘을 튕긴다.
        // 저작된 칸 아이콘은 원본처럼 빈 칸에서만 보인다.
        internal void Bind(HudInventoryPanel panel, InventoryCellRef cell, string label, string itemName, Sprite? icon, int count)
        {
            bool changed = _panel != null && _cell.Id != cell.Id;
            ShowItem(panel, cell, itemName, icon, count);
            RememberSlotIconColor();
            _label.text = label;
            _slotIcon.gameObject.SetActive(cell.IsEmpty);
            if (changed) _punchElapsed = 0f;
        }

        // 원본 SlotDisplay.Refresh처럼 아이템 칸의 Slot.SlotIcon을, 없으면 템플릿에 저작된 기본 칸 아이콘을 쓴다.
        // 캐릭터 칸은 칸마다 저작한 아이콘을 그대로 쓰므로 부르지 않는다.
        internal void SetSlotIcon(Sprite? icon)
        {
            if (!_defaultSlotIconKnown)
            {
                _defaultSlotIcon = _slotIcon.sprite;
                _defaultSlotIconKnown = true;
            }
            _slotIcon.sprite = icon != null ? icon : _defaultSlotIcon;
        }

        // HUD 소유자가 바뀔 때 앞 화면의 명령을 부르지 않게 연결을 끊고 표시 효과를 즉시 멈춘다. 내용 표시는 다음 Bind가 덮는다.
        internal override void ResetBinding()
        {
            base.ResetBinding();
            HidePluggable();
            StopAnimations();
        }

        // 원본 OnStartDragItem과 OnEndDragItem의 PluggableIndicator Show와 Hide다. 지금 알파에서 목표까지 0.2초 속도로 옮긴다.
        internal void SetPluggable(bool pluggable)
        {
            _pluggableTarget = pluggable ? 1f : 0f;
            _fading = _pluggable.alpha != _pluggableTarget;
        }

        // 화면을 닫을 때는 원본 SkipHide처럼 페이드 없이 바로 숨긴다.
        internal void HidePluggable()
        {
            _pluggableTarget = 0f;
            _pluggable.alpha = 0f;
            _fading = false;
        }

        // 원본 DenialPunch다. 칸 아이콘을 빨간색에서 원래 색으로 되돌리고, 진행 중인 튕김은 원본 DOKill처럼 멈춘다.
        // 원본처럼 칸 아이콘은 빈 칸에서만 보이므로 찬 칸의 거절은 보이지 않는다.
        internal void DenialPunch()
        {
            RememberSlotIconColor();
            _punchElapsed = -1f;
            _slotIcon.transform.localScale = Vector3.one;
            _slotIcon.color = _slotIconDenialColor;
            _denialElapsed = 0f;
        }

        // 원본 OnEnable처럼 다시 보일 때 호버 표시를 끈다.
        void OnEnable() => _hovering.SetActive(false);

        // 꺼진 동안에는 Update가 돌지 않으므로 효과를 끝 상태로 맞춰, 다시 켰을 때 멈춘 크기나 색이 남지 않게 한다.
        void OnDisable()
        {
            StopAnimations();
            _pluggable.alpha = _pluggableTarget;
            _fading = false;
        }

        // 효과가 없으면 바로 돌아간다. 컴포넌트를 끄면 포인터 이벤트도 못 받으므로 끄지 않고, 장비 칸 열 개만 불린다.
        void Update()
        {
            if (_punchElapsed < 0f && _denialElapsed < 0f && !_fading) return;
            float delta = Time.unscaledDeltaTime;
            if (_punchElapsed >= 0f)
            {
                _punchElapsed += delta;
                bool done = _punchElapsed >= _punchDuration;
                _slotIcon.transform.localScale = Vector3.one * (done ? 1f : PunchScale(_punchElapsed));
                if (done) _punchElapsed = -1f;
            }
            if (_denialElapsed >= 0f)
            {
                _denialElapsed += delta;
                float progress = Mathf.Clamp01(_denialElapsed / _denialPunchDuration);
                _slotIcon.color = Color.Lerp(_slotIconDenialColor, _slotIconColor, OutQuad(progress));
                if (progress >= 1f) _denialElapsed = -1f;
            }
            if (_fading)
            {
                // 한 프레임 알파 이동은 다른 페이드 View와 같은 UiFade 규칙이다.
                float alpha = UiFade.Step(_pluggable.alpha, _pluggableTarget, delta, _pluggableFadeDuration);
                _pluggable.alpha = alpha;
                _fading = alpha != _pluggableTarget;
            }
        }

        // 원본 DOPunchScale(진동 10)은 0.1초 길이에서 두 구간이 되어, 앞 1/3 동안 줄었다가 남은 2/3 동안 제 크기로 돌아온다.
        // 구간마다 DOTween 기본 OutQuad 이징을 쓴다.
        static float PunchScale(float elapsed)
        {
            const float shrink = _punchDuration / 3f;
            float amount = elapsed < shrink ? OutQuad(elapsed / shrink) : 1f - OutQuad((elapsed - shrink) / (_punchDuration - shrink));
            return 1f + _slotIconPunchScale * amount;
        }

        static float OutQuad(float progress) => progress * (2f - progress);

        void RememberSlotIconColor()
        {
            if (_slotIconColorKnown) return;
            _slotIconColor = _slotIcon.color;
            _slotIconColorKnown = true;
        }

        void StopAnimations()
        {
            _punchElapsed = -1f;
            _denialElapsed = -1f;
            _slotIcon.transform.localScale = Vector3.one;
            if (_slotIconColorKnown) _slotIcon.color = _slotIconColor;
        }

        // 원본처럼 왼쪽 클릭은 칸 아이콘을 튕기고 찬 칸을 선택한다. 더블클릭과 우클릭은 찬 칸의 활성화를 요청한다.
        // 장비 칸에는 선택 테두리가 없어 패널에 셀 View를 넘기지 않는다.
        public void OnPointerClick(PointerEventData data)
        {
            bool left = data.button == PointerEventData.InputButton.Left;
            if (left && data.clickCount < 2) _punchElapsed = 0f;
            // 인벤토리 화면은 InventoryScreen.Open만 열고, 같은 호출의 Refresh가 장비 칸 10개를 모두 묶는다.
            // 묶이기 전이나 연결을 끊은 칸은 _cell이 비어 있어 여기서 끝나므로 아래에서 _panel은 늘 있다.
            if (_cell.IsEmpty) return;
            if (data.button == PointerEventData.InputButton.Right || (left && data.clickCount >= 2)) _panel.ActivateCell(_cell, null);
            else if (left) _panel.SelectCell(_cell, null);
        }

        public void OnPointerEnter(PointerEventData data) => _hovering.SetActive(true);

        public void OnPointerExit(PointerEventData data) => _hovering.SetActive(false);
    }
}
