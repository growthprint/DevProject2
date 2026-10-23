#nullable enable annotations
using System;
using UnityEngine.InputSystem;

namespace Game.App
{
    // 공유 입력 액션이 꺼져 있을 때만 켜고, 이 임대가 켠 경우에만 끈다. 액션별 참조 카운트는 두지 않는다.
    // 씬 전환은 나가는 씬 루트를 먼저 끄고(OnDisable) 들어오는 씬을 켠다(OnEnable). 이 순서 덕분에 새 씬이 같은 액션을 다시 소유한다.
    internal sealed class InputActionEnableReleaseHandle : IDisposable
    {
        // 이 임대가 켠 액션(소유하지 않으면 null)이다.
        InputAction? _action;

        // 생성은 Acquire만 한다.
        InputActionEnableReleaseHandle(InputAction? action) => _action = action;

        internal bool OwnsEnable => _action != null;

        // 이미 켜진 액션은 다른 소유자의 것이라 아무것도 소유하지 않는 임대를 돌려준다.
        internal static InputActionEnableReleaseHandle Acquire(InputAction action)
        {
            if (action.enabled) return new InputActionEnableReleaseHandle(null);
            action.Enable();
            return new InputActionEnableReleaseHandle(action);
        }

        // 이 임대가 켠 액션만 한 번 끄며 반복 호출은 무시한다.
        public void Dispose()
        {
            InputAction? action = _action;
            _action = null;
            action?.Disable();
        }
    }
}
