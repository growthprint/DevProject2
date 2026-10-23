#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 MultiSceneLocation.GetLocationTransform 자리다. 과제 생성기의 위치 표에서 메인 씬과 지금 켠 하위 씬의 위치만 찾고, 없으면 false다.
    // 그래서 과제 위치와 표시 위치의 지도 점은 다른 하위 씬 것을 섞지 않는다.
    internal delegate bool QuestLocationLookup(QuestLocationRef location, out Transform target);

    // 과제 생성기(QuestTaskSpawner)가 지도 화면에 내는 과제 지도 점이다. 원작 MapElementForTask가 켜는 점과
    // 아이템 생성 도우미가 켠 점을 모은다. 위치 조회는 생성기가 한 번 만들어 넘긴 대리자를 써서 읽을 때 할당하지 않는다.
    internal sealed class QuestTaskMapPoints
    {
        // 아이템 생성 도우미가 켠 지도 점이다. 원작처럼 그 아이템을 주우면 끈다. 바닥 아이템 지점은 풀에서 다시 쓰이므로
        // 떨군 때의 구분자가 그대로인지까지 본다. Subscene은 떨군 위치의 하위 씬 번호이고 메인 씬은 -1이다.
        readonly struct SpawnedPoint
        {
            internal SpawnedPoint(QuestMapPoint point, InteractablePickup pickup, string sourceId, int subscene)
            {
                Point = point;
                Pickup = pickup;
                SourceId = sourceId;
                Subscene = subscene;
            }

            internal QuestMapPoint Point { get; }
            internal InteractablePickup Pickup { get; }
            internal string SourceId { get; }
            internal int Subscene { get; }
            internal bool PickedUp => Pickup == null || !Pickup.isActiveAndEnabled || Pickup.SourceId != SourceId;
        }

        readonly QuestLocationLookup _findLocation;
        readonly List<SpawnedPoint> _spawnedPoints = new List<SpawnedPoint>();
        readonly List<QuestMapPoint> _mapPoints = new List<QuestMapPoint>();
        // 마지막으로 다시 계산한 프레임이다. -1이면 다음 읽기에서 다시 계산한다.
        int _mapFrame = -1;

        internal QuestTaskMapPoints(QuestLocationLookup findLocation) => _findLocation = findLocation;

        // 제출 과제는 소지 수에 따라 켜고 꺼지므로 읽는 프레임마다 한 번 다시 계산한다.
        // world와 quests는 생성기가 연결 중일 때만 있고, 둘은 함께 서고 진다.
        internal IReadOnlyList<QuestMapPoint> Get(GameplayWorldContext? world, QuestManager? quests)
        {
            if (_mapFrame != Time.frameCount) Rebuild(world, quests);
            return _mapPoints;
        }

        // 원작 SpawnItem처럼 지도 표시를 고른 위치 하나로 켜고, 그 아이템을 주우면 끈다. 과제에 그 표시가 없으면 켜지 않는다.
        // subscene은 떨군 위치의 하위 씬 번호라, 그 하위 씬이 꺼진 동안에는 점을 내지 않는다.
        internal void AddSpawned(OriginalQuestDefinition quest, OriginalQuestTaskDefinition task, int elementIndex, Vector3 position,
            InteractablePickup pickup, int subscene)
        {
            if (!TryElement(task, elementIndex, out QuestMapElementDefinition? element)) return;
            var point = new QuestMapPoint(quest.Id, task.Id, quest.NameKey, position, element!.Range, element);
            _spawnedPoints.Add(new SpawnedPoint(point, pickup, pickup.SourceId, subscene));
            _mapFrame = -1;
        }

        // 끝난 과제의 아이템 지도 점을 바로 뺀다. 다시 계산할지는 호출자가 정한다.
        internal void RemoveTask(int questId, int taskId)
        {
            for (int i = _spawnedPoints.Count - 1; i >= 0; i--)
                if (_spawnedPoints[i].Point.QuestId == questId && _spawnedPoints[i].Point.TaskId == taskId) _spawnedPoints.RemoveAt(i);
        }

        // 진행 중 목록이 바뀌면 끝났거나 진행 중이 아닌 과제의 아이템 지도 점을 빼고 다음 읽기에서 다시 계산한다.
        internal void PruneInactive(QuestManager quests)
        {
            _mapFrame = -1;
            for (int i = _spawnedPoints.Count - 1; i >= 0; i--)
            {
                QuestMapPoint point = _spawnedPoints[i].Point;
                if (!quests.IsActive(point.QuestId) || quests.IsTaskFinished(point.QuestId, point.TaskId)) _spawnedPoints.RemoveAt(i);
            }
        }

        // 과제 진행이 바뀌어 같은 프레임 안에서도 다음 읽기가 다시 계산해야 할 때 부른다.
        internal void Invalidate() => _mapFrame = -1;

        // 생성기 연결을 끊을 때 모든 지도 점을 비운다.
        internal void Clear()
        {
            _spawnedPoints.Clear();
            _mapPoints.Clear();
            _mapFrame = -1;
        }

        // 과제 자신의 지도 표시는 원작 과제마다 켜는 조건이 다르다. 위치 도달은 미완료 동안 과제 위치와 반경 하나로, 이벤트 과제는
        // 미완료 동안, 제출 과제는 소지 수가 모자랄 때만 켠다. 다른 과제는 표시를 켜지 않는다. 아이템 지도 점은 주운 것을 빼고 더한다.
        void Rebuild(GameplayWorldContext? world, QuestManager? owner)
        {
            _mapFrame = Time.frameCount;
            _mapPoints.Clear();
            // 지도 화면은 연결 전후에도 읽으므로 월드만 보고, 퀘스트 소유자는 월드와 함께 서고 진다.
            if (world == null) return;
            QuestManager quests = owner!;
            world.TrySession(out GameSessionManager? session);
            IReadOnlyList<OriginalQuestDefinition> definitions = quests.CatalogQuests;
            for (int i = 0; i < definitions.Count; i++)
            {
                OriginalQuestDefinition quest = definitions[i];
                if (!quests.IsActive(quest.Id)) continue;
                IReadOnlyList<OriginalQuestTaskDefinition> tasks = quest.Tasks;
                for (int t = 0; t < tasks.Count; t++)
                {
                    OriginalQuestTaskDefinition task = tasks[t];
                    if (!TryElement(task, task.MapElementIndex, out QuestMapElementDefinition? element)
                        || quests.IsTaskFinished(quest.Id, task.Id)) continue;
                    switch (task.Kind)
                    {
                        case QuestTaskKind.ReachLocation:
                            if (_findLocation(task.Location, out Transform reach))
                                _mapPoints.Add(new QuestMapPoint(quest.Id, task.Id, quest.NameKey, reach.position, task.Radius, element!));
                            break;
                        case QuestTaskKind.TaskEvent:
                            AddElementPoints(quest, task, element!);
                            break;
                        // 원작 CheckItemEnough처럼 가진 수는 소유 출처 도우미로 센다. 세션이 없으면 표시하지 않는다.
                        case QuestTaskKind.SubmitItems:
                            if (session != null && session.Inventory.CountOwned(task.ItemId, ItemSourceRules.Owned(session)) < task.RequiredCount)
                                AddElementPoints(quest, task, element!);
                            break;
                    }
                }
            }
            for (int i = _spawnedPoints.Count - 1; i >= 0; i--)
            {
                if (_spawnedPoints[i].PickedUp) _spawnedPoints.RemoveAt(i);
                // 아이템 지도 점도 과제 위치처럼 메인 씬과 지금 켠 하위 씬 것만 낸다. 다른 하위 씬의 점은 지우지 않고 그 하위 씬을 켜면 다시 낸다.
                else if (world.IsSubsceneActive(_spawnedPoints[i].Subscene)) _mapPoints.Add(_spawnedPoints[i].Point);
            }
        }

        // 원작 MapElementForTask.Spawn처럼 표시의 위치 중 이 씬에서 찾은 곳마다 점 하나를 둔다.
        void AddElementPoints(OriginalQuestDefinition quest, OriginalQuestTaskDefinition task, QuestMapElementDefinition element)
        {
            IReadOnlyList<QuestLocationRef> locations = element.Locations;
            for (int i = 0; i < locations.Count; i++)
                if (_findLocation(locations[i], out Transform target))
                    _mapPoints.Add(new QuestMapPoint(quest.Id, task.Id, quest.NameKey, target.position, element.Range, element));
        }

        // 과제의 지도 표시 번호가 목록 밖이면 그 과제는 표시를 켜지 않는다.
        static bool TryElement(OriginalQuestTaskDefinition task, int index, out QuestMapElementDefinition? element)
        {
            IReadOnlyList<QuestMapElementDefinition> elements = task.MapElements;
            element = index >= 0 && index < elements.Count ? elements[index] : null;
            return element != null;
        }
    }
}
