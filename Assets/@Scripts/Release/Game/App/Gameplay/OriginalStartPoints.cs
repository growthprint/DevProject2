#nullable enable
using System;
using UnityEngine;

namespace Game.App
{
    // 원작 하위 씬 위치 중 StartPoints/와 StartPoints_<n>/ 아래 출발점 목록이다. 원작 LevelManager.GetPlayerStartLocation처럼
    // 지도 선택 출격이 비콘 번호의 목록에서 무작위로 하나를 고르고, 그 번호 목록이 없으면 StartPoints로 되돌아간다.
    // 전투 월드 컨트롤러와 같은 오브젝트에 두고, 제작 도구(Game/Foundation/Author Ground Zero Start Points)가 씬의 원작 위치로 채운다.
    [DisallowMultipleComponent]
    public sealed class OriginalStartPoints : MonoBehaviour
    {
        // 출발점 하나다. 비콘 번호 0은 원작 StartPoints, n은 StartPoints_n이다. 하위 씬 번호는 컨트롤러 하위 씬 목록의 번호이고,
        // 하위 씬 루트 밖이면 -1이라 지금 하위 씬을 그대로 둔다.
        [Serializable]
        struct Point
        {
            [SerializeField] internal int _beaconIndex;
            [SerializeField] internal Vector3 _position;
            [SerializeField] internal int _subscene;
        }

        [SerializeField] Point[] _points = Array.Empty<Point>();

        // 원작 GetPlayerStartLocation이다. 고를 목록이 없으면 false다. 매 출격 한 번만 불려도 할당 없이 두 번 훑는다.
        internal bool TryPick(int beaconIndex, System.Random random, out Vector3 position, out int subscene)
        {
            int key = beaconIndex > 0 && Count(beaconIndex) > 0 ? beaconIndex : 0;
            int count = Count(key);
            position = default;
            subscene = -1;
            if (count == 0) return false;
            int pick = random.Next(count);
            foreach (Point point in _points)
            {
                if (point._beaconIndex != key) continue;
                if (pick-- > 0) continue;
                position = point._position;
                subscene = point._subscene;
                return true;
            }
            return false;
        }

        int Count(int beaconIndex)
        {
            int count = 0;
            foreach (Point point in _points)
                if (point._beaconIndex == beaconIndex) count++;
            return count;
        }
    }
}
