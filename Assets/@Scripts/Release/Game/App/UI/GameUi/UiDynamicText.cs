#nullable enable annotations
using System.Collections.Generic;
using TMPro;

namespace Game.App
{
    // 상태에 따라 바뀌는 TMP 문구를 번역해 넣는다. 같은 요소의 정적 LocalizedUguiText를 꺼 동적 문구를 덮지 않게 하고,
    // 같은 id의 구독을 교체해 마지막 키의 비동기 결과만 적용한다. 번역 구독은 UI 바인딩 동안만 살아 있다.
    internal sealed class UiDynamicText
    {
        // 정적 번역을 이미 끈 동적 문구 요소다. 같은 요소의 두 번째 Set부터 TryGetComponent를 건너뛴다.
        // Release 코드에는 끈 정적 번역을 다시 켜는 곳이 없어서 바인딩이 바뀌어도 기억해 둔다.
        readonly HashSet<TMP_Text> _dynamicElements = new HashSet<TMP_Text>();
        // 바인딩 동안의 번역 구독이다. 바인딩 밖에서는 null이고, Set은 앱 UI가 바인딩된 동안에만 불린다.
        LocalizedTextBindings? _bindings;

        internal void Bind() => _bindings = new LocalizedTextBindings();

        // 해제된 UI에는 로캘 변경 콜백이 도착하지 않게 한다.
        internal void Unbind()
        {
            _bindings?.Dispose();
            _bindings = null;
        }

        // id는 호출자가 넘기는 고정 문자열이며 제품 객체 이름과 같은 값이다.
        internal void Set(TMP_Text element, string key, string id)
        {
            // 요소마다 처음 한 번만 정적 번역을 찾아 끈다. 정적 번역이 없는 요소에서도 할당이 생기지 않게 TryGetComponent를 쓴다.
            if (_dynamicElements.Add(element) && element.TryGetComponent(out LocalizedUguiText staticBinding)) staticBinding.enabled = false;
            // 호출자는 모두 앱 UI의 바인딩 확인 뒤에 오므로 번역 구독이 있다.
            _bindings!.Set(id, key, value => element.text = value);
        }
    }
}
