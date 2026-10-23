#nullable enable annotations
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // uGUI 선택을 읽고 고르는 공용 헬퍼다. 모달 스택과 여러 화면이 같은 선택 규칙을 쓰도록 한 곳에 둔다.
    internal static class UiFocus
    {
        // 현재 선택의 Selectable을 부모까지 찾아, 모달이 닫힌 뒤 다른 패널의 선택도 복원할 수 있게 한다.
        // UnityEngine.Object라서 ?.를 쓰지 않고 null을 직접 비교한다.
        internal static Selectable? Current()
        {
            EventSystem? system = EventSystem.current;
            if (system == null) return null;
            GameObject? selected = system.currentSelectedGameObject;
            return selected == null ? null : selected.GetComponentInParent<Selectable>();
        }

        // 숨거나 잠긴 컨트롤은 탐색 이벤트를 받지 못하므로, 활성이고 상호작용 가능한 Selectable만 고른다.
        internal static bool CanFocus(Selectable? selectable) =>
            selectable != null && selectable.IsActive() && selectable.IsInteractable();

        // 선택할 수 있고 활성 EventSystem이 있으면 선택하고 true를 돌려준다. 아니면 상태를 바꾸지 않고 false다.
        internal static bool TryFocus(Selectable? selectable)
        {
            if (!CanFocus(selectable) || EventSystem.current == null) return false;
            selectable!.Select();
            return true;
        }

        // 씬을 읽는 동안 uGUI 입력 처리를 멈추고, 멈춘 EventSystem을 돌려준다. 이미 없거나 꺼져 있으면 null이다.
        // Input System UI 모듈은 마지막으로 누른 UI(예: 메뉴의 이어하기 단추)를 기억해 매 프레임 null과 비교한다. 그 단추가 앞 씬과
        // 함께 파괴되면 이 비교(Loading.IsObjectAvailable)가 씬을 읽는 로딩 스레드의 잠금을 기다려, 커튼 위에서 1.4~1.9초 멈춘다.
        // 모듈은 포인터를 지워도 이벤트 데이터를 재사용해 그 기억이 남으므로 공개 API로 비울 수 없다. 로딩 화면은 읽는 동안 입력을 받지 않는다.
        internal static EventSystem? SuspendInput()
        {
            EventSystem? system = EventSystem.current;
            if (system == null || !system.enabled) return null;
            system.enabled = false;
            return system;
        }

        // SuspendInput이 멈춘 EventSystem을 다시 켠다. 앱이 이미 파괴했으면 아무것도 하지 않는다.
        internal static void ResumeInput(EventSystem? system)
        {
            if (system != null) system.enabled = true;
        }
    }
}
