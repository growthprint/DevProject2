#nullable enable

using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    // 확정 세션의 소유자다. 스냅샷 확정과 알림, 명령 게이트, 후보 채택만 맡고, 시계나 생존 수치 같은 현재 값은 각 상태 소유자가 갖는다.
    // 세션을 바꾸는 명령은 역할별 명령 묶음(Campaign, World, Inventory)에 있고, 모두 이 확정 경로를 거친다.
    // 명령 게이트는 각 명령이나, 그 명령만 부르는 유일한 호출자가 먼저 확인한다.
    public sealed class GameSessionManager
    {
        // 확정 세션의 유일한 원본 값인 불변 스냅샷이다. 따로 고치는 작업 사본이 없어 저장과 표시가 같은 값을 공유한다.
        GameSessionSnapshot _snapshot;
        // Runtime이 이 Manager를 교체한 뒤에는 명령을 영구히 거부한다.
        bool _closed;
        // Runtime이 명시적으로 열기 전까지 변경을 거부한다.
        bool _commandsAllowed;
        // 구독자가 알림 중에 낸 후속 명령의 알림도 앞선 알림 뒤에 모든 구독자에게 순서대로 전달한다.
        readonly Queue<GameSessionSnapshot> _notifications = new Queue<GameSessionSnapshot>();
        // 알림 중에 다시 들어온 알림은 중첩해 돌리지 않고 대기열에만 넣는다.
        bool _notifying;

        // 명령은 Runtime이 열기 전까지 거부한다. 이어하기 스냅샷은 코덱과 값 팩토리가 이미 검증했고 새 게임은 아이템이 없으므로
        // 여기서 다시 검증하지 않는다.
        public GameSessionManager(GameSessionSnapshot snapshot)
        {
            // GameRuntime은 null을 거른 스냅샷만 넘긴다.
            _snapshot = snapshot;
            Campaign = new SessionCampaignCommands(this);
            World = new SessionWorldCommands(this);
            Inventory = new SessionInventoryCommands(this);
        }

        // 도입부 진행, 출격, 월드 종료 후보와 프롤로그 거울 닫힘 명령이다.
        internal SessionCampaignCommands Campaign { get; }
        // 처치와 대사 같은 원본 사건의 완료 기록 명령이다.
        internal SessionWorldCommands World { get; }
        // 원본 아이템 소유 그래프 명령이다.
        internal SessionInventoryCommands Inventory { get; }

        // 표시 구독자는 확정된 읽기 전용 값만 전달받는다. 알림 안에서 독점 작업을 예약하거나 Runtime 상태를 바꿔 상태 소유자의
        // 명령 관문(GameRuntime.AcceptsSessionCommands)을 닫지 않는다. 아이템 사용은 소비를 확정한 뒤 같은 호출에서 생존 효과를 적용하기 때문이다.
        public event Action<GameSessionSnapshot>? Changed;

        // 변경이 없으면 같은 불변 스냅샷을 저장과 표시에서 안전하게 공유한다.
        public GameSessionSnapshot Snapshot() => _snapshot;

        // 전환과 종료를 조정하는 앱 계층만 명령 수락 상태를 바꾼다. 닫힌 Manager는 다시 열 수 없다.
        internal void SetCommandsAllowed(bool allowed) => _commandsAllowed = allowed && !_closed;

        // Runtime이 세션을 교체할 때 영구히 닫는다. 명령을 거부하고, Changed 구독을 버린다.
        internal void Close()
        {
            _closed = true;
            _commandsAllowed = false;
            Changed = null;
        }

        // 상태 변경을 확정하는 유일한 경로다. 명령 묶음은 바뀐 부분을 검증한 값을, Runtime은 코덱이나 명령이 만든 후보를
        // 현재 세션에만 넘기므로 다시 검증하지 않고 확정 순서대로 알린다. 명령 게이트는 바꾸지 않고, 저장 시각은 저장 관리자가 붙인다.
        internal void Commit(GameSessionSnapshot next)
        {
            _snapshot = next;
            Notify(next);
        }

        // Runtime이 명령을 허용했는지 알린다. 명령 묶음은 거짓이면 로그 없이 거절하고, 다음 프레임에 다시 시도하는 첫 월드 초기화는
        // 이 값으로 일시적 거절을 가려낸다. 닫힌 세션은 _commandsAllowed가 늘 거짓이라 _closed를 따로 보지 않는다.
        // 세션 명령은 메인 스레드에서만 오므로 스레드는 보지 않는다. 정산 대기 결과는 레거시 저장에만 있고 이어하기가 세션 생성 전에 정산하므로,
        // 세션 명령은 그 상태를 만나지 않는다.
        internal bool AcceptsCommands => _commandsAllowed;

        // 알림 중에 다시 들어온 알림도 순서대로 전달한다. 구독자 예외는 ObserverDispatch가 구독자별로 격리해 한 번 기록하고,
        // 확정된 명령은 되돌리지 않은 채 나머지 구독자와 대기 알림을 처리한다.
        void Notify(GameSessionSnapshot snapshot)
        {
            _notifications.Enqueue(snapshot);
            if (_notifying) return;
            _notifying = true;
            try
            {
                while (_notifications.Count != 0)
                {
                    GameSessionSnapshot committed = _notifications.Dequeue();
                    // 값 오버로드로 넘겨 알림마다 대리자를 만들지 않는다. 모든 구독자가 같은 스냅샷을 받지만 읽기 전용이라 바꿀 수 없다.
                    ObserverDispatch.InvokeEachValue(Changed, committed);
                }
            }
            finally { _notifying = false; }
        }
    }
}
