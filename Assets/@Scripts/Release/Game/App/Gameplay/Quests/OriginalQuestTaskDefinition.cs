#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 Task 종류다. 범위 안에서 쓰지 않는 미니게임과 저장 값 확인 과제는 NotProgressed로 두어 끝나지 않게 한다.
    // 기본값이 진행되는 과제가 되지 않도록 NotProgressed를 0으로 둔다.
    public enum QuestTaskKind
    {
        NotProgressed = 0,
        SubmitItems = 1,
        KillCount = 2,
        UseItem = 3,
        TaskEvent = 4,
        ReachLocation = 5,
        Evacuate = 6,
        SubmitMoney = 7,
        UnlockPerk = 8,
        ConstructBuilding = 9,
        UnlockBeacon = 10,
    }

    // 원작 SpawnItemForTask와 SpawnPrefabForTask 두 종류다.
    public enum QuestTaskSpawnKind
    {
        Item = 0,
        Prefab = 1,
    }

    // 원작 MultiSceneLocation이다. 서브 씬 ID와 그 씬 안 경로 이름으로 위치를 찾으므로 씬 Transform 참조 대신 문자열로 둔다.
    [Serializable]
    public sealed class QuestLocationRef
    {
        [SerializeField] string _sceneId = "";
        [SerializeField] string _locationName = "";
        [SerializeField] string _displayName = "";

        public string SceneId => _sceneId;
        public string LocationName => _locationName;
        public string DisplayName => _displayName;
    }

    // 원작 MapElementForTask다. 원작 과제와 생성 도우미가 이 표시를 켜고 끄며, 위치마다 지도 점 하나를 만든다.
    [Serializable]
    public sealed class QuestMapElementDefinition
    {
        [SerializeField] QuestLocationRef[] _locations = Array.Empty<QuestLocationRef>();
        [SerializeField] float _range;
        // 원작 Sprite 에셋 이름이다. 데이터에 에셋 참조를 두지 않으므로 화면이 이름으로 제품 아이콘을 찾는다.
        [SerializeField] string _iconName = "";
        [SerializeField] Color _iconColor = Color.white;
        [SerializeField] Color _shadowColor = Color.white;
        [SerializeField] float _shadowDistance;

        public IReadOnlyList<QuestLocationRef> Locations => _locations;
        public float Range => _range;
        public string IconName => _iconName;
        public Color IconColor => _iconColor;
        public Color ShadowColor => _shadowColor;
        public float ShadowDistance => _shadowDistance;
    }

    // 원작 과제 생성 도우미 하나다. 원작 기록 키가 componentID를 쓰므로 그 값을 그대로 둔다.
    [Serializable]
    public sealed class QuestTaskSpawnDefinition
    {
        [SerializeField] QuestTaskSpawnKind _kind;
        [SerializeField] string _componentId = "";
        // 아이템 도우미만 쓴다. 원작 기본값은 -1이다.
        [SerializeField] int _itemId = -1;
        // 프리팹 도우미의 원작 프리팹 이름이다. 제품 프리팹은 씬 제작 도구가 이 이름으로 짝짓는다.
        [SerializeField] string _prefabName = "";
        [SerializeField] QuestLocationRef[] _locations = Array.Empty<QuestLocationRef>();
        // 원작 SpawnItemForTask.mapElement가 가리키는 표시다. 같은 과제 MapElements의 번호이며 없으면 -1이다.
        [SerializeField] int _mapElementIndex = -1;

        public QuestTaskSpawnKind Kind => _kind;
        public string ComponentId => _componentId;
        public int ItemId => _itemId;
        public string PrefabName => _prefabName;
        public IReadOnlyList<QuestLocationRef> Locations => _locations;
        public int MapElementIndex => _mapElementIndex;
    }

    // 원작 Task 하나다. 종류별 값을 한 형식에 모으고, 그 종류가 쓰지 않는 값은 원작 기본값으로 둔다.
    // 진행 수와 끝남 여부는 QuestManager 저장 값이 소유한다.
    [Serializable]
    public sealed class OriginalQuestTaskDefinition
    {
        [SerializeField] int _id;
        [SerializeField] QuestTaskKind _kind;
        // 원작 클래스 이름이다. NotProgressed 과제가 원래 어떤 과제였는지 보고와 화면에서 알 수 있게 남긴다.
        [SerializeField] string _originalTypeName = "";
        // 원작 forceFinish다. 켜져 있으면 원작 IsFinished가 종류와 관계없이 참을 돌려준다.
        [SerializeField] bool _forceFinish;
        // 제출과 사용 과제의 itemTypeID다.
        [SerializeField] int _itemId;
        // 제출 과제의 requiredAmount, 처치와 사용 과제의 requireAmount다.
        [SerializeField] int _requiredCount = 1;
        [SerializeField] string _eventKey = "";
        // 원작 과제의 description 키다. 이벤트 과제와 저장 값 확인 과제만 가진다.
        [SerializeField] string _descriptionKey = "";
        [SerializeField] string _buildingId = "";
        [SerializeField] string _perkTreeId = "";
        [SerializeField] string _perkName = "";
        [SerializeField] string _beaconId = "";
        [SerializeField] int _beaconIndex;
        [SerializeField] int _money;
        // 처치와 탈출 과제 자신의 requireSceneID다. 처치 과제는 이 값만 보고, 탈출 과제는 비면 퀘스트의 요구 씬을 본다.
        [SerializeField] string _sceneId = "";
        [SerializeField] QuestLocationRef _location = new();
        [SerializeField] float _radius = 1f;
        // 처치 과제 거르기다. 적은 원작 CharacterRandomPreset의 nameKey이고, 비어 있으면 모든 적을 센다.
        [SerializeField] string _enemyNameKey = "";
        [SerializeField] bool _requireWeapon;
        [SerializeField] int _weaponItemId;
        // 원작 requireHeadShot과 withoutHeadShot이다. 원작 판정은 피해 정보의 치명 여부를 본다.
        [SerializeField] bool _requireCrit;
        [SerializeField] bool _forbidCrit;
        [SerializeField] bool _requireBuff;
        [SerializeField] int _buffId;
        // 처치와 사용 과제는 이 값이 켜져 있으면 레벨이 시작될 때 센 수를 0으로 되돌린다.
        [SerializeField] bool _resetOnLevelInitialized;
        [SerializeField] QuestTaskSpawnDefinition[] _spawns = Array.Empty<QuestTaskSpawnDefinition>();
        [SerializeField] QuestMapElementDefinition[] _mapElements = Array.Empty<QuestMapElementDefinition>();
        // 원작 과제의 mapElement 필드가 가리키는 표시다. MapElements의 번호이며 없으면 -1이다.
        [SerializeField] int _mapElementIndex = -1;

        public int Id => _id;
        public QuestTaskKind Kind => _kind;
        public string OriginalTypeName => _originalTypeName;
        public bool ForceFinish => _forceFinish;
        public int ItemId => _itemId;
        public int RequiredCount => _requiredCount;
        public string EventKey => _eventKey;
        public string DescriptionKey => _descriptionKey;
        public string BuildingId => _buildingId;
        public string PerkTreeId => _perkTreeId;
        public string PerkName => _perkName;
        public string BeaconId => _beaconId;
        public int BeaconIndex => _beaconIndex;
        public int Money => _money;
        public string SceneId => _sceneId;
        public QuestLocationRef Location => _location;
        public float Radius => _radius;
        public string EnemyNameKey => _enemyNameKey;
        public bool RequireWeapon => _requireWeapon;
        public int WeaponItemId => _weaponItemId;
        public bool RequireCrit => _requireCrit;
        public bool ForbidCrit => _forbidCrit;
        public bool RequireBuff => _requireBuff;
        public int BuffId => _buffId;
        public bool ResetOnLevelInitialized => _resetOnLevelInitialized;
        public IReadOnlyList<QuestTaskSpawnDefinition> Spawns => _spawns;
        public IReadOnlyList<QuestMapElementDefinition> MapElements => _mapElements;
        public int MapElementIndex => _mapElementIndex;
    }
}
