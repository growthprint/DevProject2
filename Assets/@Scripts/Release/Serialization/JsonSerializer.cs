#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace DevProject.Serialization
{
    // 기능 영역과 무관하게 프로젝트 전체가 함께 쓰는 Newtonsoft 설정과 객체 읽기 규칙이다.
    // 구성 지점이 인스턴스를 만들어 각 코덱에 넘긴다.
    public sealed class JsonSerializer
    {
        // 문자열 판독과 객체 변환에 같은 중첩 깊이 제한을 쓴다.
        const int _maxDepth = 32;

        // 설정과 계약 캐시는 변경 가능한 상태라 전역으로 두지 않고 인스턴스마다 따로 둔다.
        readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { IgnoreSerializableAttribute = false },
            DateParseHandling = DateParseHandling.None,
            TypeNameHandling = TypeNameHandling.None,
            MaxDepth = _maxDepth
        };

        // 중복 키는 오류로 거부하고 주석은 건너뛰는 판독 설정이다. 판독기는 설정을 읽기만 하므로 여러 스레드의 동시 판독이 함께 써도 된다.
        readonly JsonLoadSettings _loadSettings = new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
            CommentHandling = CommentHandling.Ignore
        };

        // 변환마다 serializer를 새로 만들어 상태가 섞이지 않게 하고, 계약 캐시는 같은 설정을 통해 재사용한다.
        public Newtonsoft.Json.JsonSerializer CreateSerializer()
        {
            return Newtonsoft.Json.JsonSerializer.Create(_settings);
        }

        // 문자열을 JSON 객체 하나로 읽는다. 구문 오류, 중복 키, 깊이 초과, 객체 뒤 추가 내용은 false와 오류 문구로 알린다.
        // 호출마다 판독기를 새로 만들어 서로 다른 입력을 동시에 읽어도 상태가 섞이지 않는다.
        public bool TryReadObject(string? json, [NotNullWhen(true)] out JObject? root, out string error)
        {
            root = null;
            // null을 판독기에 넘기면 예외가 나므로 먼저 판독 실패로 돌려준다.
            if (json == null)
            {
                error = "JSON 문자열이 없습니다.";
                return false;
            }

            using StringReader input = new StringReader(json);
            using JsonTextReader reader = new JsonTextReader(input)
            {
                DateParseHandling = DateParseHandling.None,
                MaxDepth = _maxDepth
            };

            try
            {
                // 판독마다 같은 설정이라 호출마다 새로 만들지 않고 인스턴스가 가진 설정을 쓴다.
                JObject value = JObject.Load(reader, _loadSettings);

                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.Comment)
                    {
                        error = "JSON 객체 뒤에 추가 내용이 있습니다.";
                        return false;
                    }
                }

                root = value;
                error = string.Empty;
                return true;
            }
            // 예외 경계: Newtonsoft 판독기는 구문 오류, 중복 키, 깊이 초과를 예외로 던지므로 오류 문구로 바꾼다.
            catch (JsonException exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }
}
