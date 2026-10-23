#nullable enable annotations
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 ItemDraggingPointerDisplay다. 아이템을 끄는 동안 포인터를 따라 그 아이콘을 보이고, 원본처럼 왼쪽 버튼을 떼면 스스로 숨는다.
    // 끌기 상태는 HudInventoryPanel이 소유하고 이 View는 표시만 맡는다. 켜져 있는 동안에만 Update가 돈다.
    public sealed class ItemDraggingPointerView : MonoBehaviour
    {
        [SerializeField] Image _icon = null!;

        // Show가 켠 활성화인지 구분한다. 프리팹이 꺼진 채 저장돼 Show의 SetActive가 Awake를 부를 때 다시 숨지 않게 한다.
        bool _shown;

        // 원본 Awake처럼 끌기 전에는 숨어 있게 한다.
        void Awake()
        {
            if (!_shown) gameObject.SetActive(false);
        }

        // 켜기 전에 위치를 맞춰, 첫 프레임에 앞 끌기의 자리에서 깜박이지 않게 한다.
        public void Show(Sprite? icon)
        {
            // 아이콘이 없는 아이템은 빈 그림을 그리지 않도록 HUD 칸과 같은 도우미로 끈다.
            UiVisibility.SetIcon(_icon, icon);
            _shown = true;
            Follow();
            gameObject.SetActive(true);
        }

        // 버튼 떼기와 끌기 끝 알림이 같은 끌기를 함께 숨길 수 있어, 이미 꺼져 있으면 SetActive를 다시 부르지 않는다.
        public void Hide()
        {
            _shown = false;
            UiVisibility.Set(gameObject, false);
        }

        // 끌기 끝 알림을 못 받는 경우에도 아이콘이 남지 않도록 원본처럼 왼쪽 버튼을 떼면 숨는다.
        // 끌기 상태는 패널이 놓기와 끌기 끝 알림으로 정리하므로 여기서는 표시만 끈다.
        void Update()
        {
            Follow();
            Mouse? mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasReleasedThisFrame) Hide();
        }

        // 공용 HUD 루트 캔버스는 원본처럼 Overlay라 화면 좌표가 곧 월드 위치다. 포인터 화면 좌표를 그대로 위치로 쓴다.
        void Follow()
        {
            Pointer? pointer = Pointer.current;
            if (pointer == null) return;
            transform.position = pointer.position.ReadValue();
        }
    }
}
