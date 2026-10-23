#nullable enable annotations
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.App
{
    // 원작 GameManager 아래의 PauseMenu와 TimeScaleManager처럼 GameManager 프리팹의 자식 컴포넌트로 둔다.
    // UI 입력은 그대로 두고 Player 입력과 시간 배율, 커서를 함께 멈춘다. 해제하면 구성 시점 상태를 한 번만 되돌린다.
    [DisallowMultipleComponent]
    public sealed class GameplayPauseController : MonoBehaviour, IGameplayPause
    {
        // UI 맵은 건드리지 않도록 Player 맵의 동작만 켜고 끈다. 입력 자산은 직렬화하지 않고 Configure가 받아 채운다.
        InputActionMap _player;
        // Configure할 때 읽은 상태다. 앱이 해제될 때 이 상태로 되돌린다.
        PauseSnapshot _original;
        // 복원을 한 번만 실행하고 예외를 던지지 않는다. Configure 전에는 null이라 구성 여부 표시로도 쓴다.
        ReleaseOnce? _release;
        // 멈추기 직전 상태다. 일부 동작만 켜져 있던 상태도 그대로 되돌린다.
        PauseSnapshot? _beforePause;
        // 일시정지와 입력 차단은 같은 복원 상태를 공유한다. 시간은 일시정지일 때만 멈춘다.
        bool _paused, _inputBlocked;
        // 복원 뒤에 늦게 풀리는 지도나 인벤토리 차단, 정지 요청이 원래 상태를 다시 덮지 않게 막는다.
        bool _released;

        // 입력 사본은 실행 중에 만들어 프리팹에 연결할 수 없으므로 Runtime 구성이 넘긴다.
        // 이때의 시간 배율, 커서, Player 동작 상태가 앱 해제의 복원 기준이다.
        internal void Configure(InputActionAsset actions)
        {
            _player = actions.FindActionMap("Player", true);
            _original = Capture();
            _release = new ReleaseOnce(RestoreOriginalAsync, this);
        }

        // 앱 구성이 Configure와 등록 직후 Player 맵을 처음 켠다. 정지나 차단을 풀면 이 켜진 상태로, 앱 해제는 구성 시점 상태로 돌아간다.
        public void EnablePlayerInput()
        {
            _player.Enable();
        }

        // 반복 정지가 복원할 상태를 덮어쓰지 않는다. 해제 뒤의 요청은 무시한다.
        public void SetPaused(bool paused)
        {
            if (_released) return;
            _paused = paused;
            RefreshBlock();
        }

        // 지도와 인벤토리가 거는 차단이다. 커서와 Player 입력만 막고 시간은 멈추지 않는다. 해제 뒤의 요청은 무시한다.
        public void SetPlayerInputBlocked(bool blocked)
        {
            if (_released) return;
            _inputBlocked = blocked;
            RefreshBlock();
        }

        void RefreshBlock()
        {
            if (_paused || _inputBlocked)
            {
                if (_beforePause == null) _beforePause = Capture();
                _player.Disable();
                Time.timeScale = _paused ? 0f : _beforePause.TimeScale;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (_beforePause != null)
            {
                PauseSnapshot previous = _beforePause;
                _beforePause = null;
                Apply(previous);
            }
        }

        // 앱 종료나 구성 실패 때 구성 시점 상태를 한 번 되돌린다. 반복 호출은 같은 완료를 공유하고, 실패 기록은 ReleaseOnce가 맡는다.
        // 앱 구성은 Configure 뒤에만 해제 목록에 등록하므로 해제 객체가 늘 있다.
        public UniTask ReleaseAsync() => _release.ReleaseAsync();

        // 앱 해제를 거치지 않고 GameManager가 파괴될 때의 백업이다. 해제는 한 번만 실행되므로 앱이 먼저 해제했으면 아무 일도 없다.
        // 기다릴 호출자가 없어 실패는 Cleanup이 기록만 한다. 구성 전이면 되돌릴 상태가 없다.
        void OnDestroy()
        {
            if (_release != null) Cleanup.RunAsync(ReleaseAsync, this).Forget();
        }

        // 정지와 차단 상태를 지우고 구성 시점의 시간 배율, 커서, Player 동작 상태를 되돌린다. 이후 요청은 무시한다.
        UniTask RestoreOriginalAsync()
        {
            _released = true;
            _paused = _inputBlocked = false;
            _beforePause = null;
            Apply(_original);
            return UniTask.CompletedTask;
        }

        // 맵 전체가 아니라 동작마다 활성 여부를 읽어, 일부만 켜진 상태도 보존한다.
        PauseSnapshot Capture()
        {
            var actions = _player.actions;
            var enabled = new bool[actions.Count];
            for (int index = 0; index < enabled.Length; index++)
                enabled[index] = actions[index].enabled;

            return new PauseSnapshot(Time.timeScale, Cursor.lockState, Cursor.visible, enabled);
        }

        // UI 맵은 두고 보관한 플레이 상태만 되돌린다. 배열 길이는 같은 Player 맵을 읽는 Capture가 맞추므로 따로 검사하지 않는다.
        void Apply(PauseSnapshot snapshot)
        {
            Time.timeScale = snapshot.TimeScale;
            Cursor.lockState = snapshot.CursorLock;
            Cursor.visible = snapshot.CursorVisible;
            // 동작을 켜고 꺼도 목록은 바뀌지 않으므로 목록을 한 번만 얻어 쓴다.
            var actions = _player.actions;
            for (int index = 0; index < actions.Count; index++)
            {
                if (snapshot.Enabled[index])
                    actions[index].Enable();
                else actions[index].Disable();
            }
        }

        // 한 시점에 읽은 시간 배율, 커서 상태, Player 동작 활성 여부를 묶은 복원 값이다. Unity 객체 참조는 담지 않는다.
        sealed class PauseSnapshot
        {
            public float TimeScale { get; }
            public CursorLockMode CursorLock { get; }
            public bool CursorVisible { get; }
            // Player 동작 순서와 같은 활성 배열이다.
            public bool[] Enabled { get; }
            public PauseSnapshot(float timeScale, CursorLockMode cursorLock, bool cursorVisible, bool[] enabled)
            {
                TimeScale = timeScale;
                CursorLock = cursorLock;
                CursorVisible = cursorVisible;
                Enabled = enabled;
            }
        }
    }
}
