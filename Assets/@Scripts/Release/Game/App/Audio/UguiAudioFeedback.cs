#nullable enable annotations
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.App
{
    // 원본 ButtonAnimation처럼 버튼 프리팹에 저작해, 포인터가 들어오면 hover, 왼쪽 버튼을 누르면 click 소리를 앱 오디오 관리자로 낸다.
    // 자식 Graphic의 이벤트가 아니라 버튼 자신의 입력만 본다. 템플릿에서 복제한 버튼도 이 컴포넌트를 함께 가져가 소리를 낸다.
    [DisallowMultipleComponent]
    public sealed class UguiAudioFeedback : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler
    {
        // 원본 AudioManager.Post("UI/hover"), Post("UI/click")의 FMOD 경로다.
        const string _hoverSound = "event:/UI/hover";
        const string _clickSound = "event:/UI/click";
        // 원본 ButtonAnimation.mute처럼 버튼마다 소리를 끈다.
        [SerializeField] bool _mute;
        UnityEngine.UI.Selectable? _selectable;

        // 같은 GameObject의 Selectable을 캐시해, 꺼져 있거나 누를 수 없는 버튼에서는 소리를 내지 않는다.
        void Awake() => _selectable = GetComponent<UnityEngine.UI.Selectable>();

        // 원본은 버튼 상태와 관계없이 소리를 내지만, 여기서는 음소거했거나 꺼졌거나 누를 수 없는 버튼을 조용히 둔다.
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Audible()) Play(_hoverSound);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && Audible()) Play(_clickSound);
        }

        bool Audible() => !_mute && _selectable != null && _selectable.IsActive() && _selectable.IsInteractable();

        // 앱 없이 도는 에디터 검사 장면에서는 오디오 관리자가 없어 조용히 건너뛴다. 열리지 않은 관리자는 재생을 무시한다.
        static void Play(string eventPath)
        {
            if (GameManager.HasInstance) GameManager.Audio.PlayOneShot(eventPath);
        }
    }
}
