#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 Interact_CustomFace.Show/Hide의 BlackScreen 전환처럼 0.5초 동안 검정을 유지한 뒤 0.25초 동안 걷는다.
    // 전체 화면 검정 Image의 CanvasGroup 알파만 바꾸고 입력은 가로채지 않는다. 시간 배율과 상관없이 흐른다.
    [DisallowMultipleComponent]
    public sealed class BlackScreenFade : MonoBehaviour
    {
        [SerializeField] CanvasGroup _group = null!;
        [SerializeField, Min(0f)] float _hold = .5f;
        // 걷는 시간으로 나누므로 다른 페이드 View와 같은 UiFade 최소 시간 아래로 내리지 않는다.
        [SerializeField, Min(UiFade._minimumDuration)] float _fade = .25f;
        [SerializeField] bool _playOnEnable;

        float _elapsed = float.PositiveInfinity;

        public bool IsPlaying => _elapsed < _hold + _fade;

        // 검정 화면에서 시작해 원본 시간대로 걷는다.
        public void Play()
        {
            _elapsed = 0f;
            _group.alpha = 1f;
        }

        void OnEnable()
        {
            if (_playOnEnable) Play();
            else if (!IsPlaying) _group.alpha = 0f;
        }

        void Update()
        {
            if (!IsPlaying) return;
            _elapsed += Time.unscaledDeltaTime;
            _group.alpha = _elapsed <= _hold ? 1f : 1f - Mathf.Clamp01((_elapsed - _hold) / _fade);
        }
    }
}
