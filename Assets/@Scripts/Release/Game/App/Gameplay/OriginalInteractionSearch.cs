#nullable enable
using UnityEngine;

namespace Game.App
{
    // 원본 CA_Interact.SearchInteractableAround의 F 상호작용 탐색 구와 영역 판정 높이다. 전투 월드와 프롤로그가 같은 값을 쓰도록 한곳에 둔다.
    internal static class OriginalInteractionSearch
    {
        // 원작 대사·안내·철수 Trigger와 프롤로그 시작 구역은 캐릭터 루트 이동 캡슐로 감지한다.
        // 여기서는 점 하나로 영역 안인지 보므로 그 캡슐 중심 높이를 쓴다. 피해 캡슐 중심(0.6)이 아니다.
        internal const float _areaProbeHeight = OriginalCharacterColliders._movementCenterHeight;
        // 원본 값이다. 플레이어 루트에서 0.5m 위, 조준 방향으로 0.2m 앞이 중심이고 반지름은 0.3m다.
        // 인벤토리 화면과 전투 월드 상호작용도 같은 높이에서 거리를 잰다.
        internal const float _probeHeight = .5f;
        const float _probeForward = .2f;
        const float _probeRadius = .3f;

        // Interactable 레이어 Collider만 호출자 버퍼에 담고 찾은 수를 돌려준다. 원본은 전역 설정으로 Trigger도 잡으므로 Collide로 고정한다.
        // 매 프레임 불리므로 버퍼는 호출자가 재사용하며, 원본 버퍼 크기는 5다.
        internal static int Overlap(Vector3 root, Vector3 aim, Collider[] buffer) =>
            Physics.OverlapSphereNonAlloc(root + Vector3.up * _probeHeight + aim * _probeForward, _probeRadius, buffer,
                1 << GameLayers.Interactable, QueryTriggerInteraction.Collide);
    }
}
