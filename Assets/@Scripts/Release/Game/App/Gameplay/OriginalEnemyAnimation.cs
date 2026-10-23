#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 CharacterAnimationControl이 적에게 하는 일이다. 이동 값, 손 상태, 무기 표시 시점, 공격 트리거를 Animator와 외형에 보낸다.
    // 적 AI가 Prepare에서 만들고, 이동 값과 꺼낸 무기는 AI가 계산해 Tick에 넘긴다.
    internal sealed class OriginalEnemyAnimation
    {
        // 원작 Movement.StandStill의 속력 기준이다. 이보다 느리면 이동 방향을 대기값 (0, 1)로 보낸다.
        const float _standStillSpeed = .1f;
        // 이보다 짧은 수평 속도는 방향이 없다고 보고 대기값을 보낸다.
        const float _minMoveSqr = 1e-6f;

        // 원작 CharacterAnimationControl처럼 컨트롤러에 있는 파라미터만 보내는 필터다. 해시는 Animator가 켜진 뒤 처음 보낼 때 모은다.
        readonly OriginalAnimatorParameters _parameters;
        // 오리 리그 외형이나 전용 리그 모델이다. 둘 다 같은 무기 표시 계약을 내준다. 모델에 없으면 비어 있다.
        readonly OriginalEnemyVisual? _appearance;
        // 전용 리그 모델이면 그 모델이다. 손 레이어 구동 대신 거른 파라미터를 보내고 달리기 여부를 소켓 회전에 넘긴다.
        readonly OriginalDedicatedRigModel? _dedicated;
        // 플레이어와 같은 규칙으로 손 상태와 공격 레이어를 보낸다. 오리 리그에만 있다.
        readonly OriginalHandAnimation? _hands;
        bool _weaponShown;

        // 전용 리그에는 Hand 레이어가 없어 손 레이어 구동과 MagicBlend를 붙이지 않는다(Docs/rules/character.md "Dedicated rigs").
        internal OriginalEnemyAnimation(Animator animator, OriginalEnemyVisual? appearance)
        {
            _parameters = new OriginalAnimatorParameters(animator);
            _appearance = appearance;
            _dedicated = appearance as OriginalDedicatedRigModel;
            if (_dedicated == null)
            {
                // 손 상태와 공격 레이어는 Animator가 켜진 뒤 Tick이 보낸다.
                _hands = new OriginalHandAnimation(animator);
                // 원본처럼 손 레이어 상태가 고른 자세로 팔을 Animator 루트 기준으로 덮는다. 걷거나 구를 때 몸이 움직여도 총은 조준 방향을 지킨다.
                GameplayComponents.GetOrAdd<OriginalMagicBlend>(animator.gameObject);
            }
        }

        // 한 프레임의 애니메이션 값을 보낸다. moveValue는 원작 GetMoveAnimationValue 결과이고, velocity는 에이전트 속도다.
        // drawn은 꺼내기 시간이 지났는지, handState는 그때의 손 상태다. 꺼내기 전에는 원본처럼 빈손 상태가 온다.
        // rightHandOut은 원작 RightHandOut 값이라 장전 중에는 drawn이어도 거짓이다. 무기 표시 시점은 drawn만 본다.
        internal void Tick(Transform body, Vector3 velocity, float moveValue, bool gunReady, bool rightHandOut, bool drawn, int handState, bool running,
            float deltaTime)
        {
            _parameters.SetFloat(OriginalAnimatorParameters._moveSpeed, moveValue);
            // 원작 GetLocalMoveDirectionAnimationValue다. 서 있으면 대기값 (0, 1)을, 움직이면 몸 기준 이동 방향을 보낸다.
            // 스파이더의 2D 블렌드가 MoveDirX로 옆걸음(WalkRight)을 고른다. 원작 currentMoveDirectionXZ처럼 수평 이동 방향만 쓴다.
            float speed = velocity.magnitude;
            Vector2 direction = Vector2.up;
            Vector3 flat = velocity;
            flat.y = 0f;
            if (speed >= _standStillSpeed && flat.sqrMagnitude > _minMoveSqr)
            {
                Vector3 local = body.InverseTransformDirection(flat.normalized);
                direction = new Vector2(local.x, local.z);
            }
            _parameters.SetFloat(OriginalAnimatorParameters._moveDirX, direction.x);
            _parameters.SetFloat(OriginalAnimatorParameters._moveDirY, direction.y);
            _parameters.SetBool(OriginalAnimatorParameters._gunReady, gunReady);
            // 꺼내기 시간이 지난 첫 프레임에 무기를 보인다.
            if (drawn && !_weaponShown)
            {
                _weaponShown = true;
                if (_appearance != null) _appearance.SetWeaponVisible(true);
            }
            // 원작 RightHandOut은 든 무기가 보이고 장전 중이 아닐 때 참이다. 보스 DuckV2 컨트롤러는 거짓이면 손을 내린다.
            _parameters.SetBool(OriginalAnimatorParameters._rightHandOut, rightHandOut);
            if (_hands != null)
            {
                _hands.SetHandState(handState);
                _hands.Tick(deltaTime);
            }
            else
            {
                // 전용 리그는 손 레이어가 없어 원작처럼 HandState 값만 거른 파라미터로 보내고, 달리기 여부를 소켓 회전에 넘긴다.
                _parameters.SetInteger(OriginalAnimatorParameters._handState, handState);
                if (_dedicated != null) _dedicated.SetRunning(running);
            }
            if (_appearance != null) _appearance.TickWeapon(deltaTime);
        }

        // 원작 CharacterAnimationControl의 Dashing이다. 보스 DuckV2 컨트롤러는 이 값으로 AnyState에서 Dash(구르기) 상태로 간다.
        // 값은 적 AI가 정하고, 컨트롤러에 이 파라미터가 없는 리그는 거른 파라미터라 보내지 않는다.
        internal void SetDashing(bool dashing) => _parameters.SetBool(OriginalAnimatorParameters._dashing, dashing);

        // 근접 휘두르기의 몸동작이다. 전용 리그는 원작 OnAttack처럼 컨트롤러에 Attack 트리거가 있을 때만 당긴다(늑대 AC_Dog의 AnyState→Attack).
        internal void PlayAttack()
        {
            if (_hands != null) _hands.PlayAttack();
            else _parameters.SetTrigger(OriginalAnimatorParameters._attack);
        }
    }
}
