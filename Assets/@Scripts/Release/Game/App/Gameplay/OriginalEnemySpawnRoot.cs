#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 CharacterSpawnerRoot다. 월드 컨트롤러가 첫 초기화 뒤 매 프레임 Tick을 부르고, 조건이 처음 맞으면 확률을 한 번 굴려 스폰 대상을 부른다.
    // 원작 Init처럼 소속 하위 씬이 처음 켜지기 전에는 판정하지 않고, 그 뒤로는 스포너를 메인 씬으로 옮긴 원작처럼 하위 씬을 떠나도 판정을 계속한다.
    // 스폰 상태는 레벨 씬 수명의 필드이며 파일에 쓰지 않는다. 씬을 다시 불러오면 처음으로 돌아간다.
    public sealed class OriginalEnemySpawnRoot : MonoBehaviour
    {
        // 원본 하위 씬 이름 뒤에 원작 SpawnerGuid를 붙인 값이다. 같은 출격이면 같은 확률 결과를 내는 시드 키다.
        [SerializeField] string _spawnerGuid = "";
        // 이 루트가 속한 원본 하위 씬 이름이다. 비어 있으면 하위 씬과 상관없이 판정한다.
        [SerializeField] string _subsceneId = "";
        [SerializeField] OriginalSpawnTiming _timing = new OriginalSpawnTiming();
        [SerializeField, Range(0f, 1f)] float _chance = 1f;
        [SerializeField] float _minimumDistance = 25f, _maximumDistance = 9999f;
        // 원작 needTrigger와 trigger다. 비어 있으면 트리거를 기다리지 않는다.
        [SerializeField] OriginalGameplayPoint? _trigger;
        // 원작 spawnerComponent다. 스포너 하나(_group)나 그룹 선택기(_selector) 중 하나만 저작한다.
        [SerializeField] OriginalEnemySpawnGroup? _group;
        [SerializeField] OriginalEnemySpawnSelector? _selector;
        // 원작 created다. 확률 실패도 스폰한 것으로 쳐서 다시 굴리지 않는다.
        bool _created;
        // 원작 inited다. 소속 하위 씬이 켜진 것을 처음 본 뒤로 참이다. 하위 씬 이름이 비어 있으면 첫 Tick에 선다.
        bool _inited;

        // 스포너가 후보를 고를 때 읽는 원작 minDistanceToPlayer, maxDistanceToPlayer다.
        internal float MinimumDistance => _minimumDistance;
        internal float MaximumDistance => _maximumDistance;
        // 스폰한 적이 거리 활성화에 소속 하위 씬으로 등록하는 이름이다.
        internal string SubsceneId => _subsceneId;

        // 첫 월드 초기화가 적 준비보다 먼저 부른다. 원작에는 스폰 전 캐릭터가 없으므로 저작한 후보를 모두 끈다.
        // 원작 Start처럼 기지 레벨에서는 최소 거리를 0으로 둔다.
        internal void Begin(bool baseLevel)
        {
            if (baseLevel) _minimumDistance = 0f;
            if (_group != null) _group.Hide();
            if (_selector != null) _selector.Hide();
        }

        // 원작 Update의 스폰 판정이다. hour는 하루 시각(시), levelTime은 시뮬레이션이 열린 뒤 쌓은 초, weather는 원작 Weather 정수다.
        // 원작 despawnIfTimingWrong은 조건이 끝나면 created를 되돌린다. 숨은 후보를 다시 쓰는 처리는 옮기지 않았다. 제로존 스포너는 모두 이 규칙을 끈다.
        // 그래서 다시 스폰하면 스포너가 아직 준비하지 않은 후보에서만 고른다.
        internal void Tick(OriginalEnemyWorld world, GameplayWorldContext shared, DifficultyState? difficulty, double hour, float levelTime, int weather)
        {
            // 원작 Update는 자기 하위 씬이 로드된 동안에만 Init을 한 번 부르고, Init 뒤로는 하위 씬과 상관없이 돈다.
            bool related = _subsceneId.Length == 0 || world.ActiveSubsceneId == _subsceneId;
            if (!_inited)
            {
                if (!related) return;
                _inited = true;
            }
            bool due = _timing.IsDue(hour, levelTime, weather);
            // 아직 스폰하지 않았고 조건 시각이며 트리거가 맞을 때 한 번만 굴린다.
            if (!_created && due && TriggerSatisfied(world))
            {
                _created = true;
                // 원작 Random.Range(0, 1) > spawnChance면 실패다. 같은 출격과 루트면 늘 같은 결과다. 확률이 1이면 실패할 수 없어 굴리지 않는다.
                if (_chance < 1f && new System.Random(world.Seed(_spawnerGuid)).NextDouble() > _chance) return;
                if (_group != null) _group.Spawn(world, shared, difficulty, this);
                else if (_selector != null) _selector.Spawn(world, shared, difficulty, this);
                // 원작은 꺼진 하위 씬 묶음 아래로 스폰해 보이지 않는다. 소속 하위 씬이 꺼진 동안 스폰한 적은 소속 하위 씬으로 등록되므로,
                // 다음 물리 틱을 기다리지 않고 거리 활성화를 바로 돌려 한 프레임도 켜지지 않게 한다.
                if (!related) world.TickDistance();
            }
            else if (_created && !due && _timing.DespawnIfTimingWrong) _created = false;
        }

        // 원작 needTrigger다. 트리거가 없으면 기다리지 않고, 있으면 그 지점이 이번 프레임 한 번 낸 플레이어 포함 결과를 읽는다.
        bool TriggerSatisfied(OriginalEnemyWorld world) => _trigger == null || _trigger.ContainsPlayerThisFrame(world.PlayerPosition);
    }
}
