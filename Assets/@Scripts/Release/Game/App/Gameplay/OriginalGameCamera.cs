#nullable enable annotations
using System;
using Game.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.App
{
    // 게임 카메라 한 프레임의 입력이다. AimScreen은 픽셀 좌표이고, InputActive는 원본 InputManager.InputActived에 해당한다.
    // 입력이 비활성이면 호출자가 ADS와 방아쇠를 false로 넘긴다. 카메라는 이 둘을 다시 거르지 않는다.
    internal readonly struct OriginalCameraInput
    {
        internal OriginalCameraInput(Vector2 aimScreen, bool ads, bool trigger, bool inputActive)
        {
            AimScreen = aimScreen;
            Ads = ads;
            Trigger = trigger;
            InputActive = inputActive;
        }
        internal Vector2 AimScreen { get; }
        internal bool Ads { get; }
        internal bool Trigger { get; }
        internal bool InputActive { get; }
    }

    // 원본 GameCamera와 CameraArm을 재현하고, 대사용 Cinemachine 블렌드는 OriginalCameraBlend에 맡긴다. OriginalPlayerRig가 만들고
    // 씬 LateUpdate에서 Tick한다. 프레임 순서: 공간 Volume 암 → 암 보간 → ADS → FOV → 조준 오프셋 → 플레이 구도 → 초점 거리 → 충격 감쇠 → 대사 카메라 블렌드.
    // 움직임 감소 옵션이면 조준 오프셋을 0으로 두고, 암과 ADS와 블렌드를 즉시 적용하며, 충격과 가상 카메라 노이즈를 생략한다.
    internal sealed class OriginalGameCamera : IDisposable
    {
        // 원본 LevelManager.prefab GameCamera의 defaultFOV/adsFOV.
        internal const float _defaultFieldOfView = 23.5f;
        internal const float _adsFieldOfView = 22.8f;
        // 원본 CameraArm 프리팹과 CameraArmControl의 기본 피치, 요, 거리다. OriginalCameraArmControl의 초기값과
        // 제작 도구가 카메라를 처음 배치하는 위치도 이 값을 쓴다.
        internal const float _defaultPitch = 55f;
        internal const float _defaultYaw = -30f;
        internal const float _defaultDistance = 45f;
        // 원본 GameCamera 값이다. 차례로 추종점 높이, VolumeProxy 높이, 기본 조준 오프셋, 조준 거리 계수, 초점 거리 보정이다.
        internal const float _targetHeight = .5f;
        const float _volumeProxyHeight = .7f;
        const float _defaultAimOffset = 5f;
        const float _defaultAimOffsetDistanceFactor = .5f;
        const float _focusOffset = 1.5f;
        // 원본 GameCamera의 조준 오프셋 보간 속도는 평상시 12, 방아쇠를 당기는 동안 1.5다.
        // 원본 CameraArm은 요와 거리를 계수 2로, 피치를 계수 5로 따라간다.
        const float _aimLerpSpeed = 12f;
        const float _triggerAimLerpSpeed = 1.5f;
        const float _armYawDistanceLerp = 2f;
        const float _armPitchLerp = 5f;
        // 원본 UpdateAds 값이다. ADSTime은 0.01초 아래로 보지 않고, 총을 들었으면 해제를 초당 4 이상으로, 총이 없으면 초당 10으로 바꾼다.
        // 총 없는 ADS는 최대 조준 오프셋을 1.25배로 늘린다.
        const float _minAdsTime = .01f;
        const float _adsReleaseMinSpeed = 4f;
        const float _noGunAdsSpeed = 10f;
        const float _noGunAimOffsetFactor = 1.25f;
        // 원본 UpdateAimOffsetNormal 값이다. 피치 계수는 피치 20도에서 0, 50도에서 1이고, 기준점은 피치 계수 1당 5만큼 화면 위아래 중간점 쪽으로 옮긴다.
        const float _pitchFactorMin = 20f;
        const float _pitchFactorMax = 50f;
        const float _anchorShiftPerPitch = 5f;
        // 충격 오프셋의 실시간 지수 감쇠율과 초점 거리의 최솟값이다.
        const float _shakeDecay = 12f;
        const float _minFocusDistance = .1f;

        readonly Camera _camera;
        readonly Transform _target;
        readonly GameObject _volumeProxy;
        readonly UniversalAdditionalCameraData? _cameraData;
        // 대사 가상 카메라와 플레이 구도 사이의 블렌드다. 플레이 카메라 쪽 Volume 가중치는 생성 때 받은 초점 Volume이 맡는다.
        readonly OriginalCameraBlend _blend;
        RuntimeVolumeProfile? _focusProfile;
        DepthOfField? _focus;
        // 생성 전 카메라의 Volume 갱신 모드다. 이 카메라는 스크립트 갱신으로 바꿔 Tick이 평가하고, Dispose가 이 값으로 되돌린다.
        VolumeFrameworkUpdateMode _previousVolumeMode;
        // 카메라를 스크립트 갱신으로 바꿨는지다. URP의 모드 전환이 Volume 관리자가 준비된 뒤에만 되므로 첫 평가 때 바꾼다.
        bool _scriptedVolumes;
        // 이 카메라의 Volume 스택을 한 번이라도 평가했는지다. 평가 전에는 암을 읽지 않고 먼저 평가한다.
        bool _volumesEvaluated;
        OriginalItemDefinition? _gun;
        float _gunAdsSpeed, _gunAimDistanceFactor;
        float _ads, _maxAimOffset, _aimOffsetDistanceFactor = _defaultAimOffsetDistanceFactor, _lerpSpeed = _aimLerpSpeed;
        float _offsetX, _offsetZ;
        float _pitch = _defaultPitch, _yaw = _defaultYaw, _distance = _defaultDistance;
        // 참이면 다음 Tick이 원본 ForceSyncPos처럼 암과 조준 오프셋을 즉시 맞추고 블렌드와 충격을 끝낸다. 생성 직후 첫 프레임도 그렇다.
        bool _snapPending = true;
        // 충격과 대사 블렌드를 걷어 낸 플레이 구도 위치와, 감쇠 중인 충격 오프셋과, 그 구도의 추종점이다.
        Vector3 _gameplayPosition, _shake, _followPoint;
        bool _disposed;

        // 원본 GameCamera.Update처럼 추종 대상 위 0.7m에 VolumeProxy를 두고 카메라의 URP volumeTrigger로 지정한다.
        // 그래야 화면의 공간 Volume과 카메라 암이 같은 지점을 기준으로 섞인다. 초점 Volume은 저작에서 빠질 수 있어 null을 받는다.
        internal OriginalGameCamera(Camera camera, Transform target, Volume? focusVolume)
        {
            // 카메라를 읽는 단계를 복제보다 먼저 둔다. 카메라가 빠진 채 들어와도 여기서 먼저 실패해 초점 복제본이 남지 않는다.
            bool hasCameraData = camera.TryGetComponent(out UniversalAdditionalCameraData data);
            // 첫 Tick의 조준 광선은 이 위치에서 쏜다.
            _gameplayPosition = camera.transform.position;
            // 초점은 공유 프로필 대신 런타임 복제본에서만 바꾼다. 씬에 저작한 초점 Volume 프로필에는 DepthOfField가 늘 있다.
            if (focusVolume != null)
            {
                var profile = new RuntimeVolumeProfile(focusVolume);
                profile.TryGet(out DepthOfField focus);
                _focus = focus;
                _focusProfile = profile;
            }
            // 초점 Volume이 빠졌거나 이미 파괴됐으면 블렌드의 플레이 카메라 쪽 Volume은 진짜 null이다.
            _blend = new OriginalCameraBlend(focusVolume != null ? focusVolume : null);
            _camera = camera;
            _target = target;
            _volumeProxy = new GameObject("OriginalVolumeProxy");
            _volumeProxy.transform.SetParent(target, false);
            _volumeProxy.transform.localPosition = Vector3.up * _volumeProxyHeight;
            // URP 카메라라면 이미 있는 카메라 데이터다. 없으면 URP가 렌더할 때 붙이는 것과 같은 컴포넌트를 여기서 붙인다.
            // 위의 실패 경로가 모두 끝난 뒤에 붙이므로 실패한 생성은 아무것도 남기지 않는다.
            if (!hasCameraData) data = camera.GetUniversalAdditionalCameraData();
            _cameraData = data;
            data.volumeTrigger = _volumeProxy.transform;
            // 카메라 암 세 값을 얻으려고 Volume 전체를 따로 한 번 더 섞지 않도록, 첫 평가 때 이 카메라의 Volume 갱신을
            // 스크립트 갱신으로 바꾼다. Tick이 프레임마다 한 번 섞고, URP는 그 스택으로 렌더하며, 암도 같은 스택에서 읽는다. 그래서 전역
            // 렌더 상태를 쓰는 Volume Override(조명, 윤곽광, 태양 안개)도 프레임마다 한 번만 돈다. 되돌릴 원래 모드는 여기서 기억한다.
            _previousVolumeMode = camera.GetVolumeFrameworkUpdateMode();
        }

        // 움직임 감소 옵션이다. 소유자가 옵션 적용 알림에서 받아 둔 값을 넘긴다.
        internal bool ReduceMotion { get; set; }

        // 지난 Tick의 플레이 추종점이다. 원본 GameCamera 루트 위치라 대사 카메라 블렌드와 충격의 영향을 받지 않는다. 첫 Tick 전에는 원점이다.
        internal Vector3 FollowPoint => _followPoint;

        // 든 총의 원본 ADSTime과 ADSAimDistanceFactor를 쓴다. 맨손과 근접 무기는 null을 넘겨 원본의 총 없는 규칙을 따른다.
        // 새 총을 들면 원본처럼 ADS 값이 0에서 시작하고, 총을 내리면 맨손 ADS가 지금 값에서 이어 간다.
        internal void SetGun(OriginalItemDefinition? gun)
        {
            if (ReferenceEquals(gun, _gun)) return;
            _gun = gun;
            if (gun == null) return;
            _ads = 0f;
            _gunAdsSpeed = 1f / Mathf.Max(_minAdsTime, gun.Stat("ADSTime"));
            _gunAimDistanceFactor = gun.Stat("ADSAimDistanceFactor");
        }

        // 원본 CameraShaker.Shake처럼 카메라 위치에 충격 속도를 더한다. 움직임 감소 옵션이면 무시하며, 충격은 실시간 지수 감쇠로 사라진다.
        internal void Impulse(Vector3 velocity)
        {
            if (!ReduceMotion) _shake += velocity;
        }

        // 순간이동 뒤에 부르는 원본 ForceSyncPos다. 다음 Tick이 암과 조준 오프셋을 한 번에 맞추고 대사 블렌드와 충격을 끝낸다.
        // 그 뒤 TAA와 모션 블러가 쓰는 화면 기록도 초기화한다.
        internal void Snap() => _snapPending = true;

        // 원본 AT_SetVirtualCamera 자리다. 블렌드는 이 화면 카메라의 지금 자세에서 시작한다. null이면 플레이 구도로 돌아간다.
        internal void SetDialogueCamera(OriginalVirtualCamera? camera) => _blend.SetTarget(camera, _camera);

        // 씬 소유자가 LateUpdate에서 부른다. 이 카메라의 Volume 스택에서 섞은 카메라 암으로 한 프레임을 진행한다.
        // 암은 원본 CameraArm처럼 지난 Tick 끝의 평가 결과를 쓴다. 첫 프레임과 순간이동 뒤에는 지금 위치로 먼저 평가해 바로 맞춘다.
        // 유일한 호출자인 리그가 추종 대상인 모터의 파괴를 먼저 보고, 씬 소유자는 리그를 놓은 뒤에 해제하므로 해제나 파괴를 다시 보지 않는다.
        internal void Tick(in OriginalCameraInput input)
        {
            if (_snapPending || !_volumesEvaluated) UpdateVolumes();
            ReadArm(out float pitch, out float yaw, out float distance);
            TickWithArm(input, pitch, yaw, distance, Time.deltaTime, Time.unscaledDeltaTime);
            // 초점 거리와 대사 Volume 가중치를 이번 Tick에서 바꾼 뒤에 섞어야 이번 프레임 렌더가 그 값을 쓴다.
            UpdateVolumes();
        }

        // Tick의 본체다. 공간 Volume 대신 목표 피치, 요, 거리와 시간 간격을 직접 받으므로 테스트도 이 메서드를 부른다.
        internal void TickWithArm(in OriginalCameraInput input, float pitch, float yaw, float distance, float deltaTime, float unscaledDeltaTime)
        {
            // 대상 위치와 대상 위 0.5m의 조준 평면을 프레임마다 한 번만 만든다. 카메라를 움직여도 대상 위치는 그대로라,
            // 조준 광선 네 개와 추종점과 초점 거리가 이 값을 함께 쓴다.
            Vector3 targetPosition = _target.position;
            var aimPlane = new Plane(Vector3.up, targetPosition + Vector3.up * _targetHeight);
            bool immediate = _snapPending || ReduceMotion;
            if (immediate) { _pitch = pitch; _yaw = yaw; _distance = distance; }
            else
            {
                _pitch = Mathf.Lerp(_pitch, pitch, _armPitchLerp * deltaTime);
                _yaw = Mathf.Lerp(_yaw, yaw, _armYawDistanceLerp * deltaTime);
                _distance = Mathf.Lerp(_distance, distance, _armYawDistanceLerp * deltaTime);
            }
            Quaternion arm = Quaternion.Euler(_pitch, _yaw, 0f);
            UpdateAds(input, deltaTime);
            float fieldOfView = Mathf.Lerp(_defaultFieldOfView, _adsFieldOfView, _ads);
            // 조준 광선은 대사 블렌드와 충격을 걷어 낸 플레이 구도에서 쏜다. 위치는 지난 프레임 값, 회전과 FOV는 이번 프레임 값이다.
            _camera.transform.SetPositionAndRotation(_gameplayPosition, arm);
            _camera.fieldOfView = fieldOfView;
            Vector3 forward = Vector3.ProjectOnPlane(arm * Vector3.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(arm * Vector3.right, Vector3.up).normalized;
            UpdateAimOffset(input, forward, right, aimPlane, deltaTime, unscaledDeltaTime);
            // 원본 GameCamera 루트가 놓이는 virtualTarget이다. 날씨 FX가 이 점을 따라가므로 남겨 둔다.
            _followPoint = targetPosition + forward * _offsetZ + right * _offsetX + Vector3.up * _targetHeight;
            _gameplayPosition = _followPoint + arm * new Vector3(0f, 0f, -_distance);
            if (_focus != null)
                _focus.focusDistance.Override(Mathf.Max(_minFocusDistance, Vector3.Distance(_gameplayPosition, targetPosition) - _focusOffset));
            _shake = immediate ? Vector3.zero : Vector3.Lerp(_shake, Vector3.zero, 1f - Mathf.Exp(-_shakeDecay * unscaledDeltaTime));
            _camera.transform.SetPositionAndRotation(_gameplayPosition + _shake, arm);
            // Snap 프레임은 원본 ForceSyncPos처럼 대사 블렌드를 끝값으로 맞춘 뒤 블렌드 단계를 돌린다.
            if (_snapPending) _blend.Finish();
            _blend.Tick(_camera, unscaledDeltaTime, ReduceMotion);
            if (!_snapPending) return;
            _snapPending = false;
            if (_cameraData != null) _cameraData.resetHistory = true;
        }

        // 원본 GameCamera.LateUpdate의 ADS 단계다. 총이면 원본 UpdateAds처럼 조준 중 초당 1/ADSTime, 해제 중 최소 초당 4로 ADS 값을 바꾼다.
        // 총이 없으면 초당 10으로 바꾼다. 최대 조준 오프셋과 거리 계수는 이 ADS 값으로 보간한다.
        // 움직임 감소 옵션이면 ADS 전환을 즉시 적용한다. 조준 오프셋은 원본 멀미 방지와 달리 조준 중에도 UpdateAimOffset이 0으로 둔다.
        void UpdateAds(in OriginalCameraInput input, float deltaTime)
        {
            // OriginalCameraInput 계약대로 호출자가 이미 거른 ADS 값을 그대로 쓴다.
            bool aiming = input.Ads;
            float goal = aiming ? 1f : 0f;
            if (_gun != null)
            {
                float speed = aiming ? _gunAdsSpeed : Mathf.Max(_gunAdsSpeed, _adsReleaseMinSpeed);
                _ads = ReduceMotion ? goal : Mathf.MoveTowards(_ads, goal, deltaTime * speed);
                _maxAimOffset = Mathf.Lerp(_defaultAimOffset, _defaultAimOffset * _gunAimDistanceFactor, _ads);
                _aimOffsetDistanceFactor = Mathf.Lerp(_defaultAimOffsetDistanceFactor, _defaultAimOffsetDistanceFactor * _gunAimDistanceFactor, _ads);
            }
            else
            {
                _ads = ReduceMotion ? goal : Mathf.MoveTowards(_ads, goal, deltaTime * _noGunAdsSpeed);
                _maxAimOffset = Mathf.Lerp(_defaultAimOffset, _defaultAimOffset * _noGunAimOffsetFactor, _ads);
                _aimOffsetDistanceFactor = _defaultAimOffsetDistanceFactor;
            }
        }

        // 원본 UpdateAimOffsetNormal이다. 화면 중앙과 위아래 가장자리 광선으로 기준점을 잡고, 조준점까지의 벡터를 최대 오프셋으로 제한해 보간한다.
        // 피치 계수는 피치 20도에서 0, 50도에서 1이다. 피치가 낮을수록 보간 속도와 기준점 보정과 오프셋이 줄어든다.
        // 입력이 비활성이면 실시간으로 0에 돌아간다. Snap 프레임은 원본 ForceSyncPos처럼 보간 비율 1로 한 번에 맞춘다.
        void UpdateAimOffset(in OriginalCameraInput input, Vector3 forward, Vector3 right, Plane aimPlane, float deltaTime, float unscaledDeltaTime)
        {
            if (ReduceMotion)
            {
                _offsetX = 0f;
                _offsetZ = 0f;
                return;
            }
            float pitchFactor = Mathf.InverseLerp(_pitchFactorMin, _pitchFactorMax, _pitch);
            // 방아쇠도 OriginalCameraInput 계약대로 호출자가 이미 거른 값이다.
            _lerpSpeed = Mathf.MoveTowards(_lerpSpeed, input.Trigger ? _triggerAimLerpSpeed : _aimLerpSpeed, deltaTime);
            _lerpSpeed = Mathf.Lerp(_triggerAimLerpSpeed, _lerpSpeed, pitchFactor);
            if (!input.InputActive)
            {
                float settle = _snapPending ? 1f : unscaledDeltaTime * _lerpSpeed;
                _offsetX = Mathf.Lerp(_offsetX, 0f, settle);
                _offsetZ = Mathf.Lerp(_offsetZ, 0f, settle);
                return;
            }
            var middle = new Vector2(_camera.pixelWidth * .5f, _camera.pixelHeight * .5f);
            Vector3 center = ScreenPointToTargetPlane(aimPlane, middle);
            Vector3 top = ScreenPointToTargetPlane(aimPlane, new Vector2(middle.x, _camera.pixelHeight));
            Vector3 bottom = ScreenPointToTargetPlane(aimPlane, new Vector2(middle.x, 0f));
            Vector3 anchor = Vector3.MoveTowards(center, (top + bottom) * .5f, _anchorShiftPerPitch * pitchFactor);
            float shift = Vector3.Distance(anchor, center);
            Vector3 aim = Vector3.ClampMagnitude(ScreenPointToTargetPlane(aimPlane, input.AimScreen) - anchor, _maxAimOffset) * pitchFactor;
            float step = _snapPending ? 1f : deltaTime * _lerpSpeed;
            _offsetX = Mathf.Lerp(_offsetX, Mathf.Clamp(Vector3.Dot(aim, right) * _aimOffsetDistanceFactor, -_maxAimOffset, _maxAimOffset), step);
            _offsetZ = Mathf.Lerp(_offsetZ, Mathf.Clamp(Vector3.Dot(aim, forward) * _aimOffsetDistanceFactor, -_maxAimOffset, _maxAimOffset) - shift, step);
        }

        // 원본 ScreenPointToCharacterPlane이다. 화면 광선과 조준 평면의 교점을 돌려주고, 만나지 않으면 원점을 돌려준다.
        Vector3 ScreenPointToTargetPlane(Plane plane, Vector2 point)
        {
            Ray ray = _camera.ScreenPointToRay(point);
            return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : Vector3.zero;
        }

        // 이 카메라의 Volume 스택을 VolumeProxy 위치와 카메라의 Volume 레이어로 한 번 섞는다. Volume 관리자가 준비 전이거나
        // 카메라가 먼저 파괴됐으면 건너뛰고, 그동안 암은 기본값이다.
        void UpdateVolumes()
        {
            if (!VolumeManager.instance.isInitialized || _cameraData == null) return;
            if (!_scriptedVolumes)
            {
                _camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);
                _scriptedVolumes = true;
            }
            _camera.UpdateVolumeStack(_cameraData);
            _volumesEvaluated = true;
        }

        // 원본 CameraArmControl Volume을 섞은 암 값을 이 카메라의 Volume 스택에서 읽는다. 아직 섞지 않았으면 원본 기본 암을 쓴다.
        // URP 기본 Volume 프로필에 OriginalCameraArmControl이 저작되어 있어 스택에서 늘 찾는다.
        void ReadArm(out float pitch, out float yaw, out float distance)
        {
            pitch = _defaultPitch; yaw = _defaultYaw; distance = _defaultDistance;
            VolumeStack? stack = _cameraData != null ? _cameraData.volumeStack : null;
            if (stack == null) return;
            OriginalCameraArmControl arm = stack.GetComponent<OriginalCameraArmControl>();
            pitch = arm._pitch.value; yaw = arm._yaw.value; distance = arm._distance.value;
        }

        // 카메라의 Volume 갱신 모드와 스택, 초점 프로필 복제본, VolumeProxy를 되돌리며 여러 번 불러도 된다.
        // 카메라 스택은 URP가 재사용 목록에 돌려받으므로 월드를 반복 준비해도 늘지 않는다. 카메라가 먼저 파괴됐으면 이 단계를 건너뛴다.
        // 해제는 던지지 않는다. 한 단계가 실패해도 Cleanup.Run이 카메라 문맥으로 기록하고 나머지를 계속한다.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UniversalAdditionalCameraData? cameraData = _cameraData;
            RuntimeVolumeProfile? focusProfile = _focusProfile;
            _focusProfile = null;
            _focus = null;
            if (cameraData != null && _scriptedVolumes)
                Cleanup.Run(() =>
                {
                    _camera.SetVolumeFrameworkUpdateMode(_previousVolumeMode);
                    _camera.DestroyVolumeStack(cameraData);
                }, _camera);
            if (focusProfile != null) Cleanup.Run(focusProfile.Dispose, _camera);
            Cleanup.Run(() => UnityObjects.DestroyOwned(_volumeProxy), _camera);
        }
    }
}
