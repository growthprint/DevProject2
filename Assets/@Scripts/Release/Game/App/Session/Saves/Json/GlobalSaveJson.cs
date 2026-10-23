#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using DevProject.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Game.App.SaveJsonFields;
using SharedJsonSerializer = DevProject.Serialization.JsonSerializer;

namespace Game.App
{
    // 슬롯 밖 공통 파일(Saves/global.json, 사용자 결정 D2)을 읽고 쓴다. 필드는 정확히 endowmentUnlocks와 rageUnlocked이고,
    // 둘 다 필수이며 모르는 필드는 거부한다. 값 규칙(None 제외, 중복 거부)은 GlobalProgressState.TryCreate가 검사한다.
    internal sealed class GlobalSaveJson : IJsonCodec<GlobalProgressState>
    {
        // 원작 Endowment_Unlock_R_<이름>의 <이름>, 곧 원작 열거 이름이다. 열거형 이름이 바뀌어도 형식이 유지되도록 고정 토큰으로 쓴다.
        // 순서는 EndowmentIndex 값 1~4와 같아 위치 + 1이 그 값이다. None은 해금 대상이 아니라 없다.
        static readonly string[] _endowmentTokens = { "Surviver", "Porter", "Berserker", "Marksman" };

        // 구성 지점이 넘긴 공용 JSON 설정을 쓴다.
        readonly SharedJsonSerializer _json;

        internal GlobalSaveJson(SharedJsonSerializer json)
        {
            _json = json;
        }

        // 예외 없이 읽는다. 구문 오류는 'global: <파서 오류>', 형식 오류는 'global.<경로> <사유>', 값 거부는 'global: 값이 올바르지 않습니다.'다.
        public bool TryRead(string json, [NotNullWhen(true)] out GlobalProgressState? value, out string error)
        {
            value = null;
            if (!_json.TryReadObject(json, out JObject? root, out string jsonError))
            {
                error = "global: " + jsonError;
                return false;
            }
            return StrictObject.TryRead<GlobalProgressState>(root, "global", ReadRoot, out value, out error);
        }

        // 검증된 값만 받으므로 재능은 늘 1~4다.
        public string Write(GlobalProgressState value, bool pretty = false)
        {
            var root = new JObject
            {
                ["endowmentUnlocks"] = WriteList(value.EndowmentUnlocks, index => new JValue(_endowmentTokens[(int)index - 1])),
                ["rageUnlocked"] = value.RageUnlocked
            };
            return root.ToString(pretty ? Formatting.Indented : Formatting.None);
        }

        static GlobalProgressState ReadRoot(StrictObject value)
        {
            IReadOnlyList<EndowmentIndex> unlocks = value.Array("endowmentUnlocks", ReadEndowment);
            bool rageUnlocked = value.Boolean("rageUnlocked");
            return value.Create(GlobalProgressState.TryCreate(unlocks, rageUnlocked, out GlobalProgressState? created), created);
        }

        // 토큰은 대소문자를 구분해 찾는다. 모르는 토큰은 '<항목 경로> 알 수 없는 재능입니다.'를 기록하고, 판독이 실패하므로 쓰이지 않을 None을 돌려준다.
        static EndowmentIndex ReadEndowment(StrictObject reader, JToken token, string path)
        {
            string text = reader.TextItem(token, path);
            for (int position = 0; position < _endowmentTokens.Length; position++)
                if (string.Equals(_endowmentTokens[position], text, StringComparison.Ordinal)) return (EndowmentIndex)(position + 1);
            reader.Fail(path + " 알 수 없는 재능입니다.");
            return EndowmentIndex.None;
        }
    }
}
