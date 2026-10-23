#nullable enable
using UnityEngine;

namespace Game.App
{
    // 원본 AIMainBrain처럼 적 AI의 시야 판정은 전투 시뮬레이션 밖의 질의로 둔다. 적의 발견 판정이 쓴다.
    // 메인 스레드 전용이다.
    internal static class OriginalSightQuery
    {
        // 원작 AIMainBrain.CheckObsticle처럼 FowBlockLayers(13, 18, 19, 21)에 닿는 Collider가 있으면 가린다. 캐릭터와 Wall(6)은 가리지 않는다.
        // 원작 설정처럼 queriesHitTriggers가 켜져 있어 Trigger도 가린다. ignoreFowBlock이면 원작 isHurtSearch처럼 FowBlock(18)만 뺀다.
        internal static bool HasLineOfSight(Vector3 origin, Vector3 target, bool ignoreFowBlock = false)
        {
            Vector3 delta = target - origin;
            int mask = GameplayDataSettings.Layers.FowBlockLayers;
            if (ignoreFowBlock) mask &= ~(1 << GameLayers.FowBlock);
            return !Physics.Raycast(origin, delta.normalized, delta.magnitude, mask, QueryTriggerInteraction.UseGlobal);
        }
    }
}
