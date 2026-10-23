#nullable enable

using System.Diagnostics.CodeAnalysis;
using DuckCharacter.AvatarParts;

namespace Game.App
{
    // 저장과 화면에 넘기는 변경 불가능한 세션 값이다. 씬, 외형, 캠페인을 담는다. 외형은 복사해서 내주고, 불변인 캠페인은 그대로 공유한다.
    // 소유 아이템은 캠페인의 원본 아이템 그래프(Campaign.Gameplay.Inventory.Items)가 갖는다.
    public sealed class GameSessionSnapshot
    {
        // 검증을 마친 외형을 복제 없이 공유하는 생성자의 표식이다. 공개 생성자와 매개변수 형식이 같아 구분이 필요하다.
        enum Trust { Validated }

        // 밖에서 고칠 수 없게 보관한 외형이다. 내줄 때는 복사본을 준다.
        readonly DuckAvatarPreset _appearance;

        // 코드가 만든 값용 생성자다. Release에서는 TryCreate와 새 게임 기본 세션(GameRuntime.CreateDefaultSession)만 부른다.
        // 저장 파일에서 읽은 값은 TryCreate를 쓴다.
        public GameSessionSnapshot(GameScene sceneId, DuckAvatarPreset appearance, CampaignState campaign)
        {
            SceneId = sceneId;
            Campaign = campaign;
            _appearance = appearance.Clone();
        }

        // TryCreate가 쓰는 유일한 검증이며, 저장을 불러올 때 거치는 관문이다. 어떤 입력에도 던지지 않는다.
        static bool Check(GameScene sceneId, DuckAvatarPreset? appearance, CampaignState? campaign)
        {
            // 세션 중 외형은 PlayerAppearance가 소유해 스냅샷의 외형을 바꾸는 경로가 없으므로, 외형 검증은 이 관문에서만 한다.
            if (appearance == null || !appearance.TryValidate()) return false;
            // 열거형도 캐스트로 정의 밖 값을 담을 수 있으므로 GameScenes.All 범위 안인지 확인한다.
            return (uint)sceneId < (uint)GameScenes.All.Count && campaign is not null;
        }

        // 저장 파일에서 읽은 값용 생성 경로다. 어떤 입력에도 던지지 않는다.
        public static bool TryCreate(GameScene sceneId, DuckAvatarPreset appearance, CampaignState campaign,
            [NotNullWhen(true)] out GameSessionSnapshot? value)
        {
            value = Check(sceneId, appearance, campaign) ? new GameSessionSnapshot(sceneId, appearance, campaign) : null;
            return value != null;
        }

        // With* 전용 생성자다. 이미 검증한 외형을 복제나 재검증 없이 공유한다.
        // 밖으로는 복사본만 내주므로, 공유해도 이미 내준 스냅샷은 바뀌지 않는다.
        GameSessionSnapshot(Trust trust, GameScene sceneId, DuckAvatarPreset sharedAppearance, CampaignState campaign)
        {
            SceneId = sceneId;
            Campaign = campaign;
            _appearance = sharedAppearance;
        }

        // 논리 씬 열거형이다. 저장 파일의 씬 문자열과의 변환은 GameScenes가 맡는다.
        public GameScene SceneId { get; }
        // 진행, 생존 상태, 정산 원장, 원본 플레이 상태를 담은 불변 값이다.
        public CampaignState Campaign { get; }
        // 저장 기록이나 새 게임 기본값의 외형이며 세션 동안 갱신하지 않는다. 현재 외형은 PlayerAppearance가 소유하고 저장도 그 값을 쓴다.
        // 호출자가 외형 안의 배열을 고쳐도 스냅샷이 바뀌지 않도록 매번 복사본을 준다.
        public DuckAvatarPreset Appearance => _appearance.Clone();

        // 신뢰할 수 있는 씬 변경 경로다. 씬만 바꾸고 외형과 캠페인은 복제 없이 공유한다.
        public GameSessionSnapshot WithScene(GameScene sceneId) => new GameSessionSnapshot(Trust.Validated, sceneId, _appearance, Campaign);

        // 신뢰할 수 있는 진행 변경 경로다. 캠페인만 바꾸고 외형은 복제 없이 공유한다.
        public GameSessionSnapshot WithCampaign(CampaignState campaign)
        {
            return new GameSessionSnapshot(Trust.Validated, SceneId, _appearance, campaign);
        }
    }
}
