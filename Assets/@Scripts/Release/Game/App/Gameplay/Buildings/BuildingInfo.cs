#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Game.App
{
    // 원작 Duckov.Buildings.BuildingInfo 하나다. 가져오기 도구가 원작 BuildingDataCollection.asset 순서대로 채운다.
    // 현재 개수, 토큰, 요구 조건 판정은 상태를 가진 건물 소유자와 규칙이 맡으므로 여기에는 정의 값만 둔다.
    [Serializable]
    public sealed class BuildingInfo
    {
        [SerializeField] string _id = "";
        // 원작이 프리팹을 이름으로 찾던 키다. 런타임은 _prefab을 쓰고, 프리팹 제작 도구만 직렬화로 읽는다.
#pragma warning disable CS0414
        [SerializeField] string _prefabName = "";
#pragma warning restore CS0414
        [SerializeField] int _maxAmount;
        [SerializeField] Cost _cost;
        [SerializeField] string[] _requireBuildings = Array.Empty<string>();
        [SerializeField] string[] _alternativeFor = Array.Empty<string>();
        [SerializeField] int[] _requireQuests = Array.Empty<int>();
        [SerializeField] Sprite? _icon;
        // 원작은 프리팹의 Building.dimensions를 읽는다. 배치 규칙이 프리팹을 로드하지 않고 칸을 세도록 가져오기 도구가 복사한다.
        [SerializeField] Vector2Int _dimensions;
        // 제품 건물 프리팹이다. 프리팹 제작 도구가 채우기 전에는 비어 있다.
        [SerializeField] AssetReferenceGameObject _prefab = new AssetReferenceGameObject("");

        public string Id => _id;
        // 원작처럼 0 이하는 개수 제한이 없다는 뜻이다.
        public int MaxAmount => _maxAmount;
        public Cost Cost => _cost;
        public IReadOnlyList<string> RequireBuildings => _requireBuildings;
        // 원작 BuildingManager.Any가 이 건물을 다른 ID의 대체로도 센다. 원작 데이터는 모두 비어 있다.
        public IReadOnlyList<string> AlternativeFor => _alternativeFor;
        public IReadOnlyList<int> RequireQuests => _requireQuests;
        public Sprite? Icon => _icon;
        public Vector2Int Dimensions => _dimensions;
        public AssetReferenceGameObject Prefab => _prefab;

        // 원작 Valid처럼 ID가 있어야 쓸 수 있는 정의다.
        public bool Valid => !string.IsNullOrEmpty(_id);
        public string DisplayNameKey => "Building_" + _id;
        // 원작 설명 키 형식대로 이름 키 끝에 설명 접미사를 붙인다.
        public string DescriptionKey => GameLocalization.DescriptionKey(DisplayNameKey);
    }
}
