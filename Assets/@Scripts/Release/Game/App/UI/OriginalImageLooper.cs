#nullable enable annotations
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 로딩 화면 클릭 표시 아이콘의 LooperClock, ImageColorLooper, ScaleLooper를 하나로 둔다.
    // 시간 배율과 상관없이 한 주기마다 처음으로 돌아가며 색, 알파, 크기를 곡선대로 바꾼다. 기본값은 원작 GameManager의 값이다.
    // 원작 ScaleLooper의 축별 곡선은 모두 1이라 균일 크기 곡선만 둔다.
    [DisallowMultipleComponent]
    public sealed class OriginalImageLooper : MonoBehaviour
    {
        [SerializeField] Image _image = null!;
        // 원작 LooperClock.duration이다. 0 이하이면 원작처럼 시계가 돌지 않는다.
        [SerializeField] float _duration = 1f;
        [SerializeField] Gradient _colorOverT = OriginalPulse();
        [SerializeField] AnimationCurve _alphaOverT = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [SerializeField] AnimationCurve _scaleOverT =
            new AnimationCurve(new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 1.5f, .8740293f, .8740293f));

        // 흰색이 짧게 나타났다가 주기의 4분의 3쯤에서 사라지는 원작 그라디언트다. 시간 값은 원작이 16비트로 저장한 값이다.
        static Gradient OriginalPulse()
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 5397f / 65535f),
                    new GradientAlphaKey(1f, 19275f / 65535f), new GradientAlphaKey(0f, 50115f / 65535f) });
            return gradient;
        }

        float _time;

        void Update()
        {
            if (_duration <= 0f) return;
            _time = (_time + Time.unscaledDeltaTime) % _duration;
            float t = _time / _duration;
            Color color = _colorOverT.Evaluate(t);
            color.a *= _alphaOverT.Evaluate(t);
            _image.color = color;
            float scale = _scaleOverT.Evaluate(t);
            transform.localScale = new Vector3(scale, scale, scale);
        }
    }
}
