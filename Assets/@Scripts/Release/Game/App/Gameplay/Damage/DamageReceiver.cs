#nullable enable
using UnityEngine;

namespace Game.App
{
    // 원작 DamageReceiver 중 단순 체력(useSimpleHealth) 대상이다. 피해 Collider(DamageReceiver 레이어) 객체에 붙어 체력 하나를 가리킨다.
    // 탄, 근접, 폭발이 Collider의 이 컴포넌트로 대상을 찾는다. 캐릭터(적, 플레이어)는 원작과 달리 이 컴포넌트 없이
    // 전투 시뮬레이션이 가진 ColliderOwnerCache의 소속 조회로 찾는다. 플레이어 모터는 월드 준비 때 붙어 저작 참조를 둘 수 없기 때문이다.
    [DisallowMultipleComponent]
    public sealed class DamageReceiver : MonoBehaviour
    {
        [SerializeField] OriginalSimpleHealth _health = null!;

        internal OriginalSimpleHealth Health => _health;

        // 원작 isHalfObsticle이다. 모래주머니 가까이에서 쏜 탄은 이 수신자를 지나간다.
        internal HalfObsticle? HalfObsticle => _health.HalfObsticle;

        internal bool Hurt(in OriginalDamageInfo info) => _health.Hurt(info);

        // 제작 도구와 테스트가 쓰는 구성이다.
        internal void Configure(OriginalSimpleHealth health) => _health = health;
    }
}
