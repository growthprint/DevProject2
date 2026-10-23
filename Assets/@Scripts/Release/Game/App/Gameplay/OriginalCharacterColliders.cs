#nullable enable
using UnityEngine;

namespace Game.App
{
    // 원작 캐릭터의 DamageReceiver 자식과 머리 높이 맞춤을 플레이어 모터, 적, Editor 제작기가 함께 쓴다.
    // 한곳에 두어 실행 중에 붙인 피해 Collider와 저작한 피해 Collider가 같은 모양을 갖게 한다.
    internal static class OriginalCharacterColliders
    {
        // 원작 Character.prefab의 DamageReceiver 캡슐 높이와 중심이다. 반지름은 원작 0.5 대신 0.3을 쓰는 알려진 차이다.
        const float _receiverRadius = .3f, _receiverHeight = 1.2f;
        static readonly Vector3 _receiverCenter = new(0f, .6f, 0f);
        // 원작 캐릭터 루트의 이동 캡슐 반지름, 높이, 중심 높이다. 플레이어 모터의 CharacterController가 쓰고,
        // 원작 대사와 철수 Trigger가 이 캡슐로 감지하므로 영역 판정 높이도 중심 높이를 쓴다.
        internal const float _movementRadius = .3f, _movementHeight = 1f, _movementCenterHeight = .5f;

        // 탄, 조준, 근접은 DamageReceiver(3)만 보므로 피해 판정을 루트 몸통과 따로 레이어 3 자식에 둔다.
        // 탄 질의가 Trigger를 무시하므로 단단한 캡슐로 만든다.
        internal static CapsuleCollider AddDamageReceiver(Transform root)
        {
            var receiver = new GameObject("DamageReceiver");
            receiver.layer = GameLayers.DamageReceiver;
            receiver.transform.SetParent(root, false);
            CapsuleCollider body = receiver.AddComponent<CapsuleCollider>();
            body.radius = _receiverRadius;
            body.height = _receiverHeight;
            body.center = _receiverCenter;
            return body;
        }

        // 원작 CharacterMainControl이 모델을 붙일 때처럼 피해 캡슐 높이를 머리 소켓 높이 + 1로 맞추고 중심을 그 절반 높이에 둔다.
        // 원작 식의 1은 머리 Collider 배율의 절반 0.5와 여유 0.5를 더한 값이다.
        internal static void FitToHead(CapsuleCollider body, Transform root, Transform head)
        {
            float height = head.position.y - root.position.y + 1f;
            body.height = height;
            body.center = Vector3.up * (height * .5f);
        }
    }
}
