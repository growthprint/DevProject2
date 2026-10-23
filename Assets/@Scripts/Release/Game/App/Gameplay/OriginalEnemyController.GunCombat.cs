#nullable enable annotations
using System;
using UnityEngine;
using UnityEngine.AI;

namespace Game.App
{
    // 원작 Combat_Normal 템플릿의 Parallel 노드다. 사격 순환(战斗射击循环)과 전투 이동 순환(战斗移动循环)이 함께 돌고,
    // 교전 중에는 가려진 표적도 늘 겨눈다(SetAim). 이동 순환은 피격 대시 순서를 먼저 보고, 갈래 키 "표적이 가려짐 && HasTarget"이 참이면
    // [0.2초 대기, 알아챈 위치 추적, 0.5초 대기]와 [임의 지점 이동, 대기]를 추적 확률로 고르고, 거짓이면 [임의 지점 이동, 대기]를 되풀이한다.
    // 키가 바뀌면 하던 이동을 끊고 새 갈래를 처음부터 한다. 원작 Shoot은 shootCanMove가 거짓이면 쏘는 동안 멈추고, 그 멈춤이 이동을 끝낸다.
    public sealed partial class OriginalEnemyController
    {
        // 원작 CharacterRandomPreset.shootCanMove와 min/maxTraceTargetChance(x가 최소, y가 최대)다. 기본값은 원작 기본값이고
        // 제작 도구가 프리셋 값으로 덮는다. 추적 확률은 Prepare가 개체마다 한 번 뽑는다.
        [SerializeField] bool _shootCanMove;
        [SerializeField] Vector2 _traceTargetChance = Vector2.one;
        float _traceChance = 1f;
        // 원작 Combat_Normal의 MoveToRandomPos(회피 반경 2 m, 각도 360, 8초)와 TraceTarget(멈춤 1 m, 8초), 추적 앞뒤 대기 값이다.
        // 경로 끝 도착 거리는 원작 AI 프리팹의 nextWaypointDistance 0.6 m다. 추적 경로는 근접 추적과 같은 TraceTarget 간격으로 다시 잡는다.
        const float _fullTurnAngle = 360f, _combatAvoidRadius = 2f, _combatMoveAngle = _fullTurnAngle, _combatMoveOverTime = 8f;
        const float _combatArriveDistance = .6f, _combatTraceStopDistance = 1f, _preTraceWait = .2f, _postTraceWait = .5f;
        // 원작 MoveToRandomPos처럼 중심에서 이 거리(제곱 0.01) 안에 서 있으면 중심 쪽 방향이 없다고 본다.
        const float _centeredSqr = .01f;
        // 원작 Shoot.semiTimeSpace다. 자동이 아닌 총(semi, bolt)은 이 간격마다만 방아쇠를 다시 당긴다.
        const float _semiTriggerSpace = .35f;
        // 원작 Combat_Normal 피격 대시 순서 끝의 Wait다. 이 동안 이동 순환이 멈춘다.
        const float _hurtDashWait = .2f;
        // 원작 Combat_Normal 64의 CheckHurt 값(hurtTimeThreshold 8, damageThreshold 기본 3)이다. 엄폐 사격 조건이 읽는다.
        const float _coverShotTime = 8f, _coverShotDamage = 3f;

        // 원작 Combat_Normal 이동 갈래의 단계다. PreTrace, Trace, PostTrace는 추적 갈래, Move와 MoveWait는 임의 지점 갈래다.
        internal enum GunMovePhase { PreTrace, Trace, PostTrace, Move, MoveWait }
        // 이동 순환 한 프레임이 에이전트에 시킬 일이다. Stop은 경로를 지우고, Trace와 Move는 새 목적지를 준다.
        internal enum GunMoveOrder { None, Stop, Trace, Move }
        // 피격 대시 순서 한 프레임의 결과다. Wait는 순서 뒤 대기, Stop은 대시 간격이 남아 멈추기만 한 것, Dash는 대시 시작이다.
        internal enum GunDashStep { None, Wait, Stop, Dash }

