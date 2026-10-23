#nullable enable
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 HealthSimpleBase다. 드럼통, 부서지는 벽, 모래주머니처럼 캐릭터가 아닌 대상의 체력이다.
    // 원작 대상은 모두 Teams.all(누구에게나 적)이라 팀을 두지 않는다. 원작 치명 판정은 탄의 치명률을 옮기지 않아 뺀다.
    // 원작은 이벤트로 Breakable과 HalfObsticle에 알리지만, 여기서는 체력만 줄이고 소유자 알림과 효과는 전투 시뮬레이션의 HurtReceiver가 한곳에서 한다.
    // 원작은 공유 폭발 버퍼에 남은 수신자가 죽은 뒤 다시 맞아 두 번 죽을 수 있다. 여기서는 한 번만 죽는다.
    [DisallowMultipleComponent]
    public sealed class OriginalSimpleHealth : MonoBehaviour
    {
        [SerializeField] bool _onlyExplosion;
        [SerializeField] float _maxHealth = 250f;
        // 원작 damageMultiplierIfNotMainCharacter다. 모래주머니는 주인공이 아닌 공격자의 피해를 0.5배로 받는다.
        [SerializeField] float _multiplierIfNotPlayer = 1f;
        [SerializeField] DamageReceiver _receiver = null!;
        [SerializeField] Breakable? _breakable;
        [SerializeField] HalfObsticle? _halfObsticle;
        // 원작 HurtVisual(useSimpleHealth)의 hitFX와 그 효과의 PlaySfx 소리다. 피해를 받아들일 때마다 피해 지점에 낸다.
        // 원작 HurtVisual의 렌더러 밝기 깜빡임은 대상 재질이 피격 밝기를 받는지 확인하지 않아 옮기지 않는다.
        [SerializeField] SpawnRule? _hitFx;
        [SerializeField] string _hitSound = "";
        float _health;
        bool _dead;

        internal float Health => _health;
        internal bool Dead => _dead;
        internal Breakable? Breakable => _breakable;
        internal HalfObsticle? HalfObsticle => _halfObsticle;
        internal SpawnRule? HitFx => _hitFx;
        internal string HitSound => _hitSound;

        void Awake() => _health = _maxHealth;

        // 원작 OnHurt의 체력 계산이다. 피해를 받아들였으면 true다. 폭발만 받는 대상은 다른 피해를 기록 없이 무시한다.
        internal bool Hurt(in OriginalDamageInfo info)
        {
            if (_dead || (_onlyExplosion && !info.Explosion)) return false;
            _health -= info.Damage * (info.FromPlayer ? 1f : _multiplierIfNotPlayer);
            if (_health > 0f) return true;
            _dead = true;
            // 원작 DamageReceiver.OnDead처럼 피해 Collider 객체를 꺼서 탄과 폭발이 더 찾지 않게 한다.
            _receiver.gameObject.SetActive(false);
            return true;
        }

        // 원작 Breakable.Awake처럼 저장에서 부서진 채 시작하는 대상은 피해 Collider를 끄고 피해를 받지 않는다.
        internal void StartBroken()
        {
            _dead = true;
            _receiver.gameObject.SetActive(false);
        }

        // 제작 도구와 테스트가 쓰는 구성이다.
        internal void Configure(float maxHealth, bool onlyExplosion, float multiplierIfNotPlayer, DamageReceiver receiver,
            Breakable? breakable, HalfObsticle? halfObsticle, SpawnRule? hitFx, string hitSound)
        {
            _maxHealth = maxHealth; _onlyExplosion = onlyExplosion; _multiplierIfNotPlayer = multiplierIfNotPlayer;
            _receiver = receiver; _breakable = breakable; _halfObsticle = halfObsticle; _hitFx = hitFx; _hitSound = hitSound;
            _health = maxHealth; _dead = false;
        }
    }
}
