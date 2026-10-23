#nullable enable
using UnityEngine;

namespace Game.App
{
    // 원작 BuildingManager.BuyAndPlace다. 설치와 토큰은 BuildingManager가, 돈과 비용 아이템은 경제 소유자와 세션 소지품이 소유한다.
    // 원작은 요구 조건과 배치 위치를 건설 화면에서만 거른다. 이 명령은 그 둘도 다시 확인하지만, 플레이어가 얻는 결과는 원작과 같다.
    internal static partial class GameplayCommands
    {
        // 관문, 정의, 최대 개수, 목록 규칙, 범위와 겹침, 토큰이나 비용을 모두 확인한 뒤 토큰을 쓰거나 비용을 내고 놓는다.
        // 원작처럼 토큰이 있으면 비용 없이 토큰 하나를 쓴다. 비용은 계좌와 현금을 모두 쓰고 기지에서는 창고도 출처다.
        // 거절이면 기록 없이 false이고 아무것도 바뀌지 않는다. 원작 성공 소리(UI/building_up)는 결과를 받은 건설 화면이 낸다.
        internal static bool TryBuyAndPlaceBuilding(GameSessionManager session, EconomyManager economy, BuildingManager buildings,
            QuestManager quests, BuildingArea area, string buildingId, Vector2Int coord, BuildingRotation rotation,
            out BuildingBuyAndPlaceResults result)
        {
            // 건물 관문을 결제 전에 본다. 결제 뒤에 놓기가 거절되면 낸 비용이 사라진다. 원작에서 소유자가 없을 때의 값을 돌려준다.
            if (!buildings.AcceptsCommands) return RejectBuilding(BuildingBuyAndPlaceResults.NoReferences, out result);
            if (!GameplayDataSettings.BuildingDataCollection.TryGetInfo(buildingId, out BuildingInfo? info))
                return RejectBuilding(BuildingBuyAndPlaceResults.InvalidBuildingInfo, out result);
            if (BuildingRules.ReachedAmountLimit(info, buildings)) return RejectBuilding(BuildingBuyAndPlaceResults.ReachedAmountLimit, out result);
            if (!BuildingRules.IsListed(info, buildings, quests)) return RejectBuilding(BuildingBuyAndPlaceResults.RequirementsNotMet, out result);
            Vector2Int dimensions = info.Dimensions;
            // 건설 화면 미리보기와 같은 배치 판정으로 다시 확인해, 화면과 명령의 판정이 어긋나지 않게 한다.
            if (!BuildingRules.IsValidPlacement(area, buildings, dimensions, rotation, coord))
                return RejectBuilding(BuildingBuyAndPlaceResults.InvalidPlacement, out result);
            bool useToken = buildings.GetTokenAmount(buildingId) > 0;
            // TryPay가 두 관문과 계좌, 현금, 아이템을 모두 확인한 뒤 아이템, 그다음 계좌 순서로 낸다. 거절이면 아무것도 바뀌지 않았다.
            if (!useToken && !TryPay(session, economy, info.Cost, ItemSourceRules.Owned(session), accountAvailable: true))
                return RejectBuilding(BuildingBuyAndPlaceResults.PaymentFailure, out result);
            // 같은 호출에서 관문과 토큰을 확인했고 영역 ID는 씬 저작 값이라 놓기는 늘 성공한다. 그래도 결과를 그대로 돌려준다.
            bool placed = buildings.TryPlace(area.AreaId, buildingId, coord, rotation, useToken, out _);
            result = placed ? BuildingBuyAndPlaceResults.Succeed : BuildingBuyAndPlaceResults.NoReferences;
            return placed;
        }

        // 같은 partial 클래스의 다른 명령 파일과 이름이 겹치지 않게 건설 이름을 붙인다.
        static bool RejectBuilding(BuildingBuyAndPlaceResults reason, out BuildingBuyAndPlaceResults result)
        {
            result = reason;
            return false;
        }
    }
}
