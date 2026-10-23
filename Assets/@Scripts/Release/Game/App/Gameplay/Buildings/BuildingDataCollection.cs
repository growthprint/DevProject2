#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.Buildings.BuildingDataCollection이다. 건물 정의 27개를 원작 순서로 담고, 그 순서가 건설 화면 목록 순서다.
    // 원작의 prefabs 목록은 정의마다 둔 제품 프리팹 참조가 대신한다. GameplayDataSettings.BuildingDataCollection으로 앱 전체가 읽는다.
    // 개집(PetHouse) 정의도 원작 데이터 그대로 남긴다. 격자와 건설 조건에서 빼는 일은 건물 규칙이 맡는다.
    [CreateAssetMenu(menuName = "Settings/Building Data Collection")]
    public sealed class BuildingDataCollection : ScriptableObject
    {
        [SerializeField] BuildingInfo[] _infos = Array.Empty<BuildingInfo>();

        public IReadOnlyList<BuildingInfo> Infos => _infos;

        // 원작 GetInfo는 없으면 빈 정의를 돌려준다. 여기서는 정의 유효성까지 보고 false로 알린다. 27개라 차례로 찾는다.
        public bool TryGetInfo(string id, [NotNullWhen(true)] out BuildingInfo? info)
        {
            foreach (BuildingInfo candidate in _infos)
            {
                if (candidate.Valid && string.Equals(candidate.Id, id, StringComparison.Ordinal))
                {
                    info = candidate;
                    return true;
                }
            }
            info = null;
            return false;
        }
    }
}
