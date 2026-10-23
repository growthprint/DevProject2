#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using SharedJsonSerializer = DevProject.Serialization.JsonSerializer;
using StrictObject = DevProject.Serialization.StrictObject;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DuckCharacter.AvatarParts
{
    // 원작 외형 JSON과 DuckAvatarPreset 사이의 변환 코덱이다. 구문 정책과 직렬화 설정은 공용 JsonSerializer가 맡고,
    // 이 코덱은 원작 필드의 형식 검사와 변환만 맡는다.
    public sealed class DuckovAppearanceJson : DevProject.Serialization.IJsonCodec<DuckAvatarPreset>
    {
        // 구성 지점이 넘긴 공용 직렬화 인스턴스다. 그 설정과 계약 캐시를 그대로 쓴다.
        readonly SharedJsonSerializer _json;

        // 원작 루트 필드 이름이다. 읽기, 쓰기, 형식 검사가 같은 대소문자 키를 쓰도록 한 곳에 둔다.
        const string _headSettingKey = "headSetting", _savedSettingKey = "savedSetting";

        public DuckovAppearanceJson(SharedJsonSerializer json)
        {
            _json = json!;
            // 부위별 원작 키는 생성할 때 한 번만 만든다. 필드 초기화가 생성자 본문보다 먼저 돌아 _partNames는 이미 채워져 있다.
            _idKeys = CreateKeys("ID");
            _infoKeys = CreateKeys("Info");
        }

        // 부위별 원작 필드 이름의 앞부분이다. 배열은 바뀔 수 있으므로 인스턴스끼리 공유하지 않는다.
        // 허용하는 원작 필드의 전체 목록은 TryEnsureOriginalShape가 정한다.
        readonly string[] _partNames = CreatePartNames();

        // 부위 인덱스별 원작 키 "<part>ID"와 "<part>Info"다. 읽기, 쓰기, 형식 검사가 호출마다 문자열을 붙이지 않도록 미리 만든다.
        readonly string[] _idKeys;
        readonly string[] _infoKeys;

        string[] CreateKeys(string suffix)
        {
            string[] keys = new string[_partNames.Length];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = _partNames[i] + suffix;
            return keys;
        }

        // enum 선언 순서가 바뀌어도 맞도록 원작 이름을 부위 인덱스마다 직접 배정한다.
        static string[] CreatePartNames()
        {
            string[] names = new string[(int)DuckAvatarPartType.Count];
            names[(int)DuckAvatarPartType.Hair] = "hair";
            names[(int)DuckAvatarPartType.Eye] = "eye";
            names[(int)DuckAvatarPartType.Eyebrow] = "eyebrow";
            names[(int)DuckAvatarPartType.Mouth] = "mouth";
            names[(int)DuckAvatarPartType.Tail] = "tail";
            names[(int)DuckAvatarPartType.Foot] = "foot";
            names[(int)DuckAvatarPartType.Wing] = "wing";
            return names;
        }

        // 손상된 원작 JSON은 던지지 않고 false와 오류 문구로 알린다.
        public bool TryRead(string json, [NotNullWhen(true)] out DuckAvatarPreset? value, out string error)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "외형 JSON이 비어 있습니다.";
                return false;
            }

            // 판독이 실패하면 판독 함수가 value를 null로 두므로 결과를 바로 value로 받는다.
            if (!TryReadValidated(json, out value, out string reason))
            {
                error = "외형 JSON을 읽을 수 없습니다: " + reason;
                return false;
            }

            error = string.Empty;
            return true;
        }

        // 구문 검사, 원작 필드 형식 검사, 변환, 값 검증 순서로 진행하고 첫 실패 사유만 돌려준다.
        bool TryReadValidated(string json, [NotNullWhen(true)] out DuckAvatarPreset? value, out string error)
        {
            value = null;
            if (!_json.TryReadObject(json, out JObject? root, out error))
                return false;
            if (!TryEnsureOriginalShape(root, out error))
                return false;
            if (!TryConvert(root, out DuckAvatarPreset? result, out error))
                return false;
            // 원작 JSON은 외부 입력이라 복원한 외형을 한 번 더 검증한다. 앞 단계가 유한수와 구조를 보장하므로 여기서 걸리는 것은
            // float 범위를 넘는 수치뿐이라 사유 문구가 하나다. 덕분에 TryRead가 성공한 값은 호출자가 다시 검증하지 않는다.
            if (!result.TryValidate())
            {
                error = "외형 수치가 float 범위를 벗어났습니다.";
                return false;
            }

            value = result;
            return true;
        }

        // 형식 검사를 통과한 루트만 받는다. 필수 필드가 모두 있으므로 '!'로 꺼낸다.
        bool TryConvert(JObject root, [NotNullWhen(true)] out DuckAvatarPreset? value, out string error)
        {
            JsonSerializer serializer = _json.CreateSerializer();
            DuckAvatarPreset result = new DuckAvatarPreset().CreateDefault();
            try
            {
                // 머리 설정을 복원해야 피부색과 머리 형태를 잃지 않는다. 변환 결과가 없으면 던지지 않고 거부한다.
                DuckHeadSetting? head = root[_headSettingKey]!.ToObject<DuckHeadSetting>(serializer);
                if (head == null)
                {
                    value = null;
                    error = _headSettingKey + " 값이 없습니다.";
                    return false;
                }
                result._headSetting = head;
                for (DuckAvatarPartType type = 0; type < DuckAvatarPartType.Count; type++)
                {
                    string infoKey = _infoKeys[(int)type];
                    result.SetID(type, root[_idKeys[(int)type]]!.ToObject<int>(serializer));
                    // 변환 결과가 없으면 SetInfo가 오류로 기록하게 두지 않고, 입력 문제이므로 읽기 실패 사유로 돌려준다.
                    DuckAvatarPartInfo? info = root[infoKey]!.ToObject<DuckAvatarPartInfo>(serializer);
                    if (info == null)
                    {
                        value = null;
                        error = infoKey + " 값이 없습니다.";
                        return false;
                    }
                    result.SetInfo(type, info);
                }
            }
            // 예외 경계: 외부 라이브러리인 Newtonsoft ToObject가 던지는 변환 예외만 잡아 오류 문구로 돌려준다.
            catch (Exception exception) when (exception is JsonException || exception is ArgumentException
                || exception is InvalidOperationException || exception is FormatException || exception is OverflowException)
            {
                value = null;
                error = exception.Message;
                return false;
            }

            value = result;
            error = string.Empty;
            return true;
        }

        // 원작 필드 순서와 형식으로 쓴다. 쓰기 호출자는 게임 저장 스냅샷과 편집 세션처럼 검증된 외형만 넘기므로
        // 구조와 값을 다시 검사하지 않는다.
        public string Write(DuckAvatarPreset appearance, bool pretty = false)
        {
            JsonSerializer serializer = _json.CreateSerializer();
            JObject root = new JObject
            {
                // 읽기가 요구하는 savedSetting과 headSetting을 원작 키로 쓴다.
                [_savedSettingKey] = true,
                [_headSettingKey] = JObject.FromObject(appearance._headSetting, serializer)
            };

            for (DuckAvatarPartType type = 0; type < DuckAvatarPartType.Count; type++)
                root[_idKeys[(int)type]] = appearance.GetID(type);

            for (DuckAvatarPartType type = 0; type < DuckAvatarPartType.Count; type++)
                root[_infoKeys[(int)type]] = JObject.FromObject(appearance.GetInfo(type), serializer);

            return root.ToString(pretty ? Formatting.Indented : Formatting.None);
        }

        // 자동 변환 전에 원작 필드를 StrictObject로 검사한다. 누락, 대소문자 오류, 모르는 필드는 경로가 붙은 첫 오류로 거부한다.
        // 수치는 유한수만 허용하고 float 범위는 복원 뒤 TryValidate가 본다. 콜백은 값을 쓰지 않고 형식만 검사한다.
        bool TryEnsureOriginalShape(JObject root, out string error)
        {
            return StrictObject.TryRead(root, "외형", appearance =>
            {
                // 루트 키는 읽기, 쓰기와 같은 상수로 검사한다.
                appearance.Boolean(_savedSettingKey);
                appearance.Object(_headSettingKey, head =>
                {
                    head.FiniteDouble("headScaleOffset");
                    head.FiniteDouble("foreheadHeight");
                    head.FiniteDouble("foreheadRound");
                    return head.Object("mainColor", ReadColor);
                });

                for (DuckAvatarPartType type = 0; type < DuckAvatarPartType.Count; type++)
                {
                    appearance.Int32(_idKeys[(int)type]);
                    appearance.Object(_infoKeys[(int)type], info =>
                    {
                        info.FiniteDouble("radius");
                        info.FiniteDouble("height");
                        info.FiniteDouble("heightOffset");
                        info.FiniteDouble("scale");
                        info.FiniteDouble("twist");
                        info.FiniteDouble("distanceAngle");
                        info.FiniteDouble("leftRightAngle");
                        return info.Object("color", ReadColor);
                    });
                }
            }, out error);
        }

        // 색상 객체는 원작 RGBA 네 유한수 필드만 허용한다.
        static bool ReadColor(StrictObject color)
        {
            color.FiniteDouble("r");
            color.FiniteDouble("g");
            color.FiniteDouble("b");
            color.FiniteDouble("a");
            return true;
        }
    }
}
