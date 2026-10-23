#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 전용 리그(스파이더 봇, 늑대) 적 모델이다(Docs/rules/character.md "Dedicated rigs"). 오리 얼굴이 없어 외형 적용 없이
    // 원작 CharacterModel의 소켓에 무기 표시만 만든다. Hand 레이어가 없으므로 적 컨트롤러는 이 모델에 손 레이어 구동과 MagicBlend를 붙이지 않는다.
    // 원작 CharacterModel.autoSyncRightHandRotation이 켜진 모델은 원작 SyncRightHandRotation처럼 LateUpdate마다 RightHandSocket을
    // 조준 방향으로 초당 360도까지 돌린다(사용자 결정 2026-10-21). 오리 리그는 이 보정을 하지 않는다.
    public sealed class OriginalDedicatedRigModel : OriginalEnemyVisual
    {
        // 원작 CharacterModel의 rightHandSocket과 helmatSocket이 가리키는 소켓이다. 제작 도구가 원본 필드의 객체 이름으로 찾아 넣는다.
        [SerializeField] Transform _rightHandSocket = null!;
        [SerializeField] Transform? _helmetSocket;
        // 원작 모델 프리팹의 autoSyncRightHandRotation 값이다. 제작 도구가 원본 값을 그대로 쓴다.
        [SerializeField] bool _syncRightHandRotation;
        // 원작 SyncRightHandRotation의 최대 회전 속도(도/초)다.
        const float _rightHandTurnSpeed = 360f;
        // 원작 Awake의 defaultRightHandLocalRotation이다. 달리는 동안 소켓이 이 자세로 돌아간다.
        Quaternion _defaultRightHandLocalRotation;
        // 원작은 캐릭터가 연결된 모델만 소켓을 돌린다. 준비가 끝난 모델만 돌리고, 달리기 여부는 적 컨트롤러가 넣는다.
        bool _prepared, _running;

        internal override Transform? HeadSocket => _helmetSocket;
        internal override Transform? RightHandSocket => _rightHandSocket;

        // 원작 CharacterMainControl.Running이다. 적 컨트롤러가 이동 애니메이션을 보낼 때마다 넣는다.
        internal void SetRunning(bool running) => _running = running;

        // 오른손 소켓은 제작 도구가 넣는 저작 연결이고 EditMode 테스트가 연결을 확인하므로, 준비는 실패하지 않는다.
        internal override bool TryPrepare(OriginalItemDefinition weapon)
        {
            _defaultRightHandLocalRotation = _rightHandSocket.localRotation;
            CreateWeapon(weapon);
            _prepared = true;
            return true;
        }

        // 원작 SyncRightHandRotation이다. 조준 방향은 원작 CurrentAimDirection(modelRoot.forward)처럼 모델 루트의 앞쪽이다.
        // Animator가 뼈를 갱신한 뒤라 부모 뼈가 움직여도 소켓은 조준 방향을 따라간다.
        void LateUpdate()
        {
            if (!_prepared || !_syncRightHandRotation) return;
            Transform? parent = _rightHandSocket.parent;
            Quaternion target = _running
                ? (parent != null ? parent.rotation : Quaternion.identity) * _defaultRightHandLocalRotation
                : Quaternion.LookRotation(transform.forward, Vector3.up);
            _rightHandSocket.rotation = Quaternion.RotateTowards(_rightHandSocket.rotation, target, _rightHandTurnSpeed * Time.deltaTime);
        }
    }
}
