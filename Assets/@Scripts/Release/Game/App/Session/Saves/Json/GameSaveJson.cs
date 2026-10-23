#nullable enable

using System.Diagnostics.CodeAnalysis;
using DevProject.Serialization;
using DuckCharacter.AvatarParts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharedJsonSerializer = DevProject.Serialization.JsonSerializer;

namespace Game.App
{
    // 게임 저장 스냅샷을 JSON으로 읽고 쓴다. 읽기는 현재 저장 형식만 받고, 쓰기는 같은 필드를 빠짐없이 기록한다.
    // 이 클래스는 루트 네 필드만 맡고, 캠페인과 플레이 상태, 아이템 그래프는 각 저장 코덱(CampaignSaveJson 등)이 맡는다.
    public sealed class GameSaveJson : IJsonCodec<GameSessionSnapshot>
    {
        // 구성 단위가 소유한 공용 JSON 설정을 함께 쓴다.
        readonly SharedJsonSerializer _json;
        // 중첩 외형 변환은 주입한 외형 코덱에 맡긴다.
        readonly IJsonCodec<DuckAvatarPreset> _appearanceJson;

        public GameSaveJson(SharedJsonSerializer json, IJsonCodec<DuckAvatarPreset> appearanceJson)
        {
            // 구성 지점은 늘 두 인수를 넘긴다.
            _json = json;
            _appearanceJson = appearanceJson;
        }

        // 같은 공용 JSON으로 원작 외형 코덱(DuckovAppearanceJson)을 연결해 만든다.
        // 외형 코덱을 직접 받는 생성자는 테스트가 대역 코덱을 넣을 때도 쓴다.
        public static GameSaveJson CreateDefault(SharedJsonSerializer json) =>
            new GameSaveJson(json, new DuckovAppearanceJson(json));

        // 루트 객체를 예외 없이 읽고 성공 여부를 돌려준다. 모든 필드는 필수이고 읽지 않은 필드는 거부한다.
        // 실패 문구는 이렇다. JSON 구문, 중복 키, 깊이, 추가 내용, null 입력은 'gameSave: <파서 오류>'다. 형식 오류는 'gameSave.<경로> <사유>'다.
        // 값 팩토리 거부는 '<객체 경로>: 값이 올바르지 않습니다.'이고, 중첩 외형 실패는 외형 코덱 문구 그대로다.
        public bool TryRead(string json, [NotNullWhen(true)] out GameSessionSnapshot? value, out string error)
        {
            value = null;
            if (!_json.TryReadObject(json, out JObject? root, out string jsonError))
            {
                error = "gameSave: " + jsonError;
                return false;
            }

            return StrictObject.TryRead<GameSessionSnapshot>(root, "gameSave", ReadRoot, out value, out error);
        }

        // 루트 네 필드를 읽는다. 원작 외형 JSON은 주입한 코덱이 읽고, 로컬 표시 메타데이터 두 필드는 여기서 외형에 넣는다.
        // displayName은 빈 문자열도 받는다. 앞에서 오류가 났으면 외형을 건드리지 않고 스냅샷도 만들지 않는다.
        GameSessionSnapshot ReadRoot(StrictObject root)
        {
            GameScene sceneId = SaveJsonFields.ReadScene(root, "sceneId");
            string appearanceText = root.Text("appearanceJson");
            DuckAvatarPreset? appearance = null;
            if (!root.Failed && !_appearanceJson.TryRead(appearanceText, out appearance, out string appearanceError))
                root.Fail(appearanceError);
            root.Object("appearanceMetadata", metadata =>
            {
                string displayName = metadata.Text("displayName");
                bool showInCustomizationScreen = metadata.Boolean("showInCustomizationScreen");
                if (root.Failed || appearance == null) return false;
                appearance._displayName = displayName;
                appearance._showInCustomizationScreen = showInCustomizationScreen;
                return true;
            });
            CampaignState campaign = root.Object("campaign", CampaignSaveJson.Read);
            if (root.Failed) return null!;
            return root.Create(GameSessionSnapshot.TryCreate(sceneId, appearance!, campaign, out GameSessionSnapshot? snapshot), snapshot);
        }

        // 루트 네 필드를 저장 형식의 속성 순서와 토큰 형식대로 기록한다. 소유 아이템은 campaign.gameplay의 원본 그래프에만 있다.
        // Appearance getter는 깊은 복사라 한 번만 받아 외형 JSON과 메타데이터에 함께 쓴다.
        public string Write(GameSessionSnapshot value, bool pretty = false)
        {
            DuckAvatarPreset appearance = value.Appearance;
            var envelope = new JObject
            {
                // 논리 씬은 GameScenes가 정한 저장 문자열로 기록한다.
                ["sceneId"] = GameScenes.Id(value.SceneId),
                ["appearanceJson"] = _appearanceJson.Write(appearance),
                ["appearanceMetadata"] = new JObject
                {
                    ["displayName"] = appearance._displayName ?? string.Empty,
                    ["showInCustomizationScreen"] = appearance._showInCustomizationScreen
                },
                ["campaign"] = CampaignSaveJson.Write(value.Campaign)
            };
            return envelope.ToString(pretty ? Formatting.Indented : Formatting.None);
        }
    }
}
