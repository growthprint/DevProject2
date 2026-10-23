#nullable enable

namespace Game.App
{
    // 판정 코드가 쓰는 원작 물리 레이어 번호다. 이름은 TagManager 레이어 이름과 같고, EditMode 테스트가 각 값을 NameToLayer와 맞춰 본다.
    // NameToLayer는 오타면 -1을 돌려 1 << -1이 조용히 레이어 31을 고르고 LayerMask.GetMask는 할당하므로, 이름 조회 대신 고정 번호를 쓴다.
    // 필드 이름 규칙을 지키면서 레이어 이름 그대로 읽히도록 상수 대신 속성으로 둔다.
    internal static class GameLayers
    {
        internal static int DamageReceiver => 3;
        internal static int Interactable => 8;
        internal static int Character => 9;
        internal static int FowBlock => 18;
        internal static int HeadCollider => 20;
    }
}
