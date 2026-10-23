#nullable enable annotations
using System;
// 도감 단추 입력 확인에 프로젝트 Log를 쓴다.
using Game.Core;

namespace Game.App
{
    // 원작 Duckov.UI.FormulasRegisterView(설계도 연구 창)다. 목록, 등록 칸, 표시, 버튼은 열쇠 등록 창과 같아 OriginalItemRegisterView가 맡고,
    // 이 창에만 있는 레시피 도감 단추만 여기 둔다. 저장된 SharedHUD 프리팹이 이 스크립트를 가리키므로 형식 이름과 파일을 둔다.
    // 제조 소유자와 저장 상태는 읽지 않는다. 열고 닫기, 입력 차단, 명령은 화면 컨트롤러(FormulaRegisterScreen)가 맡는다.
    public sealed class OriginalFormulasRegisterView : OriginalItemRegisterView
    {
        // 원작 머리의 레시피 도감 단추(Content/BtnContainer_1/btn open formula index → ViewsProxy.ShowFormulasIndexView)다.
        // 화면 컨트롤러가 받아 도감 창 열기로 넘긴다.
        internal event Action? IndexRequested;

        // 도감 단추도 제작 도구가 Runtime Only 영속 OnClick으로 연결한다. 단추는 열린 창 안에만 있어 열린 동안만 눌린다.
        public void OnIndexClicked()
        {
            Log.Instance.Assert(IsShown, "닫힌 설계도 연구 창의 도감 단추가 눌렸습니다.", this);
            IndexRequested?.Invoke();
        }
    }
}
