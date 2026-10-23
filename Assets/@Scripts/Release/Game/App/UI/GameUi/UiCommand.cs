#nullable enable annotations
using System;

namespace Game.App
{
    // 앱 공통 UI 명령 목록이다. SelectSaveSlot은 슬롯만 고르고 로드나 새 게임을 실행하지 않는다.
    // 외형 편집 완료는 편집 화면 버튼이 Runtime에 직접 요청해서 여기 없다. CancelCustomization은 ESC 경로만 쓴다.
    // 저장 없는 메뉴 복귀와 종료는 기지 밖 이탈 확인만 쓴다. 기지 종료의 저장 실패는 Runtime이 기록하고 종료를 계속한다.
    internal enum UiCommand { Refresh, NewGame, Continue, DeleteSave, MainMenu, Quit, AbortQuit, Customize, CancelCustomization, RecoverBackup, SelectSaveSlot, MainMenuWithoutSaving, QuitWithoutSaving }

    // 명령별 로딩 문구와 오류 문구의 번역 키다. 키는 명령마다 한 번만 만들어 두어 호출마다 문자열을 새로 만들지 않는다.
    internal static class UiCommandText
    {
        // 명령 값을 색인으로 쓰는 로딩 문구 키 표와 오류 문구 키 표다. 둘 다 정적 초기화가 한 번만 만든다.
        static readonly string[] _loadingKeys = CreateKeys("loading.", "save.checking");
        static readonly string[] _errorKeys = CreateKeys("error.", "save.error");

        // 명령 값이 곧 표의 색인이다. 정의 밖 값은 내부 코드 결함으로만 생겨 범위를 검사하지 않는다.
        internal static string LoadingKey(UiCommand command) => _loadingKeys[(int)command];
        // 로딩 문구 키와 같은 색인 규칙을 쓴다. 슬롯 선택만 저장 상태의 오류 문구를 쓴다.
        internal static string ErrorKey(UiCommand command) => _errorKeys[(int)command];

        // 열거형 값이 0부터 선언 순서대로 이어지므로 값을 색인으로 쓴다. 저장 없는 이탈은 같은 이탈의 키를 다시 쓰고,
        // 슬롯 선택은 저장 상태 문구를 쓴다. 나머지는 접두어 뒤에 명령 이름을 붙인다.
        static string[] CreateKeys(string prefix, string selectSaveSlotKey)
        {
            string[] names = Enum.GetNames(typeof(UiCommand));
            var keys = new string[names.Length];
            for (int index = 0; index < names.Length; index++) keys[index] = prefix + names[index];
            keys[(int)UiCommand.SelectSaveSlot] = selectSaveSlotKey;
            keys[(int)UiCommand.MainMenuWithoutSaving] = prefix + nameof(UiCommand.MainMenu);
            keys[(int)UiCommand.QuitWithoutSaving] = prefix + nameof(UiCommand.Quit);
            return keys;
        }
    }
}
