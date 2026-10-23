#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 BuildingManager, BuildingArea, BuildingInfo에 흩어진 격자 계산과 건설 조건이다. 건설 화면과 배치 명령이 같은 함수를 써서
    // 목록과 명령의 판정이 어긋나지 않게 한다. 좌표는 원작 월드 정수 좌표이고, 상태는 받은 소유자의 조회로만 읽는다.
    internal static class BuildingRules
    {
        // 원작 공통 규칙대로 회전이 홀수면 폭과 높이를 바꾼다.
        internal static Vector2Int RotatedDimensions(Vector2Int dimensions, BuildingRotation rotation) =>
            (int)rotation % 2 != 0 ? new Vector2Int(dimensions.y, dimensions.x) : dimensions;

        // 원작 GetOccupyingCoords다. 좌하단 좌표부터 한 줄씩 채운다. 건설 화면의 칸 표시가 쓴다.
        internal static Vector2Int[] GetOccupyingCoords(Vector2Int dimensions, BuildingRotation rotation, Vector2Int coord)
        {
            Vector2Int size = RotatedDimensions(dimensions, rotation);
            var coords = new Vector2Int[size.x * size.y];
            for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                    coords[x + size.x * y] = coord + new Vector2Int(x, y);
            return coords;
        }

        // cell이 coord에 놓인 건물의 점유 칸 안인지다. 점유 칸은 직사각형이라 칸 목록을 만들지 않고 구간으로 본다.
        internal static bool Occupies(Vector2Int dimensions, BuildingRotation rotation, Vector2Int coord, Vector2Int cell)
        {
            Vector2Int size = RotatedDimensions(dimensions, rotation);
            return cell.x >= coord.x && cell.x < coord.x + size.x && cell.y >= coord.y && cell.y < coord.y + size.y;
        }

        // 원작 Collide처럼 두 건물의 점유 칸이 하나라도 같으면 참이다. 매 프레임 미리보기가 부를 수 있어 구간으로 비교한다.
        // 크기가 0인 건물은 칸이 없어 겹치지 않는다.
        internal static bool Overlaps(Vector2Int firstDimensions, BuildingRotation firstRotation, Vector2Int first,
            Vector2Int secondDimensions, BuildingRotation secondRotation, Vector2Int second)
        {
            Vector2Int a = RotatedDimensions(firstDimensions, firstRotation);
            Vector2Int b = RotatedDimensions(secondDimensions, secondRotation);
            if (a.x <= 0 || a.y <= 0 || b.x <= 0 || b.y <= 0) return false;
            return first.x < second.x + b.x && second.x < first.x + a.x && first.y < second.y + b.y && second.y < first.y + a.y;
        }

        // 원작 CoordToWorldPosition과 BuildingData.GetTransformPosition이다. 점유 칸 직사각형의 가운데다.
        internal static Vector3 CoordToWorldPosition(Vector2Int coord, Vector2Int dimensions, BuildingRotation rotation)
        {
            Vector2Int size = RotatedDimensions(dimensions, rotation);
            return new Vector3(coord.x - 0.5f + size.x / 2f, 0f, coord.y - 0.5f + size.y / 2f);
        }

        // 원작 CursorToCoord다. 커서 칸을 건물 가운데로 보도록 반올림한 좌표에서 회전한 크기의 절반(정수 나눗셈)을 뺀다.
        internal static Vector2Int CursorToCoord(Vector3 point, Vector2Int dimensions, BuildingRotation rotation)
        {
            Vector2Int size = RotatedDimensions(dimensions, rotation);
            return new Vector2Int(Mathf.RoundToInt(point.x) - size.x / 2, Mathf.RoundToInt(point.z) - size.y / 2);
        }

        // 원작 IsPlacementWithinRange 식 그대로다. size는 중심에서의 반폭이라 (17, 13)이면 33×25칸이 된다.
        internal static bool IsPlacementWithinRange(Vector2Int centerCoord, Vector2Int size, Vector2Int dimensions, BuildingRotation rotation,
            Vector2Int coord)
        {
            Vector2Int rotated = RotatedDimensions(dimensions, rotation);
            Vector2Int local = coord - centerCoord;
            return local.x > -size.x && local.y > -size.y && local.x + rotated.x <= size.x && local.y + rotated.y <= size.y;
        }

        // 원작 IsValidPlacement다. 범위, 같은 영역 설치 건물과의 칸 겹침, 물리 겹침 순서로 본다. 물리 검사가 가장 비싸 마지막이다.
        // 건설 화면 미리보기와 배치 명령이 같은 판정을 쓴다.
        internal static bool IsValidPlacement(BuildingArea area, BuildingManager buildings, Vector2Int dimensions, BuildingRotation rotation,
            Vector2Int coord) =>
            IsPlacementWithinRange(area.CenterCoord, area.Size, dimensions, rotation, coord)
            && !buildings.Collides(area.AreaId, dimensions, rotation, coord)
            && !area.PhysicsCollide(dimensions, rotation, coord);

        // 원작 BuildingInfo.ReachedAmountLimit다. 최대 개수가 0 이하이면 제한이 없다.
        internal static bool ReachedAmountLimit(BuildingInfo info, BuildingManager buildings) =>
            info.MaxAmount > 0 && buildings.GetBuildingAmount(info.Id) >= info.MaxAmount;

        // 원작 RequirementsSatisfied다. 요구 건물은 토큰을 빼고 세고, 요구 퀘스트는 모두 끝나야 한다.
        // 개집은 격자에 놓을 수 없어 Any("PetHouse")가 늘 거짓이므로, 개집을 요구하는 정의는 따로 막지 않아도 조건을 넘지 못한다.
        internal static bool RequirementsSatisfied(BuildingInfo info, BuildingManager buildings, QuestManager quests)
        {
            IReadOnlyList<string> required = info.RequireBuildings;
            for (int index = 0; index < required.Count; index++)
                if (!buildings.Any(required[index])) return false;
            return quests.AreFinished(info.RequireQuests);
        }

        // 원작 BuildingSelectionPanel의 목록 규칙이다. 지금 지어져 있는 건물은 요구 건물을 회수해도 목록에 남는다.
        internal static bool IsListed(BuildingInfo info, BuildingManager buildings, QuestManager quests) =>
            buildings.GetBuildingAmount(info.Id) > 0 || RequirementsSatisfied(info, buildings, quests);
    }
}
