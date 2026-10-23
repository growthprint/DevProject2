#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 Alert_Normal 경계 트리와 OnSound, OnHurt가 쓰는 noticed다. 소리나 피격으로 알아챘지만 표적이 없는 적이 순찰 대신 이 트리를 돈다.
    // 들어오면 걷기로 바꾸고 0.1초 선 뒤 세 갈래가 함께 돈다. 늘 도는 탐색(×1.25, 시야각, 야간 배율 없음)은 배치 탐색이 맡아 찾으면 교전한다.
    // 새로 알아채면 알아챈 위치를 0.1초 보고 조사를 처음부터 다시 한다. 조사는 추적 확률로 한 번 갈려 알아챈 위치 둘레를 돌거나 그쪽을 겨눈 채
    // 오가고, forgetTime이 지나면 순찰로 돌아간다. 원작의 "?" 팝업과 음성은 제품에 없다.
    public sealed partial class OriginalEnemyController
    {
        // 원작 Alert_Normal 값이다. 입장 대기, 다시 알아챈 뒤 보는 시간, noticed와 CheckHurt(0.1초 안 2 이상)의 시간 창이다.
        const float _alertEnterTime = .1f, _alertLookTime = .1f, _alertNoticeTime = .4f, _alertHurtTime = .1f, _alertHurtDamage = 2f;
        // 원작 Alert_Normal 조사 갈래의 MoveToRandomPos 값이다. 추적 갈래는 회피 0.2 m, 반경 3 m, 8초 이동 뒤 3초 대기,
        // 겨누는 갈래는 회피 1 m, 반경 4 m, 3초 이동 뒤 1~2초 대기다. 각도는 원작 노드 값을 확인하지 못해 전 방향으로 둔다.
        const float _alertTraceAvoid = .2f, _alertTraceRadius = 3f, _alertTraceMoveTime = 8f, _alertTraceWait = 3f;
        const float _alertAimAvoid = 1f, _alertAimRadius = 4f, _alertAimMoveTime = 3f, _alertMoveAngle = 360f;
        static readonly Vector2 _alertAimWaitRange = new Vector2(1f, 2f);
        // 원작 AICharacterController.patrolTurnSpeed 기본값(초당 도)이다. 표적이 없는 동안은 이 속도로 돈다.
        const float _patrolTurnSpeed = 200f;

        // 경계 단계다. Start는 다음 틱의 입장 처리, Enter는 입장 대기, Look은 알아챈 위치 보기, Move와 Wait는 조사 이동과 대기다.
        internal enum AlertStep { Start, Enter, Look, Move, Wait }
        // 경계 한 틱이 에이전트에 시킬 일이다. Begin은 입장 처리, Stop은 경로 지우기, Move는 새 조사 지점, Leave는 순찰로 돌아가기다.
        internal enum AlertOrder { None, Begin, Stop, Move, Leave }

        // 경계 트리를 도는지다. 교전을 시작하면 BeginCombat이 끈다.
        bool _alerted;
        // 원작 noticed와 noticeTimeMarker다. 소리와 피격이 켜고, 경계가 다시 알아챔으로 읽은 뒤 끈다.
        bool _noticed;
        float _noticeTime;
        AlertStep _alertStep;
        // 지금 단계가 끝나는 시각(대기는 끝 시각, 이동은 제한 시각)과 조사가 forgetTime에 이르는 시각이다.
        float _alertStepUntil, _alertForgetAt;
        // 이번 조사의 갈래다. 참이면 알아챈 위치 둘레를 돌고, 거짓이면 그쪽을 겨눈 채 오간다.
        bool _alertTrace;

        // 원작 OnSound와 OnHurt가 noticed, noticeTimeMarker, noticeFromPos를 쓰는 자리다. 표적과 잊는 시간은 바꾸지 않는다.
        // 교전 중이 아니면 원작 순찰 트리처럼 경계로 넘어가고, 이미 경계 중이면 경계가 다시 알아챔으로 받는다.
        void Notice(Vector3 from)
        {
            _noticed = true;
            _noticeTime = Time.time;
            _noticePosition = from;
            if (_aware || _alerted) return;
            _alerted = true;
            _alertStep = AlertStep.Start;
        }

        // 경계 한 프레임이다. 단계 결정은 StepAlert가 하고, 여기서는 그 결과를 에이전트에 옮긴 뒤 몸 방향을 정한다.
        // 원작처럼 조사 지점의 표본이나 경로를 얻지 못하면 도착한 것으로 보고 대기로 넘어간다. 겨누는 동안은 에이전트 대신 몸을 돌린다.
        void TickAlert(Vector3 position)
        {
            float now = Time.time;
            switch (StepAlert(now, PathEndReached(_agent.pathPending, _agent.hasPath, _agent.remainingDistance)))
            {
                case AlertOrder.Begin:
                    // 원작 경계 입장의 SetRun(false)다. 순찰 이동을 끊고 앞 교전의 총기 이동 순환과 수동 회전도 비운다.
                    LeaveGunCombat();
                    SetRunning(false);
                    _agent.ResetPath();
                    break;
                case AlertOrder.Stop:
                    _agent.ResetPath();
                    break;
                case AlertOrder.Move:
                {
                    float avoid = _alertTrace ? _alertTraceAvoid : _alertAimAvoid, radius = _alertTrace ? _alertTraceRadius : _alertAimRadius;
                    if (!TrySampleCombatPoint(_noticePosition, avoid, radius, _alertMoveAngle, out Vector3 point) || !_agent.SetDestination(point))
                    {
                        EndAlertMove(now);
                        _agent.ResetPath();
                    }
                    break;
                }
                case AlertOrder.Leave:
                    _agent.ResetPath();
                    SetManualFacing(false);
                    return;
            }
            _agent.isStopped = _alertStep != AlertStep.Move;
            bool aim = _alertStep == AlertStep.Look || (!_alertTrace && (_alertStep == AlertStep.Move || _alertStep == AlertStep.Wait));
            SetManualFacing(aim);
            if (aim) Face(_noticePosition - position, _patrolTurnSpeed);
        }

        // 경계 단계 결정이다. 에이전트를 읽거나 쓰지 않고 시각과 경로 끝 도착만 받아 EditMode 시험이 NavMesh 없이 돌린다.
        // forgetTime은 조사를 시작할 때부터 재고, 다시 알아채 조사를 처음부터 하면 새로 잰다.
        AlertOrder StepAlert(float now, bool reached)
        {
            switch (_alertStep)
            {
                case AlertStep.Start:
                    _alertStep = AlertStep.Enter;
                    _alertStepUntil = now + _alertEnterTime;
                    _alertForgetAt = float.PositiveInfinity;
                    return AlertOrder.Begin;
                case AlertStep.Enter:
                    if (now < _alertStepUntil) return AlertOrder.None;
                    return NoticedAgain(now) ? LookAtNotice(now) : StartInvestigation(now);
            }
            if (now >= _alertForgetAt)
            {
                _alerted = false;
                return AlertOrder.Leave;
            }
            switch (_alertStep)
            {
                case AlertStep.Look:
                    return now < _alertStepUntil ? AlertOrder.None : StartInvestigation(now);
                case AlertStep.Move:
                    if (NoticedAgain(now)) return LookAtNotice(now);
                    return reached || now > _alertStepUntil ? EndAlertMove(now) : AlertOrder.None;
                default:
                    if (NoticedAgain(now)) return LookAtNotice(now);
                    return now < _alertStepUntil ? AlertOrder.None : NextAlertMove(now);
            }
        }

        // 원작 경계의 두 번째 갈래 조건이다. 0.1초 안에 공격자가 준 2 이상의 피해나 0.4초 안의 noticed면 참이고, noticed를 끈다.
        bool NoticedAgain(float now)
        {
            bool hurt = now - _lastHurt < _alertHurtTime && _lastHurtDamage >= _alertHurtDamage && _lastHurtFromCharacter;
            if (!hurt && !(_noticed && now - _noticeTime < _alertNoticeTime)) return false;
            _noticed = false;
            return true;
        }

        // 하던 조사 이동을 멈추고 알아챈 위치를 0.1초 본다. 그 뒤 조사를 처음부터 한다.
        AlertOrder LookAtNotice(float now)
        {
            _alertStep = AlertStep.Look;
            _alertStepUntil = now + _alertLookTime;
            return AlertOrder.Stop;
        }

        // 원작 traceTargetChance로 조사 갈래를 한 번 고르고 forgetTime을 새로 잰 뒤 첫 이동을 한다.
        AlertOrder StartInvestigation(float now)
        {
            _alertTrace = _random!.NextDouble() < _traceChance;
            _alertForgetAt = now + _forgetTime;
            return NextAlertMove(now);
        }

        // 갈래의 다음 조사 이동을 시작한다. 제한 시간은 추적 갈래 8초, 겨누는 갈래 3초다.
        AlertOrder NextAlertMove(float now)
        {
            _alertStep = AlertStep.Move;
            _alertStepUntil = now + (_alertTrace ? _alertTraceMoveTime : _alertAimMoveTime);
            return AlertOrder.Move;
        }

        // 조사 이동을 끝내고 갈래의 대기로 넘어간다. 원작 OnStop처럼 남은 경로를 지운다.
        AlertOrder EndAlertMove(float now)
        {
            _alertStep = AlertStep.Wait;
            _alertStepUntil = now + (_alertTrace ? _alertTraceWait : RandomRange(_alertAimWaitRange));
            return AlertOrder.Stop;
        }
    }
}
