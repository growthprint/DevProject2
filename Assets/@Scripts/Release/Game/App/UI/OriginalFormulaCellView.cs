#nullable enable annotations
namespace Game.App
{
    // 설계도 연구 창의 칸이다. 표시와 클릭 규칙은 열쇠 등록 창과 같아 OriginalRegisterCellView가 맡는다. 저장된 SharedHUD 프리팹이
    // 이 스크립트를 가리키므로 형식 이름과 파일을 둔다. 목록 칸의 원작 highlightIndicator는 아직 해금하지 않은 설계도를, 등록 칸 이름은 Formula 태그다.
    public sealed class OriginalFormulaCellView : OriginalRegisterCellView
    {
    }
}
