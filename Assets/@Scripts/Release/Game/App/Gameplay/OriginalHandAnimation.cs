#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 CharacterAnimationControl_MagicBlend처럼 든 무기의 손 상태와 공격 레이어 가중치를 Animator에 보낸다.
    // 플레이어 모터와 적이 같은 규칙을 쓰고, 소유자가 자기 애니메이션 갱신에서 Tick을 부른다. 플레이어 모터는 시간이 멈춘 프레임과
    // 게임플레이가 멈춘 일부 프레임에는 Tick을 건너뛴다.
    internal sealed class OriginalHandAnimation
    {
        // 원본 HandheldAnimationType 값이다. 손 레이어 전환이 이 값으로 빈손, 일반 손(손에 든 수류탄 같은 아이템), 총, 근접 자세를 고른다.
        internal const int _emptyHand = 0, _normalHand = 1, _gunHand = 2, _meleeHand = 3;
        // 원본 ItemAgentHolder.holdStadyTime이다. 무기를 바꾸면 이 시간 동안 무기를 숨기고 빈손 상태를 보낸다.
        internal const float _drawTime = .15f;
        const string _attackLayerName = "MeleeAttack";
        // 원본 모델의 attackTime이다. 공격 레이어 가중치 곡선을 이 시간에 걸쳐 재생한다.
        const float _attackTime = .55f;
        // 원본 모델의 attackLayerWeightCurve다. 키의 시간, 값, 접선이 원본과 같다.
        static readonly AnimationCurve _attackWeight = new(
            new Keyframe(0f, 0f, 43.15789f, 43.15789f),
            new Keyframe(.023170734f, 1f, 0f, 0f),
            new Keyframe(.6106899f, 1f, 0f, 0f),
            new Keyframe(1f, 0f, -2.5686462f, -2.5686462f));
        const int _unresolvedLayer = int.MinValue;

        readonly Animator _animator;
        // 꺼진 Animator는 레이어 번호를 돌려주지 않으므로, 켜진 뒤 처음 쓸 때 이름으로 찾는다.
        int _attackLayer = _unresolvedLayer;
        int _handState;
        // 마지막 공격 뒤 지난 시간이다. 음수면 공격 레이어가 쉬는 중이다.
        float _attackElapsed = -1f;

        internal OriginalHandAnimation(Animator animator) => _animator = animator;

        // 원본 무기 프리팹의 handAnimationType처럼 총은 2, 근접 무기는 3, 그 밖의 아이템과 빈손은 0이다.
        internal static int StateOf(OriginalItemDefinition? weapon) =>
            weapon == null ? _emptyHand : weapon.IsGun ? _gunHand : weapon.IsMelee ? _meleeHand : _emptyHand;

        // 원본처럼 손 상태는 값만 받아 두고 다음 Tick이 보낸다. 그래서 Animator가 꺼진 동안에 불러도 되고,
        // 같은 프레임의 Tick보다 늦게 오거나 Tick을 건너뛴 프레임에 오면 Animator에는 다음 Tick에 들어간다.
        internal void SetHandState(int state) => _handState = state;

        // 원본 OnAttack처럼 근접 휘두르기 때마다 Attack 트리거를 당기고 가중치 곡선을 처음부터 다시 잰다.
        // 총 발사는 부르지 않는다. 원작 화면에서 사격은 몸을 비틀지 않는다(사용자 결정 2026-10-16).
        internal void PlayAttack()
        {
            // 파라미터 해시는 이동 컨트롤러를 쓰는 모든 캐릭터가 같은 표를 읽는다.
            _animator.SetTrigger(OriginalAnimatorParameters._attack);
            _attackElapsed = 0f;
        }

        // 원본 UpdateAttackLayerWeight처럼 공격 뒤 0.55초 동안 곡선대로 공격 레이어 가중치를 주고, 끝나면 0으로 둔다.
        internal void Tick(float deltaTime)
        {
            // 파라미터 해시는 이동 컨트롤러를 쓰는 모든 캐릭터가 같은 표를 읽는다.
            _animator.SetInteger(OriginalAnimatorParameters._handState, _handState);
            // 씬 전환 중처럼 Animator가 아직 컨트롤러를 묶지 않았으면 레이어 번호가 -1로 나오므로, 묶인 뒤에만 찾는다.
            if (_attackLayer == _unresolvedLayer)
            {
                if (!_animator.isInitialized) return;
                _attackLayer = _animator.GetLayerIndex(_attackLayerName);
                // 공격 레이어는 이동 컨트롤러의 저작 내용이다. 없으면 휘두르기 몸동작만 빠진다.
                if (_attackLayer >= 0) _animator.SetLayerWeight(_attackLayer, 0f);
            }
            if (_attackElapsed < 0f || _attackLayer < 0) return;
            _attackElapsed += deltaTime;
            float weight = 0f;
            if (_attackElapsed < _attackTime) weight = _attackWeight.Evaluate(_attackElapsed / _attackTime);
            else _attackElapsed = -1f;
            _animator.SetLayerWeight(_attackLayer, weight);
        }
    }
}
