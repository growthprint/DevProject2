#nullable enable annotations
namespace Game.App
{
    // 등록 칸 클릭을 받는 등록 창 View다. 열쇠 등록 창과 설계도 연구 창이 같은 칸(OriginalRegisterCellView)을 쓰므로 칸은 이 계약으로만 의도를 넘긴다.
    // list는 목록 번호이고 등록 칸이면 OriginalRegisterCellView._slotList다. activate는 원작 더블클릭(등록 칸으로 옮기기, 등록 칸에서는 빼기)이다.
    internal interface IOriginalRegisterCellOwner
    {
        void CellClicked(int list, string itemId, bool activate);
    }
}
