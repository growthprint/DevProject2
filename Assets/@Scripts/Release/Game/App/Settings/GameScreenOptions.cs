#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 ResolutionSetter처럼 해상도, 화면 모드, 수직 동기화, 프레임 제한을 화면에 적용하고 실제 화면 모드를 확인할 때를 정한다.
    // 바뀐 화면 모드를 저장할지는 GameOptionsManager가 정한다. 관리자가 메인 스레드에서만 쓴다.
    internal sealed class GameScreenOptions
    {
        // 원본 ResolutionSetter의 fullScreenChangeCheckCoolTime(1초)이다. 화면을 적용한 직후에는 창에 반영될 때까지
        // 확인을 미뤄, 적용 중인 값을 Alt+Enter 변경으로 착각하지 않는다.
        const float _modeCheckDelay = 1f;
        float _modeCheckTimer = _modeCheckDelay;
        // 마지막으로 화면에 적용을 마친 옵션이다. 원본 OptionsManager가 바뀐 키의 설정만 적용하듯 이 값과 다른 항목만 다시 적용한다.
        // null이면 아직 적용하지 않았다. 관리자는 후보 사본을 고치지 않으므로 사본을 그대로 담는다.
        GameOptions? _applied;

        // 실제 창이 전체 화면 계열인지 원본 UpdateFullScreenCheck와 같은 기준으로 본다. 테두리 없는 창과 독점 전체 화면이 여기에 든다.
        internal static bool IsFullScreen
        {
            get
            {
                FullScreenMode mode = Screen.fullScreenMode;
                return mode == FullScreenMode.FullScreenWindow || mode == FullScreenMode.ExclusiveFullScreen;
            }
        }

        // 옵션 적용 때마다 불린다. 저장값은 바꾸지 않고 화면에만 적용한다.
        internal void Apply(GameOptions options)
        {
            // Editor 검증이 개발자의 창이나 프로젝트 품질 설정을 바꾸지 않게 한다.
            if (!Application.isEditor)
            {
                GameOptions? shown = _applied;
                // 원본 ResolutionSetter처럼 마지막으로 요청한 값과 해상도나 화면 모드가 다를 때만 다시 요청하고 실제 화면과는 비교하지 않는다.
                // Alt+Enter 뒤 낡은 모드 후보가 오면 요청 없이 그 모드가 기록되지만, 관리자의 다음 SyncScreenMode 확인이 실제 모드로 다시 저장한다.
                if (ScreenSizeOrModeChanged(shown, options))
                {
                    // 원본 UpdateResolutionAndScreenMode처럼 화면비 1.3666667 미만은 16:9 너비로 넓혀 적용하되 저장값은 그대로 둔다.
                    // 모드 전환이 창에 반영될 때까지 화면 모드 확인을 미룬다.
                    Vector2Int size = GameResolutions.ApplySize(options.Width, options.Height);
                    DelayModeCheck();
                    Screen.SetResolution(size.x, size.y,
                        options.WindowMode == GameOptions._borderlessWindowMode ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
                }
                if (shown == null || shown.VSync != options.VSync) QualitySettings.vSyncCount = options.VSync ? 1 : 0;
                if (shown == null || shown.FrameRate != options.FrameRate) Application.targetFrameRate = options.FrameRate;
                _applied = options;
            }
        }

        // 관리자가 매 프레임 부른다. 대기 시간을 줄이고, 실제 화면 모드를 확인할 때가 되었으면 true다.
        // 확인을 건너뛴 프레임은 대기 시간을 되돌리지 않아 다음 프레임에 다시 확인한다.
        internal bool TickModeCheck(float deltaTime)
        {
            _modeCheckTimer -= deltaTime;
            return !(_modeCheckTimer > 0f);
        }

        // 화면을 적용하거나 바뀐 모드의 저장을 요청한 직후, 창에 반영될 때까지 다음 확인을 미룬다.
        internal void DelayModeCheck() => _modeCheckTimer = _modeCheckDelay;

        // 해상도 재요청이 필요한지다. 수직 동기화와 프레임 제한은 Apply가 항목마다 따로 비교한다.
        static bool ScreenSizeOrModeChanged(GameOptions? shown, GameOptions options) =>
            shown == null || shown.Width != options.Width || shown.Height != options.Height || shown.WindowMode != options.WindowMode;
    }
}
