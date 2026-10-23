#nullable enable

namespace Game.App
{
    // 여섯 저장 슬롯 중 하나를 가리키는 번호다. record라서 번호가 같으면 같은 값으로 비교된다.
    public sealed record GameSaveSlotId
    {
        // 원본 게임의 저장 슬롯 수다.
        public const int _count = 6;
        // 유일한 생성 경로다. 번호는 화면 표시와 같은 1~6이고, 코드가 정한 번호나 제작된 여섯 메뉴 버튼에서만 온다.
        public GameSaveSlotId(int number)
        {
            Number = number;
        }
        public int Number { get; }
    }

}
