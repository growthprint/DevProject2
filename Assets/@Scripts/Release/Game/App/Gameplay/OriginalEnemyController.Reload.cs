#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 AI 재장전이다. 원작 총 프리팹의 탄창이 비어 있어 출현 때 0발이고, 쏠 때마다 1발 줄며, 예비 탄은 원작 AI처럼 무한이다.
    // 원작 TryToReloadIfEmpty가 도는 자리(출현 뒤 순찰 진입, 연사 끝)와 빈 방아쇠가 장전을 시작한다. 장전 중에는 쏘지 않는다.
    public sealed partial class OriginalEnemyController
    {
        // 원작 CharacterRandomPreset.canTalk다. 거짓이면 장전 문구를 띄우지 않는다. 기본값은 원작 기본값이고 제작 도구가 프리셋 값으로 덮는다.
        [SerializeField] bool _canTalk = true;
        // 원작 TryToReloadIfEmpty의 문구 키와, 원작 CharacterMainControl.PopText가 문구를 띄우는 플레이어 거리(55 m)의 제곱이다.
        const string _reloadingPopKey = "PopText_Reloading";
        const float _sqrReloadPopDistance = 55f * 55f;
        // 출현 뒤 첫 순찰 진입(초기화 Wait 0.2초 + 순찰 Wait 0.2초)과, 교전이나 경계를 떠나 순찰로 돌아온 뒤 순찰 Wait 0.2초까지의 시간이다.
        const float _spawnReloadDelay = .4f, _patrolReloadDelay = .2f;

        // 탄창에 든 탄 수와 장전 진행 시간이다. 진행 시간이 음수면 장전 중이 아니다.
        int _rounds;
        float _reloadElapsed = -1f;
        // 원작 _reloadSoundLoopEvent처럼 적을 따라가는 장전 시작음 핸들이다. 끝나거나 멈추면 비운다.
        GameAudioEvent? _reloadAudio;
        // 순찰 자리 TryToReloadIfEmpty를 부를 시각이다. 음수면 예정이 없다. 첫 Tick에 출현 시각으로 정하고, 순찰로 돌아올 때마다 다시 정한다.
        // 순찰은 교전(_aware)도 경계(_alerted)도 아닌 상태다. 원작 경계 트리에는 이 노드가 없다.
        float _patrolReloadAt = -1f;
        bool _reloadArmed, _wasPatrolling;
        // 원작 TryToReloadIfEmpty.isFirstTime의 반대다. 자리마다 따로 두며, 첫 실행의 빈 탄창 장전은 문구 없이 조용하다.
        bool _patrolReloadUsed, _attackReloadUsed;
        // 원작 needAutoReload다. singleBullet 총이 1발을 넣고 덜 찼으면 참이고, 다음 Tick이 이어서 장전한다.
        bool _needAutoReload;

        // 원작 CheckReloading(reloadAction.Running)이다. 사격 순환과 총기 피격 대시가 읽는다.
        internal bool Reloading => _reloadElapsed >= 0f;

        // 원작 CharacterAnimationControl의 RightHandOut이다. 든 무기가 보이고 장전 중이 아닐 때만 참이라 장전하는 동안 손을 내린다.
        bool HandOut(bool drawn) => drawn && !Reloading;

        // 원작 MagicBlend의 GunReady다. 총을 들었고, 장전 중이 아니고, 탄이 남았을 때만 참이다.
        bool GunReady => _aware && _weapon!.IsGun && !Reloading && _rounds > 0;

        // Animate가 Halted가 아닌 모든 Update 끝에서 부른다. 진행 시간은 이 프레임의 deltaTime만 더해 일시정지 동안 멈추고,
        // 장전 시간은 원작 ItemAgent_Gun.ReloadTime처럼 매 프레임 ReloadSpeedGain 현재값으로 다시 읽는다. now는 EditMode 시험이 넣는 시각이다.
        void TickReload(float now, float deltaTime)
        {
            bool patrolling = !_aware && !_alerted;
            if (!_reloadArmed) { _reloadArmed = true; _patrolReloadAt = now + _spawnReloadDelay; }
            else if (patrolling && !_wasPatrolling) _patrolReloadAt = now + _patrolReloadDelay;
            _wasPatrolling = patrolling;
            if (_patrolReloadAt >= 0f && now >= _patrolReloadAt)
            {
                _patrolReloadAt = -1f;
                ReloadIfEmpty(ref _patrolReloadUsed);
            }
            if (!Reloading)
            {
                // 원작 ready 상태는 방아쇠가 남은 장전보다 먼저다. shootDelay가 끝나 장전만 기다리던 연사가 시작할 때까지 미룬다.
                if (_needAutoReload && !(_shootAt > 0f && now >= _shootAt)) StartReload();
                return;
            }
            _reloadElapsed += deltaTime;
            if (_reloadElapsed >= _weapon!.ReloadTime(_abilities!.GetCurrent(CharacterAttributes.ReloadSpeedGain))) FinishReload();
        }

        // 원작 TryToReloadIfEmpty다. 든 총이 없으면 첫 실행 표시도 그대로 둔다. 탄창이 비었으면 장전하고, 그 자리의 첫 실행이 아니면
        // 장전 문구를 띄운다. 이미 장전 중이면 새로 시작하지 않는다.
        void ReloadIfEmpty(ref bool used)
        {
            if (!_weapon!.IsGun) return;
            if (_rounds <= 0)
            {
                if (!Reloading) StartReload();
                if (used) PopReloading();
            }
            used = true;
        }

        // 사격 한 번(산탄 전체)에 1발을 쓴다. 장전 중이면 쏘지 않고, 빈 탄창이면 원작 empty 상태의 방아쇠처럼 장전을 시작한다.
        // autoReload 무기(활)는 마지막 탄을 쏜 직후 장전한다.
        bool TryUseRound()
        {
            if (Reloading) return false;
            if (_rounds <= 0) { StartReload(); return false; }
            _rounds--;
            if (_rounds <= 0 && _weapon!.AutoReload) StartReload();
            return true;
        }

        // 원작 BeginReload와 PostStartReloadSound다. 가득 찬 탄창은 장전하지 않고, 앞 시작음은 즉시 멈춘 뒤 새 시작음을 적에 붙여 낸다.
        void StartReload()
        {
            _needAutoReload = false;
            if (_rounds >= _weapon!.MagazineCapacity) return;
            _reloadElapsed = 0f;
            StopReloadSound();
            string? start = _weapon.ReloadStartSound;
            if (start != null) _reloadAudio = _world!.Context.Audio.PlayAttached(start, transform);
        }

        // 원작 LoadBullets와 PostReloadSuccessSound다. fullMag는 용량까지, singleBullet은 1발을 넣고, 시작음을 멈춘 뒤 끝음을 낸다.
        void FinishReload()
        {
            int capacity = _weapon!.MagazineCapacity;
            bool single = _weapon.SingleBulletReload;
            _rounds = single ? Mathf.Min(_rounds + 1, capacity) : capacity;
            _reloadElapsed = -1f;
            StopReloadSound();
            string? end = _weapon.ReloadEndSound;
            if (end != null) _world!.Context.Audio.PlayOneShot(end, transform.position);
            _needAutoReload = single && _rounds < capacity;
        }

        // 원작 PopText는 플레이어 55 m 안에서만 보인다. 원작 말풍선 대신 Poptext_OutOfAmmo와 같은 떠오르는 문구를 쓴다.
        void PopReloading()
        {
            if (!_canTalk || (transform.position - _world!.PlayerPosition).sqrMagnitude > _sqrReloadPopDistance) return;
            _world.Context.View.PopText(GameLocalization.Text(_reloadingPopKey), transform.position, _world.Context.Camera);
        }

        // 원작 StopReloadSound처럼 시작음을 즉시 멈춘다.
        void StopReloadSound()
        {
            _reloadAudio?.Stop(false);
            _reloadAudio = null;
        }

        // 사망 처리가 한 번 부른다. 붙은 시작음을 멈추고 장전 상태를 비운다.
        internal void StopReload()
        {
            StopReloadSound();
            _reloadElapsed = -1f;
            _needAutoReload = false;
        }

        // 거리 비활성화나 파괴로 꺼지면 붙은 시작음만 멈춘다. 진행 시간은 Update가 멈춰 그대로 남는다.
        // 다른 partial은 OnDisable을 만들지 않고, 꺼질 때 할 일이 생기면 이 메서드에 더한다.
        void OnDisable() => StopReloadSound();
    }
}
