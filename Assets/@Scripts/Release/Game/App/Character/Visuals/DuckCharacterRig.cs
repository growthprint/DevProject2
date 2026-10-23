#nullable enable annotations

using System;
using DuckCharacter.AvatarParts;
using Game.Core;
using UnityEngine;

namespace DuckCharacter
{
    // 캐릭터 리그의 본과 소켓 참조를 들고 머리 보정을 적용한다. 외형 데이터와 장비 규칙은 다루지 않는다.
    [DisallowMultipleComponent]
    [AddComponentMenu("Duck Character/Duck Character Rig")]
    public sealed class DuckCharacterRig : MonoBehaviour
    {
        // 머리 설정이 변형하는 머리 본과 이마 본이다. 둘은 서로 다른 본이어야 한다.
        [SerializeField] Transform? _headJoint;
        [SerializeField] Transform? _foreheadJoint;

        // 원본의 이마 기본 높이를 이 리그 좌표에 맞춘 값이다. 머리 설정의 이마 높이를 여기에 더한다.
        [SerializeField] float _foreheadDefaultHeight = 0.19f;

        // 헬멧 소켓 높이는 이마 높이에 이 보정값을 더해 정한다. 원작 캐릭터 모델 프리팹은 모두 -0.1을 쓴다.
        // 원작 코드 기본값 -0.2를 쓰면 소켓이 0.1 m 낮아져 머리 조준 구가 몸통까지 덮는다.
        [SerializeField] float _helmetSocketYOffset = -0.1f;

        // 부위 하나의 소켓 쌍이다. 좌우가 있는 부위만 오른쪽 소켓을 쓴다.
        [Serializable]
        struct AvatarSocketPair
        {
            // 한쪽뿐인 부위의 소켓이거나 좌우 부위의 왼쪽 소켓이다.
            [SerializeField]
            public Transform? _first;
            // 좌우 부위의 오른쪽 소켓이다. 외형 부위 중에는 발과 날개만 쓴다.
            [SerializeField]
            public Transform? _second;
        }

        // 외형 부위 enum 값을 인덱스로 쓰는 소켓 배열이다.
        [SerializeField]
        AvatarSocketPair[] _avatarSockets = new AvatarSocketPair[(int)DuckAvatarPartType.Count];

        // 장갑 날개를 외형 적용 때 한 번 붙이는 손 파츠 소켓이다. 왼쪽은 LeftHandSocket, 오른쪽은 RightHandSocket이다.
        // 원본 UpdateHands처럼 매 프레임 손 위치를 복사하지 않는다. 원본처럼 무기를 붙이는 손 장비 소켓과 같은 원본 손 소켓을 가리킨다.
        [SerializeField] AvatarSocketPair _handPartSockets;

        // 손을 포함한 모든 장비 부착점을 EquipPartType 순서의 고정 배열로 보관한다.
        [SerializeField]
        Transform?[] _equipmentSockets = new Transform?[(int)EquipPartType.EquipCount];

        // 몸통 렌더링은 렌더링 하위 객체가, 애니메이션 재생과 미리보기 설정은 애니메이션 하위 객체가 맡는다.
        [SerializeField] DuckCharacterRendering _rendering = new DuckCharacterRendering();
        [SerializeField] DuckCharacterAnimation _animation = new DuckCharacterAnimation();
        // 장비 표시가 몸통 렌더러와 본 사전을 얻는 입구다.
        public DuckCharacterRendering Rendering => _rendering;
        // 화면 코드가 Animator를 직접 찾지 않고 이 재생 객체를 쓰게 한다.
        public DuckCharacterAnimation Animation => _animation;

        // 외형이 일부만 적용된 채 실패하지 않도록 적용 전에 필수 리그 참조를 검사한다.
        // 결함은 여기서 Log.Instance.Fail로 사유를 보고하고 false를 돌려주므로 호출자는 다시 기록하지 않는다.
        internal bool TryCheckRuntimeReady()
        {
            // Unity는 직렬화 하위 객체를 null로 두지 않으므로 _rendering 자체는 검사하지 않는다.
            // 머리 본과 이마 본은 둘 다 있고 서로 달라야 한다.
            if (_rendering.BodyRenderer == null || !IsDistinctPair(_headJoint, _foreheadJoint))
                return Log.Instance.Fail(new InvalidOperationException("몸통 렌더러와 서로 다른 머리·이마 본 참조가 필요합니다."), this);
            // 손 장비 소켓은 좌우가 모두 있고 서로 달라야 한다.
            Transform? leftHand = GetEquipmentSocket(EquipPartType.LeftHand);
            Transform? rightHand = GetEquipmentSocket(EquipPartType.RightHand);
            if (!IsDistinctPair(leftHand, rightHand))
                return Log.Instance.Fail(new InvalidOperationException("손 장비를 부착할 서로 다른 왼손·오른손 참조가 필요합니다."), this);
            // 머리 소켓이 머리 본의 직접 자식이어야 한 번 적용한 보정이 머리 애니메이션을 따라간다.
            // 헬멧과 마스크 소켓은 설정하지 않아도 되지만, 설정했다면 머리 본의 직접 자식이어야 한다.
            Transform? helmet = GetEquipmentSocket(EquipPartType.Helmet);
            Transform? mask = GetEquipmentSocket(EquipPartType.FaceMask);
            if (!TryGetAvatarSockets(DuckAvatarPartType.Hair, out Transform? hair, out _) || hair!.parent != _headJoint
                || !IsHeadChildOrUnset(helmet) || !IsHeadChildOrUnset(mask))
                return Log.Instance.Fail(new InvalidOperationException("머리깃과 설정된 헬멧·마스크 소켓은 머리 본의 직접 자식이어야 합니다."), this);
            return true;
        }

