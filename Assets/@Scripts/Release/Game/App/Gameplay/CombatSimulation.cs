#nullable enable
using System;
using System.Collections.Generic;
using DuckCharacter;
using Game.App.World;
using Game.Core;
using Game.GAS;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.App
{
    // 전투 월드의 무기, 장전, 탄환, 효과 상태와 행동 우선순위를 맡는다. 저장되는 변경은 세션과 상태 소유자의 명령으로만 한다.
    // 아이템 사용은 ItemUseAction, 탄약 HUD 문자열은 CombatAmmoHud, 입자 효과의 대여와 반환은 CombatEffects가 맡고 이 클래스가 하나씩 가진다.
    // 적 시야 판정은 OriginalSightQuery다.
    // 씬 참조는 GameplayWorldContext로, 사격 입력은 생성자로 받으며 컨트롤러를 거꾸로 참조하지 않는다.
    // 단순 체력 대상(드럼통, 벽, 모래주머니) 피해와 원작 폭발은 CombatSimulation.Damage.cs에 나눠 둔다.
    internal sealed partial class CombatSimulation : IInventoryActions
    {
        // 인벤토리 화면이 프롤로그와 같은 계약으로 행동 상태와 아이템 사용을 묻는다.
        bool IInventoryActions.ActionInProgress => ActionInProgress;
        bool IInventoryActions.UsingItem => UsingItem;
        bool IInventoryActions.TryBeginUse(string id) => TryBeginUse(id);
        readonly GameplayWorldContext _world;
        readonly InputAction _attack;
        // 플레이어 루트의 피격 표시다. 이 표시를 내는 곳이 전투뿐이라 컨텍스트가 아니라 여기서 가진다.
        readonly OriginalHurtVisual _playerHurt;
        // 피격 큐를 구독한 플레이어 GAS 시스템과 처리기다. 시스템은 씬보다 오래 살아 해제 때 같은 시스템에서 풀고,
        // 처리기는 구독과 해제가 같은 인스턴스를 쓰도록 한 번만 만든다.
        readonly AbilitySystem _playerAbilities;
        readonly Action<AbilitySystem, GameplayCueParameters> _onPlayerCue;
        // 원본 CA_Reload다. 장전하는 동안 실행 중이고 장전 상태는 이 실행 여부로만 본다. 아이템 사용과 대시 어빌리티가 시작하며 끊는다.
        readonly GameplayAbilityDefinition _reloadAbility;
        // 원본 CA_UseItem과 BulletCountHUD 자리다. 월드 수명 동안 하나씩 가진다.
        readonly ItemUseAction _itemUse;
        readonly CombatAmmoHud _ammoHud;
        // 원본 ItemSetting_Gun의 목표 탄종과 BulletTypeHUD 목록이다. 탄종을 바꾸며 소지품에 못 넣은 탄은 바닥 아이템 생성 객체가 떨군다.
        readonly BulletTypeSwitch _bulletTypes;
        readonly DroppedItemSpawner _drops;
        // 원작 CA_Skill과 손에 든 스킬 아이템이다. 원작 CancelSkill은 ADS와 같은 우클릭이라 Player/Aim으로 조준을 취소한다.
        readonly SkillItemAction _skillItem;
        readonly InputAction _cancelSkill;
        // 날아가는 수류탄이다. 원작 Grenade.Update처럼 이 클래스가 시간을 재고 폭발시킨다.
        readonly List<ThrownGrenade> _grenades = new();
        // 손에 스킬 아이템을 들었거나, 들고 있던 스킬 아이템이 다 떨어져 손이 빈 상태다. 원작은 무기를 다시 들게 하지 않으므로
        // 무기 키를 누를 때까지 고른 칸의 무기를 손에 들지 않는다.
        bool _weaponPutAway;
        GameAudioEvent? _reloadAudio;
        string? _weaponId;
        float _actionUntil, _nextShot;
        // 원본 ActionProgressHUD 진행률을 계산할 때 쓰는 장전의 전체 시간이다. 아이템 사용 시간은 ItemUseAction이 가진다.
        float _actionTotal;
        string _activeSlot = GameplayWeaponSlots._primary;
        // _activeSlot의 원본 WeaponButtons 순서다. 칸이 바뀔 때만 계산해 두어 WeaponIndex가 매 프레임 문자열을 비교하지 않게 한다.
        int _activeSlotIndex = GameplayWeaponSlots.IndexOf(GameplayWeaponSlots._primary);
        // 루트 위 사격 기준 높이다. 플레이어와 적의 탄 시작점, 적의 조준점, 적 근접의 피해 지점이 함께 쓴다.
        const float _chestHeight = .75f;
        // 원작 CharacterMainControl.UseItem이 쓸 수 없는 아이템에 띄우는 알림 번역 키다.
        const string _notUsableKey = "UI_Item_NotUsable";
        // 무기 정의별 FMOD 발사, 장전 시작, 장전 끝 경로 캐시다. 씬 해제 때 비운다.
        // 정의의 키는 실행 중 바뀌지 않으므로 처음 쓸 때 한 번 만들어 매번 같은 문자열을 넘긴다.
        readonly Dictionary<OriginalItemDefinition, WeaponSounds> _weaponSounds = new();
        // 든 무기의 정의와 비조준, ADS 산포 프로필이다. 매 프레임 조회하지 않도록 무기 인스턴스가 바뀔 때만 RefreshEquipment가 계산한다.
        OriginalItemDefinition? _weaponDefinition;
        // _weaponDefinition을 계산한 무기 노드의 칸 목록이다. 같은 무기에서 부착물이 바뀌었는지 참조로 본다.
        IReadOnlyList<ItemSlotState>? _weaponAttachments;
        ScatterProfile _hipScatter, _adsScatter;
        // 손에 든 무기 표시다. Muzzle 캐시, 표시 반동, 파괴는 적과 같은 WeaponVisual이 맡는다.
        WeaponVisual? _weaponVisual;
        readonly List<Projectile> _projectiles = new();
        // Unity 객체가 아닌 탄 시뮬레이션 상태의 풀이다. 각 탄은 처음 대여한 World 궤적을 계속 들고, 재사용 때 활성과 방출만 켜고 끈다.
        readonly Stack<Projectile> _projectilePool = new();
        // 명중 프레임의 궤적은 원본 Trail 수명만큼 남긴 뒤 초기화한다.
        readonly List<Projectile> _fadingProjectiles = new();
        // 탄착, 피격, 사망 효과와 총구 섬광, 공격 예고 효과의 대여와 반환을 맡는다. 월드 수명 동안 하나를 가진다.
        readonly CombatEffects _effects;
        // 탄 충돌 필터, 화면 조준, 근접과 폭발 판정이 함께 쓰는 Collider 소속 캐시다. 씬 수명이라 이 월드가 가지고 정리 때 비운다.
        readonly ColliderOwnerCache _owners = new();
        // 든 무기의 현재 산포다.
        float _scatter;
        readonly RaycastHit[] _hits = new RaycastHit[64];
        // 원본 CA_Attack.CreateAttackSound 경로다. 원본 근접 무기 프리팹은 모두 soundKey Default를 쓴다.
        const string _meleeSwingSound = "event:/SFX/Combat/Melee/attack_default";
        // 원본 HurtVisual의 HitFx가 내는 생체 피격음이다. 플레이어와 적이 같은 소리를 쓰고, 치명타면 crit을 쓴다.
        const string _organicHitSound = "event:/SFX/Combat/Hit/hit_organic_normal";
        const string _organicCritSound = "event:/SFX/Combat/Hit/hit_organic_crit";
        // 원본 명중 표시음이다. 머리를 맞혀 처치하면 머리 처치음, 머리만 맞히면 머리 명중음, 그 밖은 일반 명중음이다.
        const string _headKillSound = "event:/SFX/Combat/Marker/killmarker_head";
        const string _headHitSound = "event:/SFX/Combat/Marker/hitmarker_head";
        const string _hitMarkerSound = "event:/SFX/Combat/Marker/hitmarker";
        // 원본 벽 탄착음이다. 캐릭터나 단순 체력 대상이 아닌 곳에 맞은 탄이 낸다.
        const string _solidHitSound = "event:/SFX/Combat/Hit/hit_solid";
        // 원본 Projectile.UpdateFlyThroughSound 값이다. 적 탄이 이 속력 이상으로 플레이어 수평 5m 안을 지나가면 탄마다 한 번 낸다.
        const string _flyBySound = "event:/SFX/Combat/Bullet/flyby";
        const float _flyByMinSpeed = 9f, _flyByRadius = 5f;
        // 원본 ItemAgent_MeleeWeapon은 공격자 위치에서 AttackRange + 0.05 구에 닿는 피해 Collider를 찾는다.
        // 공격 방향 뒤쪽이어도 0.5에 공격자 피해 반지름 0.3을 더한 거리 안이면 맞는다.
        const float _meleeRangeMargin = .05f, _meleeNearDistance = .8f;
        // 원본 ItemAgent_MeleeWeapon의 피해 지점은 대상에서 공격 방향으로 0.2 앞이다. 근접 피해 효과와 적 근접의 플레이어 피해가 함께 쓴다.
        const float _meleeHitForward = .2f;
        // 원본 근접 명중 카메라 충격이다. 캐릭터 오른쪽으로 0.05 세기에 무작위 흔들림 0.3을 섞는다.
        const float _meleeShake = .3f, _meleeImpulse = .05f;
        // 원본 ItemAgent_Gun 발사처럼 총구 반대 방향으로 게임 카메라에 주는 반동 충격 세기다.
        const float _shootImpulse = .07f;
        // 원본 적 산포 보정이 플레이어가 달린다고 보는 속력이다.
        const float _runningScatterSpeed = 4.1f;
        // 원본 저내구도 추가 산포다. 내구도 비율이 0.5에서 0으로 내려갈수록 발사 산포(최소 1)의 0배에서 1.5배까지를 더한다.
        const float _lowDurabilityScatter = 1.5f, _lowDurabilityRatio = .5f;
        // 원본 캐릭터 산포 배율의 하한이다.
        const float _minScatterMultiplier = .1f;
        // 조준 상태가 바뀌어 기본 산포보다 작아졌을 때 원본처럼 산포를 복구하는 배수다.
        const float _fastRecoveryFactor = 6f;
        // 원작 Grenade처럼 던진 뒤 던진 캐릭터와 다시 부딪치기까지의 시간이다.
        const float _throwerCollisionSeconds = .5f;
        // 원본 DamageAction처럼 맞은 지점이 없는 피해의 피격 표시는 캐릭터 위 이 높이에 낸다.
        const float _hurtFallbackHeight = .8f;
        // 원본 근접 무기의 Collider[6] 버퍼다. 판정마다 새 배열을 만들지 않고, 6개를 넘는 Collider는 원작처럼 보지 않는다.
        readonly Collider[] _meleeHits = new Collider[6];
        // 무기를 바꾼 뒤 원본 holdStadyTime이 끝나는 시각이다. 그때까지 무기를 숨기고 공격하지 않으며, _drawing이면 끝날 때 무기를 보인다.
        float _drawUntil;
        bool _drawing;
        // 원본 CA_Attack처럼 휘두른 뒤 DealDamageTime에 판정할 근접 공격들이다.
        readonly List<PendingMelee> _pendingMelee = new();
        // 공격 소리를 들을 적이다. 적 파사드가 생성 때 자기의 나온 적 목록을 넘겨, 스폰 후보 전체 대신 그 목록만 돈다.
        IReadOnlyList<OriginalEnemyController> _soundListeners = Array.Empty<OriginalEnemyController>();
        internal void BindSoundListeners(IReadOnlyList<OriginalEnemyController> listeners) => _soundListeners = listeners;
        // 플레이어 발소리 처리기다. 구독과 해제가 같은 인스턴스를 쓰도록 한 번만 만든다.
        readonly Action<Vector3, bool> _onFootstep;
        // 발소리 반경의 무거움 판정에 쓰는 소지 무게와, 그 무게를 잰 확정 그래프다. 걸음마다 그래프 전체를 돌지 않도록
        // 그래프 참조가 바뀔 때만 다시 잰다. 확정 그래프는 바뀌지 않는 값이라 같은 참조면 무게도 같다.
        ItemOwnershipState? _footstepWeightGraph;
        float _footstepWeight;
        internal CombatSimulation(GameplayWorldContext world, InputAction attack, InputAction cancelSkill, DroppedItemSpawner drops)
        {
            _world = world;
            _attack = attack;
            _cancelSkill = cancelSkill;
            _drops = drops;
            // 효과는 이 월드의 객체 서비스에서 빌리고 반환한다.
            _effects = new CombatEffects(world.Objects);
            // 월드 준비 때 한 번만 확보해, 다시 들어온 씬에서도 플레이어 루트의 피격 표시가 하나만 남게 한다.
            _playerHurt = GameplayComponents.GetOrAdd<OriginalHurtVisual>(world.Motor.gameObject);
            // 원본 OnHurtEvent처럼 적 피해와 기아 피해가 모두 플레이어 시스템의 피격 큐로 표시를 낸다. ReleaseCombatObjects가 푼다.
            _onPlayerCue = OnPlayerCue;
            PlayerVitals vitals = world.Runtime.Vitals;
            _playerAbilities = vitals.Abilities;
            _playerAbilities.CueNotified += _onPlayerCue;
            // 다른 행동이 장전을 끊으면 장전음과 진행 표시를 내린다. ReleaseCombatObjects가 처리기를 푼다.
            _reloadAbility = vitals.AbilitySet.Reload!;
            CharacterActionAbility.SetCanceledHandler(_playerAbilities, _reloadAbility, ClearReload);
            // 사용과 탄약 HUD도 같은 월드 참조 묶음을 쓴다.
            _itemUse = new ItemUseAction(world);
            _bulletTypes = new BulletTypeSwitch(world);
            _ammoHud = new CombatAmmoHud(world, _bulletTypes);
            _skillItem = new SkillItemAction(world);
            // 원작 CharacterSoundMaker처럼 플레이어 발소리를 나온 적에게 알린다. 모터는 이 월드와 같은 씬에 있고 ReleaseCombatObjects가 푼다.
            _onFootstep = OnFootstep;
            world.Motor.FootstepPosted += _onFootstep;
        }
        // 원본 CA_UseItem 같은 아이템 사용이 진행 중인지다. 인벤토리 화면이 사용, 장착 버튼 표시와 장착 변경 차단에 쓴다.
        internal bool UsingItem => _itemUse.Active;
        // 원본 CharacterMainControl.currentAction처럼 장전, 아이템 사용, 대시, 지연 상호작용 중 하나가 진행 중인지다. 네 행동 어빌리티가
        // 실행 중에 State.Action 아래 태그를 쥔다. 원본 Interact():2440은 행동이 없을 때만 상호작용을 시작하므로, F 지점 상호작용과 기지 Tab 창고가 이 값을 확인한다.
        internal bool ActionInProgress => _playerAbilities.Tags.HasTag(CharacterTags.Action);
        // HUD 조준점이 읽는 현재 산포다.
        internal float Scatter => _scatter;
        // 손에 든 스킬 아이템 인스턴스다. 퀵슬롯 HUD가 원본 usingIndicator를 켤 칸을 찾는다.
        internal string? HeldSkillItem => _skillItem.Item;
        // 든 무기의 원본 WeaponButtons 순서다. 무기가 없으면 -1이다.
        internal int WeaponIndex => _weaponId == null ? -1 : _activeSlotIndex;
        // 씬 해제 때 전투 객체를 정리하며, 여러 번 불러도 안전하다.
        // World에서 대여한 궤적과 입자는 여기서 참조만 놓고, 뒤이은 씬 정리의 World 해제가 반환하거나 파괴한다.
        internal void ReleaseCombatObjects()
        {
            // 플레이어 시스템은 다음 씬에서도 피격 큐를 보내므로, 내려가는 월드에 표시가 오지 않게 먼저 푼다. 다시 풀어도 무해하다.
            // 장전 취소 처리기도 먼저 풀고 진행 중인 장전 어빌리티를 끝내, 내려가는 HUD를 건드리지 않게 한다.
            _playerAbilities.CueNotified -= _onPlayerCue;
            CharacterActionAbility.SetCanceledHandler(_playerAbilities, _reloadAbility, null);
            _playerAbilities.CancelAbility(_reloadAbility);
            _reloadAudio?.Stop(false); _reloadAudio = null;
            // 내려가는 월드의 적이 발소리를 듣지 않게 푼다. 이벤트 해제는 관리 코드라 모터가 이미 파괴됐어도 되고, 다시 풀어도 무해하다.
            // 무게 캐시가 잡은 그래프도 씬 수명 상태라 놓는다.
            _world.Motor.FootstepPosted -= _onFootstep;
            _footstepWeightGraph = null;
            // 총을 파괴하기 전에 그 총구의 섬광을 반환해, 섬광이 총과 함께 파괴되어 누수로 보고되지 않게 한다.
            DisposeWeaponVisual();
            // 효과 목록은 CombatEffects가 참조만 놓는다.
            _projectiles.Clear(); _fadingProjectiles.Clear(); _projectilePool.Clear(); _effects.Clear(); _pendingMelee.Clear();
            // 날아가는 수류탄과 손에 든 아이콘, 조준 표시도 World에서 빌린 객체라 참조만 놓는다. 조준 어빌리티는 스킬 행동이 끝낸다.
            _grenades.Clear();
            _skillItem.Release();
            // Collider 소속 캐시와 폭발이 맞힌 캐릭터 목록도 씬 객체를 가리키는 씬 수명 상태라 무기 소리 캐시와 함께 비운다.
            _weaponSounds.Clear();
            _owners.Clear();
            _explosionDamaged.Clear();
            // 진행 중인 사용은 소비 없이 버리고, 남아 있는 모터의 사용 상태를 푼다.
            _itemUse.Release();
            _bulletTypes.Release();
        }
        // 전환 확정의 Deactivate나 편집 진입의 Suspend로 씬 루트가 꺼진 동안 장전음을 멈추고, 다시 켜지면 재개한다.
        // SetPaused는 앱 오디오가 닫혔거나 이벤트가 끝난 뒤에도 던지지 않으므로 예외를 잡지 않는다.
        internal void SetAudioPaused(bool paused) => _reloadAudio?.SetPaused(paused);
        // 사용이나 장전 중이 아니고 발사 간격이 지났으면, 자동 무기는 누르는 동안, 단발 무기는 누른 프레임에 공격한다.
        // 원본은 꺼내는 0.15초 동안 무기 에이전트를 꺼 두므로 그동안에도 공격하지 않는다.
        internal void TickAttack()
        {
            // 원작 CharacterInputControl.Update처럼 스킬 아이템을 들었으면 좌클릭이 공격 대신 조준과 투척이다.
            if (_skillItem.Holding) { TickSkillInput(); return; }
            // 공격을 막는 프레임이면 끝낸다. AttackGated가 무기 정의가 없는 프레임도 거르므로 아래에서는 정의가 있다.
            if (AttackGated) return;
            OriginalItemDefinition weapon = _weaponDefinition!;
            if (weapon.Automatic ? _attack.IsPressed() : _attack.WasPressedThisFrame()) Attack(weapon);
        }

        // 공격 입력을 받지 않는 프레임이다. 장전과 아이템 사용 어빌리티가 실행 중에 Block.Attack을 쥐고,
        // 발사 간격과 꺼내기 시간이 지나지 않았거나 든 무기가 없어도 막는다.
        bool AttackGated =>
            _playerAbilities.Tags.HasTag(CharacterTags.BlockAttack) || Time.time < _nextShot || Time.time < _drawUntil || _weaponDefinition == null;

        // 퀵슬롯, 사용 버튼, 더블클릭이 함께 쓰는 아이템 사용 시작이다. 원본 ActionPriorities는 장전 1 < 사용 3 < 대시 4 < 상호작용 7이다.
        // 원본 StartAction:1614-1632처럼 사용, 대시, 지연 상호작용 중이면 거절하고, 장전 중이면 탄창을 그대로 둔 채 장전을 취소한다.
        // 이 규칙은 아이템 사용 어빌리티의 태그가 갖고, 진행과 완료는 ItemUseAction이 맡는다.
        internal bool TryBeginUse(string instanceId)
        {
            if (!_itemUse.CanBegin) return false;
            // 장전을 취소하기 전에 아이템의 정의를 찾는다. 씬 정리 중이라 그래프가 없으면 상태를 바꾸지 않은 채 거절한다.
            if (!_world.TryItems(out ItemOwnershipState? items)) return false;
            OriginalItemDefinition definition = OwnedDefinition(items, instanceId);
            // 원작 CharacterMainControl.UseItem처럼 쓸 수 없는 아이템은 사용을 시작하지 않고 "사용할 수 없음" 알림만 띄운다.
            // 체력은 아이템 사용 효과(ItemUseExecution)와 같은 Base 현재 체력과 최대 체력 현재값으로 비교한다.
            if (!SurvivalRules.CanUse(definition.Heal, definition.Energy, definition.Water,
                    _playerAbilities.GetBase(CharacterAttributes.CurrentHealth), _playerAbilities.GetCurrent(CharacterAttributes.MaxHealth)))
            {
                _world.Notify(_notUsableKey);
                return false;
            }
            return _itemUse.TryBegin(instanceId, definition);
        }

        // 원작 CharacterInputControl.ShortCutInput이다. 쓸 수 있는 아이템은 사용하고, 스킬 아이템은 손에 들고, 그 밖에는 아무 일도 하지 않는다.
        // 원작의 손에 드는 에이전트가 있는 일반 아이템(근접 무기 등)은 이 게임에서 들 수 없어 아무 일도 하지 않는다.
        internal void TryUseShortcut(string instanceId)
        {
            // 씬 정리 중이라 그래프가 없으면 기록 없이 무시한다. 퀵슬롯 칸은 그래프의 소지 아이템만 가리킨다.
            if (!_world.TryItems(out ItemOwnershipState? items)) return;
            OriginalItemDefinition definition = OwnedDefinition(items, instanceId);
            if (definition.UseTime > 0) TryBeginUse(instanceId);
            else if (_world.Catalog.TryGetSkill(definition.Id, out OriginalItemSkill? skill)) HoldSkillItem(instanceId, definition, skill);
        }

        // 원작 ChangeHoldItem이다. 사용이나 조준 중(원작 CanEditInventory false)이거나 같은 아이템을 이미 들었으면 무시한다.
        // 든 무기를 내리는 일은 무기를 바꿀 때와 같고, 원작 holdStadyTime 동안 아이콘을 숨긴 뒤 일반 손 자세로 든다.
        void HoldSkillItem(string instanceId, OriginalItemDefinition definition, OriginalItemSkill skill)
        {
            if (_playerAbilities.Tags.HasTag(CharacterTags.BlockWeaponSwitch) || _skillItem.Item == instanceId) return;
            _weaponPutAway = true;
            PutAwayWeapon();
            _world.GameCamera.SetGun(null);
            _skillItem.Hold(instanceId, skill, definition.Icon);
            _drawUntil = Time.time + OriginalHandAnimation._drawTime;
            _drawing = true;
        }

        // 원작 CharacterInputControl.Update의 handheldSkill 입력이다. 우클릭은 원작 CancelSkill 콜백처럼 먼저 처리하고,
        // 누른 프레임은 조준 시작, 뗀 프레임은 던지기다.
        void TickSkillInput()
        {
            if (_cancelSkill.WasPressedThisFrame()) _skillItem.Cancel();
            if (_attack.WasPressedThisFrame()) _skillItem.TryStartAim();
            else if (_attack.WasReleasedThisFrame() && _skillItem.TryRelease(out string? item, out OriginalItemSkill? skill, out Vector3 releasePoint))
                ThrowGrenade(item, skill, releasePoint);
        }

        // 원작 Skill_Grenade.OnRelease와 ItemSetting_Skill.onRelease다. 손 소켓에서 조준점 쪽으로 같은 수평 거리만큼 던지고,
        // 던진 캐릭터와는 0.5초 동안 부딪치지 않는다. 기지가 아니면 던진 아이템을 하나 줄인다.
        void ThrowGrenade(string item, OriginalItemSkill skill, Vector3 releasePoint)
        {
            Transform socket = _world.Character.Rig.GetEquipmentSocket(EquipPartType.RightHand)!;
            Vector3 start = socket.position;
            Vector3 offset = releasePoint - _world.PlayerPosition;
            offset.y = 0f;
            Vector3 target = start + offset;
            target.y = releasePoint.y;
            // 투사체 규칙은 카탈로그 스킬에 저작되어 있고, 열린 월드가 방금 빌린 핸들이라 대여와 활성화가 실패하지 않는다.
            WorldObjectHandle handle = _world.Objects.Spawn(skill.Projectile, new Pose(start, socket.rotation))!;
            OriginalGrenade grenade = handle.Instance!.GetComponent<OriginalGrenade>();
            _world.Objects.Activate(handle);
            // 플레이어 루트의 이동 Collider는 모터가 붙인 CharacterController다.
            grenade.Launch(start, SkillItemAction.LaunchVelocity(start, target, skill.VerticalSpeed), _world.Motor.GetComponent<CharacterController>());
            _grenades.Add(new ThrownGrenade(handle, grenade, skill));
            if (skill.ReleaseSound.Length > 0) _world.Audio.PlayOneShot(skill.ReleaseSound, _world.PlayerPosition);
            // 씬 정리 중이라 세션이 없으면 기록 없이 줄이지 않는다. 세션 명령은 규칙 거절을 기록 없이 false로 돌려준다.
            if (skill.ReduceCount && _world.TrySession(out GameSessionManager? session)) session.Inventory.TryConsumeThrown(item);
        }

        // 원작 Grenade.Update다. delayFromCollide 수류탄은 처음 부딪친 뒤부터 시간을 재고, 지나면 원작 CreateExplosion으로 터진다.
        // 던진 캐릭터와의 충돌은 0.5초 뒤 되돌리고, 충돌이 요청한 소리를 낸다.
        void TickGrenades(float deltaTime)
        {
            for (int i = _grenades.Count - 1; i >= 0; i--)
            {
                ThrownGrenade thrown = _grenades[i];
                OriginalGrenade body = thrown.Body;
                thrown.Life += deltaTime;
                if (thrown.Life > _throwerCollisionSeconds) body.RestoreThrowerCollision();
                if (body.TakeCollideSound()) _world.Audio.PlayOneShot(body.CollideSound, body.transform.position);
                if (!thrown.Skill.DelayFromCollide || body.Collided) thrown.Delay += deltaTime;
                if (thrown.Delay <= thrown.Skill.Delay) continue;
                _grenades.RemoveAt(i);
                Vector3 center = body.transform.position;
                _world.Objects.Despawn(thrown.Handle);
                // 원작 Skill_Grenade의 damageInfo는 던진 주인공이 준 폭발 피해다. 원작 canHurtSelf처럼 Explode는 플레이어도 맞힌다.
                Explode(center, thrown.Skill.EffectRange, new OriginalDamageInfo(thrown.Skill.Damage, thrown.Skill.ArmorPiercing, true, true, center, Vector3.up));
            }
        }

        // 원본 CA_Reload.OnStop → CancleReload처럼 장전을 취소한다. 탄창은 바꾸지 않는다. 어빌리티를 취소하면 처리기 ClearReload가 정리한다.
        // 장전 중이 아니면 어빌리티 취소가 아무 일도 하지 않아 호출자가 장전 여부를 먼저 보지 않는다.
        void CancelReload() => _playerAbilities.CancelAbility(_reloadAbility);

        // 장전하는 무기는 늘 든 무기다. 무기를 바꾸면 RefreshEquipment와 SelectWeapon이 장전을 먼저 취소한다.
        bool Reloading => _playerAbilities.IsAbilityActive(_reloadAbility);

        // 장전 어빌리티가 취소되면 불린다. 장전 상태는 어빌리티가 끝나며 풀리므로 장전 시작음과 진행 표시만 내린다. 앱 종료로 GAS 시스템이 먼저
        // 해제되며 부를 수도 있어, 공용 HUD가 이 월드에 연결된 동안만 진행 표시를 내린다.
        void ClearReload()
        {
            _reloadAudio?.Stop(false); _reloadAudio = null;
            if (_world.PresentationAttached) _world.Hud.SetActionProgress(false, 0, false);
        }

        // 아이템 사용이나 장전의 진행률을 HUD에 보이고, 완료 시각이 되면 세션 명령으로 확정한다. 둘은 동시에 진행하지 않는다.
        // 원본 Drug.Heal처럼 음수 회복 피해의 피격 표시 지점은 캐릭터 발 위치다.
        internal void TickAction()
        {
            if (_itemUse.Active)
            {
                if (_itemUse.Tick()) PlayHurtFeedback(_world.PlayerPosition, Vector3.up);
                return;
            }
            // 원본 우선순위에서 대시가 장전보다 높아, 대시 어빌리티가 시작하면서 장전을 이미 취소했다. 장전 여부는 어빌리티로 본다.
            if (!Reloading) return;
            // 원본 ActionProgressHUD처럼 장전은 하단 슬라이더와 조준점 원형에 진행률을 보인다.
            float remaining = Mathf.Max(0, _actionUntil - Time.time);
            _world.Hud.SetActionProgress(true, _actionTotal > 0 ? 1 - remaining / _actionTotal : 1, true);
            if (Time.time < _actionUntil) return;
            // 세션 명령 전에 장전 상태를 지역 변수에 담고 어빌리티와 함께 먼저 끝낸다. 그래야 커밋 알림으로 다시 그리는 인벤토리 화면이
            // 끝난 상태를 보고, 명령이 던져도 다음 프레임에 같은 완료를 반복하지 않는다.
            // 무기를 바꾸면 장전이 먼저 취소되므로, 장전한 무기와 그 정의는 지금 든 무기의 것이다.
            string reloadedWeapon = _weaponId!;
            OriginalItemDefinition weapon = _weaponDefinition!;
            GameAudioEvent? reloadAudio = _reloadAudio;
            _reloadAudio = null;
            _playerAbilities.EndAbility(_reloadAbility);
            _world.Hud.SetActionProgress(false, 1, false);
            try
            {
                // 씬 정리 중이라 세션이나 그래프가 없으면 기록 없이 완료를 확정하지 않는다. 반환해도 finally가 남은 장전 시작음을 멈춘다.
                // 세션 명령은 규칙 거절을 기록 없이 false로 돌려주고, 불변식 위반은 직접 보고한다.
                if (!_world.TryItems(out GameSessionManager? session, out ItemOwnershipState? items)) return;
                // 원본 LoadBulletsFromInventory처럼 목표 탄종의 소지 탄을 넣고, 탄창에 다른 탄종이 남아 있으면 먼저 꺼낸다.
                // 목표는 StartReload가 정했다. 장전에 성공하면 시작음을 멈추고 종료음을 낸다.
                int target = _bulletTypes.Target(items, reloadedWeapon);
                if (OriginalInventoryQueries.TryLoadedAmmo(items, reloadedWeapon, out ItemInstanceState? loaded) && loaded != null &&
                    loaded.OriginalItemId != target && !UnloadMagazine(session, reloadedWeapon)) return;
                if (session.Inventory.TryReload(_world.Catalog, reloadedWeapon, target))
                {
                    reloadAudio?.Stop(false); reloadAudio = null;
                    string? reloadEnd = Sounds(weapon).ReloadEnd;
                    // 오디오가 꺼져 있으면 관리자가 재생을 조용히 무시한다.
                    if (reloadEnd != null) _world.Audio.PlayOneShot(reloadEnd, _world.PlayerPosition);
                }
            }
            // 거절되거나 예외가 나도 남은 장전 시작음을 멈춘다.
            finally { reloadAudio?.Stop(false); }
        }

        internal void StartReload()
        {
            // 원본 우선순위에서 장전이 가장 낮아 다른 행동 중에는 시작하지 않는다. 그 규칙은 장전 어빌리티의 태그가 갖는다.
            // 무기 정의는 무기 ID가 정해질 때만 함께 정해지므로 정의가 있으면 ID도 있다.
            if (_weaponDefinition == null || _playerAbilities.CanActivateAbility(_reloadAbility) != AbilityActivationResult.Activated) return;
            OriginalItemDefinition weapon = _weaponDefinition;
            string weaponId = _weaponId!;
            if (!weapon.IsGun) return;
            // 원본 ItemAgent_Gun.BeginReload 규칙이다. 씬 정리 중이라 그래프가 없거나 탄창이나 소지품을 읽을 수 없는 그래프 결함이면
            // 기록 없이 끝낸다. 결함 기록은 탄약 HUD와 발사가 한다.
            if (!_world.TryItems(out ItemOwnershipState? items) ||
                !OriginalInventoryQueries.TryLoadedAmmo(items, weaponId, out ItemInstanceState? current)) return;
            // 원본 AutoSetTypeInInventory처럼 목표 탄종이 없으면 탄창의 탄, 그다음 소지품의 첫 호환 탄으로 정하고, 그래도 없으면 장전하지 않는다.
            int target = _bulletTypes.Target(items, weaponId);
            if (target == BulletTypeSwitch._none)
            {
                ItemInstanceState? first = current;
                if (first == null && !OriginalInventoryQueries.TryCompatibleAmmo(items, _world.Catalog, weapon, out first, out _)) return;
                if (first == null) return;
                target = first.OriginalItemId;
                _bulletTypes.SetTarget(weaponId, target);
                _ammoHud.Invalidate();
            }
            // 탄창이 목표 탄종으로 가득 차면 장전하지 않는다. 다른 탄종이 들어 있으면 가득 차도 바꾸려고 장전한다.
            // 탄창 수량은 원본 GetBulletCount에 해당하는 MagazineCount로, 용량은 세션 장전과 같은 MagazineCapacity로 읽는다.
            int loaded = OriginalInventoryQueries.MagazineCount(items, weaponId);
            if (loaded >= weapon.MagazineCapacity && current?.OriginalItemId == target) return;
            // 목표 탄종의 소지 탄이 없으면 장전하지 않는다. 원본처럼 탄약 없음 PopText는 탄창도 비었을 때만 띄운다.
            if (!OriginalInventoryQueries.TryCompatibleAmmo(items, _world.Catalog, weapon, out _, out int reserve, target)) return;
            if (reserve <= 0) { if (loaded <= 0) _world.PopText("Poptext_OutOfAmmo"); return; }
            // 위에서 CanActivateAbility로 확인했고 그 뒤 어빌리티 상태가 바뀌지 않아 활성화가 거절되지 않는다.
            _playerAbilities.TryActivateAbility(_reloadAbility);
            // 장전 시간은 원본 ItemAgent_Gun.ReloadTime처럼 캐릭터의 ReloadSpeedGain 현재값만큼 줄어든다.
            _actionTotal = weapon.ReloadTime(_playerAbilities.GetCurrent(CharacterAttributes.ReloadSpeedGain));
            _actionUntil = Time.time + _actionTotal;
            string? reloadStart = Sounds(weapon).ReloadStart;
            // 원본 AudioObject처럼 긴 장전음은 장전하며 걷는 플레이어를 따라간다.
            // 오디오가 꺼져 있으면 관리자가 null을 돌려주어, 장전 끝의 정지와 일시정지도 아무 일도 하지 않는다.
            if (reloadStart != null) _reloadAudio = _world.Audio.PlayAttached(reloadStart, _world.Motor.transform);
        }

        // 무기 키 입력이다. 원본 CA_UseItem은 CanEditInventory가 false라 ChangeHoldItem:1676이 거절하므로, 사용 중에는 무시한다.
        // 원본 CA_Reload는 CanEditInventory가 true라, 장전 중이면 장전을 취소하고 바꾼다.
        // 원본 ChangeHoldItem은 같은 아이템이면 false라, 이미 든 칸의 키는 장전도 멈추지 않고 무시한다.
        // 스킬 아이템을 들었거나 손이 비었으면 고른 칸이 같아도 그 칸의 무기를 다시 든다.
        internal void SelectWeapon(string slot)
        {
            // 아이템 사용과 스킬 조준 어빌리티가 실행 중에 Block.WeaponSwitch를 쥔다.
            if (_playerAbilities.Tags.HasTag(CharacterTags.BlockWeaponSwitch) || slot == _activeSlot && !_weaponPutAway) return;
            // 원본처럼 장전 중이면 취소하고 바꾼다.
            CancelReload();
            // 원작 ChangeHoldItem처럼 손의 스킬 아이템을 내린다. 고른 칸이 비었으면 그대로 빈손이다.
            if (_weaponPutAway)
            {
                _weaponPutAway = false;
                _skillItem.Drop();
                _drawing = false;
                _world.Motor.Hands.SetHandState(OriginalHandAnimation._emptyHand);
            }
            _activeSlot = slot;
            _activeSlotIndex = GameplayWeaponSlots.IndexOf(slot);
            RefreshEquipment();
        }

        // 원본 BulletTypeHUD.Update의 T 입력과 OnSwitchInput의 위아래 입력이다. 원본 CanOpenList처럼 행동이 진행 중이면 목록을 닫고 열지 않는다.
        // 총을 들지 않았으면 T를 무시한다. 목록이 열린 채 T를 누르면 고른 탄종으로 바꾼다.
        internal void TickBulletTypes(bool toggle, int direction)
        {
            if (_bulletTypes.IsOpen && ActionInProgress) _bulletTypes.Close();
            if (direction != 0 && _bulletTypes.IsOpen) _bulletTypes.Move(direction);
            if (!toggle || _weaponDefinition == null || !_weaponDefinition.IsGun) return;
            if (_bulletTypes.IsOpen)
            {
                int type = _bulletTypes.Selected;
                _bulletTypes.Close();
                SetBulletType(type);
                return;
            }
            // 씬 정리 중이라 그래프가 없으면 기록 없이 열지 않는다. 무기 정의가 있으면 ID도 있다.
            if (ActionInProgress || !_world.TryItems(out ItemOwnershipState? items)) return;
            string weaponId = _weaponId!;
            _bulletTypes.Open(items, _world.Catalog, _weaponDefinition, _bulletTypes.Target(items, weaponId));
        }

        // 원본 CloseList다. 인벤토리, 대화, 일시정지처럼 게임플레이 입력이 꺼진 프레임에 컨트롤러가 부른다.
        internal void CloseBulletTypes() => _bulletTypes.Close();

        // 원본 BulletTypeHUD.SetBulletType과 SetTargetBulletType이다. 목표가 바뀔 때만, 앞 목표가 있었으면 탄창을 비우고 장전을 시도한다.
        // 탄종 목록은 행동이 없을 때만 열리므로 장전은 다른 행동에 막히지 않는다.
        void SetBulletType(int type)
        {
            string weaponId = _weaponId!;
            if (!_world.TryItems(out GameSessionManager? session, out ItemOwnershipState? items)) return;
            int target = _bulletTypes.Target(items, weaponId);
            if (target == type) return;
            _bulletTypes.SetTarget(weaponId, type);
            _ammoHud.Invalidate();
            if (target != BulletTypeSwitch._none) UnloadMagazine(session, weaponId);
            StartReload();
        }

        // 원본 TakeOutAllBullets처럼 탄을 소지품에 넣고, 자리가 없어 남은 탄은 원본 PickupItem 실패처럼 플레이어 주변 사방에 떨군다.
        // 세션 명령 게이트가 닫혀 있으면 기록 없이 false다.
        bool UnloadMagazine(GameSessionManager session, string weaponId)
        {
            if (!session.Inventory.TryUnloadMagazine(_world.Catalog, weaponId, out IReadOnlyList<ItemOwnershipState> dropped)) return false;
            // 원본 Drop(…, Vector3.forward, 360)처럼 플레이어 둘레로 떨구는 규칙은 바닥 아이템 생성 객체 한곳에 있다.
            if (dropped.Count > 0) _drops.SpawnAroundPlayer(dropped);
            return true;
        }

        // 컨트롤러의 HUD 갱신이 매 프레임 읽는 탄창 수 문자열이다. 캐시와 BulletTypeHUD 표시는 CombatAmmoHud가 맡고, 든 무기만 넘긴다.
        internal string AmmoText() => _ammoHud.Text(_weaponId, _weaponDefinition);

        // 같은 갱신에서 AmmoText 뒤에 읽는 소지 수 문자열이다. AmmoText가 그 프레임의 값으로 맞춘다.
        internal string AmmoReserveText => _ammoHud.ReserveText;

        // 로캘이 바뀌면 다음 탄약 HUD 갱신에서 탄약 이름을 다시 읽는다.
        internal void InvalidateAmmoText() => _ammoHud.Invalidate();

        // 선택한 무기 칸의 장착 인스턴스가 바뀌었을 때만 장전 취소, 무기 표시, 산포 프로필, 카메라, 조준 보행 배율을 다시 맞춘다.
        // 같은 무기에 부착물만 바뀌면 원작처럼 꺼내기와 장전은 그대로 두고 수치에 따른 값만 다시 맞춘다.
        internal void RefreshEquipment()
        {
            // 씬 정리 중이라 그래프가 없으면 기록 없이 장비 상태를 그대로 둔다.
            // 검증된 그래프에는 플레이어 루트가 늘 있어 칸 조회가 실패하지 않는다.
            if (!_world.TryItems(out ItemOwnershipState? items)) return;
            // 원작 OnAgentItemTreeChanged처럼 든 스킬 아이템이 다 쓰였거나 소지품을 떠나면 손이 빈다. 무기는 다시 들지 않는다.
            if (_skillItem.Holding && items.PositionIn(items.CharacterId!, _skillItem.Item!) == null)
            {
                _skillItem.Drop();
                _drawing = false;
                _world.Motor.Hands.SetHandState(OriginalHandAnimation._emptyHand);
            }
            string? id = null;
            if (!_weaponPutAway) OriginalInventoryQueries.TryEquippedIn(items, _activeSlot, out id);
            if (id == _weaponId)
            {
                // 노드의 칸 목록은 칸을 바꿀 때만 새로 만들어지므로 참조가 같으면 부착물도 같다. 발사와 장전은 칸 목록을 공유한다.
                if (id != null && !ReferenceEquals(items.Find(id)!.Slots, _weaponAttachments)) ApplyWeaponStats(items, id);
                return;
            }
            PutAwayWeapon();
            _weaponId = id;
            // 원본 ItemAgentHolder처럼 새 무기는 0.15초 숨긴 채 빈손 상태를 보낸다. 시간이 지나면 TickProjectiles가 무기를 보이고
            // 손 상태를 바꿔, 손 레이어가 팔을 들어 올리는 꺼내기 동작이 된다. 무기를 내리면 바로 빈손이다.
            _drawUntil = Time.time + OriginalHandAnimation._drawTime;
            _drawing = id != null;
            // 게임 카메라의 ADS 규칙도 든 무기를 따른다. 총이면 원본 ADSTime과 ADSAimDistanceFactor를, 맨손과 근접 무기는 총 없음 규칙을 쓴다.
            if (id == null) { _world.GameCamera.SetGun(null); return; }
            ApplyWeaponStats(items, id);
            // 장착 직후 첫 탄환도 원본 기본 산포에서 시작한다.
            _scatter = _hipScatter.Minimum;
            OriginalItemDefinition definition = _weaponDefinition!;
            // 표시 프리팹이 있는 무기만 표시를 만든다. 총의 표시 프리팹은 원본 Muzzle 소켓을 늘 갖는다.
            // 만든 표시는 꺼내기 시간이 끝날 때까지 숨긴다.
            if (definition.Visual != null)
            {
                WeaponVisual visual = WeaponVisual.Create(definition.Visual, _world.Character.Rig.GetEquipmentSocket(EquipPartType.RightHand));
                visual.SetVisible(false);
                _weaponVisual = visual;
            }
        }

        // 든 무기를 손에서 내린다. 장전 중 무기를 창고에 넣거나 해제하면 사라진 인스턴스를 참조하지 않도록 장전을 취소하고,
        // 원본 OnHoldAgentChanged처럼 탄종 목록을 닫는다. 조준 반동 복구를 끝내고 두 이동 배율을 맨손일 때의 원본 값으로 돌린다.
        void PutAwayWeapon()
        {
            CancelReload();
            _bulletTypes.Close();
            _weaponId = null;
            _weaponDefinition = null;
            _weaponAttachments = null;
            // 쏜 직후 무기를 바꿔도 재생 중인 섬광은 옛 총과 함께 파괴되지 않고 풀로 돌아간다.
            DisposeWeaponVisual();
            // 뒤이어 새 무기나 손 아이콘 표시를 만들 수 있으므로, 다음 피격 표시가 플레이어 렌더러를 다시 모으게 한다.
            _playerHurt.MarkRenderersChanged();
            _scatter = 0;
            _world.Motor.Hands.SetHandState(OriginalHandAnimation._emptyHand);
            _drawing = false;
            _world.Motor.ClearRecoil();
            _world.Motor.SetAdsWalkSpeedMultiplier(OriginalPlayerMotor._defaultAdsWalkSpeedMultiplier);
            _world.Motor.SetWeaponMoveSpeedMultiplier(OriginalCharacter.WeaponMoveSpeedMultiplier(null));
        }

        // 든 무기의 수치에 따른 값을 맞춘다. 정의는 꽂힌 부착물의 Parent modifier를 반영한 값이라, 원작 Item.Stats처럼 부착물이 사격,
        // 장전, 카메라, 이동 배율을 바꾼다. 정의는 위에서 찾은 그래프에서 읽어 세션을 다시 찾지 않는다.
        void ApplyWeaponStats(ItemOwnershipState items, string id)
        {
            ItemInstanceState weapon = items.Find(id)!;
            _weaponAttachments = weapon.Slots;
            OriginalItemDefinition definition = _world.Catalog.Effective(items, weapon);
            _weaponDefinition = definition;
            _world.GameCamera.SetGun(definition.IsGun ? definition : null);
            // 비조준과 ADS 프로필은 각자 정해진 상수 능력치 키로 만들어, 키 문자열을 매번 잇지 않는다.
            _hipScatter = ScatterProfile.Hip(definition);
            _adsScatter = ScatterProfile.Ads(definition);
            // 원본 ItemAgent_Gun.AdsWalkSpeedMultiplier처럼 든 총의 스탯을 조준 보행 배율로 쓴다. 근접 무기와 스탯이 없는 총은 기본값을 쓴다.
            if (definition.IsGun) _world.Motor.SetAdsWalkSpeedMultiplier(definition.Stat("AdsWalkSpeedMultiplier", OriginalPlayerMotor._defaultAdsWalkSpeedMultiplier));
            // 원본은 꺼내기 시간과 상관없이 손에 든 순간부터 무기 이동 배율이 걷기와 달리기에 붙는다.
            _world.Motor.SetWeaponMoveSpeedMultiplier(OriginalCharacter.WeaponMoveSpeedMultiplier(definition));
        }

        // 든 무기로 한 번 공격하고, 쏜 총이면 적에게 소리를 알린다. weapon은 TickAttack이 null을 거른 _weaponDefinition이다.
        void Attack(OriginalItemDefinition weapon)
        {
            // 적과 같은 원본 공격 간격 규칙을 정의에서 읽는다.
            _nextShot = Time.time + weapon.AttackInterval;
            if (weapon.IsGun)
            {
                // 소모하기 직전 탄창의 첫 탄약을 발사 정보에 고정한다. 무기 ID는 TickAttack이 확인한 무기 정의와 함께 정해진다.
                // 씬 정리 중이라 세션이나 그래프가 없거나, 탄창을 읽을 수 없는 그래프 구조 결함이면 쏘지 않는다.
                string weaponId = _weaponId!;
                if (!_world.TryItems(out GameSessionManager? session, out ItemOwnershipState? items) ||
                    !OriginalInventoryQueries.TryLoadedAmmo(items, weaponId, out ItemInstanceState? loaded)) return;
                // 원본 GunStates.empty처럼 빈 총의 방아쇠를 누르면 Holder.TryToReload로 장전을 시도한다. 탄약이 없으면 StartReload가 PopText를 띄운다.
                if (loaded == null) { if (_attack.WasPressedThisFrame()) StartReload(); return; }
                // 탄창에 든 탄은 그래프의 아이템이라 늘 카탈로그 정의가 있다. 저장 읽기 검사가 모르는 ID를 막으므로 DefinitionOf로 읽는다.
                OriginalItemDefinition ammo = _world.Catalog.DefinitionOf(loaded.OriginalItemId);
                // 발사 위치는 WeaponVisual이 캐시한 Muzzle이다. 장착한 총은 표시 프리팹과 원본 Muzzle 소켓을 늘 가지므로 표시와 Muzzle이 있다.
                Transform muzzle = _weaponVisual!.Muzzle!;
                // 플레이어 사격 보정은 아바타 연결이 원본 플레이어 정의로 채운 GAS 어트리뷰트의 현재값이다. 버프가 바꿀 수 있어 발사마다 읽는다.
                AbilitySystem character = _playerAbilities;
                // 규칙상 거절은 기록 없이 false다. 불변식 위반은 세션 명령이 직접 보고하므로 여기서 다시 기록하지 않는다.
                if (!session.Inventory.TryFire(weaponId)) return;
                // 수평 회전과 총구의 3차원 조준을 따로 다룬다. 머리와 몸 조준은 원작 InputManager처럼 레이어 레이로 여기서 한 번만 한다.
                // 애니메이션이 옮긴 머리 구를 레이가 보도록 먼저 Transform을 물리에 맞춘다. 맞은 Collider의 소속은 이 월드의 캐시에서 찾는다.
                Physics.SyncTransforms();
                OriginalPlayerMotor motor = _world.Motor;
                OriginalAimResult aim = OriginalBallistics.Aim(_world.Camera, motor.AimScreenPosition, motor.AimPoint, _owners);
                // 탄속 배율은 원본 CharacterMainControl.GunBulletSpeedMultiplier가 읽는 BulletSpeedMultiplier 스탯이고,
                // 사거리는 원본 ItemAgent_Gun.BulletDistance처럼 GunDistanceMultiplier를 곱한다.
                // 플레이어 탄은 쏜 적이 없어 플레이어 팀 탄이다.
                var shot = new OriginalShot(motor.transform, null, aim, weapon, ammo,
                    character.GetCurrent(CharacterAttributes.GunDamageMultiplier), character.GetCurrent(CharacterAttributes.BulletSpeedMultiplier),
                    character.GetCurrent(CharacterAttributes.GunCritDamageGain), character.GetCurrent(CharacterAttributes.GunDistanceMultiplier));
                // 원본처럼 캐릭터 산포 배율과, 무기 내구도가 낮을 때의 추가 산포를 발사 순간에 정한다.
                // 내구도는 발사를 확정한 뒤의 그래프에서 읽고, 값이 없으면 최대 내구도로 보아 추가 산포를 주지 않는다.
                float spread = _scatter * Mathf.Max(_minScatterMultiplier, character.GetCurrent(CharacterAttributes.GunScatterMultiplier));
                // 발사 명령이 확정한 뒤의 스냅샷이라 이번 발사의 내구도 감소가 반영되어 있다.
                CampaignState campaign = session.Snapshot().Campaign;
                float? absolute = campaign.Gameplay.Inventory.Items?.Find(weaponId)?.AbsoluteDurability;
                float durability = weapon.Durability > 0 ? (absolute ?? weapon.Durability) / weapon.Durability : 1;
                spread += Mathf.Max(1, spread) * Mathf.Lerp(_lowDurabilityScatter, 0, Mathf.InverseLerp(0, _lowDurabilityRatio, durability));
                // 원본처럼 섬광이 총구를 따라가도록 위치가 아닌 총구 소켓을 넘긴다.
                Fire(shot, muzzle, _world.PlayerPosition + Vector3.up * _chestHeight, weapon, spread);
                ScatterProfile profile = motor.IsAiming ? _adsScatter : _hipScatter;
                _scatter = Mathf.Clamp(_scatter + profile.Growth, profile.Minimum, profile.Maximum);
                // Muzzle이 있으면 표시도 있으므로 '!'가 안전하다. 표시 반동은 WeaponVisual이 진행한다.
                // 원작 화면에서 사격은 몸을 비틀지 않고 총 모델만 밀리므로 공격 레이어 몸동작은 내지 않는다(사용자 결정 2026-10-16).
                _weaponVisual!.Kick();
                // 조준 반동은 입력을 가진 모터가 처리한다. 무기, 캐릭터, 난이도의 원본 수치를 넘기며, 난이도는 난이도 소유자의 현재 값이다.
                // 위에서 세션을 찾았으므로 앱이 살아 있어 GameManager에서 바로 읽는다.
                motor.AddRecoil(weapon, character.GetCurrent(CharacterAttributes.RecoilControl), DamageRules.Recoil(GameManager.Difficulty.Current));
                // 원본 ItemAgent_Gun 발사처럼 총구 반대 방향 반동 충격을 게임 카메라에 준다.
                _world.GameCamera.Impulse(-muzzle.forward * _shootImpulse);
                // 발사마다 문자열을 만들지 않도록 발사음 경로는 무기 정의별 캐시에서 읽는다.
                // 오디오가 꺼져 있으면 관리자가 재생을 조용히 무시한다.
                string? shootSound = Sounds(weapon).Shoot;
                if (shootSound != null) _world.Audio.PlayOneShot(shootSound, _world.PlayerPosition);
                // 원작 ItemAgent_Gun.cs:1032처럼 쏜 총소리만 나온 적에게 알린다. 원작 근접 무기는 AI 소리를 내지 않는다.
                AlertEnemies(_world.PlayerPosition, weapon.Stat("SoundRange"));
            }
            else
            {
                // 원본 CA_Attack.OnStart처럼 휘두르는 순간 소리와 휘두르기 동작을 내고, 피해는 정의의 DealDamageTime 뒤에 판정한다.
                _world.Motor.Hands.PlayAttack();
                _world.Audio.PlayOneShot(_meleeSwingSound, _world.PlayerPosition);
                _pendingMelee.Add(new PendingMelee(null, weapon, Time.time + weapon.DealDamageTime));
            }
        }

        // 소리를 나온 적에게 저작 순서대로 알린다. sourceEnemy는 소리를 낸 적이고, 플레이어 소리면 비어 있다.
        // 위치와 범위는 듣는 적마다 같아 호출자가 한 번 구해 넘긴다. 열거자를 할당하지 않도록 색인으로 돈다.
        internal void AlertEnemies(Vector3 source, float range, OriginalEnemyController? sourceEnemy = null)
        {
            for (int index = 0; index < _soundListeners.Count; index++) _soundListeners[index].Hear(source, range, sourceEnemy);
        }

        // 원작 CharacterSoundMaker.PostFootStepSound처럼 플레이어 발소리마다 걷기나 달리기 반경으로 나온 적에게 알린다.
        // 장비 효과가 반경과 최대 무게를 바꿀 수 있어 걸음마다 GAS 현재값을 읽는다. 원작은 반경 0에도 알리지만 아무도 못 들어 부르지 않는다.
        void OnFootstep(Vector3 position, bool running)
        {
            AbilitySystem player = _playerAbilities;
            float range = OriginalCharacterMotion.FootstepNoiseRange(running, player.GetCurrent(CharacterAttributes.WalkSoundRange),
                player.GetCurrent(CharacterAttributes.RunSoundRange), CarriedWeight(), player.GetCurrent(CharacterAttributes.MaxWeight));
            if (range > 0f) AlertEnemies(position, range);
        }

        // 원작 CharacterItem.TotalWeight처럼 플레이어 루트 아래 장비와 소지품 무게를 모두 더한다. 인벤토리 화면 무게와 같은 질의다.
        // 세션이 없는 정리 프레임에는 마지막으로 잰 무게를 쓴다. 검증된 소유 그래프라 플레이어 루트가 늘 있다.
        float CarriedWeight()
        {
            if (_world.TryItems(out ItemOwnershipState? items) && !ReferenceEquals(items, _footstepWeightGraph))
            {
                OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? character);
                _footstepWeight = OriginalInventoryQueries.TotalWeight(items, _world.Catalog, character!.InstanceId);
                _footstepWeightGraph = items;
            }
            return _footstepWeight;
        }

        // 적 공격을 실행한다: 총은 적 총구에서 원본 산포를 거친 탄환을 쏘고, 근접은 휘두르는 소리를 내고 DealDamageTime 뒤에 판정한다.
        // 휘두르기와 발사 몸동작은 적이 자기 Animator로 낸다. 사격 피해 배율은 적의 GunDamageMultiplier 어트리뷰트다.
        internal void EnemyAttack(OriginalEnemyController enemy, OriginalItemDefinition weapon)
        {
            Vector3 origin = enemy.transform.position + Vector3.up * _chestHeight;
            if (weapon.IsGun)
            {
                // 적도 플레이어처럼 실제 총구에서 쏘고, 같은 탄도와 차폐 검사를 거친다. 적 총 표시 프리팹은 Muzzle 소켓을 늘 갖는다.
                Transform muzzle = enemy.Muzzle!;
                // 표적이 플레이어면 이전처럼 플레이어 가슴 높이를 노리고, 다른 팀 적이면 적 컨트롤러가 내는 표적 조준점을 노린다.
                // 플레이어 조준점을 이 클래스에 두어 플레이어를 노리는 사격은 이전과 같다.
                bool targetPlayer = enemy.TargetIsPlayer;
                Vector3 aimPoint = targetPlayer ? _world.PlayerPosition + Vector3.up * _chestHeight : enemy.TargetAimPoint;
                // 사거리는 플레이어처럼 원본 ItemAgent_Gun.BulletDistance가 곱하는 GunDistanceMultiplier다. 적은 프리셋 gunDistanceMultiplier
                // (버섯 1.2, 꼬마덕 1.25)를 그 어트리뷰트에 넣는다.
                // 쏜 적을 발사 정보에 담아 탄의 팀 거르기와 명중 공격자로 쓴다.
                // 원작 ItemAgent_Gun.cs:1169-1170처럼 적의 GunCritDamageGain과 GunCritRateGain 현재값으로 치명 배율과 확률을 정한다.
                // 적 탄은 탄 정의를 넘기지 않아 탄의 치명 증가는 0이다.
                AbilitySystem shooter = enemy.Abilities;
                var shot = new OriginalShot(enemy.transform, enemy, new OriginalAimResult(aimPoint, null, false), weapon, null,
                    shooter.GetCurrent(CharacterAttributes.GunDamageMultiplier), enemy.BulletSpeedMultiplier,
                    critGain: shooter.GetCurrent(CharacterAttributes.GunCritDamageGain),
                    distanceMultiplier: shooter.GetCurrent(CharacterAttributes.GunDistanceMultiplier),
                    critRateGain: shooter.GetCurrent(CharacterAttributes.GunCritRateGain));
                // 적 탄환도 원본 산포 보정을 거쳐 실제로 맞아야 피해를 준다. 원작 AICharacterController.cs:326처럼 화면 밖과 달리기 보정은
                // 표적이 플레이어일 때만 본다. 적끼리 싸울 때는 보정 없이 프리셋 산포만 곱한다.
                bool outside = targetPlayer && IsOffscreen(_world.Camera.WorldToViewportPoint(enemy.transform.position));
                // 섬광이 적 총구를 따라가도록 총구 소켓을 넘긴다. 적의 기본 산포는 비조준 프로필의 기본값과 같은 식이다.
                // 달리기 판정은 제곱근 없이 제곱 속력으로 비교하고, 경계값의 아주 작은 부동소수 오차는 허용한다.
                bool running = targetPlayer && _world.Motor.Velocity.sqrMagnitude > _runningScatterSpeed * _runningScatterSpeed;
                Fire(shot, muzzle, origin, weapon, ScatterProfile.Hip(weapon).Minimum * enemy.ScatterMultiplier(running, outside));
                // 적 발사음 경로도 무기 정의별 캐시에서 읽는다. 오디오가 꺼져 있으면 관리자가 재생을 조용히 무시한다.
                string? shootSound = Sounds(weapon).Shoot;
                if (shootSound != null) _world.Audio.PlayOneShot(shootSound, origin);
            }
            else
            {
                _world.Audio.PlayOneShot(_meleeSwingSound, enemy.transform.position);
                // 플레이어 근접과 같은 정의의 DealDamageTime 뒤에 판정한다.
                _pendingMelee.Add(new PendingMelee(enemy, weapon, Time.time + weapon.DealDamageTime));
            }
        }

        // 뷰포트 좌표가 카메라 뒤이거나 화면 사각형 밖이면 화면 밖이다. 원본 적 산포 보정이 이 값을 본다.
        static bool IsOffscreen(Vector3 viewport) =>
            viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1;

        // 원본 SpawnAlertFx처럼 적이 휘두르기 직전에 오른손 소켓에서 공격 예고 효과를 낸다. 효과는 TickProjectiles가 매 프레임
        // CombatEffects로 소켓 위치에 옮긴다.
        internal void EnemyMeleeAlert(Transform socket)
        {
            // 공격 예고 효과는 앱 수명 게임플레이 데이터라 원작처럼 GameplayDataSettings.Prefabs의 AlertFxPrefab에서 읽는다.
            // 대여, 활성화, 재생은 다른 효과와 같은 CombatEffects가 하고, 소켓의 자식으로 붙이지 않는다.
            _effects.AddAlert(GameplayDataSettings.Prefabs.AlertFxPrefab!, socket);
        }

        // DealDamageTime이 지난 근접 공격을 그 순간의 위치로 판정한다. 그사이 죽거나 사라진 적의 공격은 버린다.
        void TickMelee(float now)
        {
            for (int i = _pendingMelee.Count - 1; i >= 0; i--)
            {
                PendingMelee melee = _pendingMelee[i];
                if (now < melee.At) continue;
                _pendingMelee.RemoveAt(i);
                if (melee.Player) ResolvePlayerMelee(melee.Weapon);
                else if (melee.Attacker != null && melee.Attacker.Targetable) ResolveEnemyMelee(melee.Attacker, melee.Weapon);
            }
        }

        // 원본 ItemAgent_MeleeWeapon.CheckCollidersInRange처럼 사거리 구에 닿은 피해 Collider 중 생성되어 살아 있는 적만 맞는다.
        // 원작처럼 벽은 보지 않는다. 맞힌 적마다 게임 카메라에 캐릭터 오른쪽으로 0.05의 충격을 주고 무작위 흔들림 0.3을 섞는다.
        void ResolvePlayerMelee(OriginalItemDefinition weapon)
        {
            Vector3 player = _world.PlayerPosition, aim = _world.Motor.AimDirection;
            // 원본 ItemAgent_MeleeWeapon처럼 플레이어의 MeleeDamageMultiplier 현재값을 곱한다.
            float damage = weapon.MeleeDamage(_playerAbilities.GetCurrent(CharacterAttributes.MeleeDamageMultiplier));
            // 원작 ItemAgent_MeleeWeapon.cs:190-191처럼 무기 치명 값에 플레이어의 근접 치명 증가를 곱하고, 맞은 대상마다 치명을 뽑는다.
            float critRate = DamageRules.MeleeCritRate(weapon.Stat("CritRate"),
                _playerAbilities.GetCurrent(CharacterAttributes.MeleeCritRateGain));
            float critFactor = DamageRules.MeleeCritFactor(weapon.Stat("CritDamageFactor", 1f),
                _playerAbilities.GetCurrent(CharacterAttributes.MeleeCritDamageGain));
            // 원작 ItemAgent_MeleeWeapon.cs:189처럼 무기 ArmorPiercing을 관통으로 넘긴다. 단순 체력 대상은 방어가 없어 쓰지 않는다.
            float armorPiercing = weapon.Stat("ArmorPiercing");
            Vector3 right = Vector3.Cross(Vector3.up, aim).normalized;
            int count = OverlapMelee(player, weapon);
            for (int i = 0; i < count; i++)
            {
                Collider collider = _meleeHits[i];
                // 원작 근접은 적이 아닌 단순 체력 대상(Teams.all)도 맞힌다. 그 대상은 피해 효과 없이 체력만 줄인다.
                // 소속은 이 월드의 Collider 소속 캐시에서 찾는다.
                _owners.FindOwner(collider, out OriginalEnemyController? enemy, out _, out DamageReceiver? receiver);
                // 원작 ItemAgent_MeleeWeapon.cs:170처럼 대시 중인 적은 호 판정과 카메라 흔들림 전에 건너뛴다.
                if (receiver == null && (enemy == null || !enemy.Targetable || enemy.IsDashing)) continue;
                // 원본처럼 방향과 거리는 높이를 빼고 피해 Collider의 Transform까지 잰다.
                Vector3 offset = Horizontal(collider.transform.position - player);
                if (!InMeleeArc(offset, aim)) continue;
                if (receiver != null)
                {
                    // 플레이어 근접은 공격자 적이 없는 플레이어 피해다. 단순 체력 대상은 원작처럼 치명이면 피해에 배율만 곱한다.
                    float receiverDamage = RollCrit(critRate, true) ? damage * critFactor : damage;
                    HurtReceiver(receiver, MeleeDamage(receiverDamage, null, collider.transform.position, offset));
                    _world.GameCamera.Impulse(MeleeHitImpulse(right));
                    continue;
                }
                // 원본 HurtVisual은 받아들여진 근접 피해에만 HitFx를 피해 지점(대상 위치에서 공격 방향으로 0.2 앞)에 낸다.
                // 원본 ItemAgent_MeleeWeapon의 카메라 흔들림은 피해 결과와 관계없이 맞힌 적마다 낸다.
                // 수신자가 없으면 위 거르기에서 적이 있음을 확인했다.
                // 피해 숫자가 피해 지점에서 뜨도록 피격 효과와 같은 지점과 방향을 피해에 함께 넘긴다.
                Vector3 direction = offset.normalized;
                Vector3 hitPoint = collider.transform.position + Vector3.up * .7f - direction * _meleeHitForward;
                // 치명이면 원작 Health.Hurt처럼 피해에 배율을 곱하고, 피해 실행이 머리 방어를 쓰며 피격음도 치명 소리다.
                // 피해 실행은 몸통이든 머리든 방어에서 무기 관통을 뺀다.
                bool crit = RollCrit(critRate, false);
                if (enemy!.Hurt(crit ? damage * critFactor : damage, armorPiercing, crit: crit, point: hitPoint, normal: -direction))
                    OrganicHit(hitPoint, -direction, crit);
                _world.GameCamera.Impulse(MeleeHitImpulse(right));
            }
        }

        // 원본 근접 명중의 카메라 충격이다. 캐릭터 오른쪽에 무작위 흔들림을 섞어, 맞힐 때마다 Random을 한 번 부른다.
        static Vector3 MeleeHitImpulse(Vector3 right) => (right + UnityEngine.Random.insideUnitSphere * _meleeShake).normalized * _meleeImpulse;

        // 원본 근접 판정처럼 판정 순간 공격자 주위 사거리 구에 닿은 피해 Collider를 맞힌다. 플레이어는 구르는 중이면 맞지 않는다.
        // 공격 방향은 적의 정면이고 원작처럼 벽은 보지 않는다. 플레이어는 하나라 한 번 맞히면 끝낸다.
        // 원작 ItemAgent_MeleeWeapon.cs:162처럼 다른 팀 적도 맞힌다. 같은 팀은 대상이 All일 때만 맞고, 공격자 자신은 맞지 않는다.
        void ResolveEnemyMelee(OriginalEnemyController enemy, OriginalItemDefinition weapon)
        {
            Vector3 attacker = enemy.transform.position, forward = enemy.transform.forward;
            int count = OverlapMelee(attacker, weapon);
            // 원작 근접처럼 범위 안의 피해 Collider를 모두 본다. 단순 체력 대상도 맞히고, 플레이어는 하나라 한 번만 맞힌다.
            bool playerHit = false;
            // 원작 ItemAgent_MeleeWeapon.cs:190-191처럼 AI도 무기 치명 값에 자기 근접 치명 증가를 곱하고, 맞은 대상마다 치명을 뽑는다.
            AbilitySystem attackerAbilities = enemy.Abilities;
            float critRate = DamageRules.MeleeCritRate(weapon.Stat("CritRate"),
                attackerAbilities.GetCurrent(CharacterAttributes.MeleeCritRateGain));
            float critFactor = DamageRules.MeleeCritFactor(weapon.Stat("CritDamageFactor", 1f),
                attackerAbilities.GetCurrent(CharacterAttributes.MeleeCritDamageGain));
            // 원작 ItemAgent_MeleeWeapon.cs:189처럼 AI 근접도 무기 ArmorPiercing을 관통으로 넘긴다. 적 대상과 플레이어가 같은 값을 받는다.
            float armorPiercing = weapon.Stat("ArmorPiercing");
            for (int i = 0; i < count; i++)
            {
                Collider collider = _meleeHits[i];
                // 소속은 이 월드의 Collider 소속 캐시에서 찾는다.
                _owners.FindOwner(collider, out OriginalEnemyController? target, out OriginalPlayerMotor? motor, out DamageReceiver? receiver);
                // 적 대상은 원작처럼 공격자 자신, 조준할 수 없는 적, 대시 중인 적, 탄과 같은 같은 팀 통과 규칙에 걸리는 적을 건너뛴다.
                // 원작은 IsEnemy가 아니라 이 통과 규칙을 쓰므로 중립 팀도 맞는다.
                if (target != null)
                {
                    if (ReferenceEquals(target, enemy) || !target.Targetable || target.IsDashing || TeamRules.BulletPasses(enemy.Team, target.Team))
                        continue;
                }
                else if (receiver == null && (motor == null || motor.IsDashing || playerHit)) continue;
                Vector3 offset = Horizontal(collider.transform.position - attacker);
                if (!InMeleeArc(offset, forward)) continue;
                // 플레이어 근접과 같은 원본 근접 피해 식에 적의 MeleeDamageMultiplier 어트리뷰트를 곱한다.
                float damage = weapon.MeleeDamage(enemy.Abilities.GetCurrent(CharacterAttributes.MeleeDamageMultiplier));
                // 단순 체력 대상에도 공격한 적을 넘겨, 부서져 터지면 연쇄 폭발도 이 적의 피해가 된다.
                // 단순 체력 대상은 원작처럼 확률과 같은 난수도 치명이고, 치명이면 피해에 배율만 곱한다.
                if (receiver != null)
                {
                    float receiverDamage = RollCrit(critRate, true) ? damage * critFactor : damage;
                    HurtReceiver(receiver, MeleeDamage(receiverDamage, enemy, collider.transform.position, offset));
                    continue;
                }
                Vector3 direction = offset.sqrMagnitude > 0f ? offset.normalized : forward;
                // 캐릭터는 치명이면 피해에 배율을 곱하고 피해 실행이 머리 방어를 쓴다. 적 대상과 플레이어 모두 같다.
                bool crit = RollCrit(critRate, false);
                float hitDamage = crit ? damage * critFactor : damage;
                // 다른 팀 적은 공격자를 넘겨 처치가 플레이어 몫이 되지 않게 한다. 원작처럼 AI끼리의 피해도 숫자를 띄우도록
                // 플레이어 근접과 같은 피해 지점과 무기 관통을 넘기고, 그 지점에 생체 피격 효과를 낸다.
                if (target != null)
                {
                    Vector3 hitPoint = collider.transform.position + Vector3.up * .7f - direction * _meleeHitForward;
                    if (target.Hurt(hitDamage, armorPiercing, fromPlayer: false, crit: crit, attacker: enemy, point: hitPoint, normal: -direction))
                        OrganicHit(hitPoint, -direction, crit);
                    continue;
                }
                Vector3 chest = _world.PlayerPosition + Vector3.up * _chestHeight;
                // 플레이어도 원작 Health.Hurt처럼 무기 관통을 뺀 몸통 또는 머리 방어로 막는다.
                DamagePlayer(hitDamage, chest - direction * _meleeHitForward, -direction, armorPiercing, crit);
                playerHit = true;
            }
        }

        // 원본 UpdateColliders처럼 공격자 위치에서 사거리 + 0.05 구에 닿는 피해 Collider를 미리 만든 버퍼로 찾는다.
        int OverlapMelee(Vector3 attacker, OriginalItemDefinition weapon)
        {
            float radius = weapon.MeleeRange + _meleeRangeMargin;
            return Physics.OverlapSphereNonAlloc(attacker, radius, _meleeHits, GameplayDataSettings.Layers.DamageReceiverLayerMask);
        }

        // 원본처럼 수평 방향이 공격 방향 앞쪽 90도 안이거나, 방향과 관계없이 _meleeNearDistance 안이면 맞는다.
        static bool InMeleeArc(Vector3 offset, Vector3 forward) =>
            Vector3.Angle(offset, forward) < 90f || offset.sqrMagnitude < _meleeNearDistance * _meleeNearDistance;

        // 탄환을 쏜다. 몸에서 총구까지의 첫 구간도 검사해, 벽 너머로 튀어나온 총구로 벽을 뚫고 쏘지 못하게 한다.
        void Fire(OriginalShot shot, Transform muzzleSocket, Vector3 chamber, OriginalItemDefinition weapon, float scatter)
        {
            // 산탄 수는 정의 접근자로 읽고, 원본처럼 정수로 자른 뒤 최소 1발이다.
            int pellets = Mathf.Max(1, (int)weapon.ShotCount);
            Vector3 muzzle = muzzleSocket.position;
            Vector3 direction = (shot.Aim.Point - muzzle).normalized;
            // 섬광은 총구의 자식으로 붙어 총구를 따라간다. 섬광 규칙은 앱 수명 게임플레이 데이터라 GameplayDataSettings.Prefabs에서 읽는다.
            _effects.AddFlash(GameplayDataSettings.Prefabs.MuzzleFlash!, muzzleSocket);
            // 산탄 분배 각도와 간격, 궤적 생성 규칙은 탄마다 같으므로 반복문 앞에서 한 번 구한다.
            // ShotAngle이 359를 넘으면 원형으로 보고, 첫 탄과 끝 탄이 겹치지 않게 분배 각도를 한 칸 줄인다.
            float angle = weapon.Stat("ShotAngle"); bool circle = angle > 359;
            if (circle) angle -= angle / pellets;
            float step = pellets > 1 ? angle / (pellets - 1) : 0;
            // 탄 궤적 규칙은 앱 수명 게임플레이 데이터라 GameplayDataSettings.Prefabs에 저작되어 있다.
            SpawnRule trailRule = GameplayDataSettings.Prefabs.BulletTrail!;
            for (int pellet = 0; pellet < pellets; pellet++)
            {
                Projectile p = _projectilePool.Count > 0 ? _projectilePool.Pop() : new Projectile();
                // 재사용한 탄도 원본 Init처럼 스치는 소리 기록을 비우고 시작한다.
                p.Shot = shot; p.Position = muzzle; p.Distance = 0; p.FlyByPlayed = false;
                // 산탄의 고정 ShotAngle 분배와 각 탄의 무작위 산포를 원본 순서대로 적용한다.
                float offset = pellets > 1 ? -angle * .5f + pellet * step - (circle && pellets % 2 == 0 ? step * .5f : 0) : 0;
                p.Direction = Quaternion.Euler(0, offset + UnityEngine.Random.Range(-.5f, .5f) * scatter, 0) * direction;
                // 처음 쓰는 탄이나 궤적이 파괴된 탄만 World에서 궤적을 빌리고, 탄을 재사용하는 동안 계속 가진다.
                // 잔상 초기화와 방출 시작은 PooledTrail.Begin 한곳에서 한다.
                if (p.Trail == null) p.Trail = RentTrail(trailRule, muzzle);
                if (p.Trail != null) p.Trail.Begin(muzzle);
                // 원본 firstFrameCheck처럼 첫 구간만 몸에서 총구까지 시작 겹침과 함께 검사한다. 소속은 이 월드의 캐시에서 찾는다.
                // 같은 팀 통과는 발사 정보의 팀으로 본다.
                // 치명 확률이 0.99를 넘는 탄은 원작처럼 모든 모래주머니를 지나간다.
                if (OriginalBallistics.Sweep(chamber, muzzle - chamber, shot.Radius, shot.Shooter, shot.Player, shot.Team, _hits, _owners,
                    out OriginalBallistics.Contact first, shot.PassesAllHalfCover))
                { Impact(p, first); Release(p); }
                else _projectiles.Add(p);
            }
        }

        // 궤적이 사라진 탄을 풀로 돌려주고 끝난 효과를 World에 반환한다. 이어서 산포 복구, 무기 표시 반동, 무기 꺼내기,
        // 예고 효과 위치, 근접 판정을 진행하고, 날아가는 탄환을 옮기며 충돌과 스치는 소리를 검사한다.
        // 컨트롤러는 이 메서드 뒤에 체력 0을 보므로 근접과 탄 피해가 같은 프레임의 사망 판정에 들어간다.
        internal void TickProjectiles()
        {
            float now = Time.time, deltaTime = Time.deltaTime;
            // 궤적이 사라질 시간이 지난 탄은 궤적을 지우고 끈 뒤 풀로 돌려준다. 궤적은 탄이 계속 가진다. 발사 정보는 Release가 이미 비웠다.
            for (int f = _fadingProjectiles.Count - 1; f >= 0; f--)
            {
                Projectile p = _fadingProjectiles[f];
                if (now < p.Until) continue;
                if (p.Trail != null) p.Trail.Park();
                _projectilePool.Push(p); _fadingProjectiles.RemoveAt(f);
            }
            // 재생이 끝났거나 파괴된 효과와 섬광을 World에 반환한다. 반환 규칙은 CombatEffects가 맡는다.
            _effects.ReturnFinished();
            if (_weaponDefinition != null)
            {
                // 현재 조준 상태의 프로필로 산포를 상한 안에 두고 기본값 쪽으로 복구한다.
                // 조준 상태가 바뀌어 기본 산포보다 작아졌으면 원본처럼 여섯 배 빠르게 복구한다.
                ScatterProfile profile = _world.Motor.IsAiming ? _adsScatter : _hipScatter;
                _scatter = Mathf.Min(_scatter, profile.Maximum);
                _scatter = Mathf.MoveTowards(_scatter, profile.Minimum, profile.Recovery * deltaTime * (_scatter < profile.Minimum ? _fastRecoveryFactor : 1));
            }
            // 무기 표시 반동은 WeaponVisual이 원본 속도로 진행한다. 밀릴 때는 초당 20, 돌아올 때는 초당 8이다.
            if (_weaponVisual != null) _weaponVisual.Tick(deltaTime);
            // 꺼내기 시간이 지나면 숨긴 무기나 손 아이콘을 보이고 든 것의 손 상태를 보낸다. 입력이 막힌 동안에도 시간은 흐른다.
            // 원작 기본 Handheld 에이전트의 handAnimationType은 일반 손 자세다.
            if (_drawing && now >= _drawUntil)
            {
                _drawing = false;
                if (_weaponVisual != null) _weaponVisual.SetVisible(true);
                _skillItem.ShowHand();
                _world.Motor.Hands.SetHandState(_skillItem.Holding ? OriginalHandAnimation._normalHand : OriginalHandAnimation.StateOf(_weaponDefinition));
            }
            // 예고 효과는 재생이 끝나면 반환하고, 그 전에는 휘두르는 손을 따라간다. 근접 판정보다 먼저 진행한다.
            _effects.TickAlerts();
            TickMelee(now);
            // 스킬 조준 표시와 날아가는 수류탄도 탄과 같은 자리에서 진행해, 폭발 피해가 같은 프레임의 사망 판정에 들어간다.
            _skillItem.TickHud();
            TickGrenades(deltaTime);
            Vector3 player = _world.PlayerPosition;
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                // 발사 정보는 읽기 전용 구조체라 반복마다 한 번만 복사해 읽는다. Release가 비우기 전까지 값이 바뀌지 않는다.
                Projectile p = _projectiles[i]; OriginalShot shot = p.Shot;
                float length = Mathf.Min(shot.Range - p.Distance, shot.Speed * deltaTime);
                // 원본 UpdateMoveAndCheck처럼 날아가는 탄은 뒤로 당긴 캐스트 한 번으로 검사하고, 시작 겹침은 다시 보지 않는다.
                // 접촉 거리는 현재 위치부터 잰 값이라 그대로 비행 거리에 더한다. 소속은 이 월드의 캐시에서 찾는다.
                // 같은 팀 통과는 발사 순간 고정한 팀으로, 모래주머니 전부 통과는 발사 순간 고정한 치명 확률로 본다.
                if (OriginalBallistics.SweepFlight(p.Position, p.Direction, length, shot.Radius, shot.Shooter, shot.Player, shot.Team, _hits, _owners,
                    out OriginalBallistics.Contact selected, shot.PassesAllHalfCover))
                {
                    p.Distance += selected.Distance; Impact(p, selected);
                    _projectiles.RemoveAt(i); Release(p); continue;
                }
                p.Position += p.Direction * length; p.Distance += length;
                // 궤적 이동은 PooledTrail이 맡는다.
                if (p.Trail != null) p.Trail.MoveTo(p.Position);
                if (p.Distance >= shot.Range) { _projectiles.RemoveAt(i); Release(p); continue; }
                // 원본 UpdateFlyThroughSound처럼 아직 날아가는 적 탄이 플레이어 옆에 이르거나 지나친 프레임에 탄마다 한 번 스치는 소리를 낸다.
                // 높이는 보지 않고, 탄 진행 방향이 플레이어 쪽을 향하는 동안은 아직 다가오는 중이라 내지 않는다.
                if (!shot.Player && !p.FlyByPlayed && shot.Speed >= _flyByMinSpeed)
                {
                    Vector3 toPlayer = Horizontal(player - p.Position);
                    if (toPlayer.sqrMagnitude <= _flyByRadius * _flyByRadius && Vector3.Dot(toPlayer, p.Direction) <= 0f)
                    {
                        p.FlyByPlayed = true;
                        _world.Audio.PlayOneShot(_flyBySound, p.Position);
                    }
                }
            }
        }

        // 실제 충돌점 한 곳에서 피해, 명중음, 입자를 함께 낸다. 피해 숫자는 맞은 캐릭터의 피격 큐가 같은 지점에 띄운다.
        void Impact(Projectile p, OriginalBallistics.Contact hit)
        {
            // 소속은 Sweep이 거르며 캐시에서 찾은 값을 접촉으로 받는다. 적이 아닌 접촉에서만 플레이어 모터가 채워진다.
            OriginalEnemyController? enemy = hit.Enemy;
            OriginalPlayerMotor? motor = hit.Motor;
            // 발사 정보는 읽기 전용 구조체라 한 번만 복사해 읽는다.
            OriginalShot shot = p.Shot;
            // 원작 Projectile.cs:360-361처럼 탄은 맞힌 대상마다 같은 치명 확률을 넘기고, 받는 쪽이 치명을 뽑는다.
            // 그래서 머리 조준 탄은 사이에 낀 적과 드럼통도 치명으로 맞힌다. 받는 쪽이 없는 벽은 뽑지 않는다.
            bool crit = (enemy != null || motor != null || hit.Receiver != null) && RollCrit(shot.CritRate, hit.Receiver != null);
            OriginalHitResult result = shot.Hit(enemy, hit.Point, p.Distance, crit);
            // 원작 Projectile은 DamageReceiver를 맞히면 피해만 주고 벽 탄착 효과와 소리를 내지 않는다. 단순 체력 대상도 같다.
            if (hit.Receiver != null)
            {
                // 적 탄이면 쏜 적을 공격자로 넘겨, 부서진 드럼통의 연쇄 폭발도 그 적의 피해가 된다.
                // 원작 HealthSimpleBase에는 방어가 없어 탄 관통은 결과를 바꾸지 않으므로 넘기지 않는다.
                HurtReceiver(hit.Receiver, new OriginalDamageInfo(result.Damage, 0f, false, shot.Player, hit.Point, hit.Normal, shot.Attacker));
                if (p.Trail != null) p.Trail.AddPoint(hit.Point);
                return;
            }
            // 탄 필터가 플레이어 Collider는 적 탄에만 접촉으로 남긴다. 적 Collider는 플레이어 탄과, 원작 팀 규칙에 따라 다른 팀 적의 탄에 남는다.
            // 원본 Health.OnHurt처럼 적이 피해를 받아들였을 때만 명중음과 생체 피격 효과를 낸다. 피해 숫자는 적의 피격 큐가 한 번 띄운다.
            // 머리를 맞혀 처치하면 머리 명중음 대신 헤드샷 처치음을 한 번만 낸다.
            // 적 탄은 쏜 적을 공격자로, 피해 출처를 거짓으로 넘겨 적끼리의 처치가 플레이어 처치로 세어지지 않게 한다.
            // 치명이면 피해 실행이 머리 방어를 쓰고 숫자가 치명 모양이 된다. 피해 지점은 숫자가 뜰 자리다.
            // 관통은 원작 Projectile.cs:362처럼 맞힌 대상마다 발사 때 고정한 같은 값을 넘긴다.
            bool hurt = enemy != null && enemy.Hurt(result.Damage, shot.ArmorPiercing, shot.Player, crit: result.Head,
                attacker: shot.Attacker, point: hit.Point, normal: hit.Normal);
            // 명중 표시음은 플레이어 탄에서만 낸다. 접촉에 적이 있으면 모터는 비어 있어, 적끼리의 명중은 아래 플레이어 피해로 가지 않는다.
            // 적 탄의 치명도 플레이어 피해 실행이 머리 방어로 막고, 탄 관통은 그 방어에서 빠진다.
            if (hurt && shot.Player)
                _world.Audio.PlayOneShot(result.Head ? (enemy!.Dead ? _headKillSound : _headHitSound) : _hitMarkerSound, result.Point);
            else if (motor != null) DamagePlayer(result.Damage, hit.Point, hit.Normal, shot.ArmorPiercing, result.Head);
            // 벽 탄착과 생체 피격은 같은 충돌점에 내되, 원본처럼 서로 다른 효과 생성 규칙과 명중음을 쓴다.
            // 플레이어가 맞은 효과와 소리는 원본 HurtVisual처럼 피해가 받아들여졌을 때만 피격 큐 처리기가 낸다.
            // 적 생체 효과도 원본 HitFx처럼 Hurt가 피해를 적용했을 때만 낸다.
            if (enemy != null) { if (hurt) OrganicHit(hit.Point, hit.Normal, result.Head); }
            else if (motor == null)
            {
                _world.Audio.PlayOneShot(_solidHitSound, result.Point);
                // 원작 GameplayDataSettings.Prefabs의 bulletHitObsticleFx 자리인 벽 탄착 효과다. 앱 수명 게임플레이 데이터다.
                _effects.Emit(GameplayDataSettings.Prefabs.BulletHitObsticleFx, hit.Point, hit.Normal);
            }
            // 궤적은 이동 없이 실제 충돌점에서 끝난다.
            if (p.Trail != null) p.Trail.AddPoint(hit.Point);
        }
        // 새로 죽은 적에게만 사망 입자를 낸다. 저장에서 시체를 복원할 때는 재생하지 않는다.
        // 사망 효과 규칙은 앱 수명 게임플레이 데이터라 GameplayDataSettings.Prefabs에서 읽는다.
        internal void DeathEffect(Vector3 position) => _effects.Emit(GameplayDataSettings.Prefabs.OrganicDeath, position, Vector3.forward);

        // 든 무기 표시를 파괴한다. 섬광은 총구의 자식이라 총과 함께 파괴되므로, 그 총구의 섬광을 먼저 반환해 풀 보관 루트로 되돌린다.
        // 적 총은 사망 때 모델만 꺼지고, 씬 정리에서는 World 해제가 적 오브젝트 파괴보다 먼저 섬광을 거두므로 여기서 다루지 않는다.
        void DisposeWeaponVisual()
        {
            if (_weaponVisual == null) return;
            _effects.ReturnFlashesOn(_weaponVisual.Muzzle);
            _weaponVisual.Dispose();
            _weaponVisual = null;
        }

        // 탄 궤적을 빌려 발사 위치에 두고 활성화한다. 그 뒤 잔상 초기화, 방출, 켜고 끄기는 모두 템플릿의 PooledTrail이 한다.
        // 핸들은 보관하지 않는다. 남은 궤적은 World를 해제할 때 반환되거나 파괴된다.
        PooledTrail RentTrail(SpawnRule rule, Vector3 muzzle)
        {
            // 열린 월드가 저작한 탄 궤적 규칙으로 방금 빌린 핸들이라 대여와 활성화가 실패하지 않는다.
            // 제작 도구가 탄 궤적 템플릿의 TrailRenderer마다 PooledTrail을 붙인다.
            WorldObjectHandle handle = _world.Objects.Spawn(rule, new Pose(muzzle, Quaternion.identity))!;
            _world.Objects.Activate(handle);
            return handle.Instance!.GetComponent<PooledTrail>();
        }
        void Release(Projectile p)
        {
            // 실제 종점에서 잔상이 사라진 뒤에만 풀로 반환한다. 방출을 멈추고 남은 잔상 수명은 PooledTrail이 알려 준다.
            if (p.Trail != null) p.Until = Time.time + p.Trail.End();
            else p.Until = Time.time;
            p.Shot = default; _fadingProjectiles.Add(p);
        }

        // 호출자가 찾은 확정 그래프에서 아이템의 원본 정의를 읽는다. 호출자는 그 그래프의 인스턴스 ID만 넘기고, 그래프의 아이템은
        // 늘 카탈로그 정의가 있다. 저장 읽기 검사가 모르는 ID를 막으므로 조회 결과를 보지 않는 DefinitionOf로 읽는다.
        OriginalItemDefinition OwnedDefinition(ItemOwnershipState items, string instanceId) =>
            _world.Catalog.DefinitionOf(items.Find(instanceId)!.OriginalItemId);

        // 피해 명령은 체력만 바꾸고 스태미나는 건드리지 않는다. 피격 표시는 생존 수치 소유자가 피해를 받아들여 피해 효과를
        // 실행할 때 보내는 피격 큐가 한 번만 낸다. 그래서 맞은 지점은 효과 문맥에 담아 큐로 돌려받는다.
        // armorPiercing은 원작 DamageInfo.armorPiercing이라 폭발, 근접, 총탄이 모두 넘긴다. crit은 원작 DamageInfo.crit이고,
        // damage에는 호출자가 치명 배율을 이미 곱했다. 피해 실행이 치명이면 머리 방어를 쓴다.
        void DamagePlayer(float damage, Vector3 point, Vector3 normal, float armorPiercing = 0f, bool crit = false)
        {
            // 씬 정리 중이라 세션이 없으면 기록 없이 피해를 확정하지 않는다. 체력 0 처리는 세션의 현재 장소가 출격 맵인지로 정하고,
            // 난이도 배율은 GameManager가 내주는 난이도 소유자의 현재 값으로 곱하며, 세션을 찾았으면 앱이 살아 있다.
            if (!_world.TrySession(out GameSessionManager? session)) return;
            _world.Runtime.Vitals.TryDamage(DamageRules.PlayerDamage(damage, GameManager.Difficulty.Current), session.Snapshot().SceneId,
                point, normal, armorPiercing, crit);
        }

        // 원작 Health.cs:358과 HealthSimpleBase.cs:39처럼 맞는 대상마다 전역 UnityEngine.Random을 한 번 뽑는다.
        // 적 AI 결정의 시드 난수와 출격 시드를 건드리지 않고 사격 산포와 같은 흐름을 쓴다. 단순 체력 대상은 확률과 같은 난수도 치명이다.
        static bool RollCrit(float rate, bool simpleHealth)
        {
            float roll = UnityEngine.Random.Range(0f, 1f);
            return simpleHealth ? DamageRules.RollsSimpleCrit(rate, roll) : DamageRules.RollsCrit(rate, roll);
        }

        // 플레이어 피격 큐를 받아 원본 OnHurtEvent처럼 피격 표시를 내고, 원작 Health.Hurt처럼 같은 지점에 최종 피해 숫자를 띄운다.
        // 맞은 지점이 없는 기아 같은 피해는 원본 DamageAction처럼 캐릭터 위 0.8을 쓴다. 큐 값은 체력 바닥 규칙 전의 피해다.
        // 큐는 생존 사본 확정보다 먼저 오므로 Vitals.Current를 읽지 않는다.
        void OnPlayerCue(AbilitySystem system, GameplayCueParameters cue)
        {
            if (cue.Tag != CharacterTags.CueHurt || cue.Event != GameplayCueEvent.Executed) return;
            GameplayEffectContext context = cue.Context;
            Vector3 point = context.HasHit ? context.HitPoint : _world.PlayerPosition + Vector3.up * _hurtFallbackHeight;
            PlayHurtFeedback(point, context.HasHit ? context.HitNormal : Vector3.up);
            if (cue.Magnitude > 0f) _world.View.Damage(point, cue.Magnitude, context.HasFlag(HurtExecution.CritFlag), _world.Camera);
        }

        // 피격 표시를 생성자가 확보한 플레이어 HurtVisual에 넘긴다. 감쇠 시간은 적과 같은 원본 값이고, 원본처럼 카메라는 흔들지 않는다.
        // 원본은 적 피해, 음수 회복, 굶주림 같은 모든 Health.Hurt가 같은 OnHurtEvent로 이 표시와 피해 지점의 HitFx(효과와 피격음)를 낸다.
        // 피해와 아이템 사용은 플레이어가 살아 있는 월드 프레임에만 진행되므로 표시는 늘 있다.
        void PlayHurtFeedback(Vector3 point, Vector3 normal)
        {
            _world.View.PlayerHit();
            _playerHurt.Flash();
            OrganicHit(point, normal, false);
        }

        // 원본 HitFx처럼 생체 피격 효과와 피격음을 한 지점에 낸다. 오디오가 꺼져 있으면 관리자가 재생을 무시한다.
        // 생체 피격 효과 규칙은 앱 수명 게임플레이 데이터라 GameplayDataSettings.Prefabs에서 읽는다.
        void OrganicHit(Vector3 point, Vector3 normal, bool crit)
        {
            _world.Audio.PlayOneShot(crit ? _organicCritSound : _organicHitSound, point);
            _effects.Emit(GameplayDataSettings.Prefabs.OrganicImpact, point, normal);
        }

        // 근접 판정과 스치는 소리 판정은 원본처럼 높이를 보지 않는다.
        static Vector3 Horizontal(Vector3 value) => new(value.x, 0f, value.z);

        // 무기 정의의 FMOD 경로 묶음을 캐시에서 찾고, 처음 쓰는 정의면 한 번 만들어 넣는다.
        WeaponSounds Sounds(OriginalItemDefinition weapon)
        {
            if (_weaponSounds.TryGetValue(weapon, out WeaponSounds sounds)) return sounds;
            sounds = new WeaponSounds(weapon);
            _weaponSounds.Add(weapon, sounds);
            return sounds;
        }

        // 무기 정의 하나의 FMOD 발사, 장전 시작, 장전 끝 경로다. 원본 키가 비어 있으면 null이고, 호출자는 이때 소리를 내지 않는다.
        readonly struct WeaponSounds
        {
            internal WeaponSounds(OriginalItemDefinition weapon)
            {
                Shoot = weapon.ShootKey.Length > 0 ? "event:/SFX/Combat/Gun/Shoot/" + weapon.ShootKey.ToLowerInvariant() : null;
                bool reload = weapon.ReloadKey.Length > 0;
                string key = weapon.ReloadKey.ToLowerInvariant();
                ReloadStart = reload ? "event:/SFX/Combat/Gun/Reload/" + key + "_start" : null;
                ReloadEnd = reload ? "event:/SFX/Combat/Gun/Reload/" + key + "_end" : null;
            }
            internal string? Shoot { get; }
            internal string? ReloadStart { get; }
            internal string? ReloadEnd { get; }
        }

        // 비조준이나 ADS 중 한 조준 상태의 원본 산포 수치다. 기본값, 최대값, 증가량에는 ScatterFactor를 곱한다.
        // 초당 복구량은 ScatterRecover × ScatterGrow × ScatterFactor × ShootSpeed다.
        readonly struct ScatterProfile
        {
            // 키 문자열을 매번 이어 붙이지 않도록 프로필마다 상수 능력치 키를 넘긴다.
            internal static ScatterProfile Hip(OriginalItemDefinition weapon) =>
                new(weapon, "ScatterFactor", "DefaultScatter", "MaxScatter", "ScatterGrow", "ScatterRecover");
            internal static ScatterProfile Ads(OriginalItemDefinition weapon) =>
                new(weapon, "ScatterFactorADS", "DefaultScatterADS", "MaxScatterADS", "ScatterGrowADS", "ScatterRecoverADS");

            ScatterProfile(OriginalItemDefinition weapon, string factorKey, string defaultKey, string maxKey, string growKey, string recoverKey)
            {
                float factor = weapon.Stat(factorKey, 1);
                float grow = weapon.Stat(growKey);
                Minimum = weapon.Stat(defaultKey) * factor;
                Maximum = weapon.Stat(maxKey) * factor;
                Growth = grow * factor;
                Recovery = weapon.Stat(recoverKey) * grow * factor * weapon.Stat(OriginalItemDefinition._shootSpeedStat);
            }
            internal float Minimum { get; }
            internal float Maximum { get; }
            internal float Growth { get; }
            internal float Recovery { get; }
        }

        // 날아가는 탄의 가변 상태다. 필드 이름 규칙에 맞추려고 자동 속성으로 둔다.
        // Trail은 World에서 빌린 궤적이다. 탄을 재사용하는 동안 계속 가지며, 객체는 World가 소유하고 수명 처리는 PooledTrail이 한다.
        sealed class Projectile
        {
            internal OriginalShot Shot { get; set; }
            internal Vector3 Position { get; set; }
            internal Vector3 Direction { get; set; }
            internal float Distance { get; set; }
            internal float Until { get; set; }
            internal PooledTrail? Trail { get; set; }
            // 이 탄이 플레이어 옆을 스치는 소리를 이미 냈는지다.
            internal bool FlyByPlayed { get; set; }
        }

        // 휘두른 뒤 판정을 기다리는 근접 공격이다. Player면 플레이어 공격이고 Attacker는 비어 있다.
        // 적 Attacker는 Unity null 비교로 파괴 여부를 함께 보므로, 플레이어 여부는 참조가 비었는지로 본다. 피해 배율은 판정 순간 공격자의 어트리뷰트에서 읽는다.
        readonly struct PendingMelee
        {
            internal PendingMelee(OriginalEnemyController? attacker, OriginalItemDefinition weapon, float at)
            {
                Attacker = attacker; Weapon = weapon; At = at;
            }
            internal bool Player => ReferenceEquals(Attacker, null);
            internal OriginalEnemyController? Attacker { get; }
            internal OriginalItemDefinition Weapon { get; }
            internal float At { get; }
        }

        // 날아가는 수류탄 하나다. Life와 Delay는 원작 Grenade의 lifeTimer와 delayTimer이고, 투사체는 World가 소유한다.
        sealed class ThrownGrenade
        {
            internal ThrownGrenade(WorldObjectHandle handle, OriginalGrenade body, OriginalItemSkill skill)
            {
                Handle = handle; Body = body; Skill = skill;
            }
            internal WorldObjectHandle Handle { get; }
            internal OriginalGrenade Body { get; }
            internal OriginalItemSkill Skill { get; }
            internal float Life { get; set; }
            internal float Delay { get; set; }
        }
    }
}
