#nullable enable
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 Breakable이다. 드럼통과 부서지는 벽의 보통, 위험, 부서진 표시와 주 Collider를 바꾸고, 부서지면 폭발하거나 저장한다.
    // 폭발과 위험 효과는 전투 시뮬레이션이 이 값을 읽어 원작 ExplosionManager와 Instantiate(dangerFx)처럼 낸다.
    [DisallowMultipleComponent]
    public sealed class Breakable : MonoBehaviour
    {
        // 원작 save와 saveKey다. 저장 키는 원작 SavesSystem 키(Breakable_<키>)에서 접두사를 뺀 값이다.
        [SerializeField] bool _save;
        [SerializeField] string _saveKey = "";
        [SerializeField] OriginalSimpleHealth _health = null!;
        [SerializeField] int _dangerHealth = 50;
        [SerializeField] bool _createExplosion;
        [SerializeField] float _explosionRadius;
        [SerializeField] float _explosionDamage;
        [SerializeField] float _explosionArmorPiercing;
        [SerializeField] GameObject _normalVisual = null!;
        [SerializeField] GameObject? _dangerVisual;
        [SerializeField] GameObject? _brokenVisual;
        [SerializeField] GameObject? _mainCollider;
        [SerializeField] SpawnRule? _dangerFx;
        bool _danger;

        internal bool CreatesExplosion => _createExplosion;
        internal SpawnRule? DangerFx => _dangerFx;
        internal float ExplosionRadius => _explosionRadius;
        internal string SaveKey => _saveKey;

        // 원작 explosionDamageInfo다. 원작처럼 부순 공격자(fromCharacter)를 폭발 피해에 넘긴다.
        internal OriginalDamageInfo ExplosionDamage(bool fromPlayer) =>
            new(_explosionDamage, _explosionArmorPiercing, true, fromPlayer, transform.position, Vector3.up);

        // 원작 Awake다. 저장에서 부서진 대상은 원작처럼 위험 상태로 두어 위험 표시를 다시 켜지 않고, 주 Collider는 저작 상태 그대로 둔다.
        // 부서진 상태는 씬을 불러오기 전에 저장 기록에서 읽혀 있다.
        void Awake()
        {
            _normalVisual.SetActive(true);
            if (_dangerVisual != null) _dangerVisual.SetActive(false);
            if (_brokenVisual != null) _brokenVisual.SetActive(false);
            if (_save && GameManager.HasInstance && GameManager.LevelObjects.IsBroken(_saveKey))
            {
                _danger = true;
                _normalVisual.SetActive(false);
                if (_brokenVisual != null) _brokenVisual.SetActive(true);
                _health.StartBroken();
            }
            else if (_mainCollider != null) _mainCollider.SetActive(true);
        }

        // 원작 OnHurt다. 남은 체력이 위험 체력 이하가 되면 한 번만 위험 표시로 바꾸고 true를 돌려준다. 호출자가 위험 효과를 낸다.
        internal bool OnHurt(float health)
        {
            if (_danger || health > _dangerHealth) return false;
            _danger = true;
            if (_dangerVisual != null)
            {
                _normalVisual.SetActive(false);
                _dangerVisual.SetActive(true);
            }
            return true;
        }

        // 원작 OnDead다. 부서진 표시로 바꾸고 주 Collider를 끈다. 저장 대상이면 레벨 물체 소유자에 기록한다.
        // 세션 관문이 닫혀 기록이 거절되면 원작 저장 시점이 없는 것과 같아 기록 없이 넘어간다.
        internal void OnDead()
        {
            _danger = true;
            _normalVisual.SetActive(false);
            if (_dangerVisual != null) _dangerVisual.SetActive(false);
            if (_brokenVisual != null) _brokenVisual.SetActive(true);
            if (_mainCollider != null) _mainCollider.SetActive(false);
            if (_save && GameManager.HasInstance) GameManager.LevelObjects.TryMarkBroken(_saveKey);
        }

        // 제작 도구와 테스트가 쓰는 구성이다.
        internal void Configure(bool save, string saveKey, OriginalSimpleHealth health, int dangerHealth, bool createExplosion,
            float explosionRadius, float explosionDamage, float explosionArmorPiercing, GameObject normalVisual, GameObject? dangerVisual,
            GameObject? brokenVisual, GameObject? mainCollider, SpawnRule? dangerFx)
        {
            _save = save; _saveKey = saveKey; _health = health; _dangerHealth = dangerHealth; _createExplosion = createExplosion;
            _explosionRadius = explosionRadius; _explosionDamage = explosionDamage; _explosionArmorPiercing = explosionArmorPiercing;
            _normalVisual = normalVisual; _dangerVisual = dangerVisual; _brokenVisual = brokenVisual; _mainCollider = mainCollider; _dangerFx = dangerFx;
            _danger = false;
        }
    }
}