        // 좌우나 머리와 이마처럼 짝을 이루는 두 참조가 모두 있고 서로 다른지다. Unity null 비교로 파괴된 참조도 없는 것으로 본다.
        static bool IsDistinctPair(Transform? a, Transform? b) => a != null && b != null && a != b;

        // 설정하지 않은 선택 소켓은 통과시키고, 설정한 소켓은 머리 본의 직접 자식인지 본다.
        bool IsHeadChildOrUnset(Transform? socket) => socket == null || socket.parent == _headJoint;

        // 원작 외형 부위 중 좌우 소켓을 따로 쓰는 부위다. 발과 날개만 해당하고 나머지는 첫 소켓 하나만 쓴다.
        public static bool IsPairedAvatarPart(DuckAvatarPartType type) => type == DuckAvatarPartType.Foot || type == DuckAvatarPartType.Wing;

        // enum 배열에서 소켓을 조회하고 누락된 좌우 참조를 거부한다. 소켓 배열 길이는 리그 프리팹을 검사하는 EditMode 테스트가 확인한다.
        public bool TryGetAvatarSockets(DuckAvatarPartType type, out Transform? first, out Transform? second)
        {
            AvatarSocketPair sockets = _avatarSockets[(int)type];
            first = sockets._first;

            // 좌우 부위는 두 소켓이 모두 있고 서로 달라야 하고, 나머지 부위는 첫 소켓만 있으면 된다.
            bool paired = IsPairedAvatarPart(type);
            second = paired ? sockets._second : null;

            return paired ? IsDistinctPair(first, second) : first != null;
        }

        // 장갑 날개를 붙일 손 파츠 소켓을 찾는다. 장갑을 요청한 적용에서만 필요하므로 TryCheckRuntimeReady는 이 소켓을 검사하지 않는다.
        // 왼손과 오른손 소켓은 둘 다 있고 서로 달라야 한다.
        internal bool TryGetHandPartSockets(out Transform? left, out Transform? right)
        {
            left = _handPartSockets._first;
            right = _handPartSockets._second;
            return IsDistinctPair(left, right);
        }

        // 설정되지 않은 부착점은 null이다. 소켓 배열 길이는 리그 프리팹을 검사하는 EditMode 테스트가 확인한다.
        public Transform? GetEquipmentSocket(EquipPartType type)
        {
            return _equipmentSockets[(int)type];
        }


        // 외형 적용이 실패하면 되돌리도록 머리 본, 이마 본, 머리 소켓, 피부색의 실제 값을 기록한다.
        // 되돌릴 외형 데이터가 없는 첫 적용도 이 값으로 되돌린다.
        internal void CaptureAppearance(DuckAppearanceState state)
        {
            state.CaptureTransform(_headJoint);
            state.CaptureTransform(_foreheadJoint);
            // 머리깃 소켓은 적용 전 TryCheckRuntimeReady가 이미 확인했으므로 조회 결과를 다시 보지 않는다.
            _ = TryGetAvatarSockets(DuckAvatarPartType.Hair, out Transform? hair, out _);
            state.CaptureTransform(hair);
            state.CaptureTransform(GetEquipmentSocket(EquipPartType.Helmet));
            state.CaptureTransform(GetEquipmentSocket(EquipPartType.FaceMask));
            _rendering.CaptureAppearance(state);
        }

        // 외형을 바꿀 때 머리 설정으로 머리 변형과 머리 소켓 보정을 한 번 적용한다. 머리 설정은 파츠 배치값과 별개다.
        // 머리와 이마 본은 외형 적용이 적용 전에 TryCheckRuntimeReady로 확인하므로 여기서 다시 보지 않는다.
        public void ApplyHead(DuckHeadSetting settings)
        {
            // 저장값은 배율 1과의 차이라서 ToHeadScale로 실제 머리 배율을 구한다.
            _headJoint.localScale = Vector3.one * DuckHeadSetting.ToHeadScale(settings._headScaleOffset);
            _foreheadJoint.localPosition = Vector3.up * (_foreheadDefaultHeight + settings._foreheadHeight);
            _foreheadJoint.localScale = new Vector3(1f, settings._foreheadRound, 1f);

            // 머리 본의 자식 소켓에 로컬 값을 넣어 매 프레임 갱신 없이 애니메이션을 따라가게 한다. 머리깃 소켓은
            // 머리 배율을 부모에게서 물려받아 이마 위치와 둥글기만 맞추고, 소켓 유무는 적용 전 준비 검사가 확인한다.
            _ = TryGetAvatarSockets(DuckAvatarPartType.Hair, out Transform? hair, out _);
            hair!.localPosition = _headJoint.InverseTransformPoint(_foreheadJoint.position);
            hair.localScale = new Vector3(1f, settings._foreheadRound, 1f);

            Transform? helmet = GetEquipmentSocket(EquipPartType.Helmet);
            if (helmet != null)
                helmet.localPosition = Vector3.up * (_foreheadDefaultHeight + settings._foreheadHeight + _helmetSocketYOffset);
        }


        // 얼굴 외형을 바꿀 때 머리 본 자식인 마스크 소켓의 로컬 높이만 눈 높이에 맞춘다.
        public void ApplyFaceOffsets(DuckAvatarPartInfo eye)
        {
            Transform? mask = GetEquipmentSocket(EquipPartType.FaceMask);
            if (mask == null)
                return;
            Vector3 position = mask.localPosition;
            position.y = eye._height + eye._heightOffset;
            mask.localPosition = position;
        }

    }
}
