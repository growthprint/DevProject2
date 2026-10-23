#nullable enable annotations
using System;
using UnityEngine;

namespace Game.App
{
    // 원본 MagicBlendAsset 하나의 값이다. 손 레이어 상태에 들어오면 이 자세로 정해진 뼈 묶음을 덮는다.
    // Editor 빌더가 원본 에셋 값을 그대로 옮기고, 실행 코드는 읽기만 한다.
    public sealed class OriginalMagicBlendPose : ScriptableObject
    {
        // 원본 LayeredBlend 한 줄이다. 뼈 묶음마다 덮을 자세로 바꾸는 비율, 기본 동작을 더하는 비율, 로컬 회전을 바꾸는 비율을 둔다.
        [Serializable]
        internal struct Layer
        {
            [SerializeField] internal string[] _bones;
            [SerializeField] internal float _baseWeight;
            [SerializeField] internal float _additiveWeight;
            [SerializeField] internal float _localWeight;
        }

        [SerializeField] OriginalMagicBlendRig? _rig;
        // 기본 동작에서 뺄 기준 자세와 그 위에 덮을 자세다. 덮을 자세가 애니메이션이면 상태에 들어온 뒤부터 재생한다.
        [SerializeField] AnimationClip? _basePose;
        [SerializeField] AnimationClip? _overlayPose;
        [SerializeField] bool _isAnimation;
        // Animator 전환 시간이 0이면 앞 자세에서 이 시간 동안 넘어간다. 원본 손 레이어 전환은 모두 0이라 이 값이 쓰인다.
        [SerializeField] float _blendTime;
        [SerializeField] float _globalWeight;
        [SerializeField] Layer[] _layers;

        internal OriginalMagicBlendRig? Rig => _rig;
        internal AnimationClip? BasePose => _basePose;
        internal AnimationClip? OverlayPose => _overlayPose;
        internal bool IsAnimation => _isAnimation;
        internal float BlendTime => _blendTime;
        internal float GlobalWeight => _globalWeight;
        internal Layer[] Layers => _layers;
    }
}
