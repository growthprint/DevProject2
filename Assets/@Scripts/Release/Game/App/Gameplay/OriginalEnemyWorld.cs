#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 적과 스포너가 월드에 묻고 맡기는 일을 모은 적 파사드다. 전투 월드 컨트롤러가 상호작용 객체 다음에 만들어 넘기므로
    // 적과 스포너는 컨트롤러를 거꾸로 참조하지 않는다. 하위 씬 이름은 컨트롤러가 넘긴 위임으로 그때그때 읽는다.
    internal sealed class OriginalEnemyWorld
    {
        // 원작 유기체 사망음 이벤트와, 사망 입자를 시체 발밑에서 띄우는 높이다.
        const string _enemyDeathSound = "event:/SFX/Combat/Hit/death_organic";
        const float _deathEffectHeight = .75f;

        readonly CombatSimulation _combat;
        readonly DeadBodySpawner _bodies;
        readonly WorldInteractions _interactions;
        readonly Func<string?> _activeSubscene;
        // 원작 AIMainBrain처럼 씬의 모든 적이 함께 쓰는 AI 표적 배치 탐색이다. 적 목록은 씬 동안 바뀌지 않아 생성 때 한 번 넘긴다.
        readonly EnemyTargetSearch _targetSearch;
        // 원작 SetActiveByPlayerDistance다. 적은 등록과 해제만 하고, 컨트롤러가 물리 틱마다 갱신한다. 파사드와 함께 레벨이 끝나면 버려진다.
        readonly OriginalDistanceActivation _distanceActivation = new();
        // 스포너가 내보냈거나 첫 초기화가 준비한 산 적이다. 체력바와 소리 알림이 스폰 후보 전체 대신 이 목록만 돈다.
        // 저작 순서대로 끼워 넣어 전체 배열을 돌 때와 판정 순서가 같다. 죽은 적은 체력바가 막대를 거두도록 남기고, 파괴될 때만 뺀다.
        readonly List<OriginalEnemyController> _spawnedEnemies;
        // _spawnedEnemies와 같은 자리의 저작 순번(Context.Enemies 위치)이다. 끼워 넣을 자리를 이분 탐색으로 찾는다.
        readonly List<int> _spawnedOrder;

        // 시간대 볼륨은 씬에 저작한 참조라 실행 중 바뀌지 않아 생성 때 한 번 받는다. 시간대가 없는 씬은 null이다.
        internal OriginalEnemyWorld(GameplayWorldContext context, CombatSimulation combat, DeadBodySpawner bodies, WorldInteractions interactions,
            OriginalTimeOfDay? timeOfDay, Func<string?> activeSubscene)
        {
            Context = context;
            _combat = combat;
            _bodies = bodies;
            _interactions = interactions;
            TimeOfDay = timeOfDay;
            _activeSubscene = activeSubscene;
            // 원작 DoSearch처럼 플레이어 피해 Collider도 탐색 후보다. 플레이어 루트로 그 Collider를 알아보고, 플레이어 어트리뷰트에서
            // 보이는 거리 배율을 읽는다. 시험 월드처럼 플레이어나 런타임이 없으면 비워 넘긴다.
            _targetSearch = new EnemyTargetSearch(context.Enemies, context.Motor != null ? context.Motor.transform : null,
                context.Runtime != null ? context.Runtime.Vitals.Abilities : null);
            // 나온 적은 많아야 저작한 적 수라 미리 잡아 실행 중 늘리지 않는다. 플레이어 공격 소리도 같은 목록을 돌도록 전투에 넘긴다.
            _spawnedEnemies = new List<OriginalEnemyController>(context.Enemies.Length);
            _spawnedOrder = new List<int>(context.Enemies.Length);
            combat.BindSoundListeners(_spawnedEnemies);
        }

        // 스포너의 알림과 상인 부착물이 쓰는 월드 공용 컨텍스트다.
        internal GameplayWorldContext Context { get; }
        // 적 AI가 원작 TimeOfDayController.AtNight와 NightViewAngleFactor를 읽는 경로다.
        internal OriginalTimeOfDay? TimeOfDay { get; }
        // 적과 스폰 그룹은 준비 뒤에만 읽는다. 플레이어가 파괴됐으면 컨텍스트가 원점을 돌려준다.
        internal Vector3 PlayerPosition => Context.PlayerPosition;
        // 초기화, 내비게이션, 씬 전환, 대사 같은 컨트롤러 조건과 Runtime 활성 판정을 컨텍스트의 위임으로 함께 본다.
        internal bool SimulationReady => Context.SimulationReady;
        // 사망 기록, 스폰 루트 소속, 거리 활성화에 쓰는 현재 하위 씬의 원본 이름이다. 하위 씬이 없거나 첫 초기화 전이면 null이다.
        internal string? ActiveSubsceneId => _activeSubscene();
        // 공용 HUD 체력바가 도는 나온 적 목록이다. 적을 준비하기 전에 연결해도 뒤에 준비한 적이 보이도록 같은 목록 객체를 내준다.
        internal IReadOnlyList<OriginalEnemyController> SpawnedEnemies => _spawnedEnemies;

        // 준비 뒤 확정 세션이 있는 동안에만 부르므로 세션을 다시 검사하지 않는다. 그동안은 앱도 살아 있어 GameManager에서 바로 읽는다.
        CampaignState Campaign => GameManager.SessionManager!.Snapshot().Campaign;

        // 런타임마다 다른 string.GetHashCode 대신 FNV-1a 해시를 써서, 같은 출격과 지점이면 늘 같은 스폰 시드를 만든다.
        internal int Seed(string key)
        {
            // 출격이 없으면 출격 ID 대신 사망 토큰으로 시드를 나눈다.
            string seedKey = (Campaign.Raid?.RaidId ?? "tutorial:" + (Campaign.Gameplay.Deaths.DeadCharacterToken ?? 0)) + ":" + key;
            return StableHash.Fnv1a(seedKey, seedKey.Length);
        }

        // 적 준비가 저장된 사망을 묻는 사건 완료 판정이다. 협력 객체와 같은 컨텍스트 규칙을 쓴다.
        internal bool EventComplete(string id) => Context.TutorialEventComplete(id);

        // 적이 저장된 사망을 복원할 때 시체를 둘 처치 위치를 찾는다. 기록이 없으면 false다.
        internal bool TryDeathPosition(string id, out Vector3 position)
        {
            bool found = CampaignEventLog.TryDeathPosition(Campaign, id, out float x, out float y, out float z);
            position = new Vector3(x, y, z);
            return found;
        }

        // 적 이동도 플레이어 모터와 같은 원본 유기체 발소리 이벤트를 쓰고, 시뮬레이션이 준비된 동안에만 재생한다.
        // 오디오가 꺼져 있으면 관리자가 재생을 조용히 무시한다. 원작 CharacterSoundMaker처럼 다른 팀 적에게도 들린다. 반경은 그 적의
        // WalkSoundRange나 달릴 때 RunSoundRange 현재값이고, 같은 팀과 거리는 Hear가 거른다. 원작의 무게 75% 이상 ×1.5는 적에게 무게가 없어 뺀다.
        internal void Footstep(Vector3 position, OriginalEnemyController enemy, bool running)
        {
            if (!SimulationReady) return;
            Context.Audio.PlayOneShot(OriginalCharacterMotion._footstepSound, position);
            float range = enemy.Abilities.GetCurrent(running ? CharacterAttributes.RunSoundRange : CharacterAttributes.WalkSoundRange);
            _combat.AlertEnemies(position, range, enemy);
        }

        // 적의 사격과 근접 공격도 전투 시뮬레이션이 같은 탄도와 피해 규칙으로 처리한다. 피해 배율은 적 어트리뷰트에 있다.
        internal void Attack(OriginalEnemyController enemy, OriginalItemDefinition weapon)
        {
            _combat.EnemyAttack(enemy, weapon);
            // 원작 ItemAgent_Gun처럼 AI 총소리도 주변 적에게 퍼진다. 같은 팀은 Hear가 거르고, 다른 팀은 경계 트리로 넘어간다.
            // 플레이어 공격 소리와 같은 나온 적 목록에 알린다.
            if (!weapon.IsGun) return;
            _combat.AlertEnemies(enemy.transform.position, weapon.Stat("SoundRange"), enemy);
        }

        // 적이 탐색 결과를 받기 전까지 한 번만 넣는다. 결과는 TargetSearched로 돌려준다.
        internal void RequestTargetSearch(OriginalEnemyController enemy) => _targetSearch.Request(enemy);

        // 적 Update가 프레임마다 부른다. 같은 프레임의 두 번째 호출부터는 아무것도 하지 않아 프레임당 3건만 처리한다.
        internal void PumpTargetSearch() => _targetSearch.Pump(Time.frameCount);

        // 준비된 산 적이 소속 하위 씬 이름과 거리 판정 여부를 함께 등록하고, 죽거나 파괴될 때 뺀다.
        internal void TrackDistance(GameObject enemy, string subscene, bool byDistance) => _distanceActivation.Register(enemy, subscene, byDistance);
        internal void ForgetDistance(GameObject enemy) => _distanceActivation.Unregister(enemy);

        // 컨트롤러가 시뮬레이션이 도는 물리 틱마다 부른다. 하위 씬을 바꾼 뒤 첫 틱이 꺼진 하위 씬의 적을 끈다.
        // 플레이어가 파괴됐으면 컨텍스트가 원점을 돌려준다.
        internal void TickDistance() => _distanceActivation.Tick(PlayerPosition, ActiveSubsceneId);

        // 준비된 산 적이 한 번 등록한다. 저작 순번은 등록할 때 한 번만 찾고, 앞선 순번의 적 뒤에 끼워 넣는다.
        internal void TrackSpawned(OriginalEnemyController enemy)
        {
            int order = Array.IndexOf(Context.Enemies, enemy);
            int index = _spawnedOrder.BinarySearch(order);
            if (index < 0) index = ~index;
            _spawnedOrder.Insert(index, order);
            _spawnedEnemies.Insert(index, enemy);
        }

        // 파괴되는 적이 부른다. 죽은 채 되살아나 등록하지 않은 적도 부르므로, 없는 적이면 아무것도 하지 않는다.
        internal void ForgetSpawned(OriginalEnemyController enemy)
        {
            int index = _spawnedEnemies.IndexOf(enemy);
            if (index < 0) return;
            _spawnedEnemies.RemoveAt(index);
            _spawnedOrder.RemoveAt(index);
        }

        // 근접 적이 휘두르기 직전에 오른손에 원본 공격 예고 효과를 낸다. 효과 풀은 전투 시뮬레이션이 가진다.
        internal void MeleeAlert(Transform socket) => _combat.EnemyMeleeAlert(socket);

        // 원작 Health.Hurt의 PopText처럼 적이 받아들인 피해마다 피해 지점에 최종 피해 숫자를 띄운다. 적의 피격 큐가 부르고,
        // 원작의 2m 위 오프셋은 GameplayUI가 더한다.
        internal void DamagePop(Vector3 point, float value, bool crit) => Context.View.Damage(point, value, crit, Context.Camera);

        // 새 처치와 저장된 사망 복원이 함께 쓴다. 시체 전리품 지점은 시체와 유실물 생성 객체가 빌린다.
        internal void RestoreCorpse(OriginalEnemyController enemy, int[] loot, Vector3 position) => _bodies.SpawnCorpse(enemy, loot, position);

        // 처치 수와 경험치를 사건과 한 명령으로 확정하려고 피해 출처만 받는다. 원작처럼 플레이어가 죽인 적만 프리셋 경험치와 처치 수를 받는다.
        // weaponItemId와 crit은 처치 과제 거르기에만 쓴다.
        internal void Died(OriginalEnemyController enemy, int[] loot, bool fromPlayer, int weaponItemId, bool crit)
        {
            // 다시 불러온 월드에서도 전리품이 사망 위치에 남도록 처치 키에 좌표를 함께 기록한다.
            Vector3 position = enemy.transform.position;
            // 처치 수, CampaignEventLog 처치 키, 경험치를 같은 프레임에 처치 명령 하나로 확정한다. 이 적의 사망을 조건으로 하는 문은
            // 커밋이 동기로 내는 세션 변경 알림이 연다. 명령이 거절되면 세 소유자 모두 기록이 없으므로 문도 열지 않는다.
            _interactions.SubmitKill(CampaignEventLog.DeathKey(enemy.SourceId, position.x, position.y, position.z), enemy.NameKey, fromPlayer,
                enemy.Experience);
            // 처치는 준비된 월드의 적에게서만 오므로 컨텍스트의 앱 수명 오디오 관리자로 바로 재생한다.
            Context.Audio.PlayOneShot(_enemyDeathSound, position);
            // 사망 입자는 처치 확정과 같은 프레임에 시작한다.
            _combat.DeathEffect(position + Vector3.up * _deathEffectHeight);
            RestoreCorpse(enemy, loot, position);
            NotifyQuestKill(enemy, fromPlayer, weaponItemId, crit);
        }

        // 원작 QuestTask_KillCount는 처치 수 기록과 따로 Health.OnDead를 받으므로 처치 명령 결과와 관계없이 과제 소유자에게 알린다.
        // 처치가 일어난 논리 씬도 넘긴다. 세션이 없으면 진행할 퀘스트도 없어 알리지 않는다. 공격자 버프 판정은 원작 버프 ID와
        // GAS 효과의 대응이 아직 없어 넘기지 않으므로, 버프가 필요한 처치 과제는 세지 않는다.
        void NotifyQuestKill(OriginalEnemyController enemy, bool fromPlayer, int weaponItemId, bool crit)
        {
            if (!Context.TrySession(out GameSessionManager? session)) return;
            GameManager.Quests.NotifyEnemyKilled(new QuestKillInfo(enemy.NameKey, fromPlayer, weaponItemId, crit, null, session!.Snapshot().SceneId));
        }
    }
}
