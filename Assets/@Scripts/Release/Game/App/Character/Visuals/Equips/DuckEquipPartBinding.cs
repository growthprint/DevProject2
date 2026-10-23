#nullable enable annotations

using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace DuckCharacter
{
    // 장비 메쉬 에셋에는 스켈레톤이 없어 런타임에 본 이름을 알 수 없다. 그래서 Editor 베이크 도구가 캐릭터 FBX
    // DuckCharacter_Base 몸통의 SkinnedMeshRenderer.bones에서 본 이름을 저장한다. 베이크한 EquipmentContent/Equip_*.asset 메쉬는
    // 몸통 bindpose 24개를 그대로 쓰므로, 본 이름도 같은 순서로 24개여야 TryBind의 개수 검사를 지난다.
    /// <summary>스킨드 장비를 캐릭터 본에 다시 묶는 값이다. <see cref="DuckEquipPart"/>의 직렬화 필드이며, 모두 검사한 뒤에만 연결한다.</summary>
    [System.Serializable]
    public class DuckEquipPartBinding
    {
        [Tooltip("캐릭터 본에 다시 묶어야 하는 렌더러.")]
        [SerializeField] SkinnedMeshRenderer[] _renderers = new SkinnedMeshRenderer[0];

        [Tooltip("메쉬 bindposes 와 같은 순서의 본 이름. 캐릭터 FBX 몸통 SkinnedMeshRenderer.bones 에서 가져온다.")]
        [SerializeField] string[] _boneNames = new string[0];

        [Tooltip("몸통 렌더러의 rootBone 이름.")]
        [SerializeField] string _rootBoneName = "";

        // Unity가 localBounds를 rootBone 기준으로 해석하므로, 베이크 도구도 같은 좌표계로 경계를 만들도록 좌표계를 적는다.
        [Tooltip("장비 동작 중 메시를 포함하도록 베이크한 rootBone 좌표 경계.")]
        [SerializeField] Bounds _localBounds;


        public bool IsSkinned
        {
            // 불완전한 스킨 메타데이터도 바인딩 검사로 보내 강체로 오인하지 않는다.
            get { return (_renderers != null && _renderers.Length > 0)
                || (_boneNames != null && _boneNames.Length > 0) || !string.IsNullOrEmpty(_rootBoneName); }
        }

        // 각 실패는 여기서 장비 이름을 붙여 Log.Instance.Fail로 보고하므로 호출자는 다시 기록하지 않는다.
        /// <summary>렌더러를 캐릭터 본에 다시 묶는다. 스킨드가 아니면 아무것도 하지 않고 true를 돌려준다.</summary>
        /// <param name="bones">캐릭터 리그가 만든, 본 이름별 트랜스폼 사전.</param>
        /// <param name="owner">실패 보고의 문맥과 문구 앞에 붙일 장비. 없으면 이름 없이 보고한다.</param>
        public bool TryBind(Dictionary<string, Transform> bones, UnityEngine.Object? owner = null)
        {
            if (!IsSkinned)
                return true;

            // 본이나 렌더러 정보가 빠진 스킨 장비는 착용 중인 장비를 바꾸기 전에 거부한다.
            if (MissingBindingData)
                return Log.Instance.Fail(new InvalidOperationException(
                    Prefix(owner) + "스킨 장비의 본 이름, rootBone 또는 렌더러가 비어 있습니다."), owner);
            // 잘못된 경계는 컬링과 렌더링을 손상시키므로 연결 전에 검사한다.
            if (InvalidLocalBounds)
                return Log.Instance.Fail(new InvalidOperationException(
                    Prefix(owner) + "스킨 장비의 localBounds가 유효하지 않습니다."), owner);

            Transform[] resolved = new Transform[_boneNames.Length];

            for (int i = 0; i < _boneNames.Length; i++)
            {
                Transform bone;
                // 빈 이름은 사전 조회 전에, 파괴된 본은 조회 뒤에 거부하고 발견 지점에서 보고한다.
                if (string.IsNullOrEmpty(_boneNames[i]) || !bones.TryGetValue(_boneNames[i], out bone) || bone == null)
                    return Log.Instance.Fail(new InvalidOperationException(Prefix(owner) + "캐릭터에서 본을 찾지 못했습니다 — "
                        + _boneNames[i] + ". 리그가 바뀌었다면 장비를 다시 구워야 합니다."), owner);

                resolved[i] = bone;
            }

            Transform root;
            if (!bones.TryGetValue(_rootBoneName, out root) || root == null)
                return Log.Instance.Fail(new InvalidOperationException(
                    Prefix(owner) + "rootBone 을 찾지 못했습니다 — " + _rootBoneName), owner);

            // 모든 렌더러를 먼저 검사해, 뒤쪽 렌더러 오류로 앞쪽 렌더러만 연결되는 일을 막는다.
            for (int i = 0; i < _renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = _renderers[i];

                if (renderer == null)
                    return Log.Instance.Fail(new InvalidOperationException(
                        Prefix(owner) + "스킨 장비의 렌더러 참조가 없습니다."), owner);

                // bones 는 반드시 mesh.bindposes 와 길이가 같아야 한다. 다르면 유니티가 조용히
                // 스키닝을 포기하고 메쉬를 원점에 그린다. 구울 때 맞춰 두지만 한 번 더 본다.
                Mesh mesh = renderer.sharedMesh;

                // 메시가 없는 장비도 성공한 교체로 처리하지 않는다.
                if (mesh == null)
                    return Log.Instance.Fail(new InvalidOperationException(
                        Prefix(owner) + "스킨 장비의 메시 참조가 없습니다."), owner);
                if (mesh.bindposeCount != resolved.Length)
                    return Log.Instance.Fail(new InvalidOperationException(Prefix(owner) + "bindposes "
                        + mesh.bindposeCount + "개와 본 " + resolved.Length + "개가 맞지 않습니다."), owner);
            }

            // 필수 자료가 모두 확인된 후에만 Unity 바인딩 상태를 확정한다.
            for (int i = 0; i < _renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = _renderers[i];
                renderer.rootBone = root;
                renderer.bones = resolved;
                renderer.localBounds = _localBounds;
            }

            return true;
        }

        // 스킨 장비로 묶는 데 필요한 본 이름, 렌더러, rootBone 이름 중 하나라도 비어 있는지다.
        bool MissingBindingData => _boneNames == null || _boneNames.Length == 0 || _renderers == null || _renderers.Length == 0
            || string.IsNullOrEmpty(_rootBoneName);

        // 베이크한 경계의 중심이나 크기에 무한대, NaN, 음수 크기가 있는지다.
        bool InvalidLocalBounds => !Finite(_localBounds.center) || !Finite(_localBounds.extents)
            || _localBounds.extents.x < 0f || _localBounds.extents.y < 0f || _localBounds.extents.z < 0f;

        // 경계 여섯 성분의 유한성만 검사하며 정점 순회는 Editor 검증에 둔다.
        static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        // 실패 문구를 "장비 이름 : 사유" 형식으로 만든다. 파괴된 장비는 Unity null 비교로 걸러 이름 없이 보고한다.
        static string Prefix(UnityEngine.Object? owner) => owner != null ? owner.name + " : " : "";
    }
}
