#nullable enable annotations
using System;
using System.Linq;
using Game.Core;
using Game.GAS;
using UnityEngine;
using UnityEngine.AI;

namespace Game.App
{
    // 원본 적 프리셋의 시야, 체력, 반응, 무기를 전투 월드에 연결한다.
    // 적은 플레이어와 같은 벽 충돌과 탄환 경로를 쓰고, 생성 위치는 원본 데이터로 제작한다.
    // 원작 AIController_Melee 전투 트리는 길어서 OriginalEnemyController.Melee.cs의 partial로 나눈다.
    // 비밀 상인의 피해 전 비적대와 상점 부착물 숨김은 OriginalEnemyController.Merchant.cs에 모아 상인 규칙을 한곳에서 읽게 한다.
    // 원작 팀 교전을 옮기며 표적이 플레이어만이 아니게 되어, 표적 규칙과 배치 탐색 연결은 .Targeting.cs, 총기 적의 사격과
    // 전투 이동 병렬 순환은 .GunCombat.cs로 나눈다(post5 P1-D).
    public sealed partial class OriginalEnemyController : MonoBehaviour
    {
        [SerializeField] string _sourceId = "";
        // Editor의 ComponentAuthoring이 필드 이름으로 채우는 원본 프리셋 이름 키다. 원작 SavesCounter처럼 처치 수의 키로 쓴다.
        [SerializeField] string _nameKey = "";
        [SerializeField] float _health;
        [SerializeField] float _sight;
        [SerializeField] float _angle;
        [SerializeField] float _reaction;
        [SerializeField] float _damageMultiplier;
        [SerializeField] float _moveMultiplier;
        [SerializeField] float _forgetTime;
        [SerializeField] int _weaponType;
        [SerializeField] int[] _loot = Array.Empty<int>();
        // 원작 CharacterRandomPreset.AddBullet의 bulletQualityDistribution(품질과 가중치), bulletFilter.requireTags, bulletExclusiveTags,
        // bulletCountRange다. 제작 도구가 프리셋 값으로 채운다. 분포가 비면 원작처럼 찾을 총알이 없어 넣지 않는다.
        [SerializeField] int[] _bulletQualities = Array.Empty<int>();
        [SerializeField] float[] _bulletQualityWeights = Array.Empty<float>();
        [SerializeField] string _bulletRequireTag = "";
        [SerializeField] string[] _bulletExcludeTags = Array.Empty<string>();
        [SerializeField] Vector2 _bulletCountRange = Vector2.one;
        // Prepare가 정한 시체 전리품의 칸별 수량이다. _loot와 같은 순서이고, 총알만 기본 수량에 원작 bulletCountRange 배율을 곱한다.
        int[] _lootCounts = Array.Empty<int>();
        [SerializeField] Animator _animator = null!;
        [SerializeField] NavMeshAgent _agent = null!;
        [SerializeField] Collider _hitCollider = null!;
        [SerializeField] OriginalGameplayPoint? _spawnTrigger;
        [SerializeField] float _spawnDelay;
        [SerializeField] Transform _model = null!;
        // 적이 월드에 묻고 맡기는 일을 받는 적 파사드다. 첫 월드 초기화나 스폰 그룹이 Prepare로 넘긴다.
        OriginalEnemyWorld? _world;
        OriginalItemDefinition? _weapon;
        // 추첨한 무기의 교전 사거리 제곱이다. Prepare가 한 번 계산해 둔다. 시야 거리는 배치 탐색이 탐색 거리로 읽는다.
        float _sqrAttackRange;
        // 원본 HeadCollider.prefab의 머리 판정 반지름이다.
        const float _headRadius = .42f;
        // 생성점과 순찰 목표를 NavMesh 위로 맞출 때 찾아볼 최대 거리다.
        const float _navSampleDistance = 2f;
        // 생성점에 NavMesh가 없을 때 오류 기록에 넣을 가장 가까운 NavMesh를 찾는 거리다. 진단 값이라 판정에는 쓰지 않는다.
        const float _navDiagnosticSearchDistance = 100f;
        // 총을 든 적은 배율을 곱한 무기 사거리의 0.75배 안에서 교전한다.
        const float _gunEngageRangeFactor = .75f;
        // 원작 AICharacterController.Update의 updateValueTimer 간격이다. 1초마다 밤 반응 시간을 다시 정한다.
        const float _valueUpdateInterval = 1f;
        // 시야 차폐 레이를 쏘는 높이다. 적과 플레이어 발밑 위 0.8m를 잇는다.
        const float _sightHeight = .8f;
        // 지점 없이 받은 피해의 숫자를 띄우는 높이다. 원작 DamageAction이 버프 피해 지점을 발밑 위 0.8 m로 둔다.
        const float _bodyPopHeight = .8f;
        // 순찰 목표를 다시 고르는 간격은 2초에 0~3초를 더한 값이다.
        const float _patrolIntervalMin = 2f, _patrolIntervalSpread = 3f;
        // 추적 경로는 0.2초마다, 목표가 0.5 m(제곱 0.25) 넘게 움직였을 때만 다시 요청한다.
        const float _repathSqrDistance = .25f, _repathInterval = .2f;
        float _lastSeen, _nextAttack, _started;
        // 원작 lastDamageInfo처럼 마지막으로 피해를 받아들인 시각이다. 맞은 적이 없으면 음의 무한대라 경과 시간이 늘 길다.
        float _lastHurt = float.NegativeInfinity;
        // 마지막으로 받아들인 피해에 공격자(주인공이나 다른 AI)가 있었는지다. 원작 CheckHurt와 isHurtSearch는 fromCharacter가 있어야 참이다.
        bool _lastHurtFromCharacter;
        // 원작 SearchEnemyAround와 CheckObsticle의 isHurtSearch 조건이다. 1.5초 안에 공격자가 준 1 이상의 피해를 받았으면
        // 탐색 거리가 두 배가 되고 시야가 FowBlock(18)을 무시한다.
        const float _hurtSearchTime = 1.5f, _hurtSearchDamage = 1f;
        // 원작 AI 템플릿의 순찰 트리가 SearchEnemyAround로 시야 발견을 하는지다. 원작 Patrol_Scare(UltraMan)는 이 노드가 없어
        // 순찰 중에는 소리와 피격으로만 알아챈다. 알아챈 뒤 전투의 시야 판정은 그대로다. 제작 도구가 그런 템플릿만 끈다.
        [SerializeField] bool _noticeBySight = true;
        // 원작처럼 적도 플레이어와 같은 캐릭터 어트리뷰트로 체력, 피해 배율, 걷기 속도를 갖는다. Prepare가 만들고 적과 함께 사라진다.
        // 준비 전이나 스폰 그룹이 고르지 않은 적은 비어 있지만, 그런 적은 생성되지 않아 체력을 읽는 곳에 오지 않는다.
        AbilitySystem? _abilities;
        // 카탈로그의 적 효과 묶음에서 꺼낸 피해 효과다. 피해 숫자를 받은 피해로 바꾸고 피격 큐를 낸다.
        GameplayEffectDefinition? _hurtEffect;
        bool _spawned, _dead, _aware;
        // 추적 경로 요청을 제한한다. 발각 직후에는 바로 요청하고, 그 뒤에는 0.2초마다 목표가 0.5 m 넘게 움직였을 때만
        // 다시 요청해 NavMesh 경로 계산 비용을 줄인다.
        bool _pathDirty;
        float _nextPathTime;
        Vector3 _lastDestination;
        Vector3 _home;
        // 생성점과 순찰 위치를 찾을 때 쓰는 NavMesh 필터다. 에이전트 타입은 실행 중 바뀌지 않아 Prepare에서 한 번 만든다.
        NavMeshQueryFilter _navFilter;
        // 순찰, 탄속, 사격 간격, 무기 후보와 가중치도 원본 프리셋 값을 받는다.
        [SerializeField] float _patrolRange, _bulletSpeedMultiplier=1f;
        [SerializeField] Vector2 _shootTimeRange, _shootSpaceRange;
        // 원본 CharacterRandomPreset.shootDelay다. 사거리에 든 뒤 연사를 시작하기 전에 기다리는 시간이다.
        [SerializeField] float _shootDelay = .2f;
        // 원본 CharacterRandomPreset처럼 난이도 공격 배율을 곱한 실제 연사와 휴식 범위다.
        // 직렬화된 프리셋 값은 그대로 두고 Prepare가 이 값만 계산한다.
        Vector2 _burstRange, _restRange;
        [SerializeField] int[] _weaponPool=Array.Empty<int>();
        [SerializeField] float[] _weaponWeights=Array.Empty<float>();
        System.Random? _random;
        // 순찰과 사격 순환의 시각이다. _shootAt은 shootDelay를 기다리는 동안의 연사 시작 시각이고, 기다리지 않을 때는 0이다.
        float _patrolUntil, _shootAt, _burstUntil, _restUntil;
        // 원본 Combat_Attack_Melee 값이다. 휘두른 뒤 0.1초 쉬고, 사거리 안이면 예고 효과를 내고 0.2초 뒤 휘두른다.
        const float _meleeWaitTime = .1f, _meleeAlertTime = .2f;
        // 다음 예고를 낼 수 있는 시각과 예고 뒤 휘두를 시각이다. 휘두를 시각이 0보다 크면 예고 중이다.
        float _meleeReadyAt, _swingAt;
        // 원본 AI는 defaultWeaponOut이 꺼져 있어 적을 처음 알아챘을 때 무기를 꺼내고 다시 넣지 않는다. 꺼낸 시각이며 음수면 아직 맨손이다.
        float _weaponOutAt = -1f;
        // 오리 리그 외형이나 전용 리그 모델이다. 둘 다 같은 소켓과 무기 표시 계약을 내준다. 모델에 없으면 비어 있다.
        OriginalEnemyVisual? _appearance;
        // 원작 CharacterAnimationControl 몫인 Animator 파라미터, 손 레이어, 무기 표시 시점을 맡는다. Prepare가 만든다.
        OriginalEnemyAnimation? _animation;
        public float BulletSpeedMultiplier=>_bulletSpeedMultiplier;
        public string SourceId => _sourceId;
        // 사망 판정의 기준이다. 저장에서 되살린 시체는 GAS 사망 태그 없이 이 값만 서고 체력 어트리뷰트는 최대치로 남으므로,
        // 체력은 HealthVisible이나 Dead를 먼저 본 뒤에 읽는다.
        public bool Dead => _dead;
        // 체력과 최대 체력은 적 어트리뷰트의 현재값이다. 체력 표시는 난이도를 반영한 최대 체력을 쓰고,
        // 적이 플레이어를 알아챘거나 다쳤을 때만 보인다. 읽는 곳은 Targetable을 먼저 보므로 준비된 적만 읽는다.
        public float Health => _abilities!.GetCurrent(CharacterAttributes.CurrentHealth);
        internal float MaxHealth => _abilities!.GetCurrent(CharacterAttributes.MaxHealth);
        // 다른 팀 적과만 싸우는 적은 플레이어를 알아챈 것이 아니므로, 다치기 전에는 지금처럼 막대를 보이지 않는다.
        internal bool HealthVisible => Targetable && (TargetIsPlayer || Health < MaxHealth);
        // 전투가 적 공격의 피해 배율을 이 어트리뷰트에서 읽는다. 준비된 적만 공격하므로 비어 있지 않다.
        internal AbilitySystem Abilities => _abilities!;
        // 원본 HeadCollider처럼 머리 조준 구는 피해 Collider와 따로 두고, 애니메이션으로 움직이는 머리 소켓에 붙인다.
        SphereCollider? _headCollider;
        internal Collider? HeadCollider => _headCollider;
        internal Collider BodyCollider => _hitCollider;
        internal bool Targetable => _spawned && !_dead && isActiveAndEnabled;
        // 첫 월드 초기화에서 준비했는지 알려 준다. 스폰 그룹이 고르지 않은 적은 월드 참조가 없다.
        // 튜토리얼 하위 씬에서 돌아올 때 준비된 적만 다시 켠다.
        internal bool Prepared => _world != null;
        // 발사 소켓과 피격 표시는 Prepare에서 한 번 찾아 두고, 발사나 피격 때마다 다시 찾지 않는다.
        Transform? _muzzle;
        OriginalHurtVisual? _hurt;
        internal Transform? Muzzle => _muzzle;
        [SerializeField] int _experience;
        // 조준 산포는 원본 적 프리셋의 산포 배율에, 목표가 달리거나 화면 밖일 때의 보정을 곱한다.
        [SerializeField] float _scatterMultiplier = 1, _runningScatter = 1, _offscreenScatter = 1;
        internal float ScatterMultiplier(bool running, bool offscreen) => Mathf.Max(.1f, _scatterMultiplier * (running ? _runningScatter : 1) * (offscreen ? _offscreenScatter : 1));
        // 원작 CharacterRandomPreset의 체력바 표시와 대시 가능 값이다. 원작 Health.RequestHealthBar처럼 체력바를 끈 적(폭풍 생물)은
        // 공용 HUD가 막대를 만들지 않는다. 기본값은 원작 클래스 기본값이고 제작 도구가 프리셋 값으로 덮는다.
        [SerializeField] bool _showHealthBar = true;
        [SerializeField] bool _canDash;
        // 원작 CharacterRandomPreset.setActiveByPlayerDistance다. 참이면 플레이어에게서 50 m 넘게 떨어진 동안 꺼진다.
        // 기본값은 원작 기본값이고 제작 도구가 프리셋 값으로 덮는다. 원작에서 거짓인 프리셋은 보스 셋과 폭풍 차량뿐이다.
        [SerializeField] bool _setActiveByPlayerDistance = true;
        internal bool ShowHealthBar => _showHealthBar;
        // 원작 프리셋의 팀(Teams 정수)과 영혼 여부다. 기본값은 원작 CharacterRandomPreset 기본값이다.
        // 팀은 팀 교전의 표적 고르기, 소리 거르기, 탄 통과가 읽는다. 영혼을 쓰는 런타임은 이 계획에 없어 그 필드만 읽지 않는 경고를 끈다.
        [SerializeField] int _team = 1;
#pragma warning disable CS0414
        [SerializeField] bool _hasSoul = true;
#pragma warning restore CS0414
        // 원작 CharacterRandomPreset.aiCombatFactor와 프리셋 스탯 BodyArmor, HeadArmor, GasMask, DashCanControl, ReloadSpeedGain이다.
        // 기본값은 원작 기본값이고 제작 도구가 필드 이름으로 프리셋 값을 덮는다. 배율은 AI끼리 싸울 때만 피해에 곱하고,
        // 스탯은 PrepareAbilities가 같은 이름 어트리뷰트의 기본값으로 넣는다. ReloadSpeedGain은 AI 장전 시간이 읽고, GasMask는 구역 피해가 없어 아직 읽는 곳이 없다.
        [SerializeField] float _aiCombatFactor = 1f;
        [SerializeField] float _bodyArmor;
        [SerializeField] float _headArmor;
        [SerializeField] float _gasMask;
        [SerializeField] float _dashCanControl;
        [SerializeField] float _reloadSpeedGain;
        // 탄 통과 규칙과 피해 배율을 정하는 전투 시뮬레이션이 읽는다.
        internal int Team => _team;
        internal float AiCombatFactor => _aiCombatFactor;
        // 원작 nightReactionTimeFactor와 nightVisionAbility다. 밤 반응 시간과 야간 시야각은 OriginalEnemyController.Melee.cs의
        // RefreshNightReaction과 ViewAngle이 읽는다(5-W2b). 원작 gunDistanceMultiplier는 총 사거리 배율이며 GunDistanceMultiplier 어트리뷰트에 넣는다.
        [SerializeField] float _nightReactionFactor = 1.5f;
        [SerializeField] float _nightVision = .5f;
        [SerializeField] float _gunDistanceMultiplier = 1f;

