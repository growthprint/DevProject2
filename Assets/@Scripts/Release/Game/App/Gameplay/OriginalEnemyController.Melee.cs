#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 AIController_Melee 템플릿의 전투 트리(Combat_Melee와 하위 트리 Combat_Attack_Melee), 원작 AICharacterController.Update의
    // 밤 반응 시간과 우두머리 따라가기, 원작 SearchEnemyAround의 야간 시야각이다(5-W2b). 근접 전투는 늑대처럼 그 템플릿을 쓰는 프리셋만 탄다.
    // 원작 순서: 알아채면 0.2초 뒤 전투를 시작한다. 공격을 원하지 않는 동안(원작 WantAttack 거짓)은 목표 둘레 combatMoveRange 안의 임의 지점으로 걷고,
    // 다쳤으면 옆으로 대시하며, shootTimeSpaceRange만큼 지나면 공격을 원한다. 공격을 원하면 목표 1.5 m 안까지 달려가고(8초 제한),
    // 0.1초 조준한 뒤 무기 사거리의 1.2배 안이면 예고 효과를 내고 0.2초 뒤 휘두른다. 0.25초 쉰 다음 다시 걷는다. 사거리 밖이면 다시 달려간다.
    public sealed partial class OriginalEnemyController
    {
        // 원작 프리셋의 AI 템플릿이 AIController_Melee인지와 combatMoveRange, combatMoveTimeRange, dashCoolTimeRange다.
        // 제작 도구(ComponentAuthoring.ConfigureCombat)가 쓰고, 기본값은 원작 CharacterRandomPreset 기본값이다.
        [SerializeField] bool _meleeAi;
        [SerializeField] float _combatMoveRange = 8f;
        [SerializeField] Vector2 _combatMoveTimeRange = new Vector2(1f, 3f);
        [SerializeField] Vector2 _dashCoolRange = new Vector2(2f, 4f);

        // 원작 Combat_Melee 트리의 값이다. 시작 대기, TraceTarget의 멈춤 거리·제한 시간·경로 재계산 간격, 공격 뒤 대기,
        // Combat_Attack_Melee의 사거리 배율(CheckTargetDistance shootRangeMultiplier), 놓치는 거리와 시간(CheckTargetDistance 24, Wait 7)이다.
        const float _meleeStartTime = .2f, _traceStopDistance = 1.5f, _traceOverTime = 8f, _tracePathInterval = .15f, _meleeRecoverTime = .25f;
        const float _meleeRangeFactor = 1.2f, _meleeLoseDistance = 24f, _meleeLoseTime = 7f;
        // 거리 판정은 제곱 거리로 비교하므로 멈춤 거리와 놓치는 거리의 제곱도 상수로 둔다.
        const float _sqrTraceStopDistance = _traceStopDistance * _traceStopDistance;
        const float _sqrMeleeLoseDistance = _meleeLoseDistance * _meleeLoseDistance;
        // 원작 CheckHurt 기본값(0.2초 안에 3 이상의 피해)이다. 대시 진행 시간, 속도 곡선, 효과음은 원작이 플레이어와 같아
        // 캐릭터 이동 공용 값을 읽는다.
        const float _dashHurtTime = .2f, _dashHurtDamage = 3f;
        // 원작 SetAim의 combatTurnSpeed 기본값(초당 도)이다. 총기 적의 교전도 이 회전을 쓴다.
        const float _combatTurnSpeed = 1200f;
        // 이보다 짧은 수평 방향은 바라보거나 대시할 방향이 없다고 본다.
        const float _minFacingSqr = .001f;

        // 원작 Combat_Melee의 진행 단계다. Start는 시작 대기, Wander는 WantAttack 거짓, Trace~Recover는 공격 순서다.
        enum MeleeStep { Start, Wander, Trace, Aim, Alert, Recover }
        MeleeStep _meleeStep;
        bool _meleeActive, _running;
        // 지금 단계가 끝나는 시각, 다음 전투 이동 시각, 다음 추적 경로 요청 시각이다.
        float _stepUntil, _nextMoveAt, _nextTracePath;
        // 원작 Dash의 마지막 대시 시각과 다음 대시까지의 간격, 진행 중인 대시의 시작 시각(음수면 대시 중이 아님)과 방향이다.
        float _lastDashAt = float.NegativeInfinity, _dashSpace, _dashStart = -1f;
        Vector3 _dashDirection;
        // 마지막으로 받아들인 피해 한 건의 실제 감소량이다. 원작 CheckHurt처럼 최대값이 아니다. Hurt가 쓴다.
        float _lastHurtDamage;
        // 원작 baseReactionTime과 updateValueTimer다. Prepare가 난이도를 곱한 반응 시간을 넣고, Update가 1초마다 밤 보정을 다시 정한다.
        float _baseReaction, _valueTimer = _valueUpdateInterval;
        // 원작 AICharacterController.group이다. 우두머리가 있는 그룹에서 스폰한 적만 가진다.
        OriginalEnemySpawnGroup? _leaderGroup;

        // 원작 CharacterRandomPreset.CreateCharacterAsync의 group.AddCharacterSpawned 자리다. 스폰 그룹이 우두머리 그룹에 넣을 때 부른다.
        internal void FollowGroup(OriginalEnemySpawnGroup group) => _leaderGroup = group;

        // 원작 AICharacterController.Update의 밤 반응 시간이다. 밤이고 지금 하위 씬이 실내가 아니면 기본 반응 시간에 밤 배율을 곱한다.
        // 시간대가 없는 씬은 낮으로 본다.
        void RefreshNightReaction()
        {
            OriginalTimeOfDay? time = _world!.TimeOfDay;
            _reaction = time != null && time.AtNightOutdoors ? _baseReaction * _nightReactionFactor : _baseReaction;
        }

        // 원작 SearchEnemyAround(affactByNightVisionAbility)의 시야각이다. 시간대의 야간 시야각 배율과 1 사이를 야간 시야 능력으로 섞는다.
        // 시간대가 없는 씬은 줄이지 않는다.
        float ViewAngle()
        {
            OriginalTimeOfDay? time = _world!.TimeOfDay;
            return time != null ? _angle * Mathf.Lerp(time.NightViewAngleFactor, 1f, _nightVision) : _angle;
        }

        // 원작 AICharacterController.Update의 우두머리 처리다. 우두머리가 목표를 가졌고 자신은 없으면 그 목표를 받고,
        // 우두머리가 없고 자신은 가졌으면 우두머리에게 준다. 순찰 중심으로 쓸 우두머리를 돌려준다. 우두머리 자신은 아무것도 나누지 않는다.
        OriginalEnemyController? ShareLeaderTarget()
        {
            if (_leaderGroup == null) return null;
            OriginalEnemyController? leader = _leaderGroup.Leader;
            if (leader == null || leader == this) return leader;
            if (leader._aware && !_aware) Adopt(leader);
            else if (!leader._aware && _aware) leader.Adopt(this);
            return leader;
        }

        // 다른 적의 목표를 받는다. 마지막 발견 시각은 둘 중 늦은 값이라, 서로 넘겨주기만 해서는 잊는 시간이 늘지 않는다.
        void Adopt(OriginalEnemyController source)
        {
            // 도발 전 비밀 상인은 무리가 목표를 넘겨도 교전하지 않는다.
            if (Passive) return;
            // 원작 searchedEnemy 나누기처럼 표적 참조(다른 팀 적이나 플레이어)도 넘긴다. 받은 표적이 같은 팀이면
            // 다음 Update의 DropInvalidTarget이 지운다. 발견 시각을 지금으로 올리지 않으려고 TakeTarget을 쓰지 않는다.
            // 받아서 교전을 시작하면 원작 FSM처럼 전투 진입 대기도 똑같이 거친다.
            if (!_aware) BeginCombat(Time.time);
            else if (_targetEnemy != source._targetEnemy) _pathDirty = true;
            _aware = true;
            _targetEnemy = source._targetEnemy;
            _noticePending = true;
            _lastSeen = Mathf.Max(_lastSeen, source._lastSeen);
        }

        // 교전이 끝나면 근접 전투를 처음 단계로 돌리고 걷기 속도로 돌아간다. 근접 AI가 아니면 아무것도 하지 않는다.
        void LeaveMeleeCombat()
        {
            if (!_meleeActive && !_running) return;
            _meleeActive = false;
            SetRunning(false);
        }

        // 원작 Combat_Melee 한 프레임이다. 단계 설명은 클래스 머리 주석을 따른다. 목표는 지금 표적(플레이어나 다른 팀 적)의 위치다.
        void TickMeleeCombat(float sqrDistance, Vector3 target, Vector3 delta)
        {
            float now = Time.time;
            if (!_meleeActive)
            {
                _meleeActive = true;
                _meleeStep = MeleeStep.Start;
                _stepUntil = now + _meleeStartTime;
            }
            switch (_meleeStep)
            {
                case MeleeStep.Start:
                    _agent.isStopped = true;
                    if (now >= _stepUntil) EnterWander(now);
                    return;
                case MeleeStep.Wander:
                    // 원작 병렬 노드처럼 같은 프레임에 피격 대시를 먼저 보고, 쉬는 시간이 지났으면 공격을 원한다.
                    if (TryStartDash(delta)) return;
                    if (now >= _stepUntil) { EnterTrace(now); return; }
                    if (now < _nextMoveAt) return;
                    // 원작 MoveToRandomPos(목표 중심, 반경 combatMoveRange)다. 순찰과 같은 임의 지점 규칙으로 NavMesh 위의 점만 쓴다.
                    _nextMoveAt = now + RandomRange(_combatMoveTimeRange);
                    if (TrySampleAround(target, _combatMoveRange, out Vector3 point)) { _agent.isStopped = false; _agent.SetDestination(point); }
                    return;
                case MeleeStep.Trace:
                    // 무기를 꺼내면 이동 배율이 바뀌므로 달리기 속도를 매 프레임 다시 넣는다.
                    _agent.speed = RunSpeed;
                    // 원작 TraceTarget의 멈춤 거리 1.5 m 안에 들면 조준 단계로 넘어간다.
                    if (sqrDistance < _sqrTraceStopDistance)
                    {
                        _agent.isStopped = true;
                        _meleeStep = MeleeStep.Aim;
                        _stepUntil = now + _meleeWaitTime;
                        return;
                    }
                    // 원작 TraceTarget은 제한 시간이 지나면 실패하고, 공격을 원하는 채로 추적을 다시 시작한다.
                    if (now >= _stepUntil) { EnterTrace(now); return; }
                    if (now < _nextTracePath) return;
                    _nextTracePath = now + _tracePathInterval;
                    _agent.isStopped = false;
                    // 추적 목표도 지금 표적이다.
                    _agent.SetDestination(target);
                    return;
                case MeleeStep.Aim:
                    Face(delta);
                    if (now < _stepUntil || !WeaponDrawn) return;
                    if (sqrDistance > SquaredRange(_weapon!.MeleeRange * _meleeRangeFactor)) { EnterTrace(now); return; }
                    Transform? hand = _appearance != null ? _appearance.RightHandSocket : null;
                    // 예고 효과 풀은 적 파사드 너머의 전투 시뮬레이션이 가진다.
                    if (hand != null) _world!.MeleeAlert(hand);
                    _meleeStep = MeleeStep.Alert;
                    _stepUntil = now + _meleeAlertTime;
                    return;
                case MeleeStep.Alert:
                    Face(delta);
                    if (now < _stepUntil) return;
                    // 원작 Attack처럼 공격 간격이 남았으면 휘두르기만 건너뛴다.
                    if (now >= _nextAttack) Attack();
                    _meleeStep = MeleeStep.Recover;
                    _stepUntil = now + _meleeRecoverTime;
                    return;
                default:
                    if (now >= _stepUntil) EnterWander(now);
                    return;
            }
        }

        // 공격을 원하지 않는 단계에 들어간다. 원작 Wait(shootTimeSpaceRange)는 난이도 배율을 곱한 휴식 범위다.
        void EnterWander(float now)
        {
            SetRunning(false);
            _meleeStep = MeleeStep.Wander;
            _stepUntil = now + RandomRange(_restRange);
            _nextMoveAt = now;
        }

        // 원작 SetRun(true)와 TraceTarget(멈춤 1.5 m, 8초)을 시작한다. 첫 경로는 바로 요청한다.
        void EnterTrace(float now)
        {
            SetRunning(true);
            _meleeStep = MeleeStep.Trace;
            _stepUntil = now + _traceOverTime;
            _nextTracePath = now;
        }

        void SetRunning(bool running)
        {
            _running = running;
            _agent.speed = running ? RunSpeed : WalkSpeed;
        }

        // 원작 SetAim처럼 목표를 바라본다. 회전 속도는 기본이 원작 combatTurnSpeed이고, 표적이 없는 경계는 patrolTurnSpeed를 넘긴다.
        void Face(Vector3 delta, float turnSpeed = _combatTurnSpeed)
        {
            delta.y = 0;
            if (delta.sqrMagnitude > _minFacingSqr)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(delta), Time.deltaTime * turnSpeed);
        }

        // 원작 CanDash와 CheckHurt다. 대시할 수 있고 0.2초 안에 3 이상 다쳤을 때만 참이다. 총기 AI와 근접 AI가 함께 쓴다.
        bool HurtDashWanted(float now) => _canDash && now - _lastHurt < _dashHurtTime && _lastHurtDamage >= _dashHurtDamage;

        // 피격 대시를 원하지 않거나 대시 간격이 남았으면 근접 AI는 피격 대시를 하지 않는다.
        bool HurtDashBlocked(float now) => !HurtDashWanted(now) || now - _lastDashAt < _dashSpace;

        // 원작 CA_Dash가 실행 중인지(CharacterMainControl.Dashing)다. 탄, 근접, 폭발 판정이 대시 중인 적을 건너뛴다.
        // 시간 조건은 거리 때문에 꺼진 동안 끝나지 못한 대시 시작 시각이 남아도 대시 중으로 보지 않게 한다.
        internal bool IsDashing => _dashStart >= 0f && Time.time - _dashStart < OriginalCharacterMotion._dashDuration;

        // 원작 DashCanControl 스탯이 0보다 큰지다. 참이면 원작처럼 대시 방향으로 몸을 돌리지 않고 Dashing 동작도 켜지 않는다.
        bool DashCanControl => _abilities!.GetCurrent(CharacterAttributes.DashCanControl) > 0f;

        // 원작 CharacterAnimationControl의 Dashing 값이다. 대시 중이고 대시 중 조작이 꺼진 적만 켠다.
        bool DashAnimationOn => IsDashing && !DashCanControl;

        // 원작 Combat_Melee의 CheckHurt와 Dash(목표 방향의 수직, 좌우 무작위)다. 대시할 수 있고, 0.2초 안에 3 이상 다쳤고,
        // 대시 간격이 지났을 때만 시작한다. 간격은 대시할 때마다 dashCoolTimeRange에서 새로 뽑는다.
        bool TryStartDash(Vector3 delta)
        {
            float now = Time.time;
            if (HurtDashBlocked(now)) return false;
            delta.y = 0f;
            if (delta.sqrMagnitude <= _minFacingSqr) return false;
            _lastDashAt = now;
            _dashSpace = RandomRange(_dashCoolRange);
            _dashDirection = Vector3.Cross(delta.normalized, Vector3.up) * (_random!.NextDouble() > .5 ? 1f : -1f);
            StartDash(now);
            return true;
        }

        // 원작 CA_Dash.OnStart다. 정해 둔 대시 방향으로 대시를 시작한다. 대시 중 조작이 꺼진 적은 ForceTurnTo처럼 몸을 대시 방향으로 바로 돌린다.
        void StartDash(float now)
        {
            _dashStart = now;
            _agent.isStopped = true;
            if (!DashCanControl) transform.rotation = Quaternion.LookRotation(_dashDirection);
            _animation!.SetDashing(DashAnimationOn);
            // 원작 CA_Dash 효과음은 플레이어와 같은 경로라 캐릭터 이동 공용 값을 쓴다. 오디오 관리자는 월드 컨텍스트가 가진 앱 수명 객체다.
            _world!.Context.Audio.PlayOneShot(OriginalCharacterMotion._dashSound, transform.position);
        }

        // 진행 중인 대시를 한 프레임 옮긴다. 대시 중이면 true다. 속도는 DashSpeed 현재값에 원작 대시 곡선을 곱하고, NavMesh 안에서만 움직인다.
        // 진행 시간과 곡선은 원작이 플레이어와 같아 캐릭터 이동 공용 값 하나를 함께 읽는다. Dashing 동작 값은 Animator가 늦게 준비되어도
        // 켜지도록 대시 프레임마다 보내고, 끝나는 프레임에 끈다.
        bool TickDash()
        {
            if (_dashStart < 0f) return false;
            bool dashing = IsDashing;
            _animation!.SetDashing(dashing && !DashCanControl);
            if (!dashing) { _dashStart = -1f; _agent.isStopped = false; return false; }
            float elapsed = Time.time - _dashStart;
            float duration = OriginalCharacterMotion._dashDuration;
            float speed = _abilities!.GetCurrent(CharacterAttributes.DashSpeed) * OriginalCharacterMotion._dashCurve.Evaluate(elapsed / duration);
            _agent.Move(_dashDirection * (speed * Time.deltaTime));
            return true;
        }
    }
}
