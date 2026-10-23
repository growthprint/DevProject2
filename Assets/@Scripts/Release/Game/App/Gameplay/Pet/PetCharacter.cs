#nullable enable annotations
using System;
using Game.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Game.App
{
    // 원작 LevelManager.petCharacter와 PetAI다. 무적이고 체력바가 없어 GAS와 피해 판정 없이 주인 모터만 따라간다.
    // SceneContext가 만들고 씬 컨트롤러가 묶고 풀며, 대기는 PetHouse가 정한다. 게이트가 참인 동안에만 움직인다.
    [DisallowMultipleComponent]
    public sealed class PetCharacter : MonoBehaviour
    {
        // 프리팹에서 꺼 둔 에이전트다. NavMesh를 찾은 뒤에만 켜서 NavMesh 밖에 에이전트를 만들지 않는다.
        [SerializeField] NavMeshAgent _agent = null!;
        // 원작 AC_Dog 복사본을 쓰는 Animator다. 원작 IdleMove 블렌드는 MoveSpeed 하나만 읽는다.
        [SerializeField] Animator _animator = null!;
        // 원작 characterModel이다. 커스터마이징 동안 이 객체만 끈다.
        [SerializeField] GameObject _model = null!;

        // 원작 SetPosition은 바닥 제약으로 땅에 붙는다. 여기서는 배치와 순간 이동 지점을 이 반경 안의 NavMesh 위로 옮긴다.
        const float _sampleDistance = 2f;
        // 적과 같은 경로 갱신 제한이다. 바뀐 행동은 바로, 그 뒤에는 0.2초마다 목표가 0.5m 넘게 움직였을 때만 다시 찾는다.
        const float _repathInterval = .2f;
        const float _repathDistanceSqr = .25f;
        // 원작 Movement의 movingInputThreshold다. 이보다 느리면 이동 방향을 바꾸지 않고 이동 애니메이션은 걷기 식을 쓴다.
        const float _moveThreshold = .02f;
        // 원작 Movement.UpdateAiming은 조준점이 0.6m보다 멀 때만 그쪽을 본다.
        const float _aimMinDistance = .6f;
        // 원작 PetPreset_NormalPet.patrolTurnSpeed다. AI가 TurnSpeed 기본값으로 넣고 Moveability 1을 곱한다.
        const float _turnSpeed = 250f;

        // Bind가 넘긴 주인 모터와 게이트다. 묶이기 전이나 Release 뒤에는 비어 Update가 아무것도 하지 않는다.
        OriginalPlayerMotor? _master;
        Func<bool>? _active;
        // 순간 이동 알림을 구독한 모터다. 모터가 먼저 파괴돼도 C# 이벤트가 처리기를 들고 있어 같은 참조로 푼다.
        OriginalPlayerMotor? _teleportSource;
        // 원작 PetAI.standBy와 standByPos다. PetHouse가 SetStandBy로 바꾼다.
        bool _standBy;
        Vector3 _standByPosition;
        // 원작 SyncSpeed의 가속 상태와 SetRun 입력이다. 원작처럼 프레임을 넘겨 유지한다.
        bool _boosted, _running;
        // 게이트가 열린 마지막 프레임에 맞춘 펫 걷기, 달리기 속도다. 게이트가 닫힌 동안 이동 애니메이션이 이 값을 쓴다.
        // 처음에는 0이라 MoveAnimationValue가 0을 돌려준다.
        float _walkSpeed, _runSpeed;
        // 에이전트가 꺼진 동안 받은 배치다. 게이트가 열린 Update가 NavMesh를 찾아 에이전트를 켠다.
        bool _placePending;
        string _pendingKey = "";
        // 행동이나 목표가 바뀌어 다음 Update에서 바로 경로를 찾아야 하는지다.
        bool _pathDirty = true;
        float _nextPathTime;
        Vector3 _lastDestination;
        // 같은 원인의 NavMesh 탐색 실패는 펫마다 한 번만 기록한다.
        readonly Log.Once _reported = new Log.Once();

        // 씬 컨트롤러가 레벨 초기화에서 한 번 부른다. 원작 PetAI.SetMaster처럼 주인 순간 이동을 따라가되,
        // 원작은 기지 레벨에서 따라가지 않으므로 호출자가 followTeleports로 정한다. 회전은 원작 회전 속도로 직접 돌린다.
        internal void Bind(OriginalPlayerMotor master, Func<bool> active, bool followTeleports)
        {
            _master = master;
            _active = active;
            _agent.updateRotation = false;
            _pathDirty = true;
            if (!followTeleports) return;
            master.Teleported += OnMasterTeleported;
            _teleportSource = master;
        }

        // 씬 컨트롤러가 주인 모터를 풀기 전에 부른다. 구독을 풀고 에이전트를 꺼 다시 움직이지 않는다.
        // 여러 번 불러도 되고, OnDestroy 백업 경로에서 먼저 파괴된 에이전트는 Unity null 비교로 건너뛴다.
        internal void Release()
        {
            OriginalPlayerMotor? source = _teleportSource;
            _teleportSource = null;
            _master = null;
            _active = null;
            if (!ReferenceEquals(source, null)) source.Teleported -= OnMasterTeleported;
            if (_agent != null) _agent.enabled = false;
        }

        // 원작 PetAI.SetStandBy다. 대기 목표가 바뀌므로 다음 Update에서 경로를 다시 찾는다.
        internal void SetStandBy(bool standBy, Vector3 position)
        {
            _standBy = standBy;
            _standByPosition = position;
            _pathDirty = true;
        }

        // 원작 CharacterMainControl.SetPosition에 해당하는 배치다. 레벨 초기화와 PetHouse가 부른다.
        internal void PlaceAt(Vector3 position) => MoveTo(position, "place");

        // 원작 Interact_CustomFace가 커스터마이징 동안 펫 모델을 끄고 켜는 것과 같다.
        internal void SetModelShown(bool shown) => _model.SetActive(shown);

        // 원작 OnMainCharacterSetPosition이다. 주인이 옮겨 간 지점의 월드 앞 0.4m, 위 0.5m로 옮긴다.
        void OnMasterTeleported(Vector3 position) => MoveTo(PetFollowRules.TeleportPoint(position), "follow");

        // 에이전트가 켜져 있으면 2m 안의 NavMesh로 바로 Warp한다. 꺼져 있으면 위치만 옮기고 게이트가 열린 Update에 맡긴다.
        void MoveTo(Vector3 target, string key)
        {
            _pathDirty = true;
            if (!_agent.enabled)
            {
                transform.position = target;
                _placePending = true;
                _pendingKey = key;
                return;
            }
            if (TrySample(target, key, out Vector3 point)) _agent.Warp(point);
        }

        // 주인 레벨의 NavMesh가 등록된 뒤 게이트가 열리므로, 꺼진 동안 받은 배치를 여기서 마친다. 못 찾으면 다음 프레임에 다시 찾는다.
        bool TryFinishPlacement()
        {
            if (!TrySample(transform.position, _pendingKey, out Vector3 point)) return false;
            transform.position = point;
            _agent.enabled = true;
            _agent.Warp(point);
            _placePending = false;
            _pathDirty = true;
            return true;
        }

        // 이 에이전트 종류의 NavMesh에서만 찾는다. 실패는 매 프레임 다시 올 수 있어 원인 키마다 한 번만 기록한다.
        bool TrySample(Vector3 target, string key, out Vector3 point)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = NavMesh.AllAreas };
            if (NavMesh.SamplePosition(target, out NavMeshHit hit, _sampleDistance, filter))
            {
                point = hit.position;
                return true;
            }
            point = target;
            if (_reported.First(key)) Log.Instance.Error("펫을 둘 NavMesh가 2m 안에 없습니다(" + key + "): " + target, this);
            return false;
        }

        // 원작 PetAI.Update와 행동 트리를 한 프레임씩 진행한다. 게이트가 거짓이면 적처럼 남은 경로를 멈추고,
        // 이동 애니메이션만 실제 속도로 맞춘다. 그동안은 주인 GAS 수치를 읽지 않고 마지막에 맞춘 속도를 쓴다.
        void Update()
        {
            OriginalPlayerMotor? master = _master;
            Func<bool>? active = _active;
            if (active == null || master == null) return;
            if (!active())
            {
                _pathDirty = true;
                if (_agent.isOnNavMesh) _agent.isStopped = true;
                Animate(false);
                return;
            }
            if (_placePending && !TryFinishPlacement()) return;
            if (!_agent.isOnNavMesh) return;
            Vector3 position = transform.position;
            Vector3 masterPosition = master.transform.position;
            PetDecision decision = PetFollowRules.Decide(Vector3.Distance(position, masterPosition), _standBy,
                Vector3.Distance(position, _standByPosition), _boosted, _running);
            _boosted = decision.Boosted;
            _running = decision.Running;
            if (decision.Action == PetAction.Teleport)
            {
                MoveTo(PetFollowRules.TeleportPoint(masterPosition), "teleport");
                return;
            }
            // 원작 SyncSpeed처럼 매 프레임 주인의 지금 걷기, 달리기 속도에 가속을 더해 펫 속도로 쓴다.
            _walkSpeed = PetFollowRules.Speed(master.CharacterWalkSpeed, decision.Boosted);
            _runSpeed = PetFollowRules.Speed(master.CharacterRunSpeed, decision.Boosted);
            Steer(decision, masterPosition);
            Turn(decision.FacesMaster, masterPosition);
            Animate(decision.Running);
        }

        // 원작 StopMoving은 경로를 버리고, TraceTarget과 MoveToRandomPos는 주인이나 대기 지점으로 경로를 잡는다.
        // 원작 경로 이동은 끝 0.6m 전까지 감속하지 않으므로 주인을 쫓을 때는 자동 감속을 끄고, 대기 지점으로 갈 때만 켠다.
        void Steer(PetDecision decision, Vector3 masterPosition)
        {
            if (decision.Action == PetAction.Stop)
            {
                _agent.isStopped = true;
                _pathDirty = true;
                return;
            }
            _agent.speed = decision.Running ? _runSpeed : _walkSpeed;
            _agent.stoppingDistance = decision.StopDistance;
            bool follows = decision.FollowsMaster;
            Vector3 target = follows ? masterPosition : _standByPosition;
            // 경로를 다시 찾는 조건은 적과 같은 갱신 제한을 따른다.
            if (NeedsRepath(follows, target))
            {
                _pathDirty = false;
                _nextPathTime = Time.time + _repathInterval;
                _lastDestination = target;
                _agent.autoBraking = !follows;
                _agent.SetDestination(target);
            }
            _agent.isStopped = false;
        }

        // 행동이나 목표가 바뀌었으면 바로 참이다. 주인을 쫓는 동안에는 0.2초가 지났고 주인이 0.5m 넘게 움직였을 때만 참이다.
        bool NeedsRepath(bool follows, Vector3 target) =>
            _pathDirty || (follows && Time.time >= _nextPathTime && (target - _lastDestination).sqrMagnitude > _repathDistanceSqr);

        // 원작 Movement.UpdateRotation처럼 초당 250도로 돈다. 주인을 보는 가지는 주인 쪽을, 나머지는 실제 이동 방향을 본다.
        void Turn(bool facesMaster, Vector3 masterPosition)
        {
            Vector3 direction = facesMaster ? masterPosition - transform.position : _agent.velocity;
            direction.y = 0f;
            float minimum = facesMaster ? _aimMinDistance : _moveThreshold;
            if (direction.sqrMagnitude <= minimum * minimum) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction, Vector3.up),
                _turnSpeed * Time.deltaTime);
        }

        // 원작 GetMoveAnimationValue다. 원작 PetAI가 펫 기본 걷기 속도를 주인 속도로 덮어 기본 걷기와 지금 걷기가 같으므로
        // 비율은 1이다. 커스터마이징으로 모델이 꺼진 동안은 Animator가 꺼져 있어 보내지 않는다.
        // 원작 AC_Dog도 같은 MoveSpeed 파라미터를 쓰므로 공용 해시를 읽는다.
        void Animate(bool running)
        {
            if (!_animator.isActiveAndEnabled) return;
            float speed = _agent.isOnNavMesh ? _agent.velocity.magnitude : 0f;
            _animator.SetFloat(OriginalAnimatorParameters._moveSpeed,
                OriginalCharacter.MoveAnimationValue(speed, running && speed > _moveThreshold, _walkSpeed, _runSpeed, _walkSpeed));
        }

        // 씬 정리를 거치지 않고 파괴되는 경우의 백업이다. 주인 모터 구독을 같은 참조로 푼다.
        void OnDestroy() => Release();
    }
}
