#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 팀 교전의 표적 규칙이다. 원작 searchedEnemy처럼 표적은 플레이어이거나 다른 팀 적이고, 둘 다 월드의 배치 탐색(EnemyTargetSearch)이 찾는다.
    // 규칙: 처음 찾은 표적은 반응 시간을 기다린 뒤 싸운다. 교전 중에는 보이는 표적을 지키고, 가려졌을 때만 새로 찾은 상대로 바로 바꾼다.
    // 소리와 피격은 표적을 정하지 않고 경계로 넘긴다(OriginalEnemyController.Alert.cs). 같은 팀이나 쓰러진 표적은 놓는다(원작 AICharacterController.cs:370).
    // 무리는 우두머리와 표적을 나눈다. _aware가 참이고 _targetEnemy가 비어 있으면 플레이어 표적이다. _aware가 거짓이면 _targetEnemy도 늘 비어 있다.
    public sealed partial class OriginalEnemyController
    {
        // 원작 SearchEnemyAround의 탐색 거리 배율이다. 순찰과 근접 전투(Combat_Melee)는 시야 ×1, 경계(Alert_Normal)는 ×1.25,
        // 총기 전투(Combat_Normal)는 ×1.5이고, 공격자에게 맞은 직후(isHurtSearch)면 다시 ×2다.
        const float _alertSearchFactor = 1.25f, _combatSearchFactor = 1.5f, _hurtSearchFactor = 2f;
        // 원작 Patrol_Scare 8번 CheckHurt(0.25초 안, 2 이상)의 피해 기준이다. 맞은 순간에 보므로 시간 창은 늘 만족한다.
        const float _scareHurtDamage = 2f;
        // 원작 Combat_Normal 15-23의 놓치기 값이다. 가려졌거나 20 m를 넘은 상태가 0.25초 이어지면 알아챈 위치를 그때 표적 위치로 두고,
        // 다시 forgetTime이 지나면 표적을 놓는다.
        const float _gunLoseDistance = 20f, _gunLoseDelay = .25f;
        const float _sqrGunLoseDistance = _gunLoseDistance * _gunLoseDistance;
        // 원작 OnHurt는 공격자 없는 피해의 알아챈 위치를 몸에서 피해 법선 쪽 3 m에 둔다.
        const float _sourcelessNoticeDistance = 3f;
        // 적이 겨누는 표적의 가슴 높이다. 전투 시뮬레이션이 플레이어를 겨누던 높이와 같다.
        const float _targetAimHeight = .75f;

        OriginalEnemyController? _targetEnemy;
        // 이번 프레임 교전 중인 표적이 가리지 않았는지(원작 !HasObsticleToTarget)다. 배치 탐색 결과를 받을지 정한다.
        bool _targetVisible;
        // 원작 noticeFromPos다. 총기 전투 이동과 경계 조사의 중심이며, 새 표적을 잡으면 다음 Update가 그 위치로 맞춘다.
        Vector3 _noticePosition;
        bool _noticePending;
        // 표적이 가려지거나 멀어진 뒤 0.25초 지점의 알아챈 위치를 아직 두지 않았는지다.
        bool _loseNoticePending;
        // 탐색 대기열에 들어가 결과를 기다리는지다.
        bool _searchQueued;
        // 원작 Combat_Normal 진입 대기(Wait ReactionTime)가 끝나는 시각이다. 교전을 새로 시작할 때만 정한다.
        float _combatEntryUntil;

        // 전투 시뮬레이션이 읽는 표적이다. 플레이어 표적일 때만 달리기와 화면 밖 산포 보정을 쓴다.
        internal bool TargetIsPlayer => _aware && _targetEnemy == null;
        internal OriginalEnemyController? TargetEnemy => _targetEnemy;
        // 적 사격이 겨누는 점이다. 공격은 교전 중에만 하므로 표적이 늘 있다.
        internal Vector3 TargetAimPoint =>
            (_targetEnemy != null ? _targetEnemy.transform.position : _world!.PlayerPosition) + Vector3.up * _targetAimHeight;

        // 플레이어 팀(0)을 적으로 보는 팀인지다. 원작 Team.IsEnemy를 따른다. 배치 탐색이 플레이어 후보를 거를 때도 읽는다.
        internal bool CanTargetPlayer => TeamRules.IsEnemy(_team, TeamRules.Player);

        // 원작 Team.IsEnemy에 같은 팀 표적 지우기를 더한 AI 표적 조건이다. All 팀(UltraMan, 상인)은 IsEnemy로는 자기 팀도 적이지만
        // 원작이 같은 팀 표적을 지우므로 서로 싸우지 않는다. 자기 자신도 같은 팀이라 빠진다. 배치 탐색도 이 조건으로 거른다.
        internal bool IsHostileTo(OriginalEnemyController other) =>
            other.Targetable && other._team != _team && TeamRules.IsEnemy(_team, other._team);

        // 순찰 트리를 도는지다. 원작은 순찰 트리의 탐색(Patrol_Normal 9)에만 야간 시야 배율을 곱한다.
        internal bool Patrolling => !_aware && !_alerted;
        // 배치 탐색이 읽는 이 적의 탐색 범위다. 거리 배율은 위 상수 설명을 따르고, 시야각은 순찰에서만 야간 배율을 곱한다.
        internal float SearchDistance =>
            _sight * (_aware ? (_meleeAi ? 1f : _combatSearchFactor) : _alerted ? _alertSearchFactor : 1f) * (HurtSearch ? _hurtSearchFactor : 1f);
        internal float SearchAngle => Patrolling ? ViewAngle() : _angle;
        internal bool HurtSearch =>
            Time.time - _lastHurt <= _hurtSearchTime && _lastHurtDamage >= _hurtSearchDamage && _lastHurtFromCharacter;
        // 교전을 시작하고 반응 시간을 기다리는 중인지다. 이 동안은 탐색 결과도 받지 않는다.
        internal bool InCombatEntry => _aware && Time.time < _combatEntryUntil;

        // 표적을 잡는다. null은 플레이어다. 새 표적이면 다음 Update에서 추적 경로를 바로 요청하고, 잊는 시간은 지금부터 잰다.
        // 교전 중 표적을 바꿀 때는 원작 FSM처럼 반응 시간을 다시 기다리지 않는다.
        void TakeTarget(OriginalEnemyController? enemy)
        {
            if (!_aware) BeginCombat(Time.time);
            else if (_targetEnemy != enemy) _pathDirty = true;
            _aware = true;
            _targetEnemy = enemy;
            _lastSeen = Time.time;
            _noticePending = true;
        }

        // 원작 FSM이 전투 상태로 들어가는 순간이다. 경계를 끝내고, Combat_Normal 템플릿은 반응 시간(밤 배율 포함)만큼 기다린다.
        // 근접 템플릿(Combat_Melee)은 TickMeleeCombat의 시작 대기 0.2초가 같은 자리다. 놓치기 갈래도 처음부터 다시 잰다.
        void BeginCombat(float now)
        {
            _pathDirty = true;
            _alerted = false;
            _loseNoticePending = true;
            _combatEntryUntil = _meleeAi ? 0f : now + _reaction;
        }

        void LoseTarget()
        {
            _aware = false;
            _targetEnemy = null;
        }

        // 원작 OnHurt와 순찰 트리의 피격 반응이다. 일반 템플릿은 알아챈 위치를 공격자 위치(공격자가 없으면 법선 쪽 3 m)로 두고,
        // 교전 중이 아니면 경계로 넘어간다. 교전 중에는 표적을 바꾸지 않는다. Patrol_Scare 템플릿은 순찰 중 공격자가 준 2 이상의 피해만
        // 그 공격자를 바로 표적으로 삼는다. 비밀 상인은 플레이어 피해로만 도발되고, 도발 전에는 AI 공격자에게만 반격한다(제품 규칙).
        void NoticeHurt(OriginalEnemyController? attacker, bool fromPlayer, Vector3 normal)
        {
            if (fromPlayer) _provoked = true;
            if (Passive)
            {
                if (!_aware && attacker != null && IsHostileTo(attacker)) TakeTarget(attacker);
                return;
            }
            if (!_noticeBySight)
            {
                if (_aware || _lastHurtDamage < _scareHurtDamage) return;
                if (attacker != null) { if (IsHostileTo(attacker)) TakeTarget(attacker); }
                else if (fromPlayer && CanTargetPlayer) TakeTarget(null);
                return;
            }
            Notice(attacker != null ? attacker.transform.position
                : fromPlayer ? _world!.PlayerPosition : transform.position + normal * _sourcelessNoticeDistance);
        }

        // 원작 AICharacterController.cs:370처럼 같은 팀 표적을 지우고, 죽었거나 사라진 AI 표적도 놓는다.
        void DropInvalidTarget()
        {
            if (_targetEnemy != null ? !IsHostileTo(_targetEnemy) : _aware && !CanTargetPlayer) LoseTarget();
        }

        // 교전 중 표적 놓치기다. 근접 템플릿은 원작 Combat_Melee 49-52처럼 가려졌거나 24 m를 넘은 상태가 7초 이어지면 놓친다.
        // 그 밖은 원작 Combat_Normal 15-23처럼 가리지 않고 20 m 안이면 붙잡고 알아챈 위치를 표적 위치로 두며, 그 조건이 끊긴 지
        // 0.25초에 알아챈 위치를 한 번 더 두고 0.25초 + forgetTime이 지나면 놓친다. 시야 거리는 보지 않는다. now는 이번 프레임 시각이다.
        void TrackTarget(float now, bool clear, float sqrDistance, Vector3 target)
        {
            if (_meleeAi)
            {
                if (clear && sqrDistance <= _sqrMeleeLoseDistance) _lastSeen = now;
                if (now - _lastSeen > _meleeLoseTime) LoseTarget();
                return;
            }
            if (clear && sqrDistance <= _sqrGunLoseDistance)
            {
                _lastSeen = now;
                _noticePosition = target;
                _loseNoticePending = true;
                return;
            }
            if (_loseNoticePending && now - _lastSeen >= _gunLoseDelay)
            {
                _noticePosition = target;
                _loseNoticePending = false;
            }
            if (now - _lastSeen > _gunLoseDelay + _forgetTime) LoseTarget();
        }

        // 원작 SearchEnemyAround처럼 결과를 받아야 다음 탐색을 넣는다. 교전 중 표적이 보이는 동안은 바꿀 일이 없어 넣지 않는다.
        // 순찰 트리에 시야 발견이 없는 적(UltraMan)은 교전 중에만 찾는다. 경계 중인 적은 늘 찾는다. 도발 전 상인은 찾지 않는다.
        void RequestTargetSearch(bool visible)
        {
            if (_searchQueued || visible || Passive || !(_aware || _noticeBySight || _alerted)) return;
            _searchQueued = true;
            _world!.RequestTargetSearch(this);
        }

        // 배치 탐색 결과다. found는 원작처럼 거르기를 통과한 후보 가운데 버퍼 순서상 마지막 다른 팀 적이고, player가 참이면
        // 그 후보가 플레이어다. 빈 결과는 표적을 지우지 않는다. 도발 전 상인, 진입 대기 중인 적, 보이는 표적이 있는 적,
        // 같은 표적은 바꾸지 않는다.
        internal void TargetSearched(OriginalEnemyController? found, bool player = false)
        {
            _searchQueued = false;
            if ((found == null && !player) || !Targetable || Passive || InCombatEntry) return;
            if (_aware && (_targetVisible || (player ? _targetEnemy == null : _targetEnemy == found))) return;
            TakeTarget(player ? null : found);
        }
    }
}
