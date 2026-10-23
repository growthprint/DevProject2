#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 사격 순환, 공격, 이동 애니메이션을 모은 partial이다. 사격마다 탄을 쓰고 장전을 기다리는 규칙은 OriginalEnemyController.Reload.cs와
    // 함께 읽는다. 표적과 소리 판정은 본문과 .Targeting.cs가 맡는다.
    public sealed partial class OriginalEnemyController
    {
        // 원본 Combat_Normal의 사격 순환이다. 사거리에 들면 shootDelay만큼 기다리고, shootTimeRange 동안 쏜 뒤 shootTimeSpaceRange만큼 쉰다.
        // 대기가 시작되면 목표가 가려지거나 사거리를 벗어나도 연사가 끝날 때까지 쏜다. 원본 Shoot.OnUpdate도 시야를 보지 않는다.
        // 휴식은 연사가 끝난 시각부터 재고, 발사 간격은 무기 공격 간격과 방아쇠 간격이 함께 제한한다. 연사 범위가 0인 총은 시작 프레임에만 쏘는 중이다.
        // now는 이번 프레임 시각이며, EditMode 시험이 시각을 넣어 순환을 돌린다.
        void TickGunCycle(bool attack, float now)
        {
            if (_burstUntil > 0 && now >= _burstUntil)
            {
                _restUntil = _burstUntil + RandomRange(_restRange);
                _burstUntil = 0;
                // 원작 Combat_Attack_Nomral은 Shoot 다음에 TryToReloadIfEmpty를 둔다. 연사가 끝난 프레임에 빈 탄창이면 장전한다.
                ReloadIfEmpty(ref _attackReloadUsed);
            }
            if (_burstUntil <= 0)
            {
                if (_shootAt <= 0)
                {
                    if (now < _restUntil || !attack) return;
                    _shootAt = now + _shootDelay;
                }
                // 원작 WaitUntil !CheckReloading이다. shootDelay가 끝나도 장전이 끝날 때까지 연사를 시작하지 않는다.
                if (now < _shootAt || Reloading) return;
                // 원본 Shoot.OnExecute처럼 연사 시작 프레임에 바로 방아쇠를 당긴다. 자동이 아닌 총의 방아쇠 간격도 여기서 새로 잰다.
                _shootAt = 0;
                _burstUntil = now + RandomRange(_burstRange);
                _semiPressAt = now;
            }
            // 방아쇠를 당긴 프레임에 무기 공격 간격도 지났을 때만 쏜다.
            if (PressTrigger(now) && now >= _nextAttack) Attack();
        }

        // 연사와 근접 공격이 함께 쓴다. 다음 공격 시각은 플레이어와 같은 원본 공격 간격 규칙을 무기 정의에서 읽어 정한다.
        // 근접 휘두르기는 Attack 트리거로 몸동작을 낸다. 총은 원작 화면처럼 몸을 비틀지 않고, 총구가 있어 실제로 쏠 때 총 모델만 민다.
        // 피해 배율은 전투가 이 적의 어트리뷰트에서 읽는다.
        void Attack()
        {
            // 총은 탄 1발을 써야 쏜다. 장전 중이거나 빈 탄창의 방아쇠는 공격 간격을 쓰지 않는다.
            if (_weapon!.IsGun && !TryUseRound()) return;
            _nextAttack = Time.time + _weapon.AttackInterval;
            // 근접 몸동작은 손 레이어나 전용 리그의 Attack 트리거 중 무엇을 쓸지 애니메이션 쪽이 정한다. Prepare가 늘 만든다.
            if (!_weapon.IsGun) _animation!.PlayAttack();
            // 총구는 외형이 만든 무기 표시에서 읽으므로 총구가 있으면 외형도 있다.
            else if (_muzzle != null) _appearance!.KickWeapon();
            // 탄도와 피해는 적 파사드 너머의 전투 시뮬레이션이 플레이어와 같은 규칙으로 처리한다.
            _world!.Attack(this, _weapon);
        }

        void Animate()
        {
            // 장전 진행은 Halted가 아닌 모든 Update가 지나는 이곳에서 잰다. 이번 프레임의 손 상태가 장전 결과를 보도록 먼저 부른다.
            TickReload(Time.time, Time.deltaTime);
            // 애니메이션 객체는 Prepare가 만들고, 준비된 적의 Update에서만 불리므로 null 검사를 하지 않는다.
            // 에이전트 속도는 이 프레임 안에서 바뀌지 않아 한 번 읽어 이동 값과 이동 방향에 함께 쓴다.
            Vector3 velocity = _agent.velocity;
            float speed = velocity.magnitude;
            // 플레이어와 같은 원본 GetMoveAnimationValue 식이다. 원작 프리셋처럼 이동 배율은 기본 걷기에 들어 있어 비율은 든 무기의 배율이다.
            // 카탈로그 무기 배율은 1.3 이하라 걷는 적의 손 레이어가 달리기 총 자세(MoveSpeed 1.5 초과)로 넘어가지 않는다.
            // 근접 AI가 추적 중 달리면(_running) 원작처럼 달리기 식을 쓴다. 그 밖의 적은 달리지 않아 걷기 식만 쓴다.
            float walk = WalkSpeed;
            // 늑대(AC_Dog IdleMove: 0 대기, 1 걷기, 2 달리기)는 근접 AI가 달리는 동안 달리기 식의 1~2 값으로 Run에 간다.
            float moveValue = OriginalCharacter.MoveAnimationValue(speed, _running, walk, _running ? RunSpeed : walk,
                _abilities!.GetBase(CharacterAttributes.WalkSpeed));
            // 꺼내기 시간이 지나면 든 무기의 손 상태를 보낸다. 그 전에는 원본처럼 빈손 상태다.
            // 무기 표시 시점은 drawn이고, 장전 중에는 RightHandOut과 GunReady를 끈다.
            bool drawn = WeaponDrawn;
            int handState = drawn ? OriginalHandAnimation.StateOf(_weapon) : OriginalHandAnimation._emptyHand;
            _animation!.Tick(transform, velocity, moveValue, GunReady, HandOut(drawn), drawn, handState, _running, Time.deltaTime);
        }
    }
}
