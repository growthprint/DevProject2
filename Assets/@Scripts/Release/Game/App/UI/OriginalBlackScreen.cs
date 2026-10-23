#nullable enable annotations
using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 GameManager의 BlackScreenCanvas다. 원작 BlackScreen과 MaterialPropertyFade처럼 FadeUI 재질의 _Progress를
    // 곡선으로 0에서 1(덮기) 또는 1에서 0(걷기)으로 옮기고, _CircleFade로 원형 전환인지 정한다. 시간 배율과 상관없이 흐른다.
    // 원작처럼 덮기 요청을 세어 겹친 덮기는 한 번만 덮고, 마지막 걷기만 걷는다. 다 걷히면 캔버스 객체를 끈다.
    [DisallowMultipleComponent]
    public sealed class OriginalBlackScreen : MonoBehaviour
    {
        static readonly int _progressId = Shader.PropertyToID("_Progress");
        static readonly int _circleFadeId = Shader.PropertyToID("_CircleFade");

        // FadeUI 재질을 쓰는 전체 화면 검정 Image다. 재질은 처음 쓸 때 사본을 만들어 이 Image에만 쓴다.
        [SerializeField] Image _image = null!;
        // 원작 BlackScreen의 defaultShowCurve와 defaultHideCurve다. 기본값이 원작 GameManager 프리팹 값이고, 곡선을 고르지 않는
        // 하위 씬 문의 덮기와 걷기가 쓴다. 원작 SceneLoader의 fadeCurve3, fadeCurve2와 키가 같다.
        [SerializeField] AnimationCurve _defaultShowCurve = new AnimationCurve(new Keyframe(0f, 0f, 2f, 2f), new Keyframe(1f, 1f, 0f, 0f));
        [SerializeField] AnimationCurve _defaultHideCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 2f));
        // 원작 BlackScreen 정적 메서드의 기본 duration 인수다.
        const float _defaultDuration = .5f;

        Material? _material;
        int _requests;
        float _from, _to, _duration;
        float _elapsed = float.PositiveInfinity;
        // 걷기를 시작하기 전에 기다릴 남은 시간이다. 원작 SceneLoader의 waitAfterSceneLoaded를 여기서 센다.
        float _delay;
        AnimationCurve? _curve;

        // 덮기나 걷기가 진행 중이면 참이다. 대기 시간도 포함한다.
        public bool IsFading => _delay > 0f || _elapsed < _duration;

        Material Material
        {
            get
            {
                if (_material == null)
                {
                    _material = new Material(_image.material);
                    _image.material = _material;
                }
                return _material;
            }
        }

        // 원작 BlackScreen.ShowAndReturnTask다. 이미 덮는 요청이 있으면 아무것도 하지 않는다. 끝났는지는 IsFading으로 본다.
        public void Show(AnimationCurve curve, float circleFade, float duration)
        {
            if (++_requests > 1) return;
            gameObject.SetActive(true);
            Begin(1f, curve, circleFade, duration, 0f);
        }

        // 원작 BlackScreen.HideAndReturnTask다. 남은 덮기 요청이 있으면 걷지 않는다. delay초 뒤에 걷기 시작한다.
        // 꺼진 캔버스는 Update가 돌지 않으므로 이미 걷힌 것으로 둔다.
        public void Hide(AnimationCurve curve, float circleFade, float duration, float delay = 0f)
        {
            if (--_requests > 0) return;
            _requests = 0;
            if (!isActiveAndEnabled)
            {
                _delay = 0f;
                _elapsed = float.PositiveInfinity;
                return;
            }
            Begin(0f, curve, circleFade, duration, delay);
        }

        // 원작 BlackScreen.ShowAndReturnTask와 HideAndReturnTask를 곡선 없이 부른 경우다. 기본 곡선, 원형 0, 원작 기본 인수 0.5초로
        // 덮고 걷는다. 요청 수는 Show와 Hide가 세므로 다른 덮기와 겹쳐도 짝이 맞는다.
        public void ShowDefault() => Show(_defaultShowCurve, 0f, _defaultDuration);
        public void HideDefault() => Hide(_defaultHideCurve, 0f, _defaultDuration);

        void Begin(float target, AnimationCurve curve, float circleFade, float duration, float delay)
        {
            Material.SetFloat(_circleFadeId, circleFade);
            _from = Material.GetFloat(_progressId);
            _to = target;
            _curve = curve;
            _duration = duration;
            _delay = delay;
            _elapsed = 0f;
        }

        void Update()
        {
            if (!IsFading) return;
            if (_delay > 0f)
            {
                _delay -= Time.unscaledDeltaTime;
                return;
            }
            _elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.LerpUnclamped(_from, _to, _curve!.Evaluate(Mathf.Clamp01(_elapsed / _duration)));
            Material.SetFloat(_progressId, progress);
            if (!IsFading && _to == 0f) gameObject.SetActive(false);
        }

        void OnDestroy() => UnityObjects.DestroyOwned(_material);
    }
}