        // 처치 명령(GameplayCommands.TryRecordKill)이 처치 수 키와 프리셋 경험치를 읽는다. 처치 수는 소유자를 모두 확인한 명령만 올리도록
        // 적은 값만 내준다.
        internal string NameKey => _nameKey;
        internal int Experience => _experience;

        // 첫 활성 프레임 전에 원본 위치와 의존성을 고정한다. 첫 월드 초기화가 Update 경로에서 부르므로 릴리즈에서는 던지지 않는다.
        // 외형 준비 실패는 리그 저작 결함이고 외형 쪽이 이미 보고하므로 호출자에게 돌려줄 실패가 없다. 총을 든 적은 외형이 만든 Muzzle에서만
        // 쏘므로 외형 연결은 EditMode 저작 검사가 지킨다. subscene은 스폰 루트의 원본 하위 씬 이름이고, 첫 월드 초기화가 준비한
        // 메인 씬 적은 비어 있다.
        internal void Prepare(OriginalEnemyWorld world, OriginalGameplayCatalog catalog, DifficultyState? difficulty, string subscene = "")
        {
            _world = world; _random=new System.Random(world.Seed(_sourceId));
            if(_weaponPool.Length>0)
            {
                double choice=_random.NextDouble()*_weaponWeights.Sum();
                int index=0;for(;index<_weaponPool.Length-1;index++) {choice-=_weaponWeights[index];if(choice<0)break;}
                _weaponType=_weaponPool[index];
                // 전리품의 총과 근접 무기를 추첨한 무기로 바꾼다. 저작한 전리품 ID는 모두 카탈로그에 있다.
                var loot = new System.Collections.Generic.List<int>(_loot.Length + 1);
                foreach (int type in _loot)
                {
                    OriginalItemDefinition item = catalog.DefinitionOf(type);
                    if (!item.IsGun && !item.IsMelee) loot.Add(type);
                }
                loot.Add(_weaponType);
                _loot = loot.ToArray();
            }
            // 추첨했거나 저작된 무기 ID도 카탈로그에 있다.
            _weapon = catalog.DefinitionOf(_weaponType);
            // 근접 사거리는 플레이어 근접 판정과 같은 정의 규칙을 쓴다. 총 사거리는 원작 ItemAgent_Gun.BulletDistance처럼
            // 프리셋 gunDistanceMultiplier를 곱한 값이라 교전 거리도 같은 배율로 늘어난다.
            float attackRange = _weapon.IsGun ? _weapon.BulletDistance * _gunDistanceMultiplier * _gunEngageRangeFactor : _weapon.MeleeRange;
            _sqrAttackRange = SquaredRange(attackRange);
            // 추첨한 무기와 원본 얼굴을 적 외형에 적용하고 Muzzle과 머리 소켓을 가져온다.
            // 외형 실패는 TryPrepare가 직접 보고하므로 결과를 보지 않고, 실패해도 Muzzle과 HeadSocket은 읽을 수 있다.
            Transform? head = null;
            // 오리 리그 외형과 전용 리그 모델을 같은 계약으로 받는다.
            if (_model.TryGetComponent(out OriginalEnemyVisual appearance))
            {
                appearance.TryPrepare(_weapon);
                _muzzle = appearance.Muzzle;
                head = appearance.HeadSocket;
                // 원본 적은 맨손으로 순찰하므로 무기 표시는 처음 알아챈 뒤 꺼낼 때까지 숨긴다.
                _appearance = appearance;
                appearance.SetWeaponVisible(false);
            }
            // 외형이 전용 리그인지는 애니메이션 쪽이 보고 손 레이어 구동을 정한다.
            _animation = new OriginalEnemyAnimation(_animator, _appearance);
            _hurt = GameplayComponents.GetOrAdd<OriginalHurtVisual>(_model.gameObject);
            // 원본 HeadCollider.prefab처럼 Helmet 소켓에 HeadCollider(20) 레이어의 단단한 구를 붙인다. 플레이어의 머리 조준 레이만 이 레이어를 본다.
            if (head != null)
            {
                var target = new GameObject("OriginalHeadAim");
                target.layer = GameLayers.HeadCollider;
                target.transform.SetParent(head, false);
                _headCollider = target.AddComponent<SphereCollider>();
                _headCollider.radius = _headRadius;
                // 원본 CharacterMainControl처럼 피해 캡슐을 머리 높이에 맞춘다. 저작된 피해 Collider는 레이어 3 자식 캡슐이다.
                if (_hitCollider is CapsuleCollider body) OriginalCharacterColliders.FitToHead(body, transform, head);
            }
            // 원본 이동 클립의 발소리 이벤트는 Animator가 있는 객체에서 받는다. 원작 CharacterSoundMaker처럼 달리는지도 넘겨
            // 다른 팀 적이 듣는 반경을 정한다.
            GameplayComponents.GetOrAdd<OriginalFootstepReceiver>(_animator.gameObject)
                .Configure(()=>{if(PostsFootstep)world.Footstep(transform.position, this, _running);});
            // 난이도 체력 배율은 플레이어 피해 배율과 같은 DamageRules 규칙으로 곱한다. 난이도는 호출자가 난이도 소유자에서 읽어 넘긴다.
            CharacterAbilitySet? abilitySet = catalog.EnemyAbilitySet;
            PrepareAbilities(abilitySet!, DamageRules.EnemyHealth(_health, difficulty));
            // 원작 baseReactionTime이다. 밤 보정은 RefreshNightReaction이 이 값에서 1초마다 다시 정한다.
            _baseReaction = _reaction * (difficulty?.EnemyReactionTimeFactor ?? 1f);
            _reaction = _baseReaction;
            _burstRange = _shootTimeRange * (difficulty?.EnemyAttackTimeFactor ?? 1f);
            _restRange = _shootSpaceRange * (difficulty?.EnemyAttackTimeSpaceFactor ?? 1f);
            // 원작 CharacterRandomPreset처럼 총기 전투 이동의 추적 확률은 개체마다 min~max에서 한 번 뽑는다.
            _traceChance = RandomRange(_traceTargetChance);
            // 시체가 주무기 총알을 갖도록 원작 생성 끝의 AddBullet을 여기서 한다. 저장 복원도 같은 시드로 같은 칸을 만든다.
            AddBullet(catalog);
            _home = transform.position; _started = Time.time;
            _navFilter = new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = NavMesh.AllAreas };
            _agent.enabled = false; _model.gameObject.SetActive(false); _hitCollider.enabled = false;
            // 이미 처치한 적은 다시 만들지 않고, 저장된 사망 위치에 원본 전리품만 되살린다. 처치 기록 키는 CampaignEventLog가 정한다.
            if (world.EventComplete(CampaignEventLog.DeathRequirement(_sourceId)))
            { _dead = true; world.RestoreCorpse(this,_loot,_lootCounts,world.TryDeathPosition(_sourceId,out Vector3 position)?position:_home); }
            // 산 적만 체력바와 소리 알림의 나온 적 목록에 넣고 거리 활성화에 등록한다. 죽은 채 되살린 적은 시체만 남아 켜고 끌 것이 없다.
            // 하위 씬에 속한 적은 거리 판정을 끈 프리셋이어도 등록해, 원작처럼 그 하위 씬이 꺼진 동안 함께 꺼진다.
            else
            {
                world.TrackSpawned(this);
                if (_setActiveByPlayerDistance || subscene.Length > 0) world.TrackDistance(gameObject, subscene, _setActiveByPlayerDistance);
            }
        }

