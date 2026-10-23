#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace DevProject.Serialization
{
    // JSON 객체를 경로와 함께 엄격히 읽는 공용 구문 규칙이다. 값 범위나 ID 의미 같은 기능 규칙은 각 코덱과 값 팩토리가 맡는다.
    // 첫 오류만 남기고 그 뒤의 읽기는 기본값만 돌려준다. 오류 없이 끝난 객체에 읽지 않은 속성이 있으면 거부한다.
    public sealed class StrictObject
    {
        // 한 번의 판독이 중첩 객체와 배열 항목 전체에서 공유하는 첫 오류다.
        sealed class ReadState
        {
            internal string? Error;
        }

        readonly JObject _value;
        readonly string _path;
        readonly ReadState _state;
        // 콜백이 읽은 속성 이름을 대소문자를 구분해 기록한다. 끝에 읽지 않은 속성을 찾는 데 쓴다.
        readonly HashSet<string> _consumed = new HashSet<string>(StringComparer.Ordinal);
        // 필드 판독과 배열 요소 판독이 같은 사유 문구를 쓰도록 한 곳에 둔다. 경로는 판독기마다 따로 붙인다.
        const string _notInteger = "값은 정수여야 합니다.";
        const string _outOfIntegerRange = "값이 정수 범위를 벗어났습니다.";
        const string _notText = "값은 문자열이어야 합니다.";

        // 같은 판독의 중첩 객체는 오류 상태를 공유한다.
        StrictObject(JObject value, string path, ReadState state)
        {
            _value = value;
            _path = path;
            _state = state;
        }

        // 중첩 객체를 포함한 이 판독에 오류가 있는지 알린다. 실패 뒤의 읽기 결과는 기본값이므로
        // 콜백은 만든 객체를 역참조하거나 수정하기 전에 이 값을 확인한다.
        public bool Failed => _state.Error != null;

        // token을 객체로 읽고 성공 여부를 돌려준다. 실패하면 value는 기본값이고 error는 경로가 붙은 첫 오류다.
        public static bool TryRead<T>(JToken? token, string path, Func<StrictObject, T> read,
            [MaybeNullWhen(false)] out T value, out string error)
        {
            var state = new ReadState();
            T result = ReadNested(token, path, read, state);
            if (state.Error != null)
            {
                value = default;
                error = state.Error;
                return false;
            }

            value = result;
            error = string.Empty;
            return true;
        }

        // 결과 없이 읽는 콜백도 같은 규칙으로 처리한다.
        public static bool TryRead(JToken? token, string path, Action<StrictObject> read, out string error) =>
            TryRead(token, path, reader =>
            {
                read(reader);
                return true;
            }, out _, out error);

        // 코덱 고유의 형식 오류를 그대로 기록한다. 호출자가 메시지에 경로를 넣어야 하며, 첫 오류만 남는다.
        public void Fail(string message) => Record(_state, message);

        // 이 객체의 속성 하나에 대한 형식 오류를 '<경로>.<이름> <사유>'로 기록한다.
        public void FailField(string name, string reason) => Record(_state, FieldPath(name) + " " + reason);

        // 값 팩토리(TryCreate) 결과를 받는다. 팩토리는 오류 문구를 주지 않으므로, 실패면 '<경로>: 값이 올바르지 않습니다.'를 기록한다.
        // 실패하면 Failed가 참이라 반환값을 쓰지 않으므로 받은 값을 그대로 돌려준다. 반환값은 Failed가 false일 때만 유효하다.
        public T Create<T>(bool ok, T? value) where T : class
        {
            if (!ok) Record(_state, _path + ": 값이 올바르지 않습니다.");
            return value!;
        }

        // 필수 속성의 원본 토큰을 읽음으로 표시하고 돌려준다. 형 변환은 호출자가 맡는다.
        // 누락이면 오류를 기록한다. 누락이거나 이미 실패했으면 null 토큰을 돌려준다.
        public JToken Raw(string name)
        {
            if (Failed) return JValue.CreateNull();
            JProperty? property = _value.Property(name, StringComparison.Ordinal);
            if (property == null)
            {
                FailField(name, "필드가 없습니다.");
                return JValue.CreateNull();
            }

            _consumed.Add(name);
            return property.Value;
        }

        // 선택 속성은 누락이면 fallback을 반환하고, 명시 null이면 거부하며, 값이 있으면 read로 읽는다.
        public T Optional<T>(string name, T fallback, Func<StrictObject, string, T> read)
        {
            if (Failed) return fallback;
            JProperty? property = _value.Property(name, StringComparison.Ordinal);
            if (property == null)
                return fallback;
            if (property.Value.Type == JTokenType.Null)
            {
                _consumed.Add(name);
                FailField(name, "값은 null일 수 없습니다.");
                return fallback;
            }

            return read(this, name);
        }

        // 문자열을 읽는다. 빈 문자열은 허용하고, null이나 문자열이 아닌 값은 거부한다.
        public string Text(string name)
        {
            JToken value = Raw(name);
            if (value.Type == JTokenType.String)
                return (string)value!;
            // 배열 요소 판독과 같은 사유 문구를 쓴다.
            FailField(name, _notText);
            return string.Empty;
        }

        // 명시 null 또는 문자열을 읽는다. 속성 자체는 필수다.
        public string? NullableText(string name)
        {
            JToken value = Raw(name);
            if (value.Type == JTokenType.Null) return null;
            if (value.Type == JTokenType.String) return (string?)value;
            FailField(name, "값은 문자열 또는 null이어야 합니다.");
            return null;
        }

        // 불리언 토큰만 읽는다.
        public bool Boolean(string name) => ToBoolean(Raw(name), name);

        // Raw로 받은 토큰을 불리언으로 변환한다. 오류는 name 속성으로 기록한다.
        bool ToBoolean(JToken value, string name)
        {
            if (value.Type == JTokenType.Boolean)
                return (bool)value;
            FailField(name, "값은 참 또는 거짓이어야 합니다.");
            return false;
        }

        // 정수 토큰만 읽고 int 표현 범위를 확인한다.
        public int Int32(string name) => ToInt32(Raw(name), name);

        // Raw로 받은 토큰을 int로 읽는다. 판정은 배열 요소와 같은 ReadInt32가 하고, 오류는 name 속성 경로로 기록한다.
        int ToInt32(JToken token, string name)
        {
            string? reason = ReadInt32(token, out int number);
            if (reason != null) FailField(name, reason);
            return number;
        }

        // 정수 토큰만 읽고 long 표현 범위를 확인한다. long을 넘는 정수는 캐스트 예외 대신 토큰 값을 직접 비교해 거부한다.
        public long Int64(string name) => ToInt64(Raw(name), name);

        // Raw로 받은 토큰을 long으로 읽는다. 판정은 ReadInt64가 하고, 오류는 name 속성 경로로 기록한다.
        long ToInt64(JToken value, string name)
        {
            string? reason = ReadInt64(value, out long number);
            if (reason != null) FailField(name, reason);
            return number;
        }

        // 정수 또는 실수 토큰의 유한한 double을 읽는다.
        public double FiniteDouble(string name) => ToFiniteDouble(Raw(name), name);

        // Raw로 받은 토큰을 유한한 double로 변환한다.
        double ToFiniteDouble(JToken value, string name)
        {
            if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
            {
                FailField(name, "값은 숫자여야 합니다.");
                return 0d;
            }

            double number = (double)value;
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                FailField(name, "값은 유한수여야 합니다.");
                return 0d;
            }

            return number;
        }

        // 유한한 수를 읽고 float 표현 범위를 확인한다.
        public float FiniteFloat(string name) => ToFiniteFloat(Raw(name), name);

        // Raw로 받은 토큰을 유한한 double로 읽은 뒤 float 표현 범위를 확인한다.
        float ToFiniteFloat(JToken value, string name)
        {
            double number = ToFiniteDouble(value, name);
            if (number < float.MinValue || number > float.MaxValue)
            {
                FailField(name, "값이 실수 범위를 벗어났습니다.");
                return 0f;
            }

            return (float)number;
        }

        // 필수 속성의 명시 null은 null로, 값이 있으면 Int64 정수로 읽는다.
        public long? NullableInt64(string name) =>
            NullableValue<long>(name, static (reader, token, property) => reader.ToInt64(token, property));

        // 필수 속성의 명시 null은 null로, 값이 있으면 Int32 정수로 읽는다.
        public int? NullableInt32(string name) =>
            NullableValue<int>(name, static (reader, token, property) => reader.ToInt32(token, property));

        // 필수 속성의 명시 null은 null로, 값이 있으면 불리언으로 읽는다.
        public bool? NullableBoolean(string name) =>
            NullableValue<bool>(name, static (reader, token, property) => reader.ToBoolean(token, property));

        // 필수 속성의 명시 null은 null로, 값이 있으면 유한 float로 읽는다.
        public float? NullableFiniteFloat(string name) =>
            NullableValue<float>(name, static (reader, token, property) => reader.ToFiniteFloat(token, property));

        // 필수 속성의 명시 null은 null로, 값이 있으면 유한 double로 읽는다.
        public double? NullableFiniteDouble(string name) =>
            NullableValue<double>(name, static (reader, token, property) => reader.ToFiniteDouble(token, property));

        // 위 Nullable* 판독의 공통 규칙이다. 속성은 필수이고 명시 null을 허용한다. Raw로 토큰을 한 번만 찾아
        // null이 아니면 같은 토큰을 변환에 넘긴다. 변환은 정적 람다라 처음 한 번만 만들어져 호출마다 할당하지 않는다.
        T? NullableValue<T>(string name, Func<StrictObject, JToken, string, T> convert) where T : struct
        {
            JToken value = Raw(name);
            return value.Type == JTokenType.Null ? (T?)null : convert(this, value, name);
        }

        // 필수 중첩 객체를 같은 공유 상태로 읽는다.
        public T Object<T>(string name, Func<StrictObject, T> read) => ReadNested(Raw(name), FieldPath(name), read, _state);

        // 명시 null 또는 중첩 객체를 읽는다. 속성 자체는 필수다.
        public T? NullableObject<T>(string name, Func<StrictObject, T> read) where T : class
        {
            JToken value = Raw(name);
            return value.Type == JTokenType.Null ? null : ReadNested(value, FieldPath(name), read, _state);
        }

        // 필수 배열의 각 요소를 '<경로>[i]' 경로로 읽는다. 요소 판독기는 이 판독기를 받아 같은 공유 상태로 오류를 기록한다.
        // 실패하면 부분 결과 대신 빈 배열을 돌려준다.
        public IReadOnlyList<T> Array<T>(string name, Func<StrictObject, JToken, string, T> readItem) =>
            ToArray(Raw(name), name, readItem);

        // Raw로 받은 토큰을 배열로 읽는다. 이미 실패했으면 Raw가 null 토큰을 주므로 아래 배열 검사가 빈 배열을 돌려준다.
        IReadOnlyList<T> ToArray<T>(JToken value, string name, Func<StrictObject, JToken, string, T> readItem)
        {
            if (!(value is JArray items))
            {
                FailField(name, "값은 배열이어야 합니다.");
                return System.Array.Empty<T>();
            }

            var result = new T[items.Count];
            string path = FieldPath(name);
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = readItem(this, items[index], path + "[" + index + "]");
                if (Failed) return System.Array.Empty<T>();
            }

            return result;
        }

        // 필수 속성의 명시 null은 null 목록으로, 값이 있으면 요소 판독기로 배열을 읽는다.
        public IReadOnlyList<T>? NullableArray<T>(string name, Func<StrictObject, JToken, string, T> readItem)
        {
            JToken value = Raw(name);
            return value.Type == JTokenType.Null ? null : ToArray(value, name, readItem);
        }

        // 객체 요소 배열을 같은 공유 상태로 읽는다.
        public IReadOnlyList<T> ObjectArray<T>(string name, Func<StrictObject, T> read) =>
            Array(name, (reader, token, path) => reader.ObjectItem(token, path, read));

        // 배열 요소 객체 하나를 같은 공유 상태로 읽는다.
        public T ObjectItem<T>(JToken token, string path, Func<StrictObject, T> read) => ReadNested(token, path, read, _state);

        // 배열 요소 정수를 읽고 int 표현 범위를 확인한다. 판정은 필드 판독과 같은 ReadInt32가 하고, 오류에는 요소 경로를 붙인다.
        public int Int32Item(JToken token, string path)
        {
            string? reason = ReadInt32(token, out int number);
            if (reason != null) Record(_state, path + " " + reason);
            return number;
        }

        // 배열 요소 문자열을 읽는다. 빈 문자열은 허용하고, null이나 문자열이 아닌 값은 거부한다.
        public string TextItem(JToken token, string path)
        {
            if (token.Type == JTokenType.String)
                return (string)token!;
            // 필드 판독과 같은 사유 문구를 쓴다.
            Record(_state, path + " " + _notText);
            return string.Empty;
        }

        // 배열 요소의 명시 null 또는 문자열을 읽는다.
        public string? NullableTextItem(JToken token, string path) =>
            token.Type == JTokenType.Null ? null : TextItem(token, path);

        // 공유 상태로 중첩 객체를 읽는다. 이미 실패했으면 콜백을 부르지 않고 기본값을 돌려준다.
        // 콜백이 오류 없이 끝나야 읽지 않은 속성을 검사한다.
        static T ReadNested<T>(JToken? token, string path, Func<StrictObject, T> read, ReadState state)
        {
            if (state.Error != null) return default!;
            if (!(token is JObject value))
            {
                Record(state, path + " 값은 객체여야 합니다.");
                return default!;
            }

            var reader = new StrictObject(value, path, state);
            T result = read(reader);
            if (state.Error == null) reader.RejectUnconsumed();
            return result;
        }

        static void Record(ReadState state, string message)
        {
            if (state.Error == null) state.Error = message;
        }

        // JValue의 정수 표현을 long으로 변환한다. long 범위를 넘는 BigInteger와 ulong은 false다.
        static bool TryInt64(object? raw, out long number)
        {
            switch (raw)
            {
                case long value:
                    number = value;
                    return true;
                case int value:
                    number = value;
                    return true;
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case uint _:
                    number = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                    return true;
                case ulong value when value <= long.MaxValue:
                    number = (long)value;
                    return true;
                case BigInteger value when value >= long.MinValue && value <= long.MaxValue:
                    number = (long)value;
                    return true;
                default:
                    number = 0;
                    return false;
            }
        }

        // 정수 토큰을 long으로 바꾼다. 실패하면 경로 없는 사유를 돌려주고, 경로는 필드 판독과 배열 요소 판독이 각자 붙인다.
        static string? ReadInt64(JToken token, out long number)
        {
            number = 0;
            if (token.Type != JTokenType.Integer) return _notInteger;
            return TryInt64(((JValue)token).Value, out number) ? null : _outOfIntegerRange;
        }

        // long으로 읽은 뒤 int 표현 범위를 확인한다. 실패하면 number는 0이다.
        static string? ReadInt32(JToken token, out int number)
        {
            number = 0;
            string? reason = ReadInt64(token, out long wide);
            if (reason != null) return reason;
            if (wide < int.MinValue || wide > int.MaxValue) return _outOfIntegerRange;
            number = (int)wide;
            return null;
        }

        // 읽지 않은 첫 속성을 '<경로>.<이름> 필드는 허용되지 않습니다'로 거부한다.
        void RejectUnconsumed()
        {
            foreach (JProperty property in _value.Properties())
            {
                if (!_consumed.Contains(property.Name))
                {
                    FailField(property.Name, "필드는 허용되지 않습니다.");
                    return;
                }
            }
        }

        string FieldPath(string name) => _path + "." + name;
    }
}
