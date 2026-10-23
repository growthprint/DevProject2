#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 지도 화면(S3)이 읽는 과제 지도 점 하나다. 원작 MapElementForTask가 찾은 위치마다 만드는 SimplePointOfInterest에 해당한다.
    internal readonly struct QuestMapPoint
    {
        internal QuestMapPoint(int questId, int taskId, string nameKey, Vector3 position, float range, QuestMapElementDefinition element)
        {
            QuestId = questId;
            TaskId = taskId;
            NameKey = nameKey;
            Position = position;
            Range = range;
            Element = element;
        }

        internal int QuestId { get; }
        internal int TaskId { get; }
        // 원작 mapElement.name = Master.DisplayName이다. 화면이 현재 로캘 문구로 바꾼다.
        internal string NameKey { get; }
        internal Vector3 Position { get; }
        // 0보다 크면 원작 IsArea 원의 반지름이다.
        internal float Range { get; }
        // 아이콘 이름과 색을 화면이 읽는다.
        internal QuestMapElementDefinition Element { get; }
    }

    // 원작 SpawnItemForTask, SpawnPrefabForTask, MapElementForTask와 QuestTask_ReachLocation의 씬 쪽 일을 레벨 씬마다 하나가 맡는다.
    // 원작은 퀘스트 복제본 컴포넌트가 했지만 프로젝트 퀘스트 데이터에는 씬 참조가 없어, 이 씬 표가 원작 위치 이름과 프리팹 이름을 씬 객체로 바꾼다.
    // 표는 Editor 제작 도구(5-T)가 채운다. 이 씬 표에 없는 위치는 건너뛰므로 기지에서는 제로존 과제 물건이 생기지 않는다.
    // 전투 월드 컨트롤러가 레벨 준비 끝에 Bind로 연결하고 레벨 정리 때 Unbind로 끊는다. 만든 기록은 QuestManager가 세션 동안 가진다.
    [DisallowMultipleComponent]
    public sealed class QuestTaskSpawner : MonoBehaviour
    {
        // 원작 MultiSceneLocation(서브 씬 ID + 씬 안 경로 이름) 하나를 이 씬의 Transform에 잇는다. 하위 씬 판정은 저작 때 적은
        // 서브 씬 ID로 한다. 도달 판정과 지도 점은 이 월드의 하위 씬 이름이면 그 하위 씬이 켜졌을 때만 쓰고, 생성 도우미는 늘 쓴다.
        [Serializable]
        sealed class LocationEntry
        {
            [SerializeField] string _sceneId = "";
            [SerializeField] string _locationName = "";
            [SerializeField] Transform _target;

            internal string SceneId => _sceneId;
            internal string LocationName => _locationName;
            internal Transform Target => _target;
        }

        // 원작 SpawnPrefabForTask의 프리팹 이름 하나를 제품 프리팹에 잇는다.
        [Serializable]
        sealed class PrefabEntry
        {
            [SerializeField] string _originalName = "";
            [SerializeField] GameObject _prefab;

            internal string OriginalName => _originalName;
            internal GameObject Prefab => _prefab;
        }

        // 위치 도달 과제 하나다. 이 씬에서 찾은 목표와 원작 반경을 든다.
        readonly struct ReachTarget
        {
            internal ReachTarget(int questId, int taskId, Transform target, float radius)
            {
                QuestId = questId;
                TaskId = taskId;
                Target = target;
                Radius = radius;
            }

            internal int QuestId { get; }
            internal int TaskId { get; }
            internal Transform Target { get; }
            internal float Radius { get; }
        }

        [SerializeField] LocationEntry[] _locations = Array.Empty<LocationEntry>();
        [SerializeField] PrefabEntry[] _prefabs = Array.Empty<PrefabEntry>();

        // Bind와 Unbind 사이에만 있다. 생성 상호작용 대상 목록, 플레이어 위치, 세션은 전투 월드 컨텍스트에서 읽는다.
        GameplayWorldContext? _world;
        // 원작 SpawnItemForTask의 InstantiateAsync + Drop이다. 아이템 하나를 그 자리에 바닥 아이템으로 두고 지점을 돌려준다. 실패하면 null이다.
        Func<int, Vector3, InteractablePickup?>? _dropItem;
        QuestManager? _quests;
        // 프리팹을 꺼진 부모 아래에 만들어, 과제 키를 덮기 전에 TaskEventEmitter의 Awake가 프리팹 기본 키를 보내지 않게 한다.
        Transform? _staging;
        readonly List<ReachTarget> _reachTargets = new List<ReachTarget>();
        // 과제 프리팹 안의 F 상호작용 대상이다. Unbind가 전투 월드 컨텍스트에서 뺀다.
        readonly List<InteractableBase> _registered = new List<InteractableBase>();
        readonly List<InteractableBase> _interactableBuffer = new List<InteractableBase>();
        // 과제 지도 점 계산이다. 위치 표 조회가 이 컴포넌트를 가리켜야 하므로 Awake에서 만든다.
        QuestTaskMapPoints _taskMapPoints = null!;

        // 지도 화면이 읽는 이 씬의 과제 지도 점이다. 다시 계산하는 시점은 지도 점 계산 객체가 정한다.
        internal IReadOnlyList<QuestMapPoint> MapPoints => _taskMapPoints.Get(_world, _quests);

        // 위치 표 조회 대리자를 한 번만 만들어 지도 점을 읽을 때 할당하지 않는다.
        // 레벨 준비의 Bind는 씬의 Awake가 모두 끝난 뒤에 온다.
        void Awake() => _taskMapPoints = new QuestTaskMapPoints(TryFindLocation);

        // 전투 월드 컨트롤러가 레벨 준비(PrepareWorld) 끝에 부른다. 원작 Start와 씬 로드 완료 때의 SpawnIfNeeded처럼 진행 중 퀘스트의
        // 미완료 과제마다 생성 도우미를 검사하고, 퀘스트를 새로 받으면 그 퀘스트만 다시 검사한다. 레벨 씬마다 하나 있는 퀘스트 씬 구성요소라
        // 원작 과제들의 OnLevelInitialized 알림도 여기서 낸다. 이때는 아직 하위 씬을 켜기 전이라 도달 대상과 지도 점은 첫 전환 알림에서 모은다.
        internal void Bind(GameplayWorldContext world, QuestManager quests, Func<int, Vector3, InteractablePickup?> dropItem)
        {
            Unbind();
            _world = world;
            _dropItem = dropItem;
            _quests = quests;
            QuestManager.QuestActivated += OnQuestActivated;
            QuestManager.TaskFinished += OnTaskFinished;
            QuestManager.Changed += OnQuestsChanged;
            quests.NotifyLevelInitialized();
            SpawnPending(true, 0);
            RebuildReachTargets();
        }

        // 레벨 정리가 부르고, 씬과 함께 파괴될 때도 한 번 더 부른다. 만든 프리팹은 이 객체의 자식이라 씬과 함께 사라지고,
        // 떨군 아이템은 바닥 아이템 소유자가 치운다.
        internal void Unbind()
        {
            if (_world == null) return;
            QuestManager.QuestActivated -= OnQuestActivated;
            QuestManager.TaskFinished -= OnTaskFinished;
            QuestManager.Changed -= OnQuestsChanged;
            for (int i = 0; i < _registered.Count; i++) _world.RemoveSpawnedInteractable(_registered[i]);
            _registered.Clear();
            _reachTargets.Clear();
            // 연결이 끊기면 아이템 지도 점과 계산해 둔 점도 함께 버린다.
            _taskMapPoints.Clear();
            _world = null;
            _dropItem = null;
            _quests = null;
        }

        void OnDestroy() => Unbind();

        void OnQuestActivated(int questId)
        {
            SpawnPending(false, questId);
            RebuildReachTargets();
        }

        // 과제가 끝나면 그 과제의 지도 점이 같은 프레임 안에서도 사라지도록 다음 읽기에서 다시 계산한다.
        void OnTaskFinished(int questId, int taskId)
        {
            RemoveTask(questId, taskId);
            _taskMapPoints.Invalidate();
        }

        // 완료, 불러오기처럼 진행 중 목록이 바뀌면 위치 도달 대상과 아이템 지도 점을 다시 맞춘다.
        void OnQuestsChanged() => RebuildReachTargets();

        // 원작 CacheLocation의 OnSubSceneLoaded 구독처럼, 컨트롤러가 하위 씬을 바꾼 직후 부른다. 생성 도우미는 Bind 때 이미 만들었으므로
        // 위치 도달 대상만 새 하위 씬 기준으로 다시 모은다. 그 다시 모으기가 지도 점도 다음 읽기에서 다시 계산하게 한다.
        internal void OnSubsceneSwitched() => RebuildReachTargets();

        // 원작 QuestTask_ReachLocation.Update다. 주인공이 반경 안에 들면 과제를 끝낸다. 과제를 끝내면 알림 처리기가 이 목록을
        // 다시 만들므로 한 프레임에 한 과제만 끝내고 순회를 멈춘다. 명령 관문이 닫혀 거절되면 다음 프레임에 다시 본다.
        void Update()
        {
            if (_world == null || _reachTargets.Count == 0) return;
            // 퀘스트 소유자는 월드와 함께 Bind에서 서고 Unbind에서 지므로 월드가 있으면 있다.
            QuestManager quests = _quests!;
            Vector3 player = _world.PlayerPosition;
            for (int i = 0; i < _reachTargets.Count; i++)
            {
                ReachTarget reach = _reachTargets[i];
                if (!IsInReach(reach, player) || !TryResolveReach(quests, reach)) continue;
                RemoveTask(reach.QuestId, reach.TaskId);
                return;
            }
        }

        // 목표가 이 씬에 있고 주인공 거리가 원작 반경보다 크지 않으면 닿은 것이다. "크지 않음"으로 비교해 NaN 거리도 닿은 것으로 본다.
        static bool IsInReach(ReachTarget reach, Vector3 player) =>
            reach.Target != null && !((player - reach.Target.position).magnitude > reach.Radius);

        // 닿은 과제를 먼저 끝내 본다. 끝냈거나, 진행 중이 아니거나, 이미 끝난 과제면 더 볼 필요가 없어 true다.
        static bool TryResolveReach(QuestManager quests, ReachTarget reach) =>
            quests.TryFinishTask(reach.QuestId, reach.TaskId) || !quests.IsActive(reach.QuestId) || quests.IsTaskFinished(reach.QuestId, reach.TaskId);

        // all이 거짓이면 questId 퀘스트만 본다. 퀘스트 ID는 음수도 있어 따로 넘긴다.
        void SpawnPending(bool all, int questId)
        {
            // Bind와 퀘스트 알림 처리기에서만 불러 연결 중이라 퀘스트 소유자가 있다.
            QuestManager quests = _quests!;
            IReadOnlyList<OriginalQuestDefinition> definitions = quests.CatalogQuests;
            for (int i = 0; i < definitions.Count; i++)
            {
                OriginalQuestDefinition quest = definitions[i];
                if ((!all && quest.Id != questId) || !quests.IsActive(quest.Id)) continue;
                IReadOnlyList<OriginalQuestTaskDefinition> tasks = quest.Tasks;
                for (int t = 0; t < tasks.Count; t++)
                {
                    OriginalQuestTaskDefinition task = tasks[t];
                    if (task.Spawns.Count == 0 || quests.IsTaskFinished(quest.Id, task.Id)) continue;
                    for (int s = 0; s < task.Spawns.Count; s++) TrySpawn(quests, quest, task, task.Spawns[s]);
                }
            }
        }

        // 원작 SpawnIfNeeded와 Spawn이다. 위치 목록에서 하나를 무작위로 고르고, 그 위치가 이 씬에 없으면 기록 없이 다음 레벨을 기다린다.
        // 원작은 캐시한 좌표로 위치가 어느 하위 씬에 있든 바로 만든다. 꺼진 하위 씬의 Transform도 저작 좌표를 들고 있어 그 좌표로 바로 만든다.
        void TrySpawn(QuestManager quests, OriginalQuestDefinition quest, OriginalQuestTaskDefinition task, QuestTaskSpawnDefinition spawn)
        {
            // 원작 아이템 도우미는 itemID가 0 이상일 때만 만든다.
            if (spawn.Kind == QuestTaskSpawnKind.Item && spawn.ItemId < 0) return;
            if (spawn.Locations.Count == 0 || quests.IsTaskHelperSpawned(quest.Id, task.Id, spawn.ComponentId)) return;
            QuestLocationRef location = spawn.Locations[UnityEngine.Random.Range(0, spawn.Locations.Count)];
            if (!TryFindEntry(location, out LocationEntry? entry)) return;
            // 아이템 지도 점은 위치의 하위 씬이 켜졌을 때만 내므로 그 번호를 함께 넘긴다. 메인 씬 위치는 -1이다.
            int subscene = _world!.SubsceneIndex(entry!.SceneId);
            if (spawn.Kind == QuestTaskSpawnKind.Item) SpawnItem(quests, quest, task, spawn, entry.Target, subscene);
            else SpawnPrefab(quests, quest, task, spawn, entry.Target);
        }

        // 원작처럼 위치를 찾으면 아이템을 만들기 전에 기록한다. 원작은 비동기 생성이 실패해도 다시 만들지 않는다.
        // 원작 SpawnItem처럼 지도 표시를 고른 위치 하나로 켜고, 그 아이템을 주우면 끈다. 지도 점은 그 위치의 하위 씬이 켜졌을 때만 낸다.
        void SpawnItem(QuestManager quests, OriginalQuestDefinition quest, OriginalQuestTaskDefinition task, QuestTaskSpawnDefinition spawn,
            Transform target, int subscene)
        {
            quests.MarkTaskHelperSpawned(quest.Id, task.Id, spawn.ComponentId);
            InteractablePickup? pickup = _dropItem!(spawn.ItemId, target.position);
            // 아이템을 만들지 못하면 지도 점도 켜지 않는다. 표시 선택과 다시 계산은 지도 점 계산 객체가 맡는다.
            if (pickup != null) _taskMapPoints.AddSpawned(quest, task, spawn.MapElementIndex, target.position, pickup, subscene);
        }

        // 원작처럼 회전 없이 그 자리에 만든다. 이벤트 과제면 프리팹 루트의 발신기 키를 과제 키로 덮은 뒤 켠다.
        // 프리팹 안 F 대상은 저작 표에 없으므로 생성 상호작용 대상으로 넣는다(검토 9번, 전투 월드 컨텍스트 목록을 쓴다).
        void SpawnPrefab(QuestManager quests, OriginalQuestDefinition quest, OriginalQuestTaskDefinition task, QuestTaskSpawnDefinition spawn,
            Transform target)
        {
            // 위치가 이 씬에 있는데 짝지은 제품 프리팹이 없으면 씬 제작 결함이다. 기록하지 않아 표를 고친 뒤 다시 만든다.
            if (!TryFindPrefab(spawn.PrefabName, out GameObject prefab))
            {
                _world!.ReportDefectOnce("quest-prefab:" + spawn.PrefabName,
                    "과제 생성기 표에 원작 프리팹의 제품 프리팹이 없습니다: " + spawn.PrefabName + " (quest " + quest.Id + ")");
                return;
            }
            GameObject instance = Instantiate(prefab, target.position, Quaternion.identity, Staging());
            if (task.Kind == QuestTaskKind.TaskEvent && instance.TryGetComponent(out TaskEventEmitter emitter)) emitter.SetKey(task.EventKey);
            // 원작 SpawnPrefabForTask는 퀘스트 객체의 씬, 곧 늘 켜진 메인 씬에 프리팹을 둔다. 그래서 위치의 하위 씬과 상관없이 늘 켜진 이 객체에 붙인다.
            instance.transform.SetParent(transform, true);
            instance.GetComponentsInChildren(true, _interactableBuffer);
            for (int i = 0; i < _interactableBuffer.Count; i++)
            {
                _world!.AddSpawnedInteractable(_interactableBuffer[i]);
                _registered.Add(_interactableBuffer[i]);
            }
            _interactableBuffer.Clear();
            quests.MarkTaskHelperSpawned(quest.Id, task.Id, spawn.ComponentId);
        }

        Transform Staging()
        {
            if (_staging != null) return _staging;
            var staging = new GameObject("QuestTaskStaging");
            staging.SetActive(false);
            staging.transform.SetParent(transform, false);
            _staging = staging.transform;
            return _staging;
        }

        // 진행 중 퀘스트의 미완료 위치 도달 과제 중 목표가 메인 씬이나 지금 켠 하위 씬에 있는 것만 둔다. 원작 과제도 불러온 씬에서만
        // 목표 Transform을 찾는다. 끝났거나 진행 중이 아닌 과제의 아이템 지도 점도 뺀다.
        void RebuildReachTargets()
        {
            _reachTargets.Clear();
            // Bind, 퀘스트 알림 처리기, 하위 씬 전환 알림에서만 불러 연결 중이라 퀘스트 소유자가 있다.
            QuestManager quests = _quests!;
            // 아이템 지도 점 정리와 다시 계산 표시는 지도 점 계산 객체가 맡는다.
            _taskMapPoints.PruneInactive(quests);
            IReadOnlyList<OriginalQuestDefinition> definitions = quests.CatalogQuests;
            for (int i = 0; i < definitions.Count; i++)
            {
                OriginalQuestDefinition quest = definitions[i];
                if (!quests.IsActive(quest.Id)) continue;
                IReadOnlyList<OriginalQuestTaskDefinition> tasks = quest.Tasks;
                for (int t = 0; t < tasks.Count; t++)
                {
                    OriginalQuestTaskDefinition task = tasks[t];
                    if (task.Kind != QuestTaskKind.ReachLocation || quests.IsTaskFinished(quest.Id, task.Id)) continue;
                    if (TryFindLocation(task.Location, out Transform target))
                        _reachTargets.Add(new ReachTarget(quest.Id, task.Id, target, task.Radius));
                }
            }
        }

        // 끝난 과제의 위치 도달 대상과 아이템 지도 점을 함께 뺀다.
        void RemoveTask(int questId, int taskId)
        {
            for (int i = _reachTargets.Count - 1; i >= 0; i--)
                if (_reachTargets[i].QuestId == questId && _reachTargets[i].TaskId == taskId) _reachTargets.RemoveAt(i);
            _taskMapPoints.RemoveTask(questId, taskId);
        }

        // 원작 MultiSceneLocation의 서브 씬 ID와 경로 이름이 모두 같은 표 항목을 찾는다. 생성 도우미가 위치를 고를 때 쓰므로 꺼진 하위 씬 것도 찾는다.
        // 직렬화 클래스 배열은 Unity가 빈 원소 없이 불러오므로 항목 자체는 늘 있다.
        bool TryFindEntry(QuestLocationRef location, out LocationEntry? found)
        {
            for (int i = 0; i < _locations.Length; i++)
            {
                LocationEntry entry = _locations[i];
                if (entry.Target == null) continue;
                if (!string.Equals(entry.SceneId, location.SceneId, StringComparison.Ordinal)
                    || !string.Equals(entry.LocationName, location.LocationName, StringComparison.Ordinal)) continue;
                found = entry;
                return true;
            }
            found = null;
            return false;
        }

        // 원작 MultiSceneLocation.GetLocationTransform처럼 불러온 공간, 곧 메인 씬과 지금 켠 하위 씬의 위치만 찾는다.
        // 위치 도달 판정과 지도 점이 쓰며, 둘 다 연결 중에만 불러 월드가 있다.
        bool TryFindLocation(QuestLocationRef location, out Transform target)
        {
            GameplayWorldContext world = _world!;
            if (TryFindEntry(location, out LocationEntry? entry) && world.IsSubsceneActive(world.SubsceneIndex(entry!.SceneId)))
            {
                target = entry.Target;
                return true;
            }
            target = null;
            return false;
        }

        // 위치 표와 같이 직렬화 클래스 배열이라 항목 자체는 늘 있다.
        bool TryFindPrefab(string originalName, out GameObject prefab)
        {
            for (int i = 0; i < _prefabs.Length; i++)
            {
                PrefabEntry entry = _prefabs[i];
                if (entry.Prefab == null || !string.Equals(entry.OriginalName, originalName, StringComparison.Ordinal)) continue;
                prefab = entry.Prefab;
                return true;
            }
            prefab = null;
            return false;
        }
    }
}
