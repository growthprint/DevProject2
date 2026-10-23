#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 플레이어가 지도에 찍은 마커다. 슬롯의 캠페인과 함께 저장되는 불변 값이다.
    public sealed class MapMarkerState
    {
        // 원본 지도 마커의 아이콘 수다(유효 IconId는 0 이상 이 값 미만).
        internal const int _iconCount = 12;
        // 원본 지도 마커의 색상 수다(유효 ColorId는 0 이상 이 값 미만).
        internal const int _colorCount = 5;

        public string Id { get; }
        // 논리 씬 열거형이다. 저장 파일의 씬 문자열과의 변환은 저장 코덱이 GameScenes로 한다.
        public GameScene SceneId { get; }
        public string RegionId { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public int IconId { get; }
        public int ColorId { get; }

        // 신뢰할 수 있는 입력용 생성자이며 유일한 대입 경로다. 신뢰할 수 없는 입력은 TryCreate를 쓴다.
        public MapMarkerState(string id, GameScene sceneId, string regionId, float x, float y, float z, int iconId, int colorId)
        {
            Id = id; SceneId = sceneId; RegionId = regionId; X = x; Y = y; Z = z; IconId = iconId; ColorId = colorId;
        }

        // TryCreate가 쓰는 유일한 검증이다. 어떤 입력에도 던지지 않는다.
        // 열거형도 캐스트로 정의 밖 값을 담을 수 있으므로 씬은 GameScenes.All 범위 안인지 확인한다.
        static bool Check(string? id, GameScene sceneId, string? regionId, float x, float y, float z, int iconId, int colorId) =>
            !string.IsNullOrWhiteSpace(id) && (uint)sceneId < (uint)GameScenes.All.Count && !string.IsNullOrWhiteSpace(regionId) &&
            float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) &&
            iconId >= 0 && iconId < _iconCount && colorId >= 0 && colorId < _colorCount;

        // 저장 파일처럼 신뢰할 수 없는 입력용 생성 경로다. 검증에 실패하면 던지지 않고 false를 돌려준다.
        public static bool TryCreate(string id, GameScene sceneId, string regionId, float x, float y, float z, int iconId, int colorId,
            [NotNullWhen(true)] out MapMarkerState? value)
        {
            value = Check(id, sceneId, regionId, x, y, z, iconId, colorId)
                ? new MapMarkerState(id, sceneId, regionId, x, y, z, iconId, colorId) : null;
            return value != null;
        }
    }
}
