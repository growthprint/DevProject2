#nullable enable
using UnityEngine;

namespace Game.App
{
    // 원작 DamageInfo 가운데 단순 체력 대상(드럼통, 벽, 모래주머니)과 폭발이 쓰는 값이다.
    // 머리 치명은 탄도 계산이 피해에 이미 곱해 넘기므로 담지 않는다. 원작 armorBreak(방어구 내구도 감소)는 아직 옮기지 않아 담지 않는다.
    internal readonly struct OriginalDamageInfo
    {
        internal float Damage { get; }
        internal float ArmorPiercing { get; }
        // 원작 isExplosion이다. 폭발만 받는 대상(부서지는 벽)이 본다.
        internal bool Explosion { get; }
        // 원작 fromCharacter가 주인공인지다. 모래주머니의 주인공 외 피해 배율과 연쇄 폭발의 공격자 전달에 쓴다.
        internal bool FromPlayer { get; }
        internal Vector3 Point { get; }
        internal Vector3 Normal { get; }
        // 원작 fromCharacter가 적일 때의 그 적이다. 플레이어와 세계(공격자 없는 폭발)는 비어 있고, 있으면 FromPlayer는 거짓이다.
        // 적이 맞으면 Hurt가 이 공격자로 aiCombatFactor 비율과 반격 표적을 정하고, 연쇄 폭발도 원작 Breakable처럼 같은 공격자를 넘긴다.
        internal OriginalEnemyController? Attacker { get; }

        // 공격자는 적이 준 피해만 넘긴다. 기본값 null이라 플레이어와 세계 피해를 만드는 호출은 그대로다.
        internal OriginalDamageInfo(float damage, float armorPiercing, bool explosion, bool fromPlayer, Vector3 point, Vector3 normal,
            OriginalEnemyController? attacker = null)
        {
            Damage = damage; ArmorPiercing = armorPiercing; Explosion = explosion; FromPlayer = fromPlayer; Point = point; Normal = normal;
            Attacker = attacker;
        }

        // 원작 ExplosionManager처럼 같은 폭발 피해를 대상마다 다른 지점과 방향으로 넘긴다. 공격자도 그대로 넘긴다.
        internal OriginalDamageInfo At(Vector3 point, Vector3 normal) => new(Damage, ArmorPiercing, Explosion, FromPlayer, point, normal, Attacker);

        // 원작 Breakable.explosionDamageInfo.fromCharacter처럼 부순 피해의 공격자를 폭발 피해에 옮긴다.
        // 파괴물이 만든 폭발 피해에는 공격자 칸이 없어 전투 시뮬레이션이 이 값으로 채운다.
        internal OriginalDamageInfo WithAttacker(OriginalEnemyController? attacker) =>
            new(Damage, ArmorPiercing, Explosion, FromPlayer, Point, Normal, attacker);
    }
}
