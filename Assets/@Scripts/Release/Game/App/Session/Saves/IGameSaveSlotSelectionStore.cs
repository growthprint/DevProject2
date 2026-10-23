#nullable enable

namespace Game.App
{
    // 마지막으로 고른 슬롯 번호를 게임 저장 파일과 따로 저장한다. 제품은 PlayerPrefs, 테스트는 메모리 구현을 쓴다.
    // 메인 스레드에서만 부른다.
    public interface IGameSaveSlotSelectionStore
    {
        // 마지막으로 선택한 1 기반 슬롯 번호를 읽는다.
        int Read();
        // 선택한 1 기반 슬롯 번호를 저장한다. 실패하면 예외로 알리고, 저장돼 있던 선택은 그대로 둔다.
        void Write(int slotNumber);
    }
}
