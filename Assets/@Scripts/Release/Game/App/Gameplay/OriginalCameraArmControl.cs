#nullable enable annotations
using System;
using UnityEngine.Rendering;

namespace Game.App
{
    // 원본 SodaCraft.CameraArmControl을 복원한 Volume 컴포넌트로, 레벨과 지역별 카메라 암 피치, 요, 거리를 정한다.
    // 기본값은 원본 필드 초기값이다. 제작 도구가 원본 값을 옮기도록 필드 이름은 원본 이름 앞에 '_'를 붙인다.
    // 원본은 혼합 결과를 CameraArm 정적 값에 쓰지만, 여기서는 게임 카메라가 자기 VolumeStack에서 읽는다.
    // _enable은 혼합에 쓰지 않고 저작 값을 보존하려고 둔다.
    [Serializable, VolumeComponentMenu("Game/Original Camera Arm Control")]
    public sealed class OriginalCameraArmControl : VolumeComponent
    {
        public BoolParameter _enable = new(false);
        // 원본 초기값은 Volume 스택이 없을 때 게임 카메라가 쓰는 기본 암과 같아서 그 상수를 함께 쓴다.
        public MinFloatParameter _pitch = new(OriginalGameCamera._defaultPitch, 0f);
        public FloatParameter _yaw = new(OriginalGameCamera._defaultYaw);
        public MinFloatParameter _distance = new(OriginalGameCamera._defaultDistance, 2f);
    }
}
