#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.App
{
    // 원본 Cinemachine 브레인의 대사 카메라 블렌드다. 플레이 구도와 대사 가상 카메라 사이를 섞고 두 쪽의 Volume 가중치를 교차한다.
    // 게임 카메라가 만들고, 대사 카메라를 바꿀 때와 매 Tick 끝에 부른다.
    internal sealed class OriginalCameraBlend
    {
        // 원본 Cinemachine 브레인의 기본 블렌드 길이다. 곡선은 EaseInOut이고 시간 배율의 영향을 받지 않는다.
        const float _blendDuration = .5f;

        // 원본 MainVirtualCamera 전용 초점 Volume이며 생성 때만 정한다. 블렌드에서 플레이 카메라 쪽 가중치를 맡는다.
        readonly Volume? _focusVolume;
        OriginalVirtualCamera? _liveCamera;
        // 블렌드에서 빠지는 쪽과 블렌드 시작 때의 Volume 가중치다. camera가 null이면 플레이 카메라의 초점 Volume을 뜻한다.
        // 블렌드 도중 대상이 바뀌어도 빠지는 Volume마다 지금 가중치에서 0으로 이어서 줄인다.
        readonly List<(OriginalVirtualCamera? camera, float weight)> _outgoing = new();
        // 들어오는 쪽 Volume의 블렌드 시작 가중치다. 빠지는 중이던 Volume이면 그 가중치에서, 아니면 0에서 시작한다.
        float _blendFromWeight;
        float _cameraBlend = 1f, _blendFromFov;
        Vector3 _blendFromPosition;
        Quaternion _blendFromRotation;

        // 초점 Volume은 저작에서 빠질 수 있어 null을 받는다.
        internal OriginalCameraBlend(Volume? focusVolume) => _focusVolume = focusVolume;

        // 원본 AT_SetVirtualCamera처럼 대상 가상 카메라를 켜고 지금 화면 자세에서 블렌드를 시작한다. null이면 플레이 구도로 돌아간다.
        // 빠지는 가상 카메라는 블렌드가 끝난 뒤 끈다.
        internal void SetTarget(OriginalVirtualCamera? next, Camera camera)
        {
            if (next == _liveCamera) return;
            // _liveCamera를 바꾸기 전에 불러야 지금 대상을 빠지는 쪽에 넣고 새 대상의 시작 가중치를 정할 수 있다.
            Begin(next, camera);
            _liveCamera = next;
            if (next != null) next.Activate();
        }

        // 원본 ForceSyncPos처럼 진행 중인 블렌드를 끝낸다. 같은 Tick의 블렌드 단계가 끝값을 적용하고 빠진 가상 카메라를 끈다.
        internal void Finish() => _cameraBlend = 1f;

        // 지금 화면 자세에서 블렌드를 새로 시작한다. 빠지는 쪽은 지금 가중치에서 0으로, 새 대상은 빠지던 가중치나 0에서 1로 섞는다.
        // 그래서 블렌드 도중 대상이 바뀌어도 가중치가 튀지 않아 DoF와 색보정이 깜박이지 않는다.
        void Begin(OriginalVirtualCamera? next, Camera camera)
        {
            bool tracked = false;
            foreach (var entry in _outgoing)
                if (ReferenceEquals(entry.camera, _liveCamera))
                {
                    tracked = true;
                    break;
                }
            if (!tracked) _outgoing.Add((_liveCamera, 0f));
            _blendFromWeight = 0f;
            for (int i = _outgoing.Count - 1; i >= 0; i--)
            {
                OriginalVirtualCamera? outgoing = _outgoing[i].camera;
                Volume? volume = VolumeOf(outgoing);
                float weight = volume != null ? volume.weight : 0f;
                if (ReferenceEquals(outgoing, next)) { _blendFromWeight = weight; _outgoing.RemoveAt(i); }
                else _outgoing[i] = (outgoing, weight);
            }
            _blendFromPosition = camera.transform.position;
            _blendFromRotation = camera.transform.rotation;
            _blendFromFov = camera.fieldOfView;
            _cameraBlend = 0f;
        }

        // 플레이 구도와 대사 가상 카메라의 위치, 노이즈를 더한 회전, FOV를 EaseInOut으로 섞고 Volume 가중치도 함께 교차한다.
        // 움직임 감소 옵션이면 블렌드를 즉시 끝내고 가상 카메라 노이즈를 생략한다. 블렌드가 끝나면 빠진 가상 카메라를 끈다.
        internal void Tick(Camera camera, float unscaledDeltaTime, bool reduceMotion)
        {
            if (_liveCamera == null && _outgoing.Count == 0 && _cameraBlend >= 1f) return;
            _cameraBlend = reduceMotion ? 1f : Mathf.Min(1f, _cameraBlend + unscaledDeltaTime / _blendDuration);
            float blend = Mathf.SmoothStep(0f, 1f, _cameraBlend);
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            if (_liveCamera != null)
            {
                position = _liveCamera.transform.position;
                rotation = _liveCamera.Rotation(unscaledDeltaTime, reduceMotion);
                fieldOfView = _liveCamera.FieldOfView;
            }
            camera.transform.SetPositionAndRotation(Vector3.Lerp(_blendFromPosition, position, blend), Quaternion.Slerp(_blendFromRotation, rotation, blend));
            camera.fieldOfView = Mathf.Lerp(_blendFromFov, fieldOfView, blend);
            Volume? live = VolumeOf(_liveCamera);
            if (live != null) live.weight = Mathf.Lerp(_blendFromWeight, 1f, blend);
            foreach ((OriginalVirtualCamera? outgoing, float weight) in _outgoing)
            {
                Volume? volume = VolumeOf(outgoing);
                if (volume != null) volume.weight = weight * (1f - blend);
            }
            if (_cameraBlend < 1f || _outgoing.Count == 0) return;
            foreach (var entry in _outgoing)
                if (entry.camera != null) entry.camera.gameObject.SetActive(false);
            _outgoing.Clear();
        }

        // 블렌드 한쪽의 Volume이다. 진짜 null 참조는 플레이 카메라의 초점 Volume을 뜻한다.
        // 파괴된 가상 카메라도 Unity의 == null에 걸리므로, ReferenceEquals로 진짜 null을 먼저 가른 뒤 Volume 없음으로 본다.
        Volume? VolumeOf(OriginalVirtualCamera? camera) => ReferenceEquals(camera, null) ? _focusVolume : camera != null ? camera.Volume : null;
    }
}
