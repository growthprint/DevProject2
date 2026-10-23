#nullable enable annotations
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.App
{
    // 원본 Cinemachine 대사 카메라를 재구성한다. CinemachineVirtualCamera의 렌즈, CinemachineBasicMultiChannelPerlin 노이즈,
    // CinemachineVolumeSettings의 초점 추적을 켜진 동안의 자세와 렌즈와 카메라별 Volume으로 제공한다.
    // 원본 AT_SetVirtualCamera처럼 켜진 카메라가 대상이 되고, 화면 블렌드는 OriginalGameCamera가 맡는다.
    // 씬 컨트롤러 LateUpdate의 게임 카메라 Tick이 Volume을 섞기 전에 초점 거리를 써야 같은 프레임 렌더에 들어가므로 LateUpdate를 앞당긴다.
    [DisallowMultipleComponent, DefaultExecutionOrder(-100)]
    public sealed class OriginalVirtualCamera : MonoBehaviour
    {
        // 원본 Handheld_tele_mild 프로필의 회전 노이즈 3층이다. 값은 피치 주파수, 피치 진폭, 요 주파수, 요 진폭 순서다.
        static readonly Vector4[] _noiseLayers = { new(.2f, 4f, .15f, 2f), new(.4f, 2f, .5f, 2f), new(.7f, 1f, .6f, 1f) };

        // 허용 시야각 범위다. 인스펙터 Range와 Editor 제작 도구의 시야각 자르기가 이 상수 하나를 함께 쓴다.
        internal const float _minFieldOfView = 1f;
        internal const float _maxFieldOfView = 179f;
        [SerializeField, Range(_minFieldOfView, _maxFieldOfView)] float _fieldOfView = 40f;
        [SerializeField, Min(0f)] float _noiseAmplitude = .5f;
        [SerializeField, Min(0f)] float _noiseFrequency = .5f;
        // 원본 CinemachineVolumeSettings의 CustomTarget 초점 대상이다. 원본처럼 대상이 없으면 초점 거리는 최소값 0이 된다.
        [SerializeField] Transform? _focusTarget;
        [SerializeField] Volume? _volume;

        // 카메라별 Volume 프로필의 런타임 복제본이다. 첫 활성화 때 만들고 OnDestroy에서 파괴한다.
        RuntimeVolumeProfile? _profile;
        // 런타임 복제본의 피사계 심도다. 매 프레임 프로필을 뒤지지 않도록 복제본을 만들 때 한 번 찾고, 없으면 초점을 쓰지 않는다.
        DepthOfField? _focus;
        // 마지막으로 초점 거리를 쓴 카메라와 대상 위치다. 둘 다 그대로면 거리도 같으므로 Volume 값을 다시 쓰지 않는다.
        Vector3 _focusCameraPosition, _focusTargetPosition;
        bool _focusWritten;
        float _noiseTime;
        Vector2 _noiseOffset;

        public float FieldOfView => _fieldOfView;
        public Volume? Volume => _volume;

        // 원본처럼 카메라 객체를 켜서 카메라별 Volume도 켠다. 초점 거리는 공유 자산이 아닌 런타임 복제본에만 쓰고,
        // 원본 Volume 설정에 피사계 심도가 없을 수 있어 있을 때만 적용한다. 켜는 즉시 한 번 써서 블렌드 첫 프레임부터 맞는 초점을 쓴다.
        internal void Activate()
        {
            gameObject.SetActive(true);
            _noiseTime = 0f;
            _noiseOffset = new Vector2(Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f));
            if (_volume == null) return;
            if (_profile == null)
            {
                _profile = new RuntimeVolumeProfile(_volume);
                if (_profile.TryGet(out DepthOfField focus)) _focus = focus;
            }
            _focusWritten = false;
            UpdateFocus();
        }

        // 원본 CinemachineVolumeSettings는 카메라가 살아 있거나 블렌드에서 빠지는 동안 매 프레임 초점 거리를 다시 쓴다.
        // 이 객체는 그 동안에만 켜져 있으므로 LateUpdate가 같은 구간을 맡는다.
        void LateUpdate() => UpdateFocus();

        // 원본 CustomTarget 추적처럼 카메라와 대상 사이 거리를 초점 거리로 쓰고, 대상이 없으면 원본처럼 0이다.
        // 매 프레임 불리므로 할당 없이 위치만 비교하고, 카메라나 대상이 움직인 프레임에만 Volume 값을 쓴다.
        void UpdateFocus()
        {
            if (_focus == null) return;
            Vector3 cameraPosition = transform.position;
            Vector3 targetPosition = _focusTarget != null ? _focusTarget.position : cameraPosition;
            if (_focusWritten && cameraPosition == _focusCameraPosition && targetPosition == _focusTargetPosition) return;
            _focusWritten = true;
            _focusCameraPosition = cameraPosition;
            _focusTargetPosition = targetPosition;
            _focus.focusDistance.Override(Vector3.Distance(cameraPosition, targetPosition));
        }

        // Cinemachine 노이즈처럼 시간 배율과 상관없는 누적 시간으로 Perlin 회전 흔들림을 더한다. 움직임 줄이기 옵션이면 더하지 않는다.
        internal Quaternion Rotation(float unscaledDelta, bool reduceMotion)
        {
            if (reduceMotion) return transform.rotation;
            _noiseTime += unscaledDelta * _noiseFrequency;
            Vector2 noise = Vector2.zero;
            foreach (Vector4 layer in _noiseLayers)
            {
                noise.x += (Mathf.PerlinNoise(layer.x * _noiseTime + _noiseOffset.x, 0f) - .5f) * layer.y;
                noise.y += (Mathf.PerlinNoise(layer.z * _noiseTime + _noiseOffset.y, 0f) - .5f) * layer.w;
            }
            return transform.rotation * Quaternion.Euler(noise.x * _noiseAmplitude, noise.y * _noiseAmplitude, 0f);
        }

        // 복제본을 파괴하면 그 안의 피사계 심도도 함께 사라지므로 초점 참조도 놓는다.
        void OnDestroy()
        {
            _profile?.Dispose();
            _profile = null;
            _focus = null;
        }
    }
}
