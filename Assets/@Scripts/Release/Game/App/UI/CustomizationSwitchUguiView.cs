#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 CustomFaceUISwitch를 재구성한 `< 번호 >` 유형 스위치다. 표시 참조만 갖고, 번호를 돌리는 명령은 CustomizationScreen이 맡는다.
    [DisallowMultipleComponent]
    public sealed class CustomizationSwitchUguiView : MonoBehaviour
    {
        // 제목 참조는 제작 때 Editor의 ComponentAuthoring이 이름으로 채우고 코드에서는 읽지 않는다. 그래서 생기는 CS0414만 여기서 끈다.
#pragma warning disable CS0414
        [SerializeField] TMP_Text _label = null!;
#pragma warning restore CS0414
        [SerializeField] TMP_Text _value = null!;
        [SerializeField] Button _previous = null!;
        [SerializeField] Button _next = null!;

        public TMP_Text Value => _value;
        public Button Previous => _previous;
        public Button Next => _next;

        // 좌우 버튼은 명령이 바뀌지 않으므로 프리팹에 저장한 OnClick이 아래 공개 핸들러를 부른다.
        // 명령은 CustomizationScreen이 BindCommands로 넘긴다. 이 규칙은 Docs/rules/ui.md에 있다.
        Action? _onPrevious, _onNext;

        // 화면이 좌우 명령을 연결한다. null을 넘기면 연결을 끊는다.
        internal void BindCommands(Action? previous, Action? next)
        {
            _onPrevious = previous;
            _onNext = next;
        }

        // 프리팹에 저장한 OnClick만 부르는 진입점이다.
        public void OnPreviousClicked() => _onPrevious?.Invoke();
        public void OnNextClicked() => _onNext?.Invoke();
    }
}
