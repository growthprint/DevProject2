#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 FadeGroup과 같은 객체의 CanvasGroupFade 하나를 합친 페이드다. 시간 배율과 상관없이 정한 시간 동안 알파를 곡선으로 옮긴다.
    // _manageActive가 참이면 원작 manageGameObjectActive처럼 보일 때 켜고 다 숨으면 끈다. 숨긴 채 저장한다.
    [DisallowMultipleComponent]
    public sealed class OriginalFadeGroup : MonoBehaviour
    {
        [SerializeField] CanvasGroup _group = null!;
        [SerializeField, Min(UiFade._minimumDuration)] float _duration = .2f;
        // 원작 showingCurve와 hidingCurve다. 기본값은 원작 SceneLoader 페이드 그룹의 값이다.
        [SerializeField] AnimationCurve _showCurve = new AnimationCurve(new Keyframe(0f, 0f, 2f, 2f), new Keyframe(1f, 1f, 0f, 0f));
        [SerializeField] AnimationCurve _hideCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));
        [SerializeField] bool _manageActive;

        float _from, _to;
        float _elapsed = float.PositiveInfinity;
        AnimationCurve? _curve;

        public bool IsFading => _elapsed < _duration;
        public bool IsShown { get; private set; }

        // 원작 CanvasGroupFade처럼 진행 중인 페이드가 있으면 지금 알파에서, 없으면 0에서 시작한다. 다 보인 그룹은 그대로 둔다.
        public void Show()
        {
            if (IsShown && !IsFading) return;
            if (_manageActive) gameObject.SetActive(true);
            Begin(1f, _showCurve);
        }

        // 다 숨은 그룹과 꺼진 객체는 Update가 돌지 않거나 할 일이 없으므로 바로 숨긴 상태로 둔다.
        public void Hide()
        {
            if (!isActiveAndEnabled || (!IsShown && !IsFading))
            {
                SkipHide();
                return;
            }
            Begin(0f, _hideCurve);
        }

        // 원작 SkipHide다. 페이드 없이 바로 숨긴다.
        public void SkipHide()
        {
            _elapsed = float.PositiveInfinity;
            _group.alpha = 0f;
            IsShown = false;
            if (_manageActive) gameObject.SetActive(false);
        }

        void Begin(float target, AnimationCurve curve)
        {
            _from = IsFading ? _group.alpha : 1f - target;
            _to = target;
            _curve = curve;
            _elapsed = 0f;
            IsShown = target > 0f;
            _group.alpha = _from;
        }

        void Update()
        {
            if (!IsFading) return;
            _elapsed += Time.unscaledDeltaTime;
            _group.alpha = Mathf.LerpUnclamped(_from, _to, _curve!.Evaluate(Mathf.Clamp01(_elapsed / _duration)));
            if (!IsFading && !IsShown && _manageActive) gameObject.SetActive(false);
        }
    }
}