        GunMovePhase _gunMovePhase;
        // 지금 단계가 끝나는 시각(대기는 끝 시각, 이동과 추적은 제한 시각)과 다음 추적 경로 요청 시각이다.
        float _gunMoveUntil, _gunTracePathAt;
        // 지금 갈래를 시작했는지와 그 갈래 키다. 교전을 떠나거나 피격 대시 순서가 끼어들면 갈래를 처음부터 다시 고른다.
        bool _gunBranchStarted, _gunBranch;
        // 원작 Combat_Normal 지역 변수 HasTarget이다. 표적이 보이면 켜지고, 가려진 갈래가 forgetTime 동안 이어지면 꺼진다.
        bool _hasTarget;
        float _hasTargetForgetAt;
        // 피격 대시 순서 뒤 대기가 끝나는 시각이다. 0이면 대기 중이 아니다.
        float _gunDashWaitUntil;
        // 자동이 아닌 총이 다음에 방아쇠를 당길 수 있는 시각이다. 연사를 시작할 때 바로 당긴다.
        float _semiPressAt;
        // 에이전트 대신 이 적이 몸 방향을 정하는지다. 총기 교전 동안 켠다.
        bool _manualFacing;

        // 총기 적의 교전 한 프레임이다. 무기를 다 꺼내기 전에는 쏘지 않고, 대기나 연사가 시작되면 표적이 가려지거나
        // 사거리를 벗어나도 순환을 이어 간다. 연사 범위가 없는 총도 같은 순환으로 한 발씩 쏜다.
        // clear는 교전 중 거리와 각도를 보지 않은 차폐 결과이고, 이동 갈래 키(원작 HasObsticleToTarget)도 이 값으로 정한다.
        void TickGunCombat(bool clear, float sqrDistance, Vector3 delta)
        {
            float now = Time.time;
            bool attack = GunAttack(clear, sqrDistance);
            SetManualFacing(true);
            Face(delta);
            if ((attack || _shootAt > 0 || _burstUntil > 0) && WeaponDrawn) TickGunCycle(attack, now);
            bool stop = GunStopped();
            _agent.isStopped = stop;
            // 원작 동적 Selector처럼 피격 대시 순서가 이동 갈래보다 먼저다. 순서가 돌면 하던 이동을 지운다(StopMoving).
            switch (StepGunHurtDash(now))
            {
                case GunDashStep.Wait: return;
                case GunDashStep.Stop: _agent.ResetPath(); return;
                case GunDashStep.Dash: _agent.ResetPath(); StartDash(now); return;
            }
            TickCombatMove(now, clear, stop);
        }

        // 원작 Combat_Normal 64-78이다. 표적이 가려지지 않았거나 엄폐 사격 조건이면, 사거리 안일 때 지금 표적을 겨눠 쏜다.
        // 탄은 지금 표적 조준점으로 가고 벽 판정을 그대로 받는다. 공격자로 표적을 바꾸지 않는다.
        bool GunAttack(bool clear, float sqrDistance) => (clear || CoverShot) && sqrDistance <= _sqrAttackRange;

        // 원작 CheckHurt다. 마지막으로 받아들인 피해 한 건이 8초 안이고, 3 이상이고, 공격 캐릭터가 있을 때만 참이다.
        // 그 공격자가 지금 표적인지는 보지 않는다.
        bool CoverShot => Time.time - _lastHurt < _coverShotTime && _lastHurtDamage >= _coverShotDamage && _lastHurtFromCharacter;

        // 원작 Shoot.OnExecute와 OnUpdate의 StopMove다. shootCanMove가 거짓이면 쏘는 동안 멈춘다.
        // 연사 범위가 없는 총은 연사를 시작한 프레임에만 쏘는 중이라 그 프레임에만 멈춘다.
        bool GunStopped() => !_shootCanMove && _burstUntil > 0;

        // 원작 Shoot의 방아쇠다. 자동 총은 연사 동안 늘 당기고, 자동이 아닌 총은 연사를 시작할 때와 그 뒤 0.35초마다만 당긴다.
        bool PressTrigger(float now)
        {
            if (_weapon!.Automatic) return true;
            if (now < _semiPressAt) return false;
            _semiPressAt = now + _semiTriggerSpace;
            return true;
        }

        // 이동 순환의 결정을 에이전트에 옮긴다. 원작처럼 표본이나 경로를 얻지 못하면 도착한 것으로 보고 다음 대기로 넘어간다.
        void TickCombatMove(float now, bool visible, bool stop)
        {
            bool reached = PathEndReached(_agent.pathPending, _agent.hasPath, _agent.remainingDistance);
            bool near = WithinTraceStop(transform.position, _noticePosition);
            switch (StepGunMove(now, visible, stop, reached, near))
            {
                case GunMoveOrder.Stop:
                    _agent.ResetPath();
                    break;
                case GunMoveOrder.Trace:
                    if (!_agent.SetDestination(_noticePosition)) FailGunMove(now);
                    break;
                case GunMoveOrder.Move:
                    if (!TrySampleCombatPoint(_noticePosition, _combatAvoidRadius, _combatMoveRange, _combatMoveAngle, out Vector3 point)
                        || !_agent.SetDestination(point)) FailGunMove(now);
                    break;
            }
        }

