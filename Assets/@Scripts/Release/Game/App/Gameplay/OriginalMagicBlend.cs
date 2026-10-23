#nullable enable annotations
using System.Collections.Generic;
using Game.Core;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;

namespace Game.App
{
    // 원본 KINEMATION MagicBlending을 다시 만든 손 자세 혼합이다. Animator 결과 위에서 자세 에셋이 고른 뼈를 Animator 루트 기준
    // 회전으로 덮어, 몸이 공격 동작으로 비틀려도 총을 든 팔은 조준 자세를 지킨다. 규칙: Docs/rules/character.md
    [DisallowMultipleComponent]
    public sealed class OriginalMagicBlend : MonoBehaviour
    {
        // 원본 출력 정렬 값이다. Animator 자체 출력보다 뒤에 평가되어 그 결과 위에 덮는다.
        const ushort _sortingOrder = 900;

        Animator _animator = null!;
        PlayableGraph _graph;
        AnimationPlayableOutput _output;
        AnimationLayerMixerPlayable _mixer;
        AnimationScriptPlayable _basePlayable;
        AnimationScriptPlayable _overlayPlayable;
        AnimationScriptPlayable _layeringPlayable;
        AnimationClipPlayable _baseClip;
        AnimationClipPlayable _overlayClip;
        LayeringJob _layering;
        NativeArray<Atom> _atoms;
        readonly Dictionary<string, int> _atomByName = new();
        RuntimeAnimatorController? _controller;
        // 지금 덮는 자세와, 손 레이어가 새로 요청해 다음 LateUpdate에 넘어갈 자세다.
        OriginalMagicBlendPose? _pose;
        OriginalMagicBlendPose? _pending;
        float _pendingDuration;
        float _blendTime;
        bool _built;

        // 플레이어 모터와 적이 Animator 객체에만 이 컴포넌트를 붙인다.
        void Awake() => _animator = GetComponent<Animator>();

        // 원본 UpdateMagicBlendAsset처럼 이번 평가에서 지금 혼합 결과를 저장하게 하고, LateUpdate에서 새 자세로 넘어간다.
        // 아직 구성하지 못했으면 요청만 기억했다가 구성할 때 섞지 않고 적용한다.
        internal void Request(OriginalMagicBlendPose pose, float duration)
        {
            _pending = pose;
            _pendingDuration = duration;
            if (!_built) return;
            _layering._cachePose = true;
            _layeringPlayable.SetJobData(_layering);
        }

        void Update()
        {
            // Animator가 다시 켜지거나 컨트롤러가 바뀌면 Animator 그래프가 새로 만들어지므로, 끊긴 출력을 버리고 다시 붙인다.
            if (_built && !Intact()) Release();
            if (!_built && !TryBuild()) return;
            // 혼합 진행도는 잡에 넘기는 가중치가 들고, 자세를 바꾼 뒤 혼합 시간에 걸쳐 1까지 올린다.
            if (_layering._blendWeight < 1f)
            {
                _layering._blendWeight = _blendTime > 0f ? Mathf.Clamp01(_layering._blendWeight + Time.deltaTime / _blendTime) : 1f;
                _layeringPlayable.SetJobData(_layering);
            }
            // 원본처럼 반복하는 애니메이션 자세는 길이를 넘으면 처음으로 돌린다. 구성을 마치면 Apply가 늘 자세를 채우고 해제해도 비우지 않는다.
            // 자세 클립은 Apply가 그대로 연결하는 저작 값이고 EditMode 저작 검사가 채워져 있음을 지키므로 여기서도 다시 보지 않는다.
            if (_pose!.IsAnimation)
            {
                AnimationClip clip = _pose.OverlayPose!;
                if (clip.isLooping && _overlayClip.GetTime() > clip.length) _overlayClip.SetTime(0);
            }
        }

        // 요청한 프레임의 평가가 지금 혼합 결과를 저장했으므로, 그 결과에서 새 자세로 넘어가기 시작한다.
        void LateUpdate()
        {
            if (!_built || _pending == null || !_layering._cachePose) return;
            Apply(_pending, _pendingDuration);
            _pending = null;
            _layering._cachePose = false;
            _layering._blendWeight = 0f;
            _layeringPlayable.SetJobData(_layering);
        }

        // 해제는 던지지 않고 기록만 하도록 Cleanup으로 돌린다.
        void OnDestroy() => Cleanup.Run(Release, this);

