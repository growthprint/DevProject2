#nullable enable
using System.Diagnostics.CodeAnalysis;
using DuckCharacter;
using Game.App.World;
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원작 ItemAgentHolder의 스킬 아이템 들기와 CA_Skill 조준, Skill_Grenade 투척 계산을 맡는다. 전투 시뮬레이션이 하나를 가진다.
    // 어느 행동을 끊고 막는지는 스킬 어빌리티 에셋의 태그가 정하고, 투사체와 폭발과 소비는 전투 시뮬레이션이 맡는다.
    internal sealed class SkillItemAction
    {
        readonly GameplayWorldContext _world;
        // 원작 CA_Skill이다. 조준하는 동안 실행 중이고, 쥐는 Block 태그가 달리기, ADS, 무기 교체, 공격을 막는다.
        readonly AbilitySystem _abilities;
        readonly GameplayAbilityDefinition _ability;
        // 손에 든 스킬 아이템 인스턴스와 그 스킬 수치, 손에 보일 아이콘이다.
        string? _item;
        OriginalItemSkill? _skill;
        Sprite? _icon;
        // 원작 기본 Handheld 에이전트처럼 오른손에 붙인 아이콘 표시다. 꺼내기 시간 동안은 비활성으로 둔다.
        WorldObjectHandle? _hand;
        // 원작 SkillInWorldHUD다. 조준하는 동안만 빌린다.
        WorldObjectHandle? _hud;
        OriginalSkillHud? _hudView;
        float _aimStarted;
        // 원작 SkillBase.lastReleaseTime이다. 스킬은 아이템 인스턴스마다 있으므로 마지막으로 던진 인스턴스와 함께 둔다.
        string? _releasedItem;
        float _releasedAt = -999f;

        // 다른 행동이나 아바타 연결 해제가 조준을 끊으면 조준 표시만 거둔다. Release가 처리기를 푼다.
        internal SkillItemAction(GameplayWorldContext world)
        {
            _world = world;
            PlayerVitals vitals = world.Runtime.Vitals;
            _abilities = vitals.Abilities;
            _ability = vitals.AbilitySet.Skill!;
            CharacterActionAbility.SetCanceledHandler(_abilities, _ability, HideHud);
        }

        internal bool Holding => _item != null;
        internal string? Item => _item;
        bool Aiming => _abilities.IsAbilityActive(_ability);

        // 원작 ChangeHoldItem처럼 손의 아이템을 바꾼다. 아이콘 표시는 비활성으로 빌려 두고 꺼내기 시간이 끝나면 ShowHand가 켠다.
        internal void Hold(string item, OriginalItemSkill skill, Sprite? icon)
        {
            Drop();
            _item = item;
            _skill = skill;
            _icon = icon;
            // 기본 Handheld 규칙은 GameplayDataSettings에 저작되어 있고, 열린 월드가 방금 빌린 핸들이라 대여가 실패하지 않는다.
            Transform socket = HandSocket;
            WorldObjectHandle hand = _world.Objects.Spawn(GameplayDataSettings.Prefabs.HandheldAgentPrefab!, new Pose(socket.position, socket.rotation))!;
            Transform shown = hand.Instance!.transform;
            shown.SetParent(socket, false);
            shown.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            // 원작 HandheldSprite.Start처럼 손에 든 아이템의 아이콘을 보인다.
            shown.GetComponentInChildren<SpriteRenderer>(true).sprite = icon;
            _hand = hand;
        }

        // 원작 holdStadyTime이 지나 에이전트를 켜는 순간이다. 들고 있지 않으면 아무 일도 하지 않는다.
        internal void ShowHand()
        {
            if (_hand != null) _world.Objects.Activate(_hand);
        }

        // 원작 DestroyCurrentItemAgent처럼 손을 비운다. 조준 중이면 원작 CheckSkillAndBinding 실패처럼 조준도 끝난다.
        internal void Drop()
        {
            _abilities.CancelAbility(_ability);
            if (_hand != null) _world.Objects.Despawn(_hand);
            _hand = null;
            _item = null;
            _skill = null;
            _icon = null;
        }

        // 원작 InputManager.StartItemSkillAim이다. 이미 조준 중이거나, 같은 아이템의 재사용 대기 중이거나, 더 높은 행동 중이면 시작하지 않는다.
        internal void TryStartAim()
        {
            if (_skill == null || Aiming) return;
            if (_releasedItem == _item && Time.time - _releasedAt < _skill.CoolDownTime) return;
            if (_abilities.TryActivateAbility(_ability) != AbilityActivationResult.Activated) return;
            _aimStarted = Time.time;
            // 조준 표시 규칙도 GameplayDataSettings에 저작되어 있다. 원작처럼 범위가 1 이하면 1로 보이고 수류탄이라 궤적을 켠다.
            WorldObjectHandle hud = _world.Objects.Spawn(GameplayDataSettings.Prefabs.SkillHud!, new Pose(_world.PlayerPosition, Quaternion.identity))!;
            _hudView = hud.Instance!.GetComponent<OriginalSkillHud>();
            _hudView.Show(_skill.EffectRange, true);
            TickHud();
            _world.Objects.Activate(hud);
            _hud = hud;
        }

        // 원작 CancleSkill이다. 던지지 않고 조준만 끝낸다. 조준 중이 아니면 아무 일도 하지 않는다.
        internal void Cancel() => _abilities.CancelAbility(_ability);

        // 원작 CA_Skill.ReleaseSkill이다. 준비 시간 전에 놓으면 던지지 않고 끝낸다. 던질 수 있으면 조준을 끝내고 원작
        // GetCurrentSkillAimPoint의 조준점과 스킬을 돌려준다. 던진 인스턴스와 시각은 재사용 대기에 남긴다.
        internal bool TryRelease([NotNullWhen(true)] out string? item, [NotNullWhen(true)] out OriginalItemSkill? skill, out Vector3 releasePoint)
        {
            item = null;
            skill = null;
            releasePoint = default;
            if (_skill == null || !Aiming) return false;
            if (Time.time - _aimStarted < _skill.SkillReadyTime)
            {
                Cancel();
                return false;
            }
            item = _item!;
            skill = _skill;
            releasePoint = AimPoint(_skill);
            _releasedItem = item;
            _releasedAt = Time.time;
            _abilities.EndAbility(_ability);
            HideHud();
            return true;
        }

        // 원작 SkillHud3D.LateUpdate다. 손 소켓에서 조준점까지 궤적을 그리고 범위 원에 준비 진행률을 넣는다.
        // 입력이 막힌 프레임에도 원작처럼 마지막 조준점으로 계속 그린다.
        internal void TickHud()
        {
            if (_hudView == null || _skill == null) return;
            float progress = _skill.SkillReadyTime > 0f ? Mathf.Clamp01((Time.time - _aimStarted) / _skill.SkillReadyTime) : 1f;
            _hudView.Tick(HandSocket.position, AimPoint(_skill), _skill.VerticalSpeed, progress);
        }

        // 씬 정리 때 부르며 여러 번 불러도 된다. 처리기를 먼저 풀어 끝내는 어빌리티가 내려가는 표시를 건드리지 않게 하고,
        // 빌린 표시는 참조만 놓는다. 뒤이은 World 해제가 반환한다.
        internal void Release()
        {
            CharacterActionAbility.SetCanceledHandler(_abilities, _ability, null);
            _abilities.CancelAbility(_ability);
            _hand = null; _hud = null; _hudView = null;
            _item = null; _skill = null; _icon = null;
        }

        // 원작 CurrentUsingAimSocket이다. 스킬 아이템은 일반 손 소켓에 들므로 오른손 소켓이다.
        // 플레이어 리그는 오른손 소켓을 늘 갖고 리그 프리팹 테스트가 확인한다.
        Transform HandSocket => _world.Character.Rig.GetEquipmentSocket(EquipPartType.RightHand)!;

        // 원작 GetCurrentSkillAimPoint다. 조준 평면의 점을 캐릭터 기준 수평 사거리 안으로 줄이고 높이는 조준점 높이를 쓴다.
        Vector3 AimPoint(OriginalItemSkill skill)
        {
            Vector3 input = _world.Motor.AimPoint, root = _world.PlayerPosition;
            Vector3 offset = input - root;
            offset.y = 0f;
            float distance = Mathf.Min(offset.magnitude, skill.CastRange);
            Vector3 point = root + offset.normalized * distance;
            point.y = input.y;
            return point;
        }

        void HideHud()
        {
            if (_hud != null) _world.Objects.Despawn(_hud);
            _hud = null;
            _hudView = null;
        }

        // 원작 Skill_Grenade.CalculateVelocity다. 수직 속도로 올라갔다가 목표 높이까지 떨어지는 시간 동안 수평 거리를 간다.
        internal static Vector3 LaunchVelocity(Vector3 start, Vector3 target, float verticalSpeed)
        {
            float gravity = Physics.gravity.magnitude;
            if (gravity <= 0f) gravity = 1f;
            float rise = verticalSpeed / gravity;
            float fall = Mathf.Sqrt(2f * Mathf.Abs(rise * verticalSpeed * .5f + start.y - target.y) / gravity);
            float total = rise + fall;
            if (total <= 0f) total = .001f;
            Vector3 from = new(start.x, 0f, start.z), to = new(target.x, 0f, target.z);
            Vector3 direction = to - from;
            if (direction.magnitude <= 0f) return Vector3.up * verticalSpeed;
            return direction.normalized * (Vector3.Distance(from, to) / total) + Vector3.up * verticalSpeed;
        }
    }
}
