#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Game.Core;
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원작 Health와 캐릭터 생존 수치처럼 플레이어의 체력, 에너지, 수분을 소유한다. GameManager 프리팹 자식의 컴포넌트이고
    // GameRuntime.Vitals로 찾는다. 저장할 때 플레이어 구역을 써 넣고, 기록을 불러오면 읽어 간다. 메인 스레드에서만 쓴다.
    // 값은 플레이어의 GAS 시스템(AbilitySystem) 어트리뷰트가 갖고, 변경은 GAS 효과로 한다. 씬보다 오래 사는 GAS 소유자(Owner)이고,
    // 씬마다 만드는 플레이어 캐릭터는 아바타(Avatar)로 연결했다가 씬을 떠날 때 뗀다. 연결은 저장 값을 바꾸지 않는다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed class PlayerVitals : SessionSectionOwner
    {
        // 값이 바뀌었다는 신호만 보낸다. 구독자는 Current를 직접 읽는다.
        public static event Action? Changed;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => Changed = null;

        // 피해, 생존 틱, 아이템 사용, 최대 체력 채우기에 거는 GAS 효과와 아바타에 주는 행동 어빌리티 묶음이다.
        // GameManager 프리팹에서 연결한다. 기본 버프와 갈증, 기아 버프는 앱 수명 설정인 GameplayDataSettings.Buffs가 갖는다.
        [SerializeField] CharacterAbilitySet? _abilitySet;

        // 처음 쓸 때 만든다. 필드 초기화는 직렬화 스레드에서 돌 수 있고, 테스트는 Awake 없는 비활성 호스트를 쓰기 때문이다.
        AbilitySystem? _abilities;
        // 저장과 HUD가 읽는 확정 사본이다. 명령이나 생존 프레임, Runtime 전환이 값을 바꾼 뒤 어트리뷰트 Base에서 만들고,
        // 체력과 에너지, 수분이 바뀔 때만 새로 만든다. 효과 실행 중에 나가는 GAS 알림은 이 확정보다 먼저 와서 확정 전 사본을 보므로,
        // 그 처리기는 사본 대신 어트리뷰트나 태그를 읽는다. null은 프롤로그가 아직 플레이어를 만들지 않았다는 저장 값이다.
        // 불변 값이라 복사하지 않는다.
        PlayerState? _player;
        // 마지막으로 스탯을 채운 원본 플레이어 정의다. 카탈로그는 씬마다 다시 로드되므로 정의가 바뀔 때만 다시 채운다.
        OriginalItemDefinition? _seededFrom;
        // 지금 연결된 플레이어 캐릭터 오브젝트다. 씬 정리 순서에 따라 이미 파괴됐을 수 있어 연결 여부는 참조로 본다.
        GameObject? _avatar;
        // 아바타를 연결할 때 잡아 두는 생존 버프 구역이다. 생존 프레임과 해제가 정적 구역을 다시 읽지 않고 붙일 때 쓴 참조로 뗀다.
        // null이면 연결된 아바타가 없어 생존 프레임이 아무것도 하지 않는다.
        GameplayDataSettings.BuffsData? _buffs;
        // 이 아바타 동안만 붙는 장비, 재능, 폭풍 효과의 기록이다. 아바타 연결 확인은 이 소유자가 하고 기록과 떼기는 그쪽이 맡는다.
        readonly PlayerAvatarEffects _avatarEffects = new();
        // 아바타와 상관없이 Base에 더해 둔 특성 스탯 합이다. 정의로 Base를 다시 채우면 이 값을 다시 더한다.
        readonly PlayerPerkStats _perkStats = new();

        // HUD, 모터, 사망 판정이 읽는 현재 생존 수치다. 읽는 쪽은 GameRuntime.HasSession으로 세션이 있는지 먼저 본다.
        public PlayerState? Current => _player;

        // 리그와 모터, 전투 시뮬레이션이 같은 플레이어 시스템의 어트리뷰트와 큐를 읽도록 어셈블리 안에 연다.
        // 처음 읽을 때 적과 같은 캐릭터 시스템 생성 규칙으로 만든다.
        internal AbilitySystem Abilities => _abilities ??= CharacterAttributeSet.CreateSystem(this);

        // 모터, 전투, 상호작용이 자기 행동 어빌리티를 이 묶음에서 꺼낸다. 칸 연결은 GameManager 프리팹 테스트가 확인한다.
        internal CharacterAbilitySet AbilitySet => _abilitySet!;

        // 기반 클래스가 저장 구독을 끊은 뒤 GAS 시스템을 놓는다. 한 번도 쓰지 않았으면 만들지 않는다.
        protected override void OnDestroy()
        {
            base.OnDestroy();
            _abilities?.Release();
        }

        // 저장마다 플레이어 구역을 한 번 쓴다.
        private protected override void WriteSection(GameSaveData data) => data.SetPlayer(_player);

        // 불러온 기록의 플레이어로 바꾼 뒤 알린다. 기록은 코덱이 검증한 값이라 다시 검사하지 않는다.
        // 메뉴에서도 불리므로 카탈로그 없이 값만 넣고, 최대치로 자르지 않는다.
        private protected override void ReadSection(GameSaveData data)
        {
            Load(data.Player);
            ObserverDispatch.InvokeEach(Changed);
        }

        // 원본 Prologue_Main의 LevelConfig saveCharacter 1처럼 새 게임 플레이어 상태를 한 번 만들고, 있으면 그대로 둔다.
        // 체력과 스태미나는 최대, 에너지와 수분은 50이다. 프롤로그에서 회복한 값은 튜토리얼로 이어지고,
        // 월드 첫 초기화 GameplayCommands.TryInitializeWorld도 이 명령을 먼저 불러 있는 플레이어를 그대로 쓴다.
        public bool TryEnsurePlayer(OriginalGameplayCatalog catalog)
        {
            if (!AcceptsCommands) return false;
            // 계산은 튜토리얼 재시작과 같은 OriginalPlayer가 하고, 플레이어가 이미 있으면 바꾸지 않는다.
            PlayerState? before = _player;
            if (before != null) return true;
            Load(OriginalPlayer(before, catalog));
            // 확정은 실패하지 않으므로 여기까지 오면 명령은 성공이다.
            Commit(before);
            return true;
        }

        // 원본 CharacterMainControl.TickVariables의 1초 틱 한 번이다. 계산은 생존 틱 효과(SurvivalTickExecution)가 하고,
        // scene이 출격 맵인지로 회복과 소모를 고른다. 플레이어가 없거나 값이 그대로면 알림 없이 성공한다.
        public bool TryTickSurvival(OriginalGameplayCatalog catalog, GameScene scene)
        {
            if (!AcceptsCommands) return false;
            PlayerState? before = _player;
            if (before == null) return true;
            // 원본 플레이어 정의는 씬 카탈로그에 늘 저작돼 있고 씬 데이터 테스트가 확인한다.
            EnsureStats(catalog.PlayerCharacter);
            // 출격 맵 판정은 피해 규칙과 같은 DamageRules.IsRaidMap을 함께 쓴다.
            ApplyEffect(new GameplayEffectSpec(_abilitySet!.SurvivalTick!, Abilities)
                .WithSetByCaller(CharacterTags.DataRaidMap, DamageRules.IsRaidMap(scene) ? 1f : 0f));
            // 확정은 실패하지 않으므로 여기까지 오면 명령은 성공이다.
            Commit(before);
            return true;
        }

        // 원본 UpdateThirstyAndStarve처럼 수분이 0이면 갈증, 에너지가 0이면 기아 버프를 건다. 기아 버프는 원작 1032_Buff_Starve처럼
        // 이어 거는 효과로 게임 시간 2초마다 피해 1을 주고, 원본 Health.Hurt처럼 사망 뒤에는 State.Dead 조건으로 멈춘다.
        // 생존 처리가 1초 틱 뒤 게임플레이 프레임마다 부른다. 효과는 상태가 바뀐 프레임에만 붙이고 떼므로, 나머지 프레임은 할당 없이 지나간다.
        internal void TickSurvivalStatus(GameScene scene, DifficultyState? difficulty, float deltaTime)
        {
            GameplayDataSettings.BuffsData? buffs = _buffs;
            PlayerState? before = _player;
            // 아바타가 연결되지 않았거나 프롤로그가 아직 플레이어를 만들지 않았으면 아무것도 하지 않는다.
            // 그래서 해제 뒤에 온 프레임도 효과를 다시 붙이지 않는다.
            if (buffs == null || before == null) return;
            SetEffectActive(buffs.Thirsty!, before.Hydration <= 0f);
            SetStarveActive(buffs.Starve!, before.Energy <= 0f, difficulty);
            // 피해 사망 규칙은 TryDamage와 같은 scene 기준이다. 주기 피해는 생존 시계를 진행할 때 실행된다.
            SetCannotDie(!DamageRules.IsRaidMap(scene));
            Abilities.TickClock(CharacterTags.ClockStatus, deltaTime);
            Commit(before);
        }

        // 원작 StormWeather가 주인공에게 폭풍 피해 버프(1111·1112)를 거는 자리다. 원작처럼 StormProtection이 기준보다 크면 면제하고, 같으면 받는다.
        // 버프는 Clock.Status 주기 효과라 피해는 생존 프레임(TickSurvivalStatus)이 사망 규칙을 맞춘 뒤 진행한다. 난이도 배율은 기아 버프와 같은
        // 출처를 Data.DamageFactor로 넘긴다. 다시 걸면 GAS가 처음 값을 그대로 쓰는데, 월드가 연결된 동안 난이도는 바뀌지 않는다.
        // 관문이 닫혔거나, 아바타나 플레이어가 없거나, 이미 죽었으면 기록 없이 거절한다. 버프는 체력을 바로 바꾸지 않아 확정하지 않는다.
        internal bool TryApplyStormBuff(GameplayEffectDefinition buff, float protectionThreshold, DifficultyState? difficulty)
        {
            if (!AcceptsCommands || ReferenceEquals(_avatar, null) || _player == null) return false;
            AbilitySystem abilities = Abilities;
            if (abilities.GetCurrent(CharacterAttributes.StormProtection) > protectionThreshold) return false;
            if (abilities.Tags.HasTagExact(CharacterTags.Dead)) return false;
            // 붙이기 전에 기록해, 붙이기가 Editor에서 GAS 결함 보고로 멈춰도 아바타 해제가 이 효과를 뗄 수 있게 한다.
            _avatarEffects.TrackStorm(buff);
            ApplyEffect(new GameplayEffectSpec(buff, abilities)
                .WithSetByCaller(CharacterTags.DataDamageFactor, DamageRules.PlayerDamageFactor(difficulty)));
            return true;
        }

        // 원본 Health.Hurt의 사망 규칙으로 체력만 줄인다. damage는 호출자가 난이도 배율을 곱한 값이라 피해 효과에 배율을 넘기지 않는다.
        // 스태미나는 원본 CharacterMainControl.currentStamina처럼 저장하지 않는 월드 값이라 저장 필드는 그대로 둔다.
        // 맞은 지점은 피격 큐로 전해져 큐를 받는 쪽이 그 자리에 연출을 낸다. 지점이 없으면 받는 쪽이 캐릭터 가슴 높이를 쓴다.
        // GAS 문맥 형식 대신 Vector3를 받아, GAS를 참조하지 않는 호출자도 같은 명령 하나를 쓴다.
        // armorPiercing은 원작 DamageInfo.armorPiercing이다. 폭발과 근접 무기가 넘기고, 0이면 피해 효과에 넣지 않는다.
        // crit은 원작 DamageInfo.crit > 0이다. 피해 실행이 머리 방어를 쓰고 피해 숫자가 치명 모양이 된다. 적 탄과 근접이 맞을 때마다 뽑아 넘긴다.
        public bool TryDamage(float damage, GameScene scene, Vector3? hitPoint = null, Vector3 hitNormal = default, float armorPiercing = 0f,
            bool crit = false)
        {
            if (!AcceptsCommands) return false;
            PlayerState? before = _player;
            // 저장이나 사용자 지정 난이도의 배율은 유한값 검사만 받아, 큰 배율을 곱한 피해량은 무한대로 넘칠 수 있다.
            // 그런 피해는 기록 없이 거절한다.
            if (float.IsInfinity(damage)) return false;
            // 사망 규칙은 scene 기준이다. 비출격 맵이면 State.CannotDie를 달아 체력 1에서 멈추게 한다.
            SetCannotDie(!DamageRules.IsRaidMap(scene));
            // 치명 비트는 맞은 지점이 없는 피해에도 실어, 지점 유무와 관계없이 피해 실행과 피격 큐가 같은 값을 본다.
            int flags = crit ? HurtExecution.CritFlag : 0;
            GameplayEffectContext context = hitPoint.HasValue
                ? GameplayEffectContext.Hit(hitPoint.Value, hitNormal, flags)
                : new GameplayEffectContext(null, false, default, default, flags);
            // 피해 효과는 조건 없는 즉시 효과라 결과를 보지 않는 효과 적용으로 건다.
            GameplayEffectSpec spec = new GameplayEffectSpec(_abilitySet!.Hurt!, Abilities).WithSetByCaller(CharacterTags.DataDamage, damage).WithContext(context);
            // 관통이 있는 피해만 값을 더한다. 관통이 없으면 피해 실행이 기본 0으로 읽는다.
            if (armorPiercing != 0f) spec = spec.WithSetByCaller(CharacterTags.DataArmorPiercing, armorPiercing);
            ApplyEffect(spec);
            // 확정은 실패하지 않으므로 여기까지 오면 명령은 성공이다.
            Commit(before);
            return true;
        }

        // 원본 LevelManager.InitLevel의 진입 회복이다. 원본은 IsBaseLevel || isNewRaidLevel이면 AddHealth(MaxHealth)를 부른다.
        // 그래서 기지와, 늘 새 출격인 GroundZero에 들어오면 체력을 최대로 채운다. 튜토리얼 재시작 회복은 ApplyTutorialRestart가 맡는다.
        // 월드 첫 초기화가 명령 관문을 확인하고 플레이어를 만든 같은 프레임에 한 번 부르므로 거절하지 않는다. 다른 장소이거나 이미 최대면 바꾸지 않는다.
        public void RestoreHealthOnLevelEntry(OriginalGameplayCatalog catalog, GameScene scene)
        {
            if (scene != GameScene.BaseCamp && scene != GameScene.ZeroZone) return;
            PlayerState? before = _player;
            // 원본 플레이어 정의는 씬 카탈로그에 늘 저작돼 있고 씬 데이터 테스트가 확인한다.
            RestoreFullHealth(catalog.PlayerCharacter);
            Commit(before);
        }

        // 아이템 사용이 소지품을 줄이기 전에 확인하는 조건이다. 관문이 닫혔거나 플레이어나 원본 플레이어 정의가 없으면
        // 기록 없이 거절한다.
        internal bool CanApplyItemEffect(OriginalGameplayCatalog catalog) => TryItemEffectTarget(catalog, out _, out _);

        // 원본 UsageUtilities.Use의 효과를 적용한다. 계산은 아이템 사용 효과(ItemUseExecution)가 한다. difficulty는 난이도 소유자의
        // 현재 값이고, scene은 음수 회복의 사망 규칙을 정한다. 값이 그대로면 알리지 않고, 대상은 호출 쪽이 먼저 확인하므로 거절하지 않는다.
        internal void ApplyItemEffect(OriginalItemDefinition effect, OriginalGameplayCatalog catalog, DifficultyState? difficulty,
            GameScene scene)
        {
            // 호출 쪽이 같은 호출에서 CanApplyItemEffect로 대상을 확인했으므로 조회 결과를 보지 않는다.
            TryItemEffectTarget(catalog, out PlayerState? before, out OriginalItemDefinition? character);
            EnsureStats(character!);
            SetCannotDie(!DamageRules.IsRaidMap(scene));
            // 아이템 사용 효과는 조건 없는 즉시 효과라 결과를 보지 않는 효과 적용으로 건다.
            ApplyEffect(new GameplayEffectSpec(_abilitySet!.ItemUse!, Abilities)
                .WithSetByCaller(CharacterTags.DataHeal, effect.Heal)
                .WithSetByCaller(CharacterTags.DataEnergy, effect.Energy)
                .WithSetByCaller(CharacterTags.DataWater, effect.Water)
                .WithSetByCaller(CharacterTags.DataDamageFactor, DamageRules.PlayerDamageFactor(difficulty)));
            Commit(before);
        }

        // 원본 튜토리얼은 사망하면 출격이 끝나고, 다시 들어올 때 새 출격으로 체력을 채운다. 플레이어가 없으면 새 게임 값으로
        // 만든 뒤 체력을 최대로 한다. 명령 관문을 보지 않는 Runtime 전환이라 거절하지 않고, 두 단계를 마친 뒤 한 번만 알린다.
        internal void ApplyTutorialRestart(OriginalGameplayCatalog catalog)
        {
            PlayerState? before = _player;
            // 원본 플레이어 정의 조회는 늘 있는 저작 정의만 쓴다.
            PlayerState ensured = OriginalPlayer(before, catalog);
            OriginalItemDefinition character = catalog.PlayerCharacter;
            Load(ensured);
            RestoreFullHealth(character);
            Commit(before);
        }

        // 출격 사망 같은 월드 종료 결과를 적용하는 Runtime 전환이라 명령 관문을 보지 않는다. 효과가 아니라 값을 그대로 넣는다.
        // 종료 준비가 만든 값만 오고, 호출 쪽이 null이 아닐 때만 부른다.
        internal void Apply(PlayerState player)
        {
            PlayerState? before = _player;
            Load(player);
            Commit(before);
        }

        // 원작 SetItem처럼 씬에 나타난 플레이어 캐릭터를 이 시스템의 아바타로 연결하고 정의의 스탯을 채운다.
        // 새 아바타는 스태미나가 최대로 시작하고 기본 버프와 행동 어빌리티를 받는다. 갈증과 기아 효과는 다음 생존 프레임에 붙어,
        // 첫 프레임은 보정 없는 이동으로 시작한다.
        internal AbilitySystem BindAvatar(GameObject avatar, OriginalItemDefinition character)
        {
            AbilitySystem abilities = Abilities;
            // 정리되지 못한 다른 아바타가 남았으면 그 효과부터 떼어, 새 아바타가 남은 보정과 주기 누적 없이 시작하게 한다.
            if (!ReferenceEquals(_avatar, null) && !ReferenceEquals(_avatar, avatar)) ReleaseAvatar();
            // 생존 버프 구역은 연결마다 한 번 읽어 두고, 생존 프레임과 해제는 이 참조만 쓴다.
            // 버프 연결은 앱 시작 검증이 확인한다.
            GameplayDataSettings.BuffsData buffs = GameplayDataSettings.Buffs;
            _buffs = buffs;
            EnsureStats(character);
            abilities.SetBaseValue(CharacterAttributes.CurrentStamina, abilities.GetCurrent(CharacterAttributes.Stamina));
            CharacterAbilitySet set = _abilitySet!;
            // 원작 1051_Buff_Base다. 같은 아바타를 다시 연결하면 붙어 있는 버프를 그대로 둔다.
            GameplayEffectDefinition baseBuff = buffs.BaseBuff!;
            if (!abilities.HasEffect(baseBuff)) ApplyEffect(new GameplayEffectSpec(baseBuff, abilities));
            abilities.SetAvatar(avatar);
            // 이 아바타의 모터, 전투, 상호작용이 시작하는 행동이다. 이미 받은 어빌리티는 그대로 둔다.
            abilities.GiveAbility(set.UseItem!);
            abilities.GiveAbility(set.Reload!);
            abilities.GiveAbility(set.Interact!);
            abilities.GiveAbility(set.Dash!);
            // 원작 CA_Skill이다. 손에 든 수류탄의 조준을 전투 쪽 스킬 행동이 시작한다.
            abilities.GiveAbility(set.Skill!);
            _avatar = avatar;
            return abilities;
        }

        // 씬을 떠나는 아바타의 연결을 끊고 아바타가 받은 효과와 어빌리티를 뗀다. 연결된 아바타가 아니면 아무것도 하지 않아 두 번 불러도 된다.
        // 생존 값과 저장 스태미나는 건드리지 않으므로 저장과 변경 알림에 영향이 없다.
        internal void UnbindAvatar(GameObject avatar)
        {
            // 정리 순서상 두 오브젝트가 모두 파괴됐을 수 있어 Unity 비교 대신 참조로 같은 아바타인지 본다.
            // 한 번도 연결하지 않았으면 아바타 참조도 비어 있어 시스템을 만들지 않고 돌아간다.
            if (ReferenceEquals(_avatar, null) || !ReferenceEquals(_avatar, avatar)) return;
            ReleaseAvatar();
        }

        // 원작 장비의 Character 대상 modifier처럼 플레이어 루트 칸에 꽂힌 아이템마다 그 장비 효과를 붙여 둔다. 확정 알림마다 불러도
        // 차이만 붙이고 떼며, 붙이거나 뗀 효과가 있으면 true다. 아바타가 없으면 해제 뒤 늦게 온 알림이 효과를 다시 붙이지 않게 아무것도 하지 않는다.
        // 아바타가 있으면 연결이 시스템을 이미 만들었으므로 시스템을 먼저 넘겨도 새로 만들지 않는다.
        internal bool SyncEquipmentEffects(ItemOwnershipState? items, OriginalGameplayCatalog catalog) =>
            !ReferenceEquals(_avatar, null) && _avatarEffects.SyncEquipment(Abilities, items, catalog);

        // 원작 PerkBehaviour의 레벨 초기화와 해금 상태 변경 때 스탯 적용처럼, 특성 소유자의 스탯 합을 Base에 맞춘다. Base가 바뀐 키가 있으면
        // true이고, 호출자는 그때 HUD 최대치를 다시 읽는다(PlayerStatusHud.RefreshHudMaxima). 호출자: 월드 첫 초기화와 PerkTreeManager.Changed.
        // Base 쓰기를 넘겨, 바뀐 키가 있을 때만 GAS 시스템을 읽는다.
        internal bool SyncPerkStats(PerkTreeManager perks) => _perkStats.Sync(perks.StatBaseDeltas, SetPerkStatBase);

        // 원작 EndowmentEntry.Activate의 캐릭터 수정자처럼 고른 재능의 효과를 아바타에 붙여 둔다. 효과가 바뀌었을 때만 true이고,
        // 호출자는 그때 HUD 최대치를 다시 읽는다. 아바타가 없으면 해제 뒤 늦게 온 알림이 효과를 다시 붙이지 않게 아무것도 하지 않는다.
        // 호출자: 월드 첫 초기화와 EndowmentManager.Changed(OriginalGameplayController).
        internal bool SyncEndowmentEffect(GameplayEffectDefinition? effect) =>
            !ReferenceEquals(_avatar, null) && _avatarEffects.SyncEndowment(Abilities, effect);

        // 아이템 효과의 확인과 적용이 같은 순서로 관문, 플레이어, 원본 플레이어 정의를 본다.
        // 회복 상한은 그 정의의 최대치다.
        bool TryItemEffectTarget(OriginalGameplayCatalog catalog, [NotNullWhen(true)] out PlayerState? before,
            [NotNullWhen(true)] out OriginalItemDefinition? character)
        {
            before = null;
            character = null;
            if (!AcceptsCommands) return false;
            before = _player;
            // 플레이어 상태가 없으면 기록 없이 거절한다.
            if (before == null) return false;
            // 원본 플레이어 정의는 씬 카탈로그에 늘 저작돼 있다. out 값의 null 계약을 맞추려고 조회 결과를 그대로 돌려준다.
            return catalog.TryGetPlayerCharacter(out character);
        }

        // 프롤로그 플레이어 생성과 튜토리얼 재시작이 같은 새 게임 플레이어를 만들도록 계산만 나눈다.
        // 플레이어가 있으면 같은 값을 돌려준다.
        PlayerState OriginalPlayer(PlayerState? before, OriginalGameplayCatalog catalog)
        {
            if (before != null) return before;
            // 원본 플레이어 정의는 카탈로그에 늘 있고, 그 최대 체력과 스태미나는 테스트가 확인하는 빌드 데이터라 검증 없이 만든다.
            return OriginalCharacter.NewOriginalPlayer(catalog.PlayerCharacter);
        }

        // 원본 AddHealth(MaxHealth)처럼 체력을 원본 최대 체력으로 채운다. 진입 회복과 튜토리얼 재시작이 함께 부른다.
        void RestoreFullHealth(OriginalItemDefinition character)
        {
            EnsureStats(character);
            ApplyEffect(new GameplayEffectSpec(_abilitySet!.RestoreFullHealth!, Abilities));
        }

        // 원작 SetItem처럼 원본 플레이어 정의의 스탯으로 Base를 채운다. 카탈로그를 받는 명령마다 부르고, 같은 정의면 건너뛴다.
        void EnsureStats(OriginalItemDefinition character)
        {
            if (ReferenceEquals(_seededFrom, character)) return;
            CharacterAttributeSet.SeedStats(Abilities, character);
            _seededFrom = character;
            // 정의로 채우면 Base가 정의 값으로 돌아가므로, 새 정의를 기준으로 더해 둔 특성 스탯을 다시 더한다.
            _perkStats.Reapply(SetPerkStatBase);
        }

        // 스탯 키의 Base를 정의 값 + delta로 쓴다. 정의를 아직 받지 않았으면 SeedDefaults와 같은 대체값을 정의 값으로 본다.
        // 특성 가져오기 도구가 캐릭터 스탯에 없는 키에서 멈추고 가져오기 테스트가 확인하므로 키는 늘 찾는다. 못 찾으면 쓸 어트리뷰트가 없어 건너뛴다.
        void SetPerkStatBase(string key, float delta)
        {
            IReadOnlyList<(GameplayAttribute Attribute, float Default)> stats = CharacterAttributes.Stats;
            for (int i = 0; i < stats.Count; i++)
            {
                GameplayAttribute attribute = stats[i].Attribute;
                if (attribute.Key != key) continue;
                float seeded = _seededFrom != null ? _seededFrom.Stat(key, stats[i].Default) : stats[i].Default;
                Abilities.SetBaseValue(attribute, seeded + delta);
                return;
            }
        }

        // 확정 사본의 값을 어트리뷰트에 넣는다. 저장 기록, 새 플레이어, 월드 종료 결과처럼 효과가 아닌 값 대입이다.
        void Load(PlayerState? player)
        {
            _player = player;
            if (player == null) return;
            AbilitySystem abilities = Abilities;
            abilities.SetBaseValue(CharacterAttributes.CurrentHealth, player.Health);
            abilities.SetBaseValue(CharacterAttributes.CurrentEnergy, player.Energy);
            abilities.SetBaseValue(CharacterAttributes.CurrentWater, player.Hydration);
        }

        // 생존 명령의 즉시 효과는 태그 조건이 없어 늘 실행된다. 아바타와 생존 상태의 지속 효과는 적용 조건이 없고
        // 없을 때만 걸어 늘 새로 붙는다. 그래서 결과를 보지 않는다. 장비와 재능 효과는 PlayerAvatarEffects가 같은 이유로 바로 건다.
        void ApplyEffect(in GameplayEffectSpec spec)
        {
            Abilities.ApplyEffect(spec);
        }

        // 상태가 바뀐 프레임에만 붙이거나 뗀다. 매 프레임 다시 걸면 효과 알림이 되풀이된다.
        void SetEffectActive(GameplayEffectDefinition definition, bool active)
        {
            AbilitySystem abilities = Abilities;
            if (active == abilities.HasEffect(definition)) return;
            if (active) ApplyEffect(new GameplayEffectSpec(definition, abilities));
            else abilities.RemoveEffect(definition);
        }

        // 기아 버프는 이어 거는 피해 효과에 난이도 배율과 방어 무시 비트(원작 1032_Buff_Starve의 ignoreArmor 1)를 넘긴다.
        // 이어 거는 효과는 부모 문맥을 그대로 받는다. 배율은 버프를 붙일 때 정하며, 월드가 연결된 동안 난이도는 바뀌지 않는다.
        // 이어 걸린 피해 효과는 부모와 함께 떨어지지 않으므로, 원작 RemoveBuffsByTag(Starve)처럼 Buff.Starve 태그로 둘을 같이 뗀다.
        void SetStarveActive(GameplayEffectDefinition starve, bool active, DifficultyState? difficulty)
        {
            AbilitySystem abilities = Abilities;
            if (active == abilities.HasEffect(starve)) return;
            if (active)
                ApplyEffect(new GameplayEffectSpec(starve, abilities)
                    .WithSetByCaller(CharacterTags.DataDamageFactor, DamageRules.PlayerDamageFactor(difficulty))
                    // 원작 1032_Buff_Starve의 DamageAction은 ignoreArmor라 방어를 건너뛴다. 이어 거는 피해 효과도 이 문맥을 물려받는다.
                    .WithContext(new GameplayEffectContext(null, false, default, default, HurtExecution.IgnoreArmorFlag)));
            else abilities.RemoveEffectsWithTag(CharacterTags.BuffStarve);
        }

        // 아바타를 비우고, 아바타 연결이 건 기본 버프와 생존 버프, 장비 효과, 행동 어빌리티, 대시 쿨다운을 모두 뗀다.
        // 아바타를 먼저 비워 진행 중인 행동을 취소한다. 취소된 대시는 쿨다운을 걸지 않아 다음 아바타는 원작 새 캐릭터처럼 바로 대시할 수 있다.
        // 기아 피해 효과를 떼면 주기 누적도 사라진다.
        void ReleaseAvatar()
        {
            CharacterAbilitySet set = _abilitySet!;
            AbilitySystem abilities = Abilities;
            abilities.SetAvatar(null);
            // 버프는 연결 때 잡아 둔 구역 참조로 뗀다. 종료 순서상 설정 등록이 먼저 풀려도 정적 구역을 읽지 않는다.
            GameplayDataSettings.BuffsData? buffs = _buffs;
            if (buffs != null)
            {
                PlayerAvatarEffects.RemoveIfActive(abilities, buffs.BaseBuff!);
                PlayerAvatarEffects.RemoveIfActive(abilities, buffs.Thirsty!);
            }
            // 기아 버프와 그 피해 효과는 태그로 함께 뗀다. 구역 참조가 없어도 기아 효과가 남지 않도록 늘 부른다.
            abilities.RemoveEffectsWithTag(CharacterTags.BuffStarve);
            // 폭풍, 장비, 재능 효과도 이 아바타 동안만 붙는다. 기아 효과 뒤, 대시 쿨다운 앞에서 모두 떼고 기록을 비운다.
            _avatarEffects.RemoveAll(abilities);
            GameplayEffectDefinition? dashCooldown = set.Dash!.CooldownEffect;
            if (dashCooldown != null) PlayerAvatarEffects.RemoveIfActive(abilities, dashCooldown);
            abilities.RemoveAbility(set.UseItem!);
            abilities.RemoveAbility(set.Reload!);
            abilities.RemoveAbility(set.Interact!);
            abilities.RemoveAbility(set.Dash!);
            abilities.RemoveAbility(set.Skill!);
            _avatar = null;
            // 해제 뒤 늦게 온 생존 프레임이 버프를 다시 붙이지 않게 구역 참조도 비운다.
            _buffs = null;
        }

        // 비출격 맵에서는 원본 메인 캐릭터처럼 죽지 않게 State.CannotDie를 단다. 피해와 아이템 효과를 걸 때, 기아 피해 시계를
        // 진행할 때 장소로 맞춘다. 태그 자체로 지금 상태를 보아 태그 변경이 막힌 뒤에도 다음 호출이 다시 맞추고, 이미 맞으면 건드리지 않는다.
        void SetCannotDie(bool cannotDie)
        {
            AbilitySystem abilities = Abilities;
            if (cannotDie == abilities.Tags.HasTagExact(CharacterTags.CannotDie)) return;
            if (cannotDie) abilities.AddLooseTag(CharacterTags.CannotDie);
            else abilities.RemoveLooseTag(CharacterTags.CannotDie);
        }

        // 어트리뷰트 값으로 확정 사본을 만들고, before와 다르면 알린다. 값이 그대로면 같은 사본을 유지한다.
        // 실패하는 단계가 없어 결과를 돌려주지 않는다.
        void Commit(PlayerState? before)
        {
            PlayerState? next = _player;
            if (next != null)
            {
                AttributeSet attributes = Abilities.Attributes;
                float health = attributes.GetBase(CharacterAttributes.CurrentHealth);
                float energy = attributes.GetBase(CharacterAttributes.CurrentEnergy);
                float water = attributes.GetBase(CharacterAttributes.CurrentWater);
                // 어트리뷰트는 GAS 효과가 저작 수치로 계산한 음수 아닌 유한값이라 믿을 수 있는 입력용 생성자를 쓴다. 스태미나는 저장 형식
                // 때문에 남기는 값이라 확정 사본의 값을 그대로 옮긴다. 실제 스태미나는 모터가 쓰는 CurrentStamina 어트리뷰트이고 저장하지 않는다.
                float stamina = next.Stamina;
                if (!SameValues(next, health, stamina, energy, water)) next = new PlayerState(health, stamina, energy, water);
            }
            _player = next;
            if (!SameValues(before, next)) ObserverDispatch.InvokeEach(Changed);
        }

        // PlayerState는 값 비교를 정의하지 않으므로 네 수치를 직접 비교한다. 생존 값은 음수 아닌 유한값이라 NaN은 없다.
        static bool SameValues(PlayerState? a, PlayerState? b) =>
            ReferenceEquals(a, b) || (a != null && b != null && SameValues(a, b.Health, b.Stamina, b.Energy, b.Hydration));

        static bool SameValues(PlayerState a, float health, float stamina, float energy, float hydration) =>
            a.Health == health && a.Stamina == stamina && a.Energy == energy && a.Hydration == hydration;
    }
}
