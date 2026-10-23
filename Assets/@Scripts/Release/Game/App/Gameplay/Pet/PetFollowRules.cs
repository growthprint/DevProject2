#nullable enable
using UnityEngine;

namespace Game.App
{
    // 원작 PetAIController 행동 트리에서 루트 노드 0이 닿는 가지만 옮긴 펫의 이번 프레임 행동이다.
    // 원작 선택 노드가 모두 dynamic이라 매 프레임 다시 고른다.
    internal enum PetAction
    {
        // 주인 4m 이내: SetRun(false), SetAim(Master), StopMoving.
        Stop,
        // 주인 4m 초과 8m 이내: SetAim(null), TraceTarget(정지 거리 2m). 이 가지는 달리기 입력을 바꾸지 않는다.
        Walk,
        // 주인 8m 초과: SetRun(true), TraceTarget(정지 거리 4m). 달리던 펫은 4~8m 가지에서도 계속 달린다.
        Run,
        // 대기 중 대기 지점 0.5m 초과: SetRun(false)로 걸어서 대기 지점으로 돌아간다.
        ReturnToStandby,
        // 대기 중 대기 지점 0.5m 이내: 대기 지점 0.1m 안을 맴돌며 주인을 본다.
        IdleAtStandby,
        // 원작 PetAI.Update: 대기가 아니고 주인과 40m를 넘게 떨어지면 주인 곁으로 순간 이동한다.
        Teleport,
    }

    // 펫 규칙 한 번의 결과다. 달리기와 가속 상태는 원작처럼 다음 프레임의 입력으로 다시 넘긴다.
    internal readonly struct PetDecision
    {
        internal PetDecision(PetAction action, float stopDistance, bool boosted)
        {
            Action = action;
            StopDistance = stopDistance;
            Boosted = boosted;
        }

        internal PetAction Action { get; }
        // 경로 이동을 끝낼 남은 거리다. 멈춤과 순간 이동은 경로를 쓰지 않아 0이다.
        internal float StopDistance { get; }
        // 원작 PetAI.SyncSpeed의 +2 가속 상태다.
        internal bool Boosted { get; }
        // 원작 SetRun 입력이다. 달리기 속도와 이동 애니메이션의 달리기 식을 고른다.
        internal bool Running => Action == PetAction.Run;
        // 원작 SetAim(Master)가 남은 가지다. 이때는 이동 방향 대신 주인을 본다.
        internal bool FacesMaster => Action == PetAction.Stop || Action == PetAction.IdleAtStandby;
        // 주인을 향해 경로를 잡는 TraceTarget 가지인지다. 대기 가지는 대기 지점을 향한다.
        internal bool FollowsMaster => Action == PetAction.Walk || Action == PetAction.Run;
    }

    // 원작 PetAI와 PetAIController 행동 트리의 거리 판정을 순수 함수로 옮긴다. 위치와 NavMesh는 PetCharacter가 다룬다.
    internal static class PetFollowRules
    {
        // 원작 PetAI.SyncSpeed: distanceToMaster > 10f면 주인 속도에 +2f를 더하고, < 8f면 주인 속도로 되돌린다.
        // 그 사이에서는 직전 상태를 유지한다.
        internal const float _boostStartDistance = 10f;
        internal const float _boostEndDistance = 8f;
        internal const float _speedBoost = 2f;
        // 원작 PetAI.Update: !standBy && distanceToMaster > 40f면 SetPosition으로 순간 이동한다.
        internal const float _teleportDistance = 40f;
        // 원작 행동 트리 노드 4 CheckTargetDistance(StandByPos, distance 0.5)다. 원작 비교는 <=다.
        internal const float _standByRadius = .5f;
        // 원작 노드 17 CheckTargetDistance(Master, 4)와 노드 22 CheckTargetDistance(Master, 8)다. 둘 다 <= 비교다.
        internal const float _stopDistance = 4f;
        internal const float _runDistance = 8f;
        // 원작 노드 25와 29 TraceTarget.stopDistance다.
        internal const float _walkTraceStopDistance = 2f;
        internal const float _runTraceStopDistance = 4f;
        // 원작 PetAIController의 AI_PathControl.stopDistance다. 대기 지점으로 가는 MoveToRandomPos가 이 거리에서 멈춘다.
        internal const float _pathStopDistance = .2f;
        // 원작 PetAI.SetPosition 호출의 주인 기준 오프셋이다. 주인 방향이 아니라 월드 앞쪽 0.4m, 위 0.5m다.
        static readonly Vector3 _teleportOffset = Vector3.forward * .4f + Vector3.up * .5f;

        // 거리는 원작처럼 높이를 포함한 Vector3.Distance다. running은 직전 결정의 달리기 입력, boosted는 직전 가속 상태다.
        // 원작 PetAI.Update는 가속을 먼저 맞춘 뒤 순간 이동을 판정하고, 행동 트리는 대기 여부부터 고른다.
        internal static PetDecision Decide(float masterDistance, bool standBy, float standByDistance, bool boosted, bool running)
        {
            bool boost = Boosted(masterDistance, boosted);
            if (!standBy && masterDistance > _teleportDistance) return new PetDecision(PetAction.Teleport, 0f, boost);
            if (standBy)
                return new PetDecision(standByDistance <= _standByRadius ? PetAction.IdleAtStandby : PetAction.ReturnToStandby,
                    _pathStopDistance, boost);
            if (masterDistance <= _stopDistance) return new PetDecision(PetAction.Stop, 0f, boost);
            // 원작 4~8m 가지는 SetRun을 부르지 않아, 8m 밖에서 켠 달리기가 4m 안 멈춤 가지에 닿을 때까지 남는다.
            if (masterDistance <= _runDistance)
                return new PetDecision(running ? PetAction.Run : PetAction.Walk, _walkTraceStopDistance, boost);
            return new PetDecision(PetAction.Run, _runTraceStopDistance, boost);
        }

        // 원작 PetAI.SyncSpeed의 비교 순서 그대로다.
        internal static bool Boosted(float masterDistance, bool boosted)
        {
            if (masterDistance > _boostStartDistance) return true;
            if (masterDistance < _boostEndDistance) return false;
            return boosted;
        }

        // 원작 SyncSpeed가 펫 WalkSpeed, RunSpeed 기본값에 넣는 주인 CharacterWalkSpeed, CharacterRunSpeed다.
        internal static float Speed(float masterSpeed, bool boosted) => boosted ? masterSpeed + _speedBoost : masterSpeed;

        // 원작 40m 순간 이동과 주인 SetPosition을 따라가는 순간 이동이 같은 지점을 쓴다.
        internal static Vector3 TeleportPoint(Vector3 masterPosition) => masterPosition + _teleportOffset;
    }
}