        // 목적지를 주지 못한 이동이나 추적을 도착으로 끝낸다. 남은 경로(순찰이나 앞선 갈래)가 이어지지 않게 지운다.
        void FailGunMove(float now)
        {
            EndGunMove(now);
            _agent.ResetPath();
        }

        // 이동 순환 한 프레임의 결정이다. 에이전트를 읽거나 쓰지 않고 시각, 표적 가시성, 쏘는 중 멈춤, 경로 끝 도착,
        // 추적 멈춤 거리 안인지를 받아 EditMode 시험이 NavMesh 없이 돌린다.
        GunMoveOrder StepGunMove(float now, bool visible, bool stop, bool reached, bool near)
        {
            if (visible) _hasTarget = true;
            else if (_gunBranchStarted && _gunBranch && now >= _hasTargetForgetAt) _hasTarget = false;
            bool branch = !visible && _hasTarget;
            // 원작 동적 BinarySelector처럼 갈래 키가 바뀌면 하던 일을 끊고 새 갈래를 처음부터 한다. forgetTime도 그때부터 잰다.
            if (!_gunBranchStarted || branch != _gunBranch)
            {
                _gunBranchStarted = true;
                _gunBranch = branch;
                _hasTargetForgetAt = now + _forgetTime;
                return ChooseGunMove(now);
            }
            switch (_gunMovePhase)
            {
                case GunMovePhase.Move:
                    return stop || reached || now > _gunMoveUntil ? EndGunMove(now) : GunMoveOrder.None;
                case GunMovePhase.Trace:
                    // 원작 TraceTarget처럼 경로를 다시 잡는 프레임에는 경로 끝 도착을 보지 않는다.
                    if (stop || near || now > _gunMoveUntil) return EndGunMove(now);
                    if (now >= _gunTracePathAt)
                    {
                        _gunTracePathAt = now + _tracePathInterval;
                        return GunMoveOrder.Trace;
                    }
                    return reached ? EndGunMove(now) : GunMoveOrder.None;
                case GunMovePhase.PreTrace:
                    if (now < _gunMoveUntil) return GunMoveOrder.None;
                    _gunMovePhase = GunMovePhase.Trace;
                    _gunMoveUntil = now + _combatMoveOverTime;
                    _gunTracePathAt = now + _tracePathInterval;
                    return GunMoveOrder.Trace;
                default:
                    return now < _gunMoveUntil ? GunMoveOrder.None : ChooseGunMove(now);
            }
        }

        // 갈래의 다음 일을 고른다. 가려진 갈래는 그때마다 추적 확률을 뽑고, 그 밖은 늘 임의 지점으로 간다.
        // 추적 앞 대기는 서 있으므로 남은 경로를 지운다.
        GunMoveOrder ChooseGunMove(float now)
        {
            if (_gunBranch && _random!.NextDouble() < _traceChance)
            {
                _gunMovePhase = GunMovePhase.PreTrace;
                _gunMoveUntil = now + _preTraceWait;
                return GunMoveOrder.Stop;
            }
            _gunMovePhase = GunMovePhase.Move;
            _gunMoveUntil = now + _combatMoveOverTime;
            return GunMoveOrder.Move;
        }

        // 이동이나 추적을 끝낸다. 원작 OnStop처럼 경로를 지우고, 이동 뒤에는 combatMoveTimeRange, 추적 뒤에는 0.5초 기다린다.
        GunMoveOrder EndGunMove(float now)
        {
            bool trace = _gunMovePhase == GunMovePhase.Trace;
            _gunMovePhase = trace ? GunMovePhase.PostTrace : GunMovePhase.MoveWait;
            _gunMoveUntil = now + (trace ? _postTraceWait : RandomRange(_combatMoveTimeRange));
            return GunMoveOrder.Stop;
        }

