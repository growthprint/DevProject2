#nullable enable
namespace Game.GAS
{
    // 어빌리티 시작 결과다. 규칙에 따른 거절은 결과 값으로만 알리고 로그를 남기지 않는다.
    public enum AbilityActivationResult
    {
        Activated,
        // 시스템이 해제됐거나 작업 중첩 한도를 넘었다.
        Invalid,
        NotGranted,
        AlreadyActive,
        // 시작 태그 조건을 만족하지 않거나 다른 어빌리티가 막고 있다.
        BlockedByTags,
        OnCooldown,
        CannotAffordCost,
        // 어빌리티 자신의 CanActivate가 거절했다.
        Rejected
    }

    // 주인 하나에 묶인 어빌리티 실행 인스턴스다. 시작, 진행, 사건, 종료 콜백은 모두 동기로 불린다.
    // 하위 클래스는 OnActivate에서 비용을 커밋하고, 할 일이 끝나면 EndAbility를 부른다.
    public abstract class GameplayAbility
    {
        protected GameplayAbility(GameplayAbilityDefinition definition, AbilitySystem system)
        {
            Definition = definition;
            AbilitySystem = system;
        }

        public GameplayAbilityDefinition Definition { get; }
        public bool IsActive { get; internal set; }
        // UE bIsAbilityEnding처럼 OnEnd가 도는 동안만 참이다. 그동안 들어온 종료, 취소, 회수는 이미 진행 중인 종료로 본다.
        internal bool IsEnding { get; set; }
        protected AbilitySystem AbilitySystem { get; }
        // 어빌리티 목록을 도는 중에 회수되면 표시만 하고, 순회가 끝난 뒤 시스템이 목록에서 뺀다.
        internal bool PendingRemove { get; set; }
        // 순회 중 회수 표시된 어빌리티는 실행 중이어도 진행, 사건, 취소를 받지 않는다.
        internal bool IsLive => IsActive && !PendingRemove;

        // 취소 순회가 끝낼 대상인지 본다. force면 CanBeCanceled를 묻지 않는다(아바타 교체, 해제).
        internal bool AcceptsCancel(bool force) => IsLive && (force || CanBeCanceled);

        // 태그, 쿨다운, 비용 검사를 통과한 뒤 마지막으로 불린다. 사거리 같은 어빌리티 고유 조건을 둔다.
        protected internal virtual bool CanActivate(in GameplayEventData trigger) => true;

        protected internal abstract void OnActivate(in GameplayEventData trigger);

        // 실행 중이면 AbilitySystem.Tick(기본 시계)마다 불린다.
        protected internal virtual void OnTick(float deltaTime)
        {
        }

        // 실행 중에 HandleGameplayEvent로 들어온 사건을 받는다(UE WaitGameplayEvent).
        protected internal virtual void OnGameplayEvent(in GameplayEventData data)
        {
        }

        // 소유 태그와 차단이 풀리기 전에 불린다. 끝날 때 거는 쿨다운은 여기서 커밋한다.
        protected internal virtual void OnEnd(bool canceled)
        {
        }

        // false면 다른 어빌리티의 취소 태그와 CancelAllAbilities에도 끝나지 않는다. 아바타가 바뀌거나 시스템이 해제될 때는 끝난다.
        protected internal virtual bool CanBeCanceled => true;

        protected bool CheckCost() => AbilitySystem.CanAffordCost(Definition);

        protected bool CommitCost() => AbilitySystem.CommitCost(Definition);

        protected void CommitCooldown() => AbilitySystem.CommitCooldown(Definition);

        protected bool CommitAbility()
        {
            if (!CommitCost()) return false;
            CommitCooldown();
            return true;
        }

        protected void EndAbility() => AbilitySystem.EndAbility(Definition);

        protected void CancelAbility() => AbilitySystem.CancelAbility(Definition);
    }
}
