#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace Game.App
{
    // 옵션의 키 설정을 Player 액션의 런타임 재정의로 적용하고, 종료 때 원래 바인딩으로 되돌린다.
    // 입력 자산 파일은 바꾸지 않는다.
    internal sealed class GameInputOptions : IAsyncReleasable
    {
        // 앱이 공유하는 입력 자산이다. 입력 맵을 켜고 끄는 일은 GameplayPauseController가 맡는다.
        readonly InputActionAsset _actions;
        // 생성 시점의 재정의다. 종료 때 이 값으로 되돌려 외부에서 설정한 재정의를 지킨다.
        readonly string _originalOverrides;
        // 게임 조준이 읽는 좌표 입력 UI/Point의 활성화 임대다. EventSystem의 입력 사본이 아니라 앱 입력 자산에서 켠다.
        // 꺼져 있을 때만 켜고 그 경우에만 끄는 규칙은 다른 컨트롤러와 같은 InputActionEnableLease가 맡는다.
        readonly InputActionEnableReleaseHandle _pointLease;
        // 복원은 한 번만 시도한다. 실패는 기록만 하고 다시 시도하거나 던지지 않는다.
        // 복원이 시작된 뒤에는 Started로 새 키 설정 적용을 거부한다.
        readonly ReleaseOnce _release;
        // GameOptions.KeyBindings와 같은 순서의 액션 경로다. 적용 때마다 문자열을 잇지 않도록 전체 경로로 두고,
        // 플레이어 조작 코드가 찾는 액션과 어긋나지 않게 GameInputPaths의 같은 상수를 쓴다.
        static readonly string[] _actionPaths =
        {
            GameInputPaths._playerMove, GameInputPaths._playerMove, GameInputPaths._playerMove, GameInputPaths._playerMove,
            GameInputPaths._playerSprint, GameInputPaths._playerJump, GameInputPaths._playerInteract, GameInputPaths._playerAttack,
            GameInputPaths._playerAim
        };
        // 이동 복합 바인딩은 키 이름이 아니라 파트 이름으로 방향을 찾는다. 순서는 경로 목록의 앞 4개와 같다.
        static readonly string[] _parts = { "up", "down", "left", "right" };

        // 입력 자산에 액션을 추가하지 않는다. 조준은 자산의 Player/Aim을 쓰고, 이 객체는 좌표 입력을 켜는 일만 맡는다.
        public GameInputOptions(InputActionAsset actions)
        {
            _actions = actions;
            // 복원 실패 로그에 입력 자산을 문맥으로 붙인다.
            _release = new ReleaseOnce(ReleaseCoreAsync, actions);
            _originalOverrides = actions.SaveBindingOverridesAsJson();
            // 조준이 읽는 좌표 액션이 있는지 구성 단계에서 확인하고, 꺼져 있을 때만 켜는 임대를 얻는다.
            _pointLease = InputActionEnableReleaseHandle.Acquire(actions.FindAction(GameInputPaths._uiPoint, true));
        }

        // 키보드와 마우스 바인딩만 바꿔 게임패드와 UI 입력은 그대로 둔다. 해제 뒤 호출이면 아무것도 바꾸지 않고 false다.
        public bool Apply(GameOptions options)
        {
            // 복원 뒤에 바꾸면 되돌릴 주체가 없으므로 예외나 로그 없이 거부한다. 해제 여부는 ReleaseOnce.Started 하나로 본다.
            if (_release.Started) return false;
            // 이동 4방향처럼 경로가 직전과 같으면 찾아 둔 액션을 다시 쓴다.
            InputAction action = null!;
            for (int index = 0; index < _actionPaths.Length; index++)
            {
                if (index == 0 || _actionPaths[index] != _actionPaths[index - 1])
                    action = _actions.FindAction(_actionPaths[index], true);
                // 바인딩 목록을 한 번만 얻어 쓴다. 재정의 직후 break하므로 반복 중에 목록이 바뀌지 않는다.
                ReadOnlyArray<InputBinding> bindings = action.bindings;
                for (int binding = 0; binding < bindings.Count; binding++)
                {
                    InputBinding entry = bindings[binding];
                    if (entry.isComposite || !(entry.path.StartsWith("<Keyboard>/", StringComparison.Ordinal) ||
                        entry.path.StartsWith("<Mouse>/", StringComparison.Ordinal))) continue;
                    // 앞쪽 이동 칸은 _parts와 같은 수라, 그 길이로 복합 바인딩의 방향 파트를 고를 칸을 정한다.
                    if (index < _parts.Length && entry.name != _parts[index]) continue;
                    action.ApplyBindingOverride(binding, options.KeyBindings[index]);
                    break;
                }
            }
            return true;
        }

        // 앱이 바꾼 키 재정의만 되돌리고 입력 자산은 파괴하지 않는다. 메인 스레드의 ResourceScope만 부르므로 스레드를 전환하지 않는다.
        // 래퍼 상태 기계 없이 ReleaseOnce의 공유 완료를 그대로 돌려준다.
        public UniTask ReleaseAsync() => _release.ReleaseAsync();

        // ReleaseOnce가 Started를 먼저 세운 뒤 부르므로, 정리 단계를 실행하기 전에 그 뒤의 Apply 호출은 이미 거부된다.
        UniTask ReleaseCoreAsync()
        {
            // 좌표 입력은 임대가 켠 경우에만 끈다. 두 정리를 따로 실행해 한쪽이 실패해도 다른 쪽은 진행하고, 실패는 기록만 한다.
            Cleanup.Run(_pointLease.Dispose, _actions);
            Cleanup.Run(() =>
            {
                _actions.RemoveAllBindingOverrides();
                _actions.LoadBindingOverridesFromJson(_originalOverrides);
            }, _actions);
            return UniTask.CompletedTask;
        }
    }
}