        // 원작 Combat_Normal의 피격 대시 순서(CanDash, CheckHurt, !CheckReloading, StopMoving, Dash, Wait 0.2초)다.
        // 장전 중이면 순서가 !CheckReloading에서 멈춰 아무것도 하지 않는다. 대시 간격이 남았어도 멈추고 기다리며, 대기가 끝나면 이동 갈래를 처음부터 한다.
        // 방향은 원작 Dash의 random 모드처럼 수평 임의 방향이고, 대시 간격은 대시할 때마다 dashCoolTimeRange에서 새로 뽑는다.
        GunDashStep StepGunHurtDash(float now)
        {
            if (_gunDashWaitUntil > 0f)
            {
                if (now < _gunDashWaitUntil) return GunDashStep.Wait;
                _gunDashWaitUntil = 0f;
            }
            // 원작 Combat_Normal 피격 대시 순서의 !CheckReloading이다. 재장전 중에는 맞아도 구르지 않는다.
            if (!HurtDashWanted(now) || Reloading) return GunDashStep.None;
            _gunDashWaitUntil = now + _hurtDashWait;
            _gunBranchStarted = false;
            if (now - _lastDashAt < _dashSpace) return GunDashStep.Stop;
            _lastDashAt = now;
            _dashSpace = RandomRange(_dashCoolRange);
            double angle = _random!.NextDouble() * Math.PI * 2d;
            _dashDirection = new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
            return GunDashStep.Dash;
        }

        // 원작 MoveToRandomPos.RandomPoint다. 중심에서 자신 쪽 방향을 U(-각도/2, 각도/2)만큼 돌리고, 거리는 U(회피, 반경)에서 고르게 뽑는다.
        // u1은 각도, u2는 거리에 쓰는 0~1 난수다. 자신이 중심에 겹치면 원작은 임의 방향에서 돌리므로 같은 분포가 되도록 전 방향으로 돌린다.
        internal static Vector3 CombatPoint(Vector3 center, Vector3 self, float avoid, float radius, float angle, float u1, float u2)
        {
            Vector3 toward = self - center;
            toward.y = 0f;
            if (toward.sqrMagnitude < _centeredSqr)
            {
                toward = Vector3.forward;
                angle = _fullTurnAngle;
            }
            float turn = Mathf.Lerp(-.5f * angle, .5f * angle, u1);
            return center + Quaternion.Euler(0f, turn, 0f) * toward.normalized * Mathf.Lerp(avoid, radius, u2);
        }

        // 전투 이동 지점을 뽑아 이 에이전트 타입의 NavMesh 위로 옮긴다. 원작 A*는 가장 가까운 노드로 가므로 2 m 안의 NavMesh 점을 쓴다.
        // 난수는 원작처럼 각도 다음 거리 순으로 뽑는다.
        bool TrySampleCombatPoint(Vector3 center, float avoid, float radius, float angle, out Vector3 point)
        {
            float u1 = (float)_random!.NextDouble(), u2 = (float)_random.NextDouble();
            Vector3 target = CombatPoint(center, transform.position, avoid, radius, angle, u1, u2);
            bool found = NavMesh.SamplePosition(target, out NavMeshHit hit, _navSampleDistance, _navFilter);
            point = hit.position;
            return found;
        }

        // 원작 AI_PathControl의 경로 끝 도착이다. 경로 계산 중이 아니고, 경로가 없거나 남은 거리가 0.6 m 미만이면 도착이다.
        // 미완성 경로는 남은 거리가 무한대일 수 있어 경로가 있을 때만 거리를 본다.
        static bool PathEndReached(bool pending, bool hasPath, float remaining) => !pending && (!hasPath || remaining < _combatArriveDistance);

        // 원작 TraceTarget의 멈춤 판정이다. 3D 거리가 1 m 미만이면 도착이다.
        static bool WithinTraceStop(Vector3 position, Vector3 target) => Vector3.Distance(position, target) < _combatTraceStopDistance;

        // 교전이 끝나면 이동 순환과 피격 대시 대기를 처음으로 돌리고 몸 방향을 에이전트에 돌려준다. 다시 교전하면 바로 첫 갈래를 고른다.
        void LeaveGunCombat()
        {
            _gunBranchStarted = false;
            _hasTarget = false;
            _gunDashWaitUntil = 0f;
            SetManualFacing(false);
        }

        // 겨누며 움직일 때 에이전트 회전이 Face와 다투지 않도록 에이전트 회전을 끈다. 값이 바뀔 때만 쓴다.
        void SetManualFacing(bool manual)
        {
            if (_manualFacing == manual) return;
            _manualFacing = manual;
            _agent.updateRotation = !manual;
        }
    }
}
