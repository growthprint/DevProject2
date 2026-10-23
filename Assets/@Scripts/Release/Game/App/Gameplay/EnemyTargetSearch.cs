#nullable enable
using System;
using System.Collections.Generic;
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원작 AIMainBrain의 탐색 대기열과 DoSearch(AIMainBrain.cs:170-232)다. 적이 결과를 받아야 다음 탐색을 넣고,
    // 씬 전체에서 프레임마다 최대 3건만 처리한다(원작 LevelManager maxSeachCount 3). 겹침 버퍼, 피해 Collider와 적의 대응표, 대기열,
    // 거르기 위임은 생성 때 만들어 프레임마다 할당하지 않는다. 원작처럼 플레이어 피해 Collider도 후보라 플레이어와 다른 팀 적을 함께 찾는다.
    // 적 파사드가 씬 동안 가진다. 메인 스레드 전용이다.
    internal sealed class EnemyTargetSearch
    {
        // 원작 maxSeachCount, 겹침 버퍼 크기(cols 15), 탐색 중심 높이(위치 + 1.5 m), 높이 차 한도 4 m,
        // 시야각과 차폐를 보지 않는 가까운 거리 1.5 m, 차폐 레이 끝 높이(대상 + 1.5 m)다.
        const int _searchesPerFrame = 3, _bufferSize = 15;
        const float _centerHeight = 1.5f, _maxHeightDifference = 4f, _senseDistance = 1.5f, _targetHeight = 1.5f;
        // 원작 CharacterMainControl.VisableDistanceFactor 스탯이다. 캐릭터 어트리뷰트 세트가 원작 스탯을 모두 등록하므로 키로 찾는다.
        static readonly GameplayAttribute _visableDistanceFactor = GameplayAttribute.Register("VisableDistanceFactor");

        readonly Collider[] _hits = new Collider[_bufferSize];
        readonly Dictionary<Collider, OriginalEnemyController> _bodies;
        readonly Queue<OriginalEnemyController> _queue;
        // 플레이어 피해 Collider의 부모인 플레이어 루트와 플레이어 어트리뷰트다. 플레이어나 런타임이 없는 시험 월드에서는 비어 있다.
        readonly Transform? _player;
        readonly AbilitySystem? _playerAbilities;
        // PickLast에 넘기는 후보 거르기다. 탐색마다 할당하지 않도록 생성 때 한 번 묶고, 지금 찾는 적과 탐색 값은 아래 필드로 넘긴다.
        readonly Func<int, bool> _passes;
        OriginalEnemyController? _searcher;
        Vector3 _center, _forward;
        float _distance, _angle;
        bool _ignoreFowBlock;
        int _pumpedFrame = -1;

        // 적마다 대기열에 많아야 하나씩 들어가므로 대기열은 적 수만큼 미리 잡는다. 적의 피해 Collider는 저작된 단일 캡슐이고,
        // 플레이어 피해 Collider는 플레이어 루트 바로 아래에 붙는다(OriginalCharacterColliders.AddDamageReceiver).
        internal EnemyTargetSearch(OriginalEnemyController[] enemies, Transform? player, AbilitySystem? playerAbilities)
        {
            _bodies = new Dictionary<Collider, OriginalEnemyController>(enemies.Length);
            foreach (OriginalEnemyController enemy in enemies) _bodies[enemy.BodyCollider] = enemy;
            _queue = new Queue<OriginalEnemyController>(enemies.Length);
            _player = player;
            _playerAbilities = playerAbilities;
            _passes = Passes;
        }

        internal void Request(OriginalEnemyController enemy) => _queue.Enqueue(enemy);

        // 원작 AIMainBrain.Update다. 같은 프레임에는 한 번만 처리한다. 기다리는 사이 쓰러지거나 꺼진 적은 찾지 않고 빈 결과만 돌려준다.
        internal void Pump(int frame)
        {
            if (frame == _pumpedFrame) return;
            _pumpedFrame = frame;
            for (int i = 0; i < _searchesPerFrame && _queue.Count > 0; i++)
            {
                OriginalEnemyController enemy = _queue.Dequeue();
                // 결과가 플레이어면 found는 비어 있고 player가 참이다.
                OriginalEnemyController? found = null;
                bool player = false;
                if (enemy.Targetable) found = Search(enemy, out player);
                enemy.TargetSearched(found, player);
            }
        }

        // 원작 DoSearch(AIMainBrain.cs:209-219)는 픽업 탐색이 없으면 가까운 순 비교가 늘 거짓이라, 거르기를 통과한 후보 가운데
        // 버퍼 순서상 마지막이 결과다. 뒤에서부터 보고 처음 통과한 후보에서 멈추면 같은 결과에 차폐 레이를 덜 쏜다. 없으면 -1이다.
        internal static int PickLast(int count, Func<int, bool> passes)
        {
            for (int index = count - 1; index >= 0; index--)
                if (passes(index)) return index;
            return -1;
        }

        // 겹친 후보 가운데 PickLast가 고른 다른 팀 적이나 플레이어를 돌려준다. 결과가 플레이어면 null과 player 참이다.
        OriginalEnemyController? Search(OriginalEnemyController enemy, out bool player)
        {
            Transform body = enemy.transform;
            _searcher = enemy;
            _center = body.position + Vector3.up * _centerHeight;
            _forward = body.forward;
            _distance = enemy.SearchDistance;
            _angle = enemy.SearchAngle;
            _ignoreFowBlock = enemy.HurtSearch;
            int count = Physics.OverlapSphereNonAlloc(_center, _distance, _hits, GameplayDataSettings.Layers.DamageReceiverLayerMask,
                QueryTriggerInteraction.Collide);
            int picked = PickLast(count, _passes);
            player = false;
            if (picked < 0) return null;
            if (_bodies.TryGetValue(_hits[picked], out OriginalEnemyController other)) return other;
            player = true;
            return null;
        }

        // 원작 DoSearch의 거르기 순서를 따른다: 높이 차, 시야각(1.5 m 안은 보지 않음), 시야 × 대상 VisableDistanceFactor,
        // 1.5 m 밖이면 차폐 레이, 팀. 팀 조건은 레이보다 먼저 봐 같은 편에게 레이를 쏘지 않는다. 적도 플레이어도 아닌 피해 Collider는 뺀다.
        bool Passes(int index)
        {
            Collider collider = _hits[index];
            float factor;
            if (_bodies.TryGetValue(collider, out OriginalEnemyController other))
            {
                if (!_searcher!.IsHostileTo(other)) return false;
                factor = other.Abilities.GetCurrent(_visableDistanceFactor);
            }
            else if (_player != null && collider.transform.parent == _player && _searcher!.CanTargetPlayer)
                factor = _playerAbilities != null ? _playerAbilities.GetCurrent(_visableDistanceFactor) : 1f;
            else return false;
            Vector3 point = collider.transform.position;
            float pointDistance = Vector3.Distance(_center, point);
            if (!InSearchView(_center, _forward, _angle, _distance * factor, point, pointDistance)) return false;
            return pointDistance <= _senseDistance || OriginalSightQuery.HasLineOfSight(_center, point + Vector3.up * _targetHeight, _ignoreFowBlock);
        }

        // 차폐를 뺀 원작 DoSearch 거르기다. 높이 차가 4 m 이하이고, 1.5 m 안이거나 시야각 안이며, 거리가 시야 안이어야 한다.
        // pointDistance는 center와 point 사이 거리다.
        internal static bool InSearchView(Vector3 center, Vector3 forward, float angle, float distance, Vector3 point, float pointDistance) =>
            Mathf.Abs(center.y - point.y) <= _maxHeightDifference
            && (pointDistance <= _senseDistance || Vector3.Angle(forward, point - center) <= angle * .5f)
            && pointDistance <= distance;
    }
}
