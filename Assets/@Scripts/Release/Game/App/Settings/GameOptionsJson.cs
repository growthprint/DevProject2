#nullable enable
using System.Diagnostics.CodeAnalysis;
using DevProject.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharedJsonSerializer = DevProject.Serialization.JsonSerializer;

namespace Game.App
{
    // 실제 옵션 저장 파일에 있는 필드만 읽고 쓰는 코덱이다. 읽기는 다른 저장 코덱처럼 공용 엄격 판독기(StrictObject)를 쓴다.
    // 옵션 파일 형식은 이 코덱 하나가 정하고, 파일 읽기와 쓰기 순서는 GameOptionsManager가 맡는다.
    internal sealed class GameOptionsJson : IJsonCodec<GameOptions>
    {
        // JSON 문법과 중복 키 검사는 공용 직렬화기에 맡긴다.
        private readonly SharedJsonSerializer _serializer = new();
        // 모든 읽기 실패 문구 앞에 붙여 옵션 파일 오류임을 알린다.
        private const string _errorPrefix = "옵션 저장 JSON을 복원할 수 없습니다: ";

        // 필드의 유무와 형식과 범위를 모두 검사하고, 잘못된 값을 기본값으로 바꿔 넣지 않는다.
        // 손상된 입력은 예외 대신 false와 첫 오류 문구로 알린다. 문법, 필드 형식과 수치 범위, 남은 필드, 옵션 규칙 순서로 검사한다.
        public bool TryRead(string json, [NotNullWhen(true)] out GameOptions? value, out string error)
        {
            value = null;
            // 대소문자가 다른 키는 필드 누락으로, 읽지 않은 키는 StrictObject가 모든 필드를 읽은 뒤 거부한다.
            if (!_serializer.TryReadObject(json, out JObject? root, out string reason) ||
                !StrictObject.TryRead<GameOptions>(root, "options", ReadOptions, out GameOptions? candidate, out reason))
            {
                error = _errorPrefix + reason;
                return false;
            }
            // 파일에 없는 키 설정은 메모리 기본값을 쓴다. TryValidate는 실패 이유를 주지 않으므로
            // 범위 위반은 예외 없이 고정 문구로 돌려준다.
            if (!candidate.TryValidate())
            {
                error = _errorPrefix + "옵션 값이 지원 범위를 벗어났습니다.";
                return false;
            }
            value = candidate;
            error = string.Empty;
            return true;
        }

        // 열세 필드를 고정된 순서로 읽어 같은 입력에서 늘 같은 첫 오류를 남긴다. 실패한 뒤의 읽기는 기본값이고,
        // 그때 만든 후보는 TryRead가 버리므로 여기서 Failed를 확인하지 않는다.
        private static GameOptions ReadOptions(StrictObject reader) => new()
        {
            Language = reader.Text("language"),
            // 음량 범위는 GameOptions.TryValidate와 같은 상수로 검사한다.
            MasterVolume = ReadInRange(reader, "masterVolume", GameOptions._minVolume, GameOptions._maxVolume),
            MusicVolume = ReadInRange(reader, "musicVolume", GameOptions._minVolume, GameOptions._maxVolume),
            SfxVolume = ReadInRange(reader, "sfxVolume", GameOptions._minVolume, GameOptions._maxVolume),
            MouseSensitivity = ReadInRange(reader, "mouseSensitivity", GameOptions._minMouseSensitivity, GameOptions._maxMouseSensitivity),
            Width = reader.Int32("width"),
            Height = reader.Int32("height"),
            WindowMode = reader.Int32("windowMode"),
            FrameRate = reader.Int32("frameRate"),
            VSync = reader.Boolean("vSync"),
            ReduceCameraMotion = reader.Boolean("reduceCameraMotion"),
            HoldToRun = reader.Boolean("holdToRun"),
            MoveRelativeToAim = reader.Boolean("moveRelativeToAim")
        };

        // JSON 키를 직접 적어서 속성 이름이 바뀌어도 저장 형식이 바뀌지 않게 한다.
        public string Write(GameOptions value, bool pretty = false)
        {
            // 유일한 호출자 GameOptionsManager.WriteAsync가 복제한 사본만 넘기므로 값은 null이 아니다.
            GameOptions options = value!;
            JObject root = new()
            {
                ["language"] = options.Language,
                ["masterVolume"] = options.MasterVolume,
                ["musicVolume"] = options.MusicVolume,
                ["sfxVolume"] = options.SfxVolume,
                ["mouseSensitivity"] = options.MouseSensitivity,
                ["width"] = options.Width,
                ["height"] = options.Height,
                ["windowMode"] = options.WindowMode,
                ["frameRate"] = options.FrameRate,
                ["vSync"] = options.VSync,
                ["reduceCameraMotion"] = options.ReduceCameraMotion,
                ["holdToRun"] = options.HoldToRun,
                // 실제 저장 파일의 마지막 필드다. 파일에 없는 설정은 기록하지 않는다.
                ["moveRelativeToAim"] = options.MoveRelativeToAim,
            };
            return root.ToString(pretty ? Formatting.Indented : Formatting.None);
        }

        // 음량과 감도는 float로 줄이기 전에 유한한 double로 범위를 검사한다. float로 먼저 줄이면 1.00000000001이
        // 1로 반올림되어 통과하기 때문이다. 앞선 오류가 있으면 FailField는 첫 오류를 그대로 둔다.
        private static float ReadInRange(StrictObject reader, string name, double minimum, double maximum)
        {
            double number = reader.FiniteDouble(name);
            if (number < minimum || number > maximum) reader.FailField(name, "값이 허용 범위를 벗어났습니다.");
            return (float)number;
        }
    }
}