        // 원작 OnDestroy처럼 파괴되는 적을 거리 활성화와 나온 적 목록에서 뺀다. 준비 전에는 파사드도 등록도 없다.
        void OnDestroy()
        {
            if (_world == null) return;
            _world.ForgetDistance(gameObject);
            _world.ForgetSpawned(this);
        }

        // 생성된 산 적이 플레이어 모터와 같은 발소리 최소 속력보다 빨리 움직이는 동안만 발소리를 낸다.
        bool PostsFootstep => !_dead && _spawned
            && _agent.velocity.sqrMagnitude > OriginalCharacterMotion._footstepMinSpeed * OriginalCharacterMotion._footstepMinSpeed;

        // 원작 CharacterRandomPreset처럼 적 어트리뷰트를 채운다. 프리셋에 없는 스탯은 원작 기본값이고, 체력, 피해 배율, 이동 속도,
        // 총 사거리 배율, 대시 금지, 방어와 대시 중 조작 같은 프리셋 스탯만 프리셋 값이다. 걷기와 달리기 속도는 원작 기본값 4와 6에 이동 배율을 곱한다.
        void PrepareAbilities(CharacterAbilitySet abilitySet, float health)
        {
            // 플레이어와 같은 캐릭터 시스템 생성 규칙으로 만들고, 아래에서 프리셋 값만 덮는다.
            AbilitySystem abilities = CharacterAttributeSet.CreateSystem(this);
            abilities.SetBaseValue(CharacterAttributes.MaxHealth, health);
            abilities.SetBaseValue(CharacterAttributes.CurrentHealth, health);
            abilities.SetBaseValue(CharacterAttributes.GunDamageMultiplier, _damageMultiplier);
            abilities.SetBaseValue(CharacterAttributes.MeleeDamageMultiplier, _damageMultiplier);
            abilities.SetBaseValue(CharacterAttributes.WalkSpeed, abilities.GetBase(CharacterAttributes.WalkSpeed) * _moveMultiplier);
            // 원작은 달리기 속도에도 이동 배율을 곱한다. 달리는 근접 AI(늑대)가 읽는다. 총 사거리 배율은 원작 SetCharacterStat 값이다.
            abilities.SetBaseValue(CharacterAttributes.RunSpeed, abilities.GetBase(CharacterAttributes.RunSpeed) * _moveMultiplier);
            abilities.SetBaseValue(CharacterAttributes.GunDistanceMultiplier, _gunDistanceMultiplier);
            // 원작 CharacterRandomPreset처럼 대시할 수 없는 프리셋은 대시 속도 스탯을 0으로 둔다.
            if (!_canDash) abilities.SetBaseValue(CharacterAttributes.DashSpeed, 0f);
            // 원작 프리셋의 몸과 머리 방어값(Spider_Rifle 4, Merchant_Myst0 5)이다. 피해 실행이 치명이면 머리, 아니면 몸 방어를 읽는다.
            // 방독, 대시 중 조작, 재장전 속도도 프리셋 스탯이다. 대시 중 조작은 대시 동작 표시가 읽는다. 기본 0은 원작 스탯 기본값과 같다.
            abilities.SetBaseValue(CharacterAttributes.BodyArmor, _bodyArmor);
            abilities.SetBaseValue(CharacterAttributes.HeadArmor, _headArmor);
            abilities.SetBaseValue(CharacterAttributes.GasMask, _gasMask);
            abilities.SetBaseValue(CharacterAttributes.DashCanControl, _dashCanControl);
            abilities.SetBaseValue(CharacterAttributes.ReloadSpeedGain, _reloadSpeedGain);
            abilities.CueNotified += OnCue;
            _hurtEffect = abilitySet.Hurt;
            _abilities = abilities;
        }

