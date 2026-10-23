#nullable enable annotations
using System;
using UnityEngine;

namespace Game.App
{
    // 원작 Door.DoorTransformInfo의 두 상태(열림·켬, 닫힘·끔) 한 쌍이다. 원작 문과, 원작이 같은 컴포넌트로 만든 스위치가 함께 쓴다.
    // 위치와 회전은 보간하고, 활성은 끝까지 움직인 뒤에만 바꾼다.
    [Serializable]
    internal struct OriginalPartPose
    {
        [SerializeField] Transform _target;
        [SerializeField] Vector3 _openPosition;
        [SerializeField] Quaternion _openRotation;
        [SerializeField] bool _openActive;
        [SerializeField] Vector3 _closedPosition;
        [SerializeField] Quaternion _closedRotation;
        [SerializeField] bool _closedActive;
        // 원작 lerpTime이 0이어도 0으로 나누지 않도록 둔 하한(초)이다. 문과 스위치가 같은 값을 쓴다.
        const float _minimumLerpTime = .01f;

        internal OriginalPartPose(Transform target, Vector3 openPosition, Quaternion openRotation, bool openActive, Vector3 closedPosition,
            Quaternion closedRotation, bool closedActive)
        {
            _target = target; _openPosition = openPosition; _openRotation = openRotation; _openActive = openActive;
            _closedPosition = closedPosition; _closedRotation = closedRotation; _closedActive = closedActive;
        }

        // progress는 열림 정도(0 닫힘, 1 열림)다. 원작 SetPartsByLerpValue처럼 완전히 닫혔을 때만 닫힘 활성을 쓰고, 그 밖에는 열림 활성을 쓴다.
        internal void Apply(float progress, bool setActivation)
        {
            _target.localPosition = Vector3.Lerp(_closedPosition, _openPosition, progress);
            _target.localRotation = Quaternion.Lerp(_closedRotation, _openRotation, progress);
            if (setActivation) _target.gameObject.SetActive(progress <= 0f ? _closedActive : _openActive);
        }

        // 원작 Door.Update의 한 프레임 이동이다. lerpTime 동안 0과 1 사이를 오가고, 도달하면 goal을 정확히 돌려줘 == 비교가 안전하다.
        internal static float Step(float progress, float goal, float lerpTime) =>
            Mathf.MoveTowards(progress, goal, Time.deltaTime / Mathf.Max(_minimumLerpTime, lerpTime));

        // 원작 SetPartsByLerpValue처럼 목록의 모든 부품에 같은 열림 정도를 적용한다.
        internal static void ApplyAll(OriginalPartPose[] parts, float progress, bool setActivation)
        {
            foreach (OriginalPartPose part in parts) part.Apply(progress, setActivation);
        }
    }
}
