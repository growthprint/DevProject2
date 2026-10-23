#nullable enable annotations
using System;
using System.Runtime.CompilerServices;

namespace Game.Core
{
    // 이벤트 소유자가 구독자 예외를 구독자별로 격리하는 공용 헬퍼다. App의 여러 영역이 함께 쓰므로
    // 영역끼리 서로 참조하지 않도록 Core에 둔다. 실패 처리는 소유자가 정하고, 생략하면 예외 로그로 남긴다.
    public static class ObserverDispatch
    {
        // 실패 처리를 생략한 호출마다 대리자를 새로 만들지 않도록 한 번만 만든다.
        // 테스트가 Log 인스턴스를 바꿀 수 있으므로 인스턴스를 붙잡지 않고 실패할 때마다 Log.Instance를 읽는다.
        static readonly Action<Exception> _logException = static error => Log.Instance.Exception(error);

        // 알림마다 GetInvocationList가 배열을 새로 만들지 않도록 대리자 인스턴스별 구독자 배열을 캐시한다.
        // 대리자는 불변이라 배열이 낡지 않고, 구독이 바뀌면 새 인스턴스가 새 항목을 쓴다. 키는 약한 참조라 버려진 대리자의 항목은 GC가 거둔다.
        // 캐시한 배열은 공유되므로 밖으로 내보내지 않고 읽기만 한다.
        static readonly ConditionalWeakTable<Delegate, Delegate[]> _invocationLists = new ConditionalWeakTable<Delegate, Delegate[]>();

        // 구독자를 하나씩 호출하고, 한 구독자가 실패해도 뒤 구독자를 계속 호출한다. 실패마다 onFailure를 한 번 부르며,
        // 생략하면 Log.Instance.Exception으로 기록한다. 정적 람다라 호출마다 대리자를 새로 만들지 않는다.
        public static void InvokeEach(Action? handlers, Action<Exception>? onFailure = null) =>
            InvokeIsolated<object?>(handlers, null, static (handler, _) => ((Action)handler)(), onFailure);

        // 구독자마다 argument()로 인자를 새로 만들어, 한 구독자가 인자를 바꿔도 다음 구독자에게 새지 않게 한다.
        // 인자 생성 실패도 구독자별로 격리한다. 인자 생성기 누락은 내부 호출자의 결함이라 검사만 한다.
        public static void InvokeEach<T>(Action<T>? handlers, Func<T> argument, Action<Exception>? onFailure = null)
        {
            Log.Instance.Assert(argument != null, "argument: 구독자 인자 생성기가 필요합니다.");
            InvokeIsolated(handlers, argument, static (handler, create) => ((Action<T>)handler)(create()), onFailure);
        }

        // 모든 구독자에게 같은 argument를 넘긴다. 불변 값이나 값 형식처럼 공유해도 되는 인자에만 쓰고,
        // 구독자마다 새 인자가 필요하면 InvokeEach<T>를 쓴다. 캡처 람다가 없어 호출마다 대리자를 만들지 않는다.
        // T가 대리자 형식이면 Func<T> 오버로드와 모호해지므로 이름을 따로 둔다.
        public static void InvokeEachValue<T>(Action<T>? handlers, T argument, Action<Exception>? onFailure = null) =>
            InvokeIsolated(handlers, argument, static (handler, value) => ((Action<T>)handler)(value), onFailure);

        // 원작의 Health.OnDead(Health, DamageInfo)처럼 보낸 쪽을 첫 인자로 넘기는 정적 이벤트용이다. 두 인자는 모든 구독자가 공유한다.
        // 두 인자를 값 튜플 상태로 묶어 정적 람다에 넘기므로 호출마다 대리자나 박싱이 생기지 않는다.
        public static void InvokeEachValue<TSender, T>(Action<TSender, T>? handlers, TSender sender, T argument,
            Action<Exception>? onFailure = null) =>
            InvokeIsolated<(TSender Sender, T Argument)>(handlers, (sender, argument),
                static (handler, state) => ((Action<TSender, T>)handler)(state.Sender, state.Argument), onFailure);

        // 모든 진입점이 함께 쓰는 순회와 격리 루프다. 상태는 인자 생성기, 공유 인자, 보낸 쪽과 인자의 묶음 중 하나이고,
        // invoke가 구독자 하나와 함께 받는다.
        static void InvokeIsolated<TState>(Delegate? handlers, TState state, Action<Delegate, TState> invoke, Action<Exception>? onFailure)
        {
            if (handlers == null)
                return;

            onFailure ??= _logException;

            // 알림을 시작할 때의 구독자 목록을 순회하므로, 순회 중에 구독이 바뀌어도 이번 알림에는 반영되지 않는다.
            foreach (Delegate handler in InvocationList(handlers))
            {
                try
                {
                    invoke(handler, state);
                }
                // 예외 경계: 구독자 하나의 실패를 소유자가 정한 처리로 넘기고 나머지 구독자 알림을 계속한다.
                catch (Exception error)
                {
                    onFailure(error);
                }
            }
        }

        // 처음 보는 대리자 인스턴스만 GetInvocationList로 배열을 만든다. 정적 람다는 한 번만 만들어지므로 캐시 적중 때는 할당이 없다.
        static Delegate[] InvocationList(Delegate handlers) =>
            _invocationLists.GetValue(handlers, static key => key.GetInvocationList());
    }
}