        // 피해 효과의 피격 큐를 받아 원본 HurtVisual처럼 피격 재질 표시를 시작한다. 받아들인 피해마다 한 번 온다.
        // Prepare가 표시를 확보한 뒤에 이 처리기를 구독하므로 표시가 늘 있다. 원작 Health.Hurt의 PopText처럼 0보다 큰 최종 피해(큐 크기)도
        // 피해 지점에 띄우고, 치명 모양은 효과 문맥의 치명 비트가 정한다. 지점 없는 피해는 원작 버프 피해처럼 몸 위 0.8 m에 띄운다.
        void OnCue(AbilitySystem system, GameplayCueParameters cue)
        {
            if (cue.Tag != CharacterTags.CueHurt || cue.Event != GameplayCueEvent.Executed) return;
            _hurt!.Flash();
            if (cue.Magnitude <= 0f) return;
            GameplayEffectContext context = cue.Context;
            Vector3 point = context.HitPoint == Vector3.zero ? transform.position + Vector3.up * _bodyPopHeight : context.HitPoint;
            _world!.DamagePop(point, cue.Magnitude, context.HasFlag(HurtExecution.CritFlag));
        }

        void Update()
        {
            // 일시정지, 저장, 대사 중에는 NavMeshAgent가 남은 경로를 따라 움직이지 않게 멈춘다. 생성된 산 적의 에이전트는 늘 켜져 있다.
            if (Halted)
            { if (_spawned && !_dead && _agent.isOnNavMesh) _agent.isStopped = true; return; }
            if (!_spawned)
            {
                // 같은 Trigger를 기다리는 적들이 Collider 판정을 반복하지 않도록 지점이 프레임마다 한 번 낸 결과를 읽는다.
                if (WaitingToSpawn) return;
                // 이 에이전트 타입의 NavMesh 위에서만 생성하고, 베이크된 원본 지형 밖이면 순간이동으로 덮지 않는다.
                // NavMesh는 씬과 함께 불러오는 베이크 데이터라 씬 로딩 경계로 본다. 없으면 오류를 기록하고 이 적을 끈다.
                if (!NavMesh.SamplePosition(_home, out NavMeshHit hit, _navSampleDistance, _navFilter))
                {
                    // 재진입 실패의 원인을 가리도록 제작 위치, 에이전트 타입, 가장 가까운 NavMesh 위치, 메시 크기를 함께 기록한다.
                    bool nearby=NavMesh.SamplePosition(_home,out NavMeshHit nearest,_navDiagnosticSearchDistance,_navFilter);
                    // 컴포넌트를 먼저 꺼서 이 오류를 한 번만 기록한다.
                    enabled = false; Log.Instance.Error("원본 적 생성점에 NavMesh가 없습니다: " + _sourceId + " home=" + _home + " agent=" + _agent.agentTypeID + " nearest=" + (nearby?nearest.position.ToString():"none") + " triangles=" + NavMesh.CalculateTriangulation().indices.Length, this); return;
                }
                transform.position = hit.position;
                // 아직 맨손이라 프리셋 이동 배율만 곱한 걷기 속도다. 무기를 꺼내면 다시 넣는다.
                _agent.enabled = true; _agent.speed = WalkSpeed;
                _model.gameObject.SetActive(true); _hitCollider.enabled = true; _spawned = true;
            }
            if (!_agent.isOnNavMesh) return;
            // 배치 표적 탐색은 이 프레임에 처음 도는 적의 Update가 한 번 돌린다. 그래서 월드 컨트롤러가 따로 틱을 부르지 않는다.
            _world.PumpTargetSearch();
            // 원작 AICharacterController.Update의 updateValueTimer처럼 1초마다 밤 반응 시간을 다시 정한다.
            _valueTimer -= Time.deltaTime;
            if (_valueTimer <= 0f) { _valueTimer = _valueUpdateInterval; RefreshNightReaction(); }
            // 같은 팀이 되었거나 쓰러진 표적은 이번 프레임 판정 전에 놓는다.
            DropInvalidTarget();
            // 시각, 표적, 적 위치는 이 Update 안에서 바뀌지 않으므로 한 번씩만 읽어 차폐와 추적에 함께 쓴다.
            // 표적은 AI 표적이 있으면 그 적이고, 없으면(알아채기 전이나 플레이어 표적) 플레이어다.
            float now = Time.time;
            Vector3 position = transform.position;
            Vector3 target = _targetEnemy != null ? _targetEnemy.transform.position : _world.PlayerPosition;
            Vector3 delta = target - position;
            // 교전 판정은 제곱근 없이 제곱 거리로 비교한다. 경계에서 생기는 아주 작은 오차는 허용한다.
            float sqrDistance = delta.sqrMagnitude;
            // 원작 Combat_Normal 진입 대기(Wait ReactionTime) 동안은 차폐 검사, 탐색, 놓치기가 모두 멈춘다.
            // 놓치기는 대기가 끝난 시각부터 재도록 그동안 마지막 발견 시각을 지금으로 둔다.
            bool entering = _aware && now < _combatEntryUntil;
            // 원작 HasObsticleToTarget처럼 교전 중에는 시야 거리와 시야각 없이 차폐만 매 프레임 본다. 알아채기 전에는
            // 플레이어도 다른 팀 적도 배치 탐색(EnemyTargetSearch)이 찾는다.
            bool clear = _aware && !entering && HasClearLine(position, target);
            _targetVisible = clear;
            // 원작 SetNoticedToTarget처럼 새로 잡은 표적의 위치를 알아챈 위치로 둔다. 총기 전투 이동의 중심이다.
            if (_noticePending) { _noticePosition = target; _noticePending = false; }
            // 진입 대기 중에는 놓치기를 재지 않고, 대기가 끝나면 원작 차폐와 거리 규칙으로 놓친다.
            if (entering) _lastSeen = now;
            else if (_aware) TrackTarget(now, clear, sqrDistance, target);
            // 표적이 없거나 가려졌으면 배치 탐색에 맡긴다. 진입 대기 중에는 탐색하지 않는다.
            if (!entering) RequestTargetSearch(clear);
            // 원작 우두머리 따라가기다. 우두머리와 목표를 나누고, 목표가 없을 때 순찰 중심을 우두머리 위치로 둔다.
            OriginalEnemyController? leader = ShareLeaderTarget();
            // 원작 UpdateAI처럼 이번 프레임 교전 여부가 정해진 뒤 상인 상점을 숨기거나 보인다. 교전 중 상점을 고르지 못하게 하려는 것이다.
            SyncHideIfFoundEnemy();
            // 시야, 소리, 피격 중 무엇으로든 처음 알아챈 프레임에 원본 TakeOutWeapon처럼 무기를 꺼내기 시작한다.
            // 원본은 손에 든 순간부터 무기 이동 배율이 걷기에 붙으므로 에이전트 속도도 이때 다시 넣는다.
            if (_aware && _weaponOutAt < 0) { _weaponOutAt = Time.time; _agent.speed = WalkSpeed; }
            // 원작 Dash처럼 대시가 이어지는 동안은 대시 동작이 이동을 맡는다.
            if (TickDash()) { Animate(); return; }
            // 원작 Alert_Normal이다. 소리나 피격으로 알아챘지만 표적이 없는 동안 순찰 대신 경계 트리를 돈다.
            // 총기 전투 이동 순환은 경계에 들어가는 첫 틱이 비우고, 그 뒤 몸 방향은 경계가 정한다.
            if (!_aware && _alerted)
            {
                ClearCombatCycles();
                TickAlert(position);
                Animate(); return;
            }
            if (!_aware)
            {
                // 총기 적의 전투 이동 순환과 수동 조준 회전도 끝낸다.
                ClearCombatCycles();
                LeaveGunCombat();
                // 원본 순찰 반경 안의 NavMesh 위치만 사용한다. 원작 patrolPosition처럼 우두머리가 있으면 그 위치가 중심이다.
                if(_patrolRange>0 && Time.time>=_patrolUntil)
                {
                    _patrolUntil=Time.time+_patrolIntervalMin+(float)_random!.NextDouble()*_patrolIntervalSpread;
                    Vector3 center = leader != null ? leader.transform.position : _home;
                    // 근접 전투 이동과 같은 임의 지점 규칙이다. 난수는 간격 다음에 각도, 반경 순으로 뽑는다.
                    if (TrySampleAround(center, _patrolRange, out Vector3 patrol)) _agent.SetDestination(patrol);
                }
                _agent.isStopped=_patrolRange<=0; Animate(); return;
            }
            // 원작 Combat_Normal 진입 대기다. 무기는 꺼내지만 서서 쏘지도 움직이지도 않는다. 근접 템플릿은 시작 대기 0.2초를 따로 둔다.
            if (now < _combatEntryUntil) { _agent.isStopped = true; Animate(); return; }
            // 원작 AIController_Melee 템플릿을 쓰는 프리셋(늑대)은 Combat_Melee 트리로 싸운다. 경계가 켠 수동 회전은 에이전트에 돌려준다.
            if (_meleeAi) { SetManualFacing(false); TickMeleeCombat(sqrDistance, target, delta); Animate(); return; }
            // 총을 든 적은 원작 Combat_Normal처럼 사격 순환과 전투 이동 순환을 함께 돌린다. 사거리, 엄폐 사격, 멈춤 규칙도 그쪽이 정한다.
            if (_weapon!.IsGun) { TickGunCombat(clear, sqrDistance, delta); Animate(); return; }
            // 근접 무기를 든 일반 AI도 Combat_Normal이라 시야 거리 없이 차폐만 보고 친다. 몸 방향은 에이전트가 정한다.
            SetManualFacing(false);
            bool attack = clear && sqrDistance <= _sqrAttackRange;
            // 근접 무기를 든 일반 AI다. 근접 예고가 시작됐으면 원본 순서대로 휘두를 때까지 멈춰 선다.
            bool engaged = _swingAt > 0;
            _agent.isStopped = attack || engaged;
            // 추적과 공격은 따로 분기한다. 재탐색 제한 조건을 공격 분기의 else에 묶으면 재탐색하지 않는 프레임마다
            // 사거리 밖에서도 공격해, 근접 적이 시야만 열리면 멀리서 피해를 준다.
            if (!attack && !engaged)
            {
                // 재탐색 조건은 _pathDirty 필드 설명을 따른다. 목표는 플레이어만이 아니라 지금 표적이다.
                if (ShouldRepath(target))
                {
                    _pathDirty = false;
                    _nextPathTime = Time.time + _repathInterval;
                    _lastDestination = target;
                    _agent.SetDestination(_lastDestination);
                }
            }
            else
            {
                // 근접 AI와 같은 원작 SetAim 회전이다.
                Face(delta);
                // 무기를 다 꺼내기 전에는 공격하지 않는다. 근접 무기는 원본 예고 순환을 따른다.
                if (WeaponDrawn) TickMeleeCycle(attack);
            }
            Animate();
        }

