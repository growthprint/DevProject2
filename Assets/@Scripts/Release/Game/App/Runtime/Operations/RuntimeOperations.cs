#nullable enable annotations
using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Game.Core;

namespace Game.App
{
    // 서로 충돌하는 독점 작업을 한 번에 하나만 예약한다. 시작, 세션 전환, 캠페인 변경, 편집, 슬롯 변경이 해당한다.
    // 명시 저장과 저장 상태 조회는 따로 진행하므로 여기에 들지 않는다. 취소는 없다. 본문은 await 뒤에 Runtime 종료를 보고 스스로 멈춘다.
    // 호출자는 메인 스레드에서 부른다.
    internal sealed class RuntimeOperations
    {
        readonly GameRuntime _runtime;
        // 예약한 독점 작업의 완료다. null이면 예약이 없다.
        Task? _task;

        internal RuntimeOperations(GameRuntime runtime) => _runtime = runtime;

        // 독점 작업이 예약돼 있는지 알린다. 종료 상태와 함께 전환 중 판정(IsBusy)이 된다.
        internal bool IsReserved => _task != null;
        // 진행 중인 독점 작업의 완료다. 종료가 그 끝을 기다린다.
        internal Task? Current => _task;

        // 독점 명령은 예약, apply, 알림, 본문 실행 순서로 시작한다. apply에는 예외 없고 세션 명령을 내지 않는 상태 대입만 둔다.
        // 세션 관문은 알림이 밀어 넣는 사본이라 예약부터 알림 전까지는 예약 전의 허용이 남기 때문이다.
        // 다른 독점 작업과 겹치지 않는 것은 IsBusy를 먼저 보는 UI와 씬 관문이 보장한다. 씬 로드와 레벨 초기화 대기도 본문 안에서 돌아,
        // 씬 흐름이 로딩 중이면 Runtime은 늘 IsBusy다.
        internal UniTask Run(Action? apply, Func<Task> body)
        {
            TaskCompletionSource<bool> completion = SharedCompletion.Create<bool>();
            _task = completion.Task;
            // 예약 전에 시작한 저장 조회의 결과는 반영하지 않는다. 독점 예약은 유휴 상태의 명령에서만 시작해
            // 앱 루트가 살아 있으므로 GameManager를 바로 읽는다.
            GameManager.Saves.InvalidateStatus();
            apply?.Invoke();
            _runtime.Publish();
            _ = TrackedTask.RunAsync(completion, body, Release, _runtime.Publish);
            return completion.Task.AsUniTaskOnCurrentContext();
        }

        // TrackedTask.RunAsync가 본문을 끝낸 뒤 부르므로, 정리가 끝나야 전환 잠금이 풀린다.
        void Release() => _task = null;
    }
}
