#nullable enable
using System;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 ItemSetting_Skill에 붙은 Skill_Grenade의 수치다. 손에 들고 던지는 아이템 하나를 아이템 ID로 가리킨다.
    // 제작 도구는 일반 수류탄처럼 이 필드만으로 원작과 같게 동작하는 스킬만 옮기고, 다른 값이 필요한 스킬이면 제작을 멈춘다.
    [Serializable]
    public sealed class OriginalItemSkill
    {
        [SerializeField] int _itemId;
        // 원작 SkillBase.coolDownTime과 onReleaseSound다. 소리가 없으면 빈 문자열이다.
        [SerializeField] float _coolDownTime;
        [SerializeField] string _releaseSound = "";
        // 원작 SkillContext의 castRange, skillReadyTime, effectRange, grenageVerticleSpeed다.
        [SerializeField] float _castRange;
        [SerializeField] float _skillReadyTime;
        [SerializeField] float _effectRange;
        [SerializeField] float _verticalSpeed;
        // 원작 Skill_Grenade의 delay, delayFromCollide와 damageInfo의 damageValue, armorPiercing이다.
        [SerializeField] float _delay;
        [SerializeField] bool _delayFromCollide;
        [SerializeField] float _damage;
        [SerializeField] float _armorPiercing;
        // 원작 ItemSetting_Skill.onRelease가 reduceCount면 던질 때마다 하나를 줄인다.
        [SerializeField] bool _reduceCount;
        // 원작 grenadePfb에서 만든 투사체 생성 규칙이다.
        [SerializeField] SpawnRule? _projectile;

        internal int ItemId => _itemId;
        internal float CoolDownTime => _coolDownTime;
        internal string ReleaseSound => _releaseSound;
        internal float CastRange => _castRange;
        internal float SkillReadyTime => _skillReadyTime;
        internal float EffectRange => _effectRange;
        internal float VerticalSpeed => _verticalSpeed;
        internal float Delay => _delay;
        internal bool DelayFromCollide => _delayFromCollide;
        internal float Damage => _damage;
        internal float ArmorPiercing => _armorPiercing;
        internal bool ReduceCount => _reduceCount;
        internal SpawnRule Projectile => _projectile!;
    }
}