        // 원본 ItemAgentHolder.holdStadyTime이 지나야 꺼낸 무기가 켜진다.
        bool WeaponDrawn => _weaponOutAt >= 0 && Time.time - _weaponOutAt >= OriginalHandAnimation._drawTime;

        // 준비되지 않았거나, 시뮬레이션을 돌릴 수 없거나, 죽은 적은 이번 프레임 AI를 돌리지 않는다.
        bool Halted => _world == null || !_world.SimulationReady || _dead;

        // 원작 spawnTimeSpace 간격이 아직 남았거나, 생성 Trigger가 있는데 플레이어가 그 안에 없으면 생성을 미룬다.
        bool WaitingToSpawn => Time.time - _started < _spawnDelay
            || (_spawnTrigger != null && !_spawnTrigger.ContainsPlayerThisFrame(_world!.PlayerPosition));

        // 교전하지 않는 동안(순찰과 경계)은 원본 전투 트리가 멈추므로 사격과 근접 순환을 비운다. 다시 교전하면 대기부터 새로 시작한다.
        // 근접 AI도 전투 트리를 처음부터 다시 시작하고 걷기로 돌아간다.
        void ClearCombatCycles()
        {
            _shootAt = 0; _burstUntil = 0; _restUntil = 0; _swingAt = 0;
            LeaveMeleeCombat();
        }

