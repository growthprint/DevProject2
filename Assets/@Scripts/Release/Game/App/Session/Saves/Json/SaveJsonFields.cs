#nullable enable

using System;
using System.Collections.Generic;
using DevProject.Serialization;
using Newtonsoft.Json.Linq;

namespace Game.App
{
    // 게임 저장 코덱들이 함께 쓰는 필드 표기 규칙이다. null과 빈 값의 구분, 목록 읽기와 쓰기, 씬 문자열과 모르는 토큰의 오류 문구를 맡는다.
    internal static class SaveJsonFields
    {
        // 저장 씬 문자열을 논리 씬으로 읽는다. 루트와 지도 마커가 함께 쓴다. GameScenes에 없는 문자열이면
        // '<경로>.<이름> 알 수 없는 씬입니다.'를 기록한다. 판독이 이미 실패했으므로 돌려주는 기본값은 쓰이지 않는다.
        internal static GameScene ReadScene(StrictObject value, string name)
        {
            string text = value.Text(name);
            if (value.Failed) return default;
            if (GameScenes.TryParse(text, out GameScene scene)) return scene;
            value.FailField(name, "알 수 없는 씬입니다.");
            return default;
        }

        // 정수와 문자열 배열 항목을 읽는 공용 대리자다. 코덱마다 같은 람다를 다시 적지 않고, 대리자도 한 번만 만든다.
        internal static readonly Func<StrictObject, JToken, string, int> _int32Item =
            static (reader, token, path) => reader.Int32Item(token, path);
        internal static readonly Func<StrictObject, JToken, string, string> _textItem =
            static (reader, token, path) => reader.TextItem(token, path);

        // 없으면 빈 목록으로 읽고 명시 null은 거부하는 선택 배열 필드다. 정수, 문자열, 객체 항목별로 둔다.
        internal static IReadOnlyList<int> OptionalInt32Array(StrictObject value, string name) =>
            value.Optional(name, (IReadOnlyList<int>)Array.Empty<int>(), static (owner, field) => owner.Array(field, _int32Item));
        internal static IReadOnlyList<string> OptionalTextArray(StrictObject value, string name) =>
            value.Optional(name, (IReadOnlyList<string>)Array.Empty<string>(), static (owner, field) => owner.Array(field, _textItem));
        internal static IReadOnlyList<T> OptionalObjectArray<T>(StrictObject value, string name, Func<StrictObject, T> read) =>
            value.Optional(name, (IReadOnlyList<T>)Array.Empty<T>(), (owner, field) => owner.ObjectArray(field, read));

        // 아래는 쓰기 공용 도우미다.
        internal static JArray WriteList<T>(IReadOnlyList<T> values, Func<T, JToken> write)
        {
            var result = new JArray();
            foreach (T value in values) result.Add(write(value));
            return result;
        }
        // 정수와 문자열 목록은 항목을 그대로 JSON 값으로 쓴다.
        internal static JArray WriteInts(IReadOnlyList<int> values) => WriteList(values, static value => new JValue(value));
        internal static JArray WriteTexts(IReadOnlyList<string> values) => WriteList(values, static value => new JValue(value));
        // 미포착(null) 목록은 명시 null로, 값이 있으면 배열로 쓴다.
        internal static JToken WriteNullableList<T>(IReadOnlyList<T>? values, Func<T, JToken> write) =>
            values == null ? (JToken)JValue.CreateNull() : WriteList(values, write);
        // 미포착(null) 하위 객체는 명시 null로, 값이 있으면 주어진 쓰기 함수로 쓴다.
        internal static JToken WriteOptional<T>(T? value, Func<T, JToken> write) where T : class => value == null ? JValue.CreateNull() : write(value);
        // 값이 없는 nullable 값형은 명시 null로, 값이 있으면 수로 쓴다.
        internal static JToken NumberOrNull<T>(T? value) where T : struct => value.HasValue ? new JValue(value.Value) : JValue.CreateNull();
        // null 문자열은 명시 null로, 빈 문자열을 포함한 값은 문자열로 쓴다.
        internal static JToken NullableString(string? value) => value == null ? JValue.CreateNull() : new JValue(value);

        // 저장 토큰을 대소문자를 구분해 값 목록에서 찾는다. 각 값의 토큰은 쓰기 함수로 얻어 토큰을 쓰기 쪽 한 곳에만 적는다.
        // 대소문자가 다르거나 숫자인 토큰은 던지지 않고 '<경로>.<이름> <사유>'로 기록한다. 판독이 실패했으므로 돌려주는 목록의 첫 값은 쓰이지 않는다.
        internal static T ReadToken<T>(StrictObject value, string name, T[] values, Func<T, string> tokenOf, string reason)
            where T : struct, Enum
        {
            string text = value.Text(name);
            for (int index = 0; index < values.Length; index++)
                if (string.Equals(tokenOf(values[index]), text, StringComparison.Ordinal)) return values[index];
            value.FailField(name, reason);
            return values[0];
        }
    }
}
