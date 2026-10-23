#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 QuestMarker.prefab의 움직임 두 컴포넌트(Duckov.UI.Animations의 LooperClock과 LocalPositionLooper)를 하나로 옮겼다.
    // 원작은 루트의 시계가 매 프레임 0~1 진행값을 내고, 자식 Parent의 반복기가 곡선으로 두 지역 위치 사이를 오간다.
    // 원작 프리팹은 두 컴포넌트를 모두 꺼 둔 채 저장해 표시가 움직이지 않는다. 제작 도구도 이 컴포넌트를 꺼진 채 저장해 원작과 같게 둔다.
    [DisallowMultipleComponent]
    public sealed class QuestMarkerMotion : MonoBehaviour
    {
        // 원작 LooperClock.duration이다. 0 이하이면 원작처럼 시계가 돌지 않고 진행값은 1이다.
        [SerializeField] float _duration = 1f;
        // 원작 LocalPositionLooper가 붙은 Parent 물체다. 제작 도구가 늘 넣는다.
        [SerializeField] Transform _target = null!;
        [SerializeField] Vector3 _localPositionA;
        [SerializeField] Vector3 _localPositionB;
        [SerializeField] AnimationCurve _curve = new AnimationCurve();
        float _time;

        // 원작 LooperElement.OnEnable은 켜질 때 지금 진행값으로 한 번 맞춘다.
        void OnEnable() => Apply();

        // 원작 LooperClock.Update처럼 시간 배율과 무관하게 돌고 한 주기마다 처음으로 돌아간다.
        void Update()
        {
            if (_duration <= 0f) return;
            _time = (_time + Time.unscaledDeltaTime) % _duration;
            Apply();
        }

        // 원작 OnTick은 Vector2.Lerp 결과를 localPosition에 넣어 z가 0이 된다. 같은 식을 쓴다.
        void Apply()
        {
            float progress = _duration > 0f ? _time / _duration : 1f;
            _target.localPosition = Vector2.Lerp(_localPositionA, _localPositionB, _curve.Evaluate(progress));
        }
    }
}