        // 첫 자세 요청이 와야 리그를 안다. Animator가 꺼져 있어 그래프나 컨트롤러 출력이 아직 없으면 다음 프레임에 다시 본다.
        bool TryBuild()
        {
            OriginalMagicBlendPose? first = _pending != null ? _pending : _pose;
            if (first == null || !_animator.isActiveAndEnabled) return false;
            PlayableGraph graph = _animator.playableGraph;
            if (!graph.IsValid() || !TryFindController(graph, out Playable controller)) return false;
            // 리그와 경로는 Locomotion 빌더가 플레이어 프리팹에서 확인한 저작 값이다. 없는 뼈는 빼고 나머지를 섞는다.
            OriginalMagicBlendRig? rig = first.Rig;
            var bones = new List<Transform>(rig!.Paths.Length);
            foreach (string path in rig.Paths)
            {
                Transform? bone = transform.Find(path);
                if (bone != null) bones.Add(bone);
            }
            _atoms = new NativeArray<Atom>(bones.Count + 1, Allocator.Persistent);
            _atomByName.Clear();
            // 0번은 Animator 루트다. 모든 회전을 이 루트 기준으로 바꿔 섞는다.
            _atoms[0] = new Atom { _handle = _animator.BindStreamTransform(transform) };
            for (int index = 0; index < bones.Count; index++)
            {
                _atoms[index + 1] = new Atom { _handle = _animator.BindStreamTransform(bones[index]) };
                _atomByName[bones[index].name] = index + 1;
            }
            _graph = graph;
            _controller = _animator.runtimeAnimatorController;
            // 원본과 같은 그래프 모양이다. 앞의 두 입력은 기준 자세와 덮을 자세를 뼈 배열에 적고, 마지막 입력이 Animator 결과를 덮는다.
            _basePlayable = AnimationScriptPlayable.Create(graph, new PoseJob { _atoms = _atoms, _overlay = false }, 1);
            _overlayPlayable = AnimationScriptPlayable.Create(graph, new PoseJob { _atoms = _atoms, _overlay = true }, 1);
            _layering = new LayeringJob { _atoms = _atoms, _blendWeight = 1f };
            _layeringPlayable = AnimationScriptPlayable.Create(graph, _layering, 1);
            _layeringPlayable.ConnectInput(0, controller, 0, 1f);
            _mixer = AnimationLayerMixerPlayable.Create(graph, 3);
            _mixer.ConnectInput(0, _basePlayable, 0, 1f);
            _mixer.ConnectInput(1, _overlayPlayable, 0, 1f);
            _mixer.ConnectInput(2, _layeringPlayable, 0, 1f);
            _output = AnimationPlayableOutput.Create(graph, "OriginalMagicBlend", _animator);
            _output.SetSourcePlayable(_mixer);
            _output.SetSortingOrder(_sortingOrder);
            _built = true;
            // 구성 전에 받은 요청이나 다시 구성하기 전의 자세는 섞을 앞 자세가 없으므로 바로 적용한다.
            Apply(first, 0f);
            _pending = null;
            return true;
        }

        // 원본처럼 Animator 자체 출력의 원천인 컨트롤러 Playable을 찾는다. 이 컴포넌트의 출력은 원천이 믹서라 걸리지 않는다.
        static bool TryFindController(PlayableGraph graph, out Playable controller)
        {
            for (int index = 0; index < graph.GetOutputCount(); index++)
            {
                Playable source = graph.GetOutput(index).GetSourcePlayable();
                if (source.IsValid() && source.GetPlayableType() == typeof(AnimatorControllerPlayable))
                {
                    controller = source;
                    return true;
                }
            }
            controller = Playable.Null;
            return false;
        }

        bool Intact() => _graph.IsValid() && _output.IsOutputValid() && _layeringPlayable.IsValid()
            && _layeringPlayable.GetInput(0).IsValid() && _animator.runtimeAnimatorController == _controller;

