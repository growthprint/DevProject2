#nullable enable
using System;
using System.Runtime.CompilerServices;
using Game.Core;

namespace Game.App
{
    // 소유 그래프 명령이 거절한 이유다. 화면은 문구 대신 이 코드로 알림을 고르고, None은 거절 없음이다.
    // Invalid는 규칙이 아닌 구조 결함이나 버그다. 검출 지점이 Defect로 한 번 보고하므로 결과를 받은 쪽은 다시 기록하지 않는다.
    // SlotMismatch는 그 장비 칸이 받지 않는 종류이거나 짝 토템 칸에 같은 종류가 있는 경우로, 화면은 원작 DenialPunch를 보인다.
    // Occupied는 놓은 칸이 차 있고 맞바꿀 수도 없는 경우다. Sticky는 원작 acceptSticky가 거짓인 펫 가방에 Sticky 아이템을 넣으려 한
    // 경우로, 원작처럼 화면은 알리지 않는다.
    internal enum InventoryRejection
    {
        None, InventoryFull, NotOwned, NotUsable, CaliberMismatch, NoAmmo, EmptyMagazine, SlotMismatch, Occupied,
        Invalid, Sticky
    }

    // 할당 없는 인벤토리 명령 결과다. 기본값(Ok)은 거절 없음이고, 규칙 거절은 Reject, 구조 결함은 Defect로 만든다.
    internal readonly struct InventoryResult
    {
        // 거절 코드만 담는다. 결과는 Ok, Reject, Defect로만 만든다.
        InventoryResult(InventoryRejection rejection) => Rejection = rejection;

        // 거절 없음 결과다. 필드 이름 규칙(_camelCase)을 지키면서 Ok라는 이름을 쓰려고 정적 속성으로 둔다.
        internal static InventoryResult Ok => default;
        internal InventoryRejection Rejection { get; }
        internal bool IsOk => Rejection == InventoryRejection.None;

        // 규칙 거절 결과를 만든다. 호출 코드는 None과 Invalid가 아닌 거절 코드 상수만 넘긴다.
        internal static InventoryResult Reject(InventoryRejection code)
        {
            return new InventoryResult(code);
        }

        // 구조 결함 결과를 만드는 유일한 경로라, 보고 없이 Invalid를 돌려주는 자리가 생기지 않는다.
        // 결함은 Log.Instance.Fail로 한 번 보고한다. 호출 위치를 넘겨 릴리스 로그가 이 팩토리가 아닌 감지한 줄을 가리키게 한다.
        internal static InventoryResult Defect(Exception defect, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
        {
            Log.Instance.Fail(defect, null, callerFile, callerLine);
            return new InventoryResult(InventoryRejection.Invalid);
        }
    }
}
