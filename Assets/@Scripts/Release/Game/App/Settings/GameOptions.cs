#nullable enable

namespace Game.App
{
    // 진행 저장과 따로 저장되는 옵션 값이다. UI가 편집하는 후보로도 쓰며, 저장 전에 TryValidate로 검사한다.
    public sealed class GameOptions
    {
        // 지원 언어는 GameLocalization의 한국어와 영어 로캘이고 기본은 한국어다.
        public string Language { get; set; } = GameLocalization._koreanLocale;
        // 아래 세 음량은 선형 0~1 배율이다. 음악과 효과음은 전체 음량과 따로 편집한다.
        public float MasterVolume { get; set; } = 1f;
        public float MusicVolume { get; set; } = 1f;
        public float SfxVolume { get; set; } = 1f;
        // 원본 입력 감도의 기본값이다. 허용 범위는 아래 _minMouseSensitivity와 _maxMouseSensitivity다.
        public float MouseSensitivity { get; set; } = 10f;
        // 해상도 목록, 장치 기본값, 화면비 보정은 GameResolutions가 맡는다. 여기서는 허용 범위 안의 저장값을 그대로 둔다.
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        // 원본 ResolutionSetter처럼 0은 테두리 없음, 1은 창 모드이고 기본은 0이다.
        public int WindowMode { get; set; }
        // 옵션 파일과 옵션 화면 드롭다운 순서가 이 번호를 그대로 쓰므로, 화면 모드를 비교하거나 정하는 곳은 이 이름을 쓴다.
        internal const int _borderlessWindowMode = 0, _windowedWindowMode = 1;
        // 원본 프레임 제한처럼 기본은 90이고, 무제한은 500으로 나타낸다. 옵션을 읽기 전의 부팅 화면도 이 값으로 묶는다.
        internal const int _defaultFrameRate = 90;
        public int FrameRate { get; set; } = _defaultFrameRate;
        // 원본처럼 수직 동기화는 기본으로 꺼져 있다.
        public bool VSync { get; set; }
        // 옵션 값만 담는다. 실제 카메라 상태는 옵션을 적용하는 쪽이 바꾼다.
        public bool ReduceCameraMotion { get; set; }
        // 원본 RunInputOptions의 기본 인덱스 0이 누르기 방식이라 기본값이 true다.
        public bool HoldToRun { get; set; } = true;
        // 원본 이동 방향 기본값은 조준 방향이 아닌 카메라 기준이다.
        public bool MoveRelativeToAim { get; set; }

        // UI나 구독자가 반환값을 수정해도 저장 서비스의 확정값이 바뀌지 않게 복제한다.
        public GameOptions Clone()
        {
            var copy = (GameOptions)MemberwiseClone();
            // 후보를 고쳐도 확정값의 키 배열이 바뀌지 않도록 배열까지 복제한다. 키 배열은 초기값이 있고 옵션 코덱이 쓰지 않아 null이 아니다.
            copy.KeyBindings = (string[])KeyBindings.Clone();
            return copy;
        }

        // 키 순서는 _bindingNames와 같아야 한다. 옵션 파일에는 저장하지 않는다.
        public string[] KeyBindings { get; set; } = new[]
        {
            "<Keyboard>/w", "<Keyboard>/s", "<Keyboard>/a", "<Keyboard>/d",
            "<Keyboard>/leftShift", "<Keyboard>/space", "<Keyboard>/f", "<Mouse>/leftButton", "<Mouse>/rightButton"
        };
        // 원본 입력 의미 이름이다. 표시 언어와 상관없이 고정된 식별자로 쓴다.
        public static readonly string[] _bindingNames = { "moveUp", "moveDown", "moveLeft", "moveRight", "sprint", "dodge", "interact", "attack", "aim" };
        // 마우스 감도 허용 범위다. 검증, 옵션 파일 판독, 옵션 화면이 함께 쓴다.
        // 판독은 double 그대로 쓰고, float가 필요한 곳은 (float)로 바꾼다. (float)0.1은 0.1f와 같은 값이다.
        internal const double _minMouseSensitivity = 0.1, _maxMouseSensitivity = 50;
        // 세 음량의 허용 범위다. 검증과 옵션 파일 판독이 함께 쓰며, 감도처럼 판독은 double로, 검증은 (float)로 바꿔 쓴다.
        internal const double _minVolume = 0, _maxVolume = 1;
        // 너비와 높이에 함께 쓰는 해상도 허용 범위다.
        const int _minResolution = 320, _maxResolution = 16384;

        // 설정 초기화는 화면에 보이는 항목만 defaults의 값으로 되돌리고, 나머지 값은 그대로 둔다.
        // 해상도 기본값은 장치마다 다르므로 호출자가 장치 기본값을 담아 넘긴다. 이 타입은 화면 API를 읽지 않는다.
        public void ResetVisibleOptions(GameOptions defaults)
        {
            Language = defaults.Language;
            Width = defaults.Width;
            Height = defaults.Height;
            WindowMode = defaults.WindowMode;
            MouseSensitivity = defaults.MouseSensitivity;
            MasterVolume = defaults.MasterVolume;
            MusicVolume = defaults.MusicVolume;
            SfxVolume = defaults.SfxVolume;
        }

        // 옵션 규칙을 검사하는 유일한 곳이다. 값을 보정하지 않고, 잘못된 후보는 예외나 로그 없이 false로 거부한다.
        // 거부를 알리는 일은 호출자가 맡는다. 저장 전에는 옵션 관리자, 파일 읽기는 옵션 코덱, 주입 기본값은 옵션 관리자 초기화가 알린다.
        // 키 설정은 파일에 없고 코드 기본값만 쓰므로 보지 않는다.
        public bool TryValidate()
        {
            // 시스템 언어 판정과 같은 지원 로캘 상수로 본다.
            if (Language != GameLocalization._koreanLocale && Language != GameLocalization._englishLocale) return false;
            // 음량 범위는 옵션 파일 판독과 같은 상수에서 읽는다. (float)0과 (float)1은 0f, 1f와 같은 값이다.
            if (!TryRange(MasterVolume, (float)_minVolume, (float)_maxVolume)) return false;
            if (!TryRange(MusicVolume, (float)_minVolume, (float)_maxVolume)) return false;
            if (!TryRange(SfxVolume, (float)_minVolume, (float)_maxVolume)) return false;
            if (!TryRange(MouseSensitivity, (float)_minMouseSensitivity, (float)_maxMouseSensitivity)) return false;
            if (Width < _minResolution || Width > _maxResolution) return false;
            if (Height < _minResolution || Height > _maxResolution) return false;
            // 화면 모드는 테두리 없음과 창 모드 두 번호만 허용한다.
            if (WindowMode != _borderlessWindowMode && WindowMode != _windowedWindowMode) return false;
            // 프레임 제한은 원본 선택지 목록에 있는 값만 허용한다.
            if (!IsSupportedFrameRate(FrameRate)) return false;
            return true;
        }

        // 원본 프레임 제한 선택지다. 500은 무제한을 나타낸다.
        static bool IsSupportedFrameRate(int frameRate) => frameRate is 60 or 90 or 120 or 144 or 240 or 500;

        // NaN과 무한대도 범위 밖으로 보고 예외 없이 false를 돌려준다.
        private static bool TryRange(float value, float minimum, float maximum) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum && value <= maximum;
    }
}