        // 원본 SetNewAsset처럼 두 자세 클립을 다시 연결하고 뼈마다 가중치를 넣는다. 묶음에 없는 뼈는 0이라 Animator 결과를 지킨다.
        void Apply(OriginalMagicBlendPose pose, float duration)
        {
            _pose = pose;
            _blendTime = duration > 0f ? duration : pose.BlendTime;
            _baseClip = Connect(_basePlayable, _baseClip, pose.BasePose, 0f);
            _overlayClip = Connect(_overlayPlayable, _overlayClip, pose.OverlayPose, pose.IsAnimation ? 1f : 0f);
            for (int index = 1; index < _atoms.Length; index++)
            {
                Atom atom = _atoms[index];
                atom._baseWeight = atom._additiveWeight = atom._localWeight = 0f;
                _atoms[index] = atom;
            }
            foreach (OriginalMagicBlendPose.Layer layer in pose.Layers)
                foreach (string bone in layer._bones)
                {
                    bool found = _atomByName.TryGetValue(bone, out int index);
                    if (!found) continue;
                    Atom atom = _atoms[index];
                    atom._baseWeight = layer._baseWeight * pose.GlobalWeight;
                    atom._additiveWeight = layer._additiveWeight * pose.GlobalWeight;
                    atom._localWeight = layer._localWeight * pose.GlobalWeight;
                    _atoms[index] = atom;
                }
        }

        // 정지 자세는 0초 시점에 멈춰 두고, 애니메이션 자세는 연결한 순간부터 재생한다. 바꾼 앞 클립은 파괴해 그래프에 쌓이지 않게 한다.
        AnimationClipPlayable Connect(AnimationScriptPlayable target, AnimationClipPlayable previous, AnimationClip? clip, float speed)
        {
            if (previous.IsValid()) _graph.DestroyPlayable(previous);
            AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetSpeed(speed);
            target.ConnectInput(0, playable, 0, 1f);
            return playable;
        }

        // 붙인 출력과 Playable만 지우고 Animator 자체 그래프와 컨트롤러 Playable은 그대로 둔다. 그래프가 이미 사라졌으면 뼈 배열만 해제한다.
        void Release()
        {
            _built = false;
            if (_graph.IsValid())
            {
                if (_output.IsOutputValid()) _graph.DestroyOutput(_output);
                DestroyPlayable(_mixer);
                DestroyPlayable(_layeringPlayable);
                DestroyPlayable(_overlayPlayable);
                DestroyPlayable(_basePlayable);
                DestroyPlayable(_overlayClip);
                DestroyPlayable(_baseClip);
            }
            _baseClip = default;
            _overlayClip = default;
            if (_atoms.IsCreated) _atoms.Dispose();
        }

        void DestroyPlayable<T>(T playable) where T : struct, IPlayable
        {
            if (playable.IsValid()) _graph.DestroyPlayable(playable);
        }

        // 뼈 하나의 스트림 핸들과 지금 자세의 가중치, 섞는 중인 두 자세, 이번 평가의 Animator 결과다.
        struct Atom
        {
            internal TransformStreamHandle _handle;
            internal float _baseWeight;
            internal float _additiveWeight;
            internal float _localWeight;
            internal Quaternion _streamRotation;
            internal Vector3 _streamPosition;
            internal AtomPose _active;
            internal AtomPose _cached;
        }

        // 회전은 Animator 루트 기준이고 위치는 부모 기준 로컬 위치다. 자세를 바꿀 때 저장한 값과 새 값을 이 단위로 섞는다.
        struct AtomPose
        {
            internal Quaternion _baseRotation;
            internal Vector3 _basePosition;
            internal Quaternion _overlayRotation;
            internal Vector3 _overlayPosition;
            internal Quaternion _overlayLocalRotation;
            internal float _baseWeight;
            internal float _additiveWeight;
            internal float _localWeight;

            internal static AtomPose Lerp(in AtomPose from, in AtomPose to, float t) => new()
            {
                _baseRotation = Quaternion.Slerp(from._baseRotation, to._baseRotation, t),
                _basePosition = Vector3.Lerp(from._basePosition, to._basePosition, t),
                _overlayRotation = Quaternion.Slerp(from._overlayRotation, to._overlayRotation, t),
                _overlayPosition = Vector3.Lerp(from._overlayPosition, to._overlayPosition, t),
                _overlayLocalRotation = Quaternion.Slerp(from._overlayLocalRotation, to._overlayLocalRotation, t),
                _baseWeight = Mathf.Lerp(from._baseWeight, to._baseWeight, t),
                _additiveWeight = Mathf.Lerp(from._additiveWeight, to._additiveWeight, t),
                _localWeight = Mathf.Lerp(from._localWeight, to._localWeight, t),
            };
        }