        // 적과 표적(플레이어나 다른 팀 적) 사이를 가리는 것이 없는지 원작 AIMainBrain.CheckObsticle 규칙으로 본다. 원작 isHurtSearch처럼
        // 피해를 받은 뒤 잠시는 FowBlock(18) 너머도 본다.
        bool HasClearLine(Vector3 position, Vector3 target) =>
            OriginalSightQuery.HasLineOfSight(position + Vector3.up * _sightHeight, target + Vector3.up * _sightHeight, HurtSearch);

        // 발각 직후이거나, 재탐색 간격이 지났고 목표가 마지막 요청 지점에서 0.5 m 넘게 움직였을 때 경로를 다시 요청한다.
        bool ShouldRepath(Vector3 target) =>
            _pathDirty || (Time.time >= _nextPathTime && (target - _lastDestination).sqrMagnitude > _repathSqrDistance);

        // 원본 CharacterWalkSpeed다. 원본 AI는 알아챘을 때 무기를 손에 들고 다시 넣지 않으므로, 꺼낸 뒤부터 든 무기의 이동 배율을 곱한다.
        // 에이전트 속도와 이동 애니메이션이 같은 값을 쓴다.
        float WalkSpeed => _abilities!.GetCurrent(CharacterAttributes.WalkSpeed)
            * (_weaponOutAt >= 0 ? OriginalCharacter.WeaponMoveSpeedMultiplier(_weapon) : 1f);
        // 원본 CharacterRunSpeed다. 걷기와 같은 무기 배율 규칙이며 달리는 근접 AI만 쓴다.
        float RunSpeed => _abilities!.GetCurrent(CharacterAttributes.RunSpeed)
            * (_weaponOutAt >= 0 ? OriginalCharacter.WeaponMoveSpeedMultiplier(_weapon) : 1f);

