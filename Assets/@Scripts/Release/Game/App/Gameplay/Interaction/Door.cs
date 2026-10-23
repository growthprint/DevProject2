#nullable enable annotations
using System;
using UnityEngine;

namespace Game.App
{
    // 원작 Door처럼 문짝을 닫힌 자세와 열린 자세 사이에서 움직인다. 조건 사건이 있는 문은 그 사건이 확정되면 DoorSync가 열고,
    // 조건이 없는 문은 같은 오브젝트의 DoorInteractable이 F로 여닫는다. 열림은 저장하지 않고 확정된 사건에서 다시 만든다.
    // 원작 부품 목록(_parts)이 있으면 원작 SetPartsByLerpValue처럼 부품마다 위치, 회전, 활성을 바꾸고 문짝 하나(_leaf)는 쓰지 않는다.
    [DisallowMultipleComponent]
    public sealed class Door : MonoBehaviour
    {
        [SerializeField] string _sourceId = "";
        // 회전하는 문짝이다. 문짝 Collider와 carving 장애물이 같은 Transform을 따라 함께 움직인다.
        [SerializeField] Transform _leaf = null!;
        [SerializeField] Vector3 _openAngles;
        // 원작 lerpTime(초)이다. 두 자세 사이를 이 시간 동안 오간다.
        [SerializeField] float _lerpTime;
        // 이 사건이 확정되면 열리는 조건이다. 비어 있으면 F 손잡이로만 여닫는다.
        [SerializeField] string _requiredEvent = "";
        // 첫 간수 전멸에 연결된 원본 초록 신호등이다. 문과 함께 켠다. 없는 문은 비워 둔다.
        [SerializeField] GameObject? _doorSignal;
        [SerializeField] OriginalPartPose[] _parts = Array.Empty<OriginalPartPose>();
        // 원작 closed 초기값의 반대다. 조건 사건이 없는 문은 첫 복원 때 이 상태로 둔다.
        [SerializeField] bool _startOpen;
        // 원작 hasSound다. 1단계 제작 문은 원작처럼 소리를 내므로 기본값을 참으로 둔다.
        [SerializeField] bool _hasSound = true;
        // 원작 openSound와 closeSound의 FMOD 경로다. 비어 있으면 DoorSync가 원작 기본 문 소리를 쓴다.
        [SerializeField] string _openSound = "";
        [SerializeField] string _closeSound = "";
        Quaternion _closedRotation;
        // 처음 움직일 때 닫힌 자세와 함께 한 번 계산한 열린 자세다. 문이 움직이는 동안 매 프레임 다시 계산하지 않는다.
        Quaternion _openRotation;
        bool _opened;
        bool _initialized;
        float _progress;

        public string SourceId => _sourceId;
        public string RequiredEvent => _requiredEvent;
        internal bool Opened => _opened;
        internal bool StartOpen => _startOpen;
        internal bool HasSound => _hasSound;
        internal string OpenSound => _openSound;
        internal string CloseSound => _closeSound;
        // 원작 부품 목록 문인지다. 제작 도구가 1단계 문짝 하나 문과 구분할 때 쓴다.
        internal bool UsesParts => _parts.Length != 0;

        // 새로 열거나 닫을 때만 알린다. 저장된 상태를 즉시 복원할 때는 알리지 않아 문 소리가 나지 않는다.
        public event Action<bool>? Changed;

        // 복원은 최종 자세를 바로 적용하고, 새로 열 때만 보간과 알림을 시작한다.
        public void SetOpen(bool opened, bool immediate = false)
        {
            if (_opened == opened && !immediate) return;
            // 원작 부품 문은 부품 목록만 움직여 문짝 자세 계산과 신호등을 쓰지 않는다.
            // 즉시 복원은 원작 Start처럼 부품 활성까지 바로 적용한다.
            bool usesParts = UsesParts;
            if (!usesParts && !_initialized)
            {
                _closedRotation = _leaf.localRotation;
                _openRotation = _closedRotation * Quaternion.Euler(_openAngles);
                _initialized = true;
            }
            _opened = opened;
            if (!usesParts && _doorSignal != null) _doorSignal.SetActive(opened);
            if (immediate)
            {
                _progress = opened ? 1f : 0f;
                ApplyPose(true);
                Physics.SyncTransforms();
            }
            else
            {
                // 멈춰서 꺼져 있던 Update를 다시 켜야 문이 보간을 시작한다.
                enabled = true;
                Changed?.Invoke(opened);
            }
        }

        public void Toggle() => SetOpen(!_opened);

        // 원작 Door처럼 문이 움직이는 동안만 Update를 돈다. 아직 움직인 적 없는 문은 첫 Update에서 스스로 꺼지고,
        // 보간을 시작하는 SetOpen만 다시 켠다. 다른 코드는 이 컴포넌트의 enabled를 읽지 않는다.
        void Update()
        {
            // 문짝 하나 문은 SetOpen이 자세를 계산하기 전이면 움직일 것이 없다. 부품 문은 이 계산이 필요 없다.
            if (!UsesParts && !_initialized) { enabled = false; return; }
            // 목표에 도달한 문은 자세를 다시 쓰지 않는다. 부품 문은 원작 Update처럼 끝까지 움직인 프레임에만 활성을 바꾼다.
            float goal = _opened ? 1f : 0f;
            if (_progress == goal) { enabled = false; return; }
            _progress = OriginalPartPose.Step(_progress, goal, _lerpTime);
            ApplyPose(_progress == goal);
        }

        // 제작 도구가 쓰는 구성이다. 신호등은 문을 만든 뒤 ConfigureSignal로 따로 쓴다.
        internal void Configure(string sourceId, Transform leaf, Vector3 openAngles, float lerpTime, string requiredEvent)
        {
            _sourceId = sourceId; _leaf = leaf; _openAngles = openAngles; _lerpTime = lerpTime; _requiredEvent = requiredEvent;
        }

        internal void ConfigureSignal(GameObject? signal) => _doorSignal = signal;

        // 원작 부품 문을 옮기는 제작 도구의 구성이다. 문짝 하나(_leaf)는 쓰지 않는다.
        internal void ConfigureParts(OriginalPartPose[] parts, bool startOpen, bool hasSound, string openSound, string closeSound)
        {
            _parts = parts; _startOpen = startOpen; _hasSound = hasSound; _openSound = openSound; _closeSound = closeSound;
        }

        // 지금 열림 정도를 자세에 쓴다. 부품 문은 부품 목록에, 문짝 하나 문은 문짝 회전에만 쓰며 활성 전환은 부품 문만 한다.
        void ApplyPose(bool setActivation)
        {
            if (UsesParts) OriginalPartPose.ApplyAll(_parts, _progress, setActivation);
            else _leaf.localRotation = Quaternion.Lerp(_closedRotation, _openRotation, _progress);
        }
    }
}