        // 정지하거나 재생 중인 자세 클립의 스트림에서 뼈마다 루트 기준 회전과 로컬 위치를 적는다.
        // 캐릭터마다 매 프레임 도는 워커 잡이라 Burst로 컴파일한다. GroundZero(적 9~12마리)에서 관리 코드로는 워커 합이 1.5~1.9ms였다.
        [BurstCompile]
        struct PoseJob : IAnimationJob
        {
            internal NativeArray<Atom> _atoms;
            internal bool _overlay;

            public void ProcessRootMotion(AnimationStream stream) { }

            public void ProcessAnimation(AnimationStream stream)
            {
                Quaternion inverseRoot = Quaternion.Inverse(_atoms[0]._handle.GetRotation(stream));
                for (int index = 1; index < _atoms.Length; index++)
                {
                    Atom atom = _atoms[index];
                    Quaternion rotation = inverseRoot * atom._handle.GetRotation(stream);
                    Vector3 position = atom._handle.GetLocalPosition(stream);
                    if (_overlay)
                    {
                        atom._active._overlayRotation = rotation;
                        atom._active._overlayPosition = position;
                        atom._active._overlayLocalRotation = atom._handle.GetLocalRotation(stream);
                    }
                    else
                    {
                        atom._active._baseRotation = rotation;
                        atom._active._basePosition = position;
                    }
                    _atoms[index] = atom;
                }
            }
        }

        // 원본 LayeringJob의 혼합 식이다. 모든 뼈의 Animator 결과를 먼저 읽고 나서 덮는다. 부모를 먼저 덮으면 자식이 읽는 값이 바뀐다.
        [BurstCompile]
        struct LayeringJob : IAnimationJob
        {
            internal NativeArray<Atom> _atoms;
            internal float _blendWeight;
            internal bool _cachePose;

            public void ProcessRootMotion(AnimationStream stream) { }

            public void ProcessAnimation(AnimationStream stream)
            {
                Quaternion root = _atoms[0]._handle.GetRotation(stream);
                Quaternion inverseRoot = Quaternion.Inverse(root);
                for (int index = 1; index < _atoms.Length; index++)
                {
                    Atom atom = _atoms[index];
                    atom._streamRotation = inverseRoot * atom._handle.GetRotation(stream);
                    atom._streamPosition = atom._handle.GetLocalPosition(stream);
                    atom._active._baseWeight = atom._baseWeight;
                    atom._active._additiveWeight = atom._additiveWeight;
                    atom._active._localWeight = atom._localWeight;
                    _atoms[index] = atom;
                }
                for (int index = 1; index < _atoms.Length; index++)
                {
                    Atom atom = _atoms[index];
                    AtomPose pose = _blendWeight >= 1f ? atom._active : AtomPose.Lerp(atom._cached, atom._active, _blendWeight);
                    // 섞는 중이 아니면 매 평가 결과도 저장한다. 상태 행동의 요청이 이번 평가보다 늦게 와도, 새로 구성해 비어 있는
                    // 캐시(0 쿼터니언)가 아니라 직전 결과에서 넘어가게 한다.
                    if (_cachePose || _blendWeight >= 1f)
                    {
                        atom._cached = pose;
                        _atoms[index] = atom;
                    }
                    // additive는 기준 자세에서 벗어난 Animator 동작을 덮을 자세에 더하는 비율이고, baseWeight는 그 결과로 바꾸는 비율이다.
                    Quaternion delta = atom._streamRotation * Quaternion.Inverse(pose._baseRotation);
                    Quaternion rotation = Quaternion.Slerp(pose._overlayRotation, delta * pose._overlayRotation, pose._additiveWeight);
                    rotation = Quaternion.Slerp(atom._streamRotation, rotation, pose._baseWeight);
                    atom._handle.SetRotation(stream, root * rotation);
                    atom._handle.SetLocalRotation(stream,
                        Quaternion.Slerp(atom._handle.GetLocalRotation(stream), pose._overlayLocalRotation, pose._localWeight));
                    Vector3 position = pose._overlayPosition + (atom._streamPosition - pose._basePosition) * pose._additiveWeight;
                    position = Vector3.Lerp(atom._streamPosition, position, pose._baseWeight);
                    atom._handle.SetLocalPosition(stream, Vector3.Lerp(position, pose._overlayPosition, pose._localWeight));
                }
            }
        }
    }
}
