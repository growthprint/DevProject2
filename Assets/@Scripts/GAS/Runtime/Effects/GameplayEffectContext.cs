#nullable enable
using UnityEngine;

namespace Game.GAS
{
    // UE GameplayEffectContext처럼 효과가 어디서 어떻게 왔는지를 담는다. 피격마다 만들므로 할당 없는 값 형식이다.
    // Flags의 비트 의미(치명타, 버프 피해 등)는 게임 쪽이 정한다.
    public readonly struct GameplayEffectContext
    {
        public GameplayEffectContext(Object? causer, bool hasHit, Vector3 hitPoint, Vector3 hitNormal, int flags)
        {
            Causer = causer;
            HasHit = hasHit;
            HitPoint = hitPoint;
            HitNormal = hitNormal;
            Flags = flags;
        }

        // 피해를 낸 물체(무기 표시, 탄, 함정 등)다. 연출과 로그 문맥에만 쓴다.
        public Object? Causer { get; }
        public bool HasHit { get; }
        public Vector3 HitPoint { get; }
        public Vector3 HitNormal { get; }
        public int Flags { get; }

        public static GameplayEffectContext Hit(Vector3 point, Vector3 normal, int flags = 0, Object? causer = null) =>
            new GameplayEffectContext(causer, true, point, normal, flags);

        public bool HasFlag(int flag) => (Flags & flag) == flag;

        public GameplayEffectContext WithFlags(int flags) => new GameplayEffectContext(Causer, HasHit, HitPoint, HitNormal, flags);
    }
}
