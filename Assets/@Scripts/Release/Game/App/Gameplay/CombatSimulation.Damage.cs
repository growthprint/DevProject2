#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 단순 체력 대상(원작 HealthSimpleBase) 피해와 원작 ExplosionManager.CreateExplosion이다.
    // 탄, 근접, 폭발이 모두 HurtReceiver로 단순 체력 대상에 피해를 주고, 부서진 대상이 폭발하면 같은 호출 안에서 연쇄한다.
    internal sealed partial class CombatSimulation
    {
        // 원작 ExplosionManager의 Collider[8], 피해를 준 캐릭터 목록, 가림 검사 RaycastHit[3]이다. 이 월드가 가지고 씬 정리 때 목록을 비운다.
        // 원작처럼 연쇄 폭발이 같은 버퍼를 다시 채우므로, 바깥 폭발은 남은 순서에서 안쪽 폭발의 결과를 읽는다. 메인 스레드에서만 쓴다.
        readonly Collider[] _explosionHits = new Collider[8];
        readonly HashSet<Object> _explosionDamaged = new HashSet<Object>();
        readonly RaycastHit[] _explosionBlocks = new RaycastHit[3];
        // 원작 ExplosionFx 프리팹의 PlaySfx 소리다. 효과 프리팹은 원작 스크립트를 지우고 가져오므로 소리는 여기서 낸다.
        const string _explosionSound = "event:/SFX/Combat/Explosive/explode_grenade";
        // 원작 CreateExplosion은 주인공이 30m 안이면 폭발 반대쪽으로 0.4 세기의 카메라 흔들림을 준다.
        const float _explosionShakeDistance = 30f, _explosionShake = .4f;
        // 원작 CreateExplosion의 피해 지점은 맞은 Collider 위 0.6이고, 가림 검사는 폭발 중심 위 0.2에서 시작한다.
        const float _explosionHitHeight = .6f, _explosionCheckLift = .2f;
        // 원작 CheckObsticle은 가림 레이의 두 점 높이를 월드 0.5로 맞춘다.
        const float _obstacleCheckHeight = .5f;

        // 원작 DamageReceiver.Hurt와 그 구독자(HurtVisual, Breakable, HalfObsticle)다. 피해를 받아들였으면 true다.
        // 피격 효과, 위험 효과, 부서진 표시를 원작 순서대로 내고, 이번 피해로 부서진 폭발 대상은 원작 Breakable.OnDead처럼 바로 폭발한다.
        bool HurtReceiver(DamageReceiver receiver, in OriginalDamageInfo info)
        {
            OriginalSimpleHealth health = receiver.Health;
            if (!receiver.Hurt(info)) return false;
            if (health.HitFx != null)
            {
                _effects.Emit(health.HitFx, info.Point, info.Normal);
                if (health.HitSound.Length != 0) _world.Audio.PlayOneShot(health.HitSound, info.Point);
            }
            Breakable? breakable = health.Breakable;
            if (breakable != null && breakable.OnHurt(health.Health) && breakable.DangerFx != null)
                _effects.Emit(breakable.DangerFx, breakable.transform.position, breakable.transform.forward);
            if (!health.Dead) return true;
            if (health.HalfObsticle != null) health.HalfObsticle.OnDead();
            if (breakable == null) return true;
            breakable.OnDead();
            // 원작 Breakable처럼 부순 피해의 공격자 적도 폭발 피해에 옮긴다. 적이 부순 드럼통의 폭발 처치는 그 적의 몫이다.
            if (breakable.CreatesExplosion)
                Explode(breakable.transform.position, breakable.ExplosionRadius, breakable.ExplosionDamage(info.FromPlayer).WithAttacker(info.Attacker));
            return true;
        }

        // 원작 ItemAgent_MeleeWeapon의 피해 지점과 방향이다. 지점은 대상에서 공격 방향으로 0.2 앞이다.
        // attacker는 휘두른 적이고 플레이어 근접이면 null이다. 피해 출처를 이 값 하나로 정해 출처와 공격자가 어긋나지 않게 한다.
        static OriginalDamageInfo MeleeDamage(float damage, OriginalEnemyController? attacker, Vector3 target, Vector3 offset)
        {
            Vector3 direction = offset.sqrMagnitude > 0f ? offset.normalized : Vector3.forward;
            return new OriginalDamageInfo(damage, 0f, false, ReferenceEquals(attacker, null), target - direction * _meleeHitForward, -direction, attacker);
        }

        // 원작 CreateExplosion(canHurtSelf 기본값 true)이다. 반지름 안의 피해 Collider 8개까지 본다.
        // 캐릭터는 벽 가림 검사를 하고, 같은 캐릭터는 한 번만, 대시 중이면 맞지 않는다. 단순 체력 대상은 가림과 중복 검사 없이 맞는다.
        void Explode(Vector3 center, float radius, OriginalDamageInfo damage)
        {
            Vector3 player = _world.PlayerPosition;
            if (Vector3.Distance(center, player) < _explosionShakeDistance)
                _world.GameCamera.Impulse((center - player).normalized * _explosionShake);
            _explosionDamaged.Clear();
            int count = Physics.OverlapSphereNonAlloc(center, radius, _explosionHits, GameplayDataSettings.Layers.DamageReceiverLayerMask);
            for (int i = 0; i < count; i++)
            {
                Collider collider = _explosionHits[i];
                // 소속은 이 월드의 Collider 소속 캐시에서 찾는다.
                _owners.FindOwner(collider, out OriginalEnemyController? enemy, out OriginalPlayerMotor? motor, out DamageReceiver? receiver);
                Object? character = enemy != null ? enemy : (Object?)motor;
                if (character == null && receiver == null) continue;
                Vector3 point = collider.transform.position + Vector3.up * _explosionHitHeight;
                if (character != null && Blocked(center + Vector3.up * _explosionCheckLift, point)) continue;
                // 원작처럼 이미 맞은 캐릭터인지와 대시 중인지를 모두 본 뒤 하나라도 맞으면 건너뛴다. 대시 중이어도 목록에는 넣는다.
                // 원작 ExplosionManager.cs:68처럼 대시 중인 적도 플레이어와 같이 건너뛴다.
                bool repeated = character != null && !_explosionDamaged.Add(character);
                bool dashing = (motor != null && motor.IsDashing) || (enemy != null && enemy.IsDashing);
                if (repeated || dashing) continue;
                OriginalDamageInfo info = damage.At(point, (point - center).normalized);
                if (receiver != null) HurtReceiver(receiver, info);
                else if (enemy != null)
                {
                    // 원작 Health.Hurt처럼 받아들인 피해에만 생체 피격 효과를 낸다. 피해 숫자는 적의 피격 큐가 이 피해 지점에 띄운다.
                    // 원작처럼 플레이어가 일으킨 폭발의 처치만 경험치와 처치 수를 받도록 피해 출처를 넘긴다.
                    // 적이 일으킨 폭발이면 그 적을 공격자로 넘긴다. 그때 피해 출처는 이미 거짓이다. 원작 canHurtSelf처럼 공격자 자신도 맞는다.
                    if (!enemy.Hurt(info.Damage, info.ArmorPiercing, info.FromPlayer, attacker: info.Attacker, point: point, normal: info.Normal))
                        continue;
                    OrganicHit(point, info.Normal, false);
                }
                else DamagePlayer(info.Damage, point, info.Normal, info.ArmorPiercing);
            }
            _effects.Emit(GameplayDataSettings.Prefabs.ExplosionFx, center, Vector3.forward);
            _world.Audio.PlayOneShot(_explosionSound, center);
        }

        // 원작 CheckObsticle이다. 두 점의 높이를 월드 0.5로 바꾼 뒤 벽과 바닥 레이어를 레이로 본다. 이 월드의 가림 검사 버퍼를 쓴다.
        bool Blocked(Vector3 start, Vector3 end)
        {
            start.y = _obstacleCheckHeight;
            end.y = _obstacleCheckHeight;
            GameplayDataSettings.LayersData layers = GameplayDataSettings.Layers;
            Vector3 delta = end - start;
            return Physics.RaycastNonAlloc(new Ray(start, delta.normalized), _explosionBlocks, delta.magnitude,
                (int)layers.WallLayerMask | (int)layers.GroundLayerMask) > 0;
        }
    }
}