        // 원본 Combat_Attack_Melee 순환이다. 쉬는 시간이 지났고 사거리 안이면 오른손에 예고 효과를 내고, 0.2초 뒤 휘두른 다음 0.1초 쉰다.
        // 원본처럼 공격 간격이 남았으면 휘두르기만 건너뛰고 순환은 이어 간다.
        void TickMeleeCycle(bool attack)
        {
            float now = Time.time;
            if (_swingAt > 0)
            {
                if (now < _swingAt) return;
                _swingAt = 0;
                _meleeReadyAt = now + _meleeWaitTime;
                if (now >= _nextAttack) Attack();
                return;
            }
            if (!attack || now < _meleeReadyAt) return;
            Transform? hand = _appearance != null ? _appearance.RightHandSocket : null;
            // 예고 효과 풀은 적 파사드 너머의 전투 시뮬레이션이 가진다.
            if (hand != null) _world!.MeleeAlert(hand);
            _swingAt = now + _meleeAlertTime;
        }

        // 사격 순환(TickGunCycle), 공격(Attack), 이동 애니메이션(Animate)은 OriginalEnemyController.Combat.cs에 있다.
        // 아래는 프리셋 범위(x 최소, y 최대)에서 이 적의 시드 난수로 값을 뽑는다.
        float RandomRange(Vector2 value)=>Mathf.Lerp(value.x,value.y,(float)_random!.NextDouble());

        // 원작 CharacterRandomPreset.AddBullet이다. 주무기가 구경 있는 총이면 분포에서 품질 하나를 뽑아, 그 구경과 태그의 총알을 원작 Search처럼
        // 찾고(없으면 품질을 낮춰 다시 찾는다) 하나를 고른다. 수량은 기본 수량에 bulletCountRange 안의 배율을 곱해 반올림하고, 원작 StackCount처럼
        // 최대 스택으로 자르며 1 미만이면 넣지 않는다. 원작 AI는 장전 때 총알을 새로 만들어 소지 총알을 쓰지 않으므로 시체에는 이 수량이 남는다.
        // 총알은 전리품 끝에 붙여 앞 칸의 번호와 저장된 루팅 키를 바꾸지 않는다. 다른 칸은 정의의 기본 수량이다.
        // 적 전용(DestroyOnLootBox) 총알은 원작처럼 시체 칸을 만들 때 DeadBodySpawner.CorpseContents가 뺀다.
        void AddBullet(OriginalGameplayCatalog catalog)
        {
            var counts = new int[_loot.Length];
            for (int index = 0; index < _loot.Length; index++)
                counts[index] = catalog.TryGet(_loot[index], out OriginalItemDefinition? item) ? item!.DefaultCount : 0;
            _lootCounts = counts;
            if (!_weapon!.IsGun || _weapon.Caliber.Length == 0 || _bulletQualities.Length == 0) return;
            int quality = _bulletQualities[PickWeighted(_bulletQualityWeights)];
            var exclude = new System.Collections.Generic.HashSet<string>(_bulletExcludeTags, StringComparer.Ordinal);
            System.Collections.Generic.IReadOnlyList<int> found = catalog.Search(_bulletRequireTag, exclude, quality, _weapon.Caliber);
            if (found.Count == 0) return;
            int type = found[_random!.Next(found.Count)];
            // 찾은 ID는 카탈로그 정의에서 나왔다.
            OriginalItemDefinition bullet = catalog.DefinitionOf(type);
            int count = bullet.MaxStack > 1 ? Mathf.Min(bullet.MaxStack, Mathf.RoundToInt(bullet.DefaultCount * RandomRange(_bulletCountRange))) : 1;
            if (count < 1) return;
            Array.Resize(ref _loot, _loot.Length + 1);
            _loot[_loot.Length - 1] = type;
            Array.Resize(ref _lootCounts, _lootCounts.Length + 1);
            _lootCounts[_lootCounts.Length - 1] = count;
        }

        // 원작 RandomContainer.GetRandom(lowPercent 0)이다. 가중치 합 안의 값을 뽑아 누적 가중치가 처음 넘는 항목의 번호를 고른다.
        int PickWeighted(float[] weights)
        {
            float total = 0f;
            foreach (float weight in weights) total += weight;
            float target = (float)_random!.NextDouble() * total, sum = 0f;
            for (int index = 0; index < weights.Length; index++)
            {
                sum += weights[index];
                if (sum >= target) return index;
            }
            return weights.Length - 1;
        }

        // 순찰과 근접 전투 이동의 임의 지점이다. 중심 둘레 원 안에서 면적에 고르게 뽑고, 이 에이전트 타입의 NavMesh 위로 옮긴 점만 쓴다.
        // 회피 반경과 반경 균등 분포가 있는 원작 MoveToRandomPos와 다르며, 총기 전투 이동만 원작 규칙의 CombatPoint를 쓴다. 난수는 각도 다음 반경 순이다.
        bool TrySampleAround(Vector3 center, float range, out Vector3 point)
        {
            float angle = (float)_random!.NextDouble() * Mathf.PI * 2, radius = Mathf.Sqrt((float)_random.NextDouble()) * range;
            Vector3 target = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
            bool found = NavMesh.SamplePosition(target, out NavMeshHit hit, _navSampleDistance, _navFilter);
            point = hit.position;
            return found;
        }
        // 음수나 NaN 범위는 -1을 돌려준다. 제곱 거리는 0 이상이므로 늘 범위 밖이 된다.
        static float SquaredRange(float range) => range >= 0 ? range * range : -1f;

        // 원작 CharacterRandomPreset.hearingAbility다. 튜토리얼과 Ground Zero의 적 프리셋은 모두 기본값 1이라 프리셋마다 읽지 않고 상수로 둔다.
        const float _hearingAbility = 1f;

