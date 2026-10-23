#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 RequireBeaconUnlocked 조건 하나다. 그 장면의 그 번호 비콘이 해금돼 있어야 항목을 누를 수 있다.
    [Serializable]
    public sealed class MapSelectionBeaconRequirement
    {
        [SerializeField] string _sceneId = string.Empty;
        [SerializeField] int _beaconIndex;

        internal string SceneId => _sceneId;
        internal int BeaconIndex => _beaconIndex;
    }

    // 원작 MapSelectionEntry 하나다. 누르면 비콘 번호를 다음 출발점 계산에 넘기고, 확인하면 비용을 낸 뒤 장면을 연다.
    [Serializable]
    public sealed class MapSelectionEntryData
    {
        // 원작 항목 GameObject 이름이다. 지도 선택 화면을 재구성할 때 원작 항목과 짝을 맞춘다.
        [SerializeField] string _sourceName = string.Empty;
        [SerializeField] string _sceneId = string.Empty;
        [SerializeField] int _beaconIndex;
        [SerializeField] Cost _cost;
        // 원작 conditions처럼 모두 맞아야 한다. 비어 있으면 늘 누를 수 있다.
        [SerializeField] MapSelectionBeaconRequirement[] _requiredBeacons = Array.Empty<MapSelectionBeaconRequirement>();
        [SerializeField] Sprite? _fullScreenImage;

        internal string SourceName => _sourceName;
        internal string SceneId => _sceneId;
        internal int BeaconIndex => _beaconIndex;
        internal Cost Cost => _cost;
        internal IReadOnlyList<MapSelectionBeaconRequirement> RequiredBeacons => _requiredBeacons;
        internal Sprite? FullScreenImage => _fullScreenImage;
    }

    // 원작 LevelManager 프리팹의 지도 선택 화면 항목 목록이다. 가져오기 도구가 원작 화면 순서대로 만든다.
    public sealed class MapSelectionData : ScriptableObject
    {
        [SerializeField] MapSelectionEntryData[] _entries = Array.Empty<MapSelectionEntryData>();

        internal IReadOnlyList<MapSelectionEntryData> Entries => _entries;
    }
}
