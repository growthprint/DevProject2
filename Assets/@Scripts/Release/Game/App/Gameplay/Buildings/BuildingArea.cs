#nullable enable annotations
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 BuildingArea다. 건설 영역의 격자 기하와 물리 겹침 검사를 맡고, 소유자가 가진 이 영역의 설치 건물을 프리팹으로 보여 준다.
    // 상태는 바꾸지 않는다. 기지 씬의 BuildingArea 오브젝트에 붙고, 메인 스레드에서만 쓴다.
    [DisallowMultipleComponent]
    public sealed class BuildingArea : MonoBehaviour
    {
        // 원작 씬 값이다. 기지는 Base_01이고, 크기 (17, 13)은 중심에서의 반폭이라 33×25칸이다.
        [SerializeField] string _areaId;
        [SerializeField] Vector2Int _size;
        [SerializeField] LayerMask _physicsCollisionLayers = -1;

        // 표시한 건물이다. 회수된 건물은 스스로 파괴되므로 찾을 때 파괴된 항목을 거른다.
        readonly List<Building> _activeBuildings = new List<Building>();
        readonly RaycastHit[] _raycastHits = new RaycastHit[5];
        // 정의나 프리팹이 없는 건물 ID를 한 번만 경고하려고 남긴다.
        readonly HashSet<string> _reportedMissing = new HashSet<string>(StringComparer.Ordinal);
        // 전투 월드 컨트롤러가 레벨 준비 때 넘긴 월드다. 영역 표시와 레벨 준비의 순서가 정해지지 않아,
        // 연결 전에 표시한 건물은 연결할 때, 연결 뒤에 표시한 건물은 만들 때 그 월드에 넣는다.
        GameplayWorldContext? _world;

        public string AreaId => _areaId;
        public Vector2Int Size => _size;
        // 원작처럼 영역 위치를 반올림한 월드 정수 좌표다.
        public Vector2Int CenterCoord =>
            new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.z));
        // 건설 화면이 커서 위치를 얻으려고 이 평면에 광선을 쏜다.
        public Plane Plane => new Plane(transform.up, transform.position);

        void Awake()
        {
            BuildingManager.Built += OnBuildingBuilt;
        }

        // 정리 순서와 상관없이 씬과 함께 파괴될 때 월드 목록에서 이 영역 건물을 뺀다.
        void OnDestroy()
        {
            BuildingManager.Built -= OnBuildingBuilt;
            Unbind();
        }

        // 전투 월드 컨트롤러가 레벨 준비에서 부르고 레벨 정리에서 Unbind를 부른다. 다른 월드에 넣은 건물은 먼저 뺀다.
        internal void Bind(GameplayWorldContext world)
        {
            Unbind();
            _world = world;
            for (int index = 0; index < _activeBuildings.Count; index++)
                if (_activeBuildings[index] != null) _activeBuildings[index].RegisterInteractables(world);
        }

        // 연결하지 않았으면 아무 일도 없다. 이미 파괴된 건물은 자기 OnDestroy가 뺐으므로 건너뛴다.
        internal void Unbind()
        {
            if (_world == null) return;
            _world = null;
            for (int index = 0; index < _activeBuildings.Count; index++)
                if (_activeBuildings[index] != null) _activeBuildings[index].UnregisterInteractables();
        }

        // 원작처럼 영역이 시작될 때 저장된 건물을 모두 그린다.
        void Start()
        {
            RepaintAll();
        }

        // 원작은 배치된 건물을 영역과 상관없이 그리지만, 영역이 하나뿐이라 이 영역 건물만 그려도 결과가 같다.
        // 건설 화면은 영역이 시작된 뒤에만 열리고 알림은 새 번호로만 오므로, Start가 그린 건물과 겹치지 않는다.
        void OnBuildingBuilt(int instanceId)
        {
            if (GameManager.Buildings.TryGetInstallation(instanceId, out BuildingInstallationState? installation)
                && string.Equals(installation.AreaId, _areaId, StringComparison.Ordinal))
                DisplayAsync(installation).Forget();
        }

        // 원작 PhysicsCollide다. 점유 칸마다 높이 0에서 2까지 위로, 2에서 0까지 아래로 쏴 막는 물체가 있으면 참이다.
        // 씬에 켜진 개집처럼 격자 데이터에 없는 물체도 이 검사로 그 칸을 막는다.
        internal bool PhysicsCollide(Vector2Int dimensions, BuildingRotation rotation, Vector2Int coord)
        {
            const float castHeight = 2f;
            Vector2Int size = BuildingRules.RotatedDimensions(dimensions, rotation);
            for (int y = coord.y; y < coord.y + size.y; y++)
            {
                for (int x = coord.x; x < coord.x + size.x; x++)
                {
                    var origin = new Vector3(x, 0f, y);
                    int hits = Physics.RaycastNonAlloc(origin, Vector3.up, _raycastHits, castHeight, _physicsCollisionLayers)
                        + Physics.RaycastNonAlloc(origin + Vector3.up * castHeight, Vector3.down, _raycastHits, castHeight, _physicsCollisionLayers);
                    if (hits > 0) return true;
                }
            }
            return false;
        }

        // 원작 GetBuildingInstanceAt이다. 건설 화면이 칸 아래의 표시 건물을 고를 때 쓴다. 아직 로드 중이면 null이다.
        internal Building GetBuildingInstanceAt(Vector2Int coord)
        {
            if (!GameManager.Buildings.TryGetBuildingAt(_areaId, coord, out BuildingInstallationState? installation)) return null;
            return FindDisplayed(installation.InstanceId);
        }

        // 원작 RepaintAll이다. Start에서 한 번만 불러 아직 표시한 건물이 없으므로, 지울 것 없이 이 영역의 저장된 건물을 만든다.
        void RepaintAll()
        {
            IReadOnlyList<BuildingInstallationState> installations = GameManager.Buildings.GetInstallations(_areaId);
            for (int index = 0; index < installations.Count; index++) DisplayAsync(installations[index]).Forget();
        }

        Building FindDisplayed(int instanceId)
        {
            for (int index = 0; index < _activeBuildings.Count; index++)
            {
                Building building = _activeBuildings[index];
                if (building != null && building.InstanceId == instanceId) return building;
            }
            return null;
        }

        // 아무도 기다리지 않는 표시 루트다. 프리팹은 이 씬의 서랍에 로드해 씬과 함께 반납한다.
        async UniTaskVoid DisplayAsync(BuildingInstallationState installation)
        {
            try
            {
                string id = installation.OriginalBuildingId;
                // 정의 없는 저장 건물과 프리팹을 아직 잇지 않은 정의는 보이지 않는다. 저장 값은 그대로 남는다.
                if (!GameplayDataSettings.BuildingDataCollection.TryGetInfo(id, out BuildingInfo info) || !info.Prefab.RuntimeKeyIsValid())
                {
                    if (_reportedMissing.Add(id)) Log.Instance.Warning("건물 프리팹이 없어 표시하지 않습니다: " + id, this);
                    return;
                }
                AssetLifetimes assets = GameManager.Live.Runtime.Assets;
                GameObject prefab = await assets.LoadAsync<GameObject>(info.Prefab, gameObject.scene);
                // 기다리는 동안 씬이나 앱이 내려갔거나 건물이 회수됐으면 만들지 않는다. 로드한 에셋은 씬 서랍이 반납한다.
                if (this == null || !GameManager.HasInstance) return;
                if (!GameManager.Buildings.TryGetInstallation(installation.InstanceId, out _)) return;
                GameObject created = Instantiate(prefab, transform);
                Building building = created.GetComponent<Building>();
                building.Setup(installation);
                created.transform.position = BuildingRules.CoordToWorldPosition(new Vector2Int(installation.X, installation.Y), info.Dimensions,
                    (BuildingRotation)installation.Rotation);
                _activeBuildings.Add(building);
                // 월드에 연결된 뒤 표시한 건물은 바로 F 상호작용 대상이 되도록 생성 대상 목록에 넣는다.
                if (_world != null) building.RegisterInteractables(_world);
            }
            // 예외 경계: 아무도 기다리지 않는 표시 루트라 프리팹 로드 실패를 잃지 않도록 여기서 한 번 기록한다.
            catch (Exception error)
            {
                Log.Instance.Exception(error, this);
            }
        }
    }
}