        // 원작 OnSound처럼 소리는 표적도 잊는 시간도 바꾸지 않고 알아챈 위치만 남긴다. 교전 중이 아니면 경계 트리로 넘어가고,
        // 표적은 경계의 탐색이 찾는다. sourceEnemy는 소리를 낸 적이고, 없으면 플레이어 소리다. 소리 낸 쪽의 팀이 자기 팀이면 듣지 않는다.
        internal void Hear(Vector3 source, float range, OriginalEnemyController? sourceEnemy = null)
        {
            int sourceTeam = sourceEnemy != null ? sourceEnemy._team : TeamRules.Player;
            // 원작 AICharacterController.OnSound처럼 높이를 뺀 수평 거리가 소리 반경에 청력을 곱한 값보다 작을 때만 듣는다.
            // 반경과 같은 거리, 음수나 NaN 반경은 듣지 못한다. 도발 전 비밀 상인은 총소리를 들어도 교전하지 않는다.
            // 거리 때문에 꺼진 적도 듣지 못한다. 원작 AI는 꺼질 때 소리 구독을 푼다. 원작 Patrol_Scare 템플릿은 소리를 쓰지 않는다.
            if (_dead || !_spawned || Passive || !_noticeBySight || sourceTeam == _team || !isActiveAndEnabled) return;
            Vector3 position = transform.position, heard = source;
            source.y = 0f;
            position.y = 0f;
            if (!(Vector3.Distance(position, source) < range * _hearingAbility)) return;
            Notice(heard);
        }

        // 원본 DamageReceiver.Hurt처럼 피해를 받아들였으면 true를 돌려준다. 죽었거나 아직 나오지 않았거나 꺼진 적은 거절하고 아무것도 바꾸지 않는다.
        // 인수는 원작 DamageInfo 값이다. armorPiercing은 관통이라 폭발, 근접, 총탄이 넘기고, fromPlayer는 주인공이 낸 피해인지,
        // attacker는 다른 AI 공격자다.
        // 공격자가 있으면 호출자는 fromPlayer를 거짓으로 넘긴다. weaponItemId와 crit은 처치 과제의 무기와 치명 거르기가 읽고, crit은 머리 방어와
        // 치명 피해 숫자도 정한다. point와 normal은 damagePoint와 damageNormal이다. point는 피해 숫자 위치이고, normal은 공격자 없는 피해에서
        // 알아챈 위치의 방향이다. 피해 숫자는 피격 큐가 띄우므로 호출자는 true일 때 명중음만 낸다.
        internal bool Hurt(float damage, float armorPiercing = 0f, bool fromPlayer = true, int weaponItemId = 0, bool crit = false,
            OriginalEnemyController? attacker = null, Vector3 point = default, Vector3 normal = default)
        {
            if (!Targetable) return false;
            // 원작 lastDamageInfo처럼 받아들인 피해의 시각과 공격자 유무만 남긴다. 원작 fromCharacter는 주인공이거나 다른 AI다.
            _lastHurt = Time.time;
            _lastHurtFromCharacter = fromPlayer || attacker != null;
            // 원본 Health.Hurt처럼 피해 효과로 체력을 줄인다. 체력은 0 아래로 내려가지 않고, 피격 재질 표시와 피해 숫자는 효과의 피격 큐가 낸다.
            // 피해 효과는 적용 조건이 없는 즉시 효과라 늘 실행되므로 결과를 보지 않는다.
            AbilitySystem abilities = _abilities!;
            var spec = new GameplayEffectSpec(_hurtEffect!, abilities).WithSetByCaller(CharacterTags.DataDamage, damage)
                .WithContext(GameplayEffectContext.Hit(point, normal, crit ? HurtExecution.CritFlag : 0));
            // 관통이 없으면 값을 넣지 않는다. 피해 실행은 넣지 않은 값을 0으로 읽으므로 관통 0과 같다.
            if (armorPiercing != 0f) spec = spec.WithSetByCaller(CharacterTags.DataArmorPiercing, armorPiercing);
            // 원작 Health.Hurt의 aiCombatFactor다. AI끼리의 피해에만 공격자 배율 / 피해자 배율을 넣고, 피해 실행이 방어 계수에 곱한다.
            // 플레이어 피해는 값을 넣지 않아 곱하지 않는다.
            if (attacker != null) spec = spec.WithSetByCaller(CharacterTags.DataCombatFactor, attacker._aiCombatFactor / _aiCombatFactor);
            // 원작 lastDamageInfo.finalDamage 자리다. 피격 대시, Scare 표적, 경계, 탐색, 엄폐 사격의 CheckHurt가 실제로 줄어든 체력을 본다.
            float before = abilities.GetCurrent(CharacterAttributes.CurrentHealth);
            abilities.ApplyEffect(spec);
            _lastHurtDamage = before - abilities.GetCurrent(CharacterAttributes.CurrentHealth);
            // 치명상도 받아들인 피해이므로 사망 처리 뒤 true다. 체력이 0이 되면 캐릭터 어트리뷰트 세트가 State.Dead를 단다.
            // 살아 있으면 원작 OnHurt처럼 알아챈다. 경계, Scare 표적, 상인 도발 규칙은 NoticeHurt가 정한다.
            if (!abilities.Tags.HasTagExact(CharacterTags.Dead)) { NoticeHurt(attacker, fromPlayer, normal); return true; }
            _dead = true; _agent.enabled = false; _hitCollider.enabled = false;
            // 죽는 프레임에 진행 중인 장전과 장전 시작음을 멈춘다.
            StopReload();
            // 사망한 머리는 같은 프레임부터 조준에서 제외한다.
            if (_headCollider != null) _headCollider.enabled = false;
            _animator.enabled = false;
            // 원본의 사망 후 루팅 전환처럼 살아 있는 캐릭터 표시를 감춘다.
            _model.gameObject.SetActive(false);
            // 죽은 상인의 상점을 고르지 못하게 같은 프레임에 끈다.
            HideAttachmentsOnDeath();
            // 원작 OnDead처럼 죽는 프레임에 거리 활성화에서 뺀다. 시체와 전리품은 따로 만든 객체라 켜고 끄지 않는다.
            _world.ForgetDistance(gameObject);
            // 처치 수, 처치 사건, 경험치는 월드가 처치 명령 하나로 확정한다. 피해 출처가 경험치와 처치 수를 받을지 정한다.
            // 처치 과제 알림도 월드가 처치 명령 뒤에 낸다.
            // AI가 죽인 적은 원작처럼 플레이어 처치가 아니다. 공격자가 있으면 경험치, 처치 수, 퀘스트가 세지 않도록 거짓을 넘긴다.
            _world.Died(this, _loot, _lootCounts, fromPlayer && attacker == null, weaponItemId, crit);
            return true;
        }
    }
}
