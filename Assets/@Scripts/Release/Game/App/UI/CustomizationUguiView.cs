#nullable enable annotations
using System;
using System.Collections.Generic;
using DuckCharacter.AvatarParts;
using UnityEngine;

namespace Game.App
{
    // 커스터마이징 화면의 고정 컨트롤과 원본 구성 참조를 직렬화한다. 프리팹이 스크립트 GUID와 필드 이름으로 값을 찾으므로
    // 클래스 이름을 파일 이름과 같게 두고 직렬화 필드 이름을 바꾸지 않는다.
    [DisallowMultipleComponent]
    public sealed class CustomizationUguiView : MonoBehaviour
    {
        // 작업 영역 참조는 제작 도구가 채우고 테스트만 직렬화 필드로 읽는다. 제품 코드가 읽지 않아 생기는 CS0414만 여기서 끈다.
#pragma warning disable CS0414
        [SerializeField] GameObject _workspace = null!;
#pragma warning restore CS0414
        // 편집 잠금은 원본 작업 패널의 CanvasGroup 하나로 모든 자식을 함께 잠근다.
        [SerializeField] CanvasGroup _workspaceGroup = null!;
        // 화면 하나를 View 하나가 표시하도록 고정 컨트롤 묶음을 별도 컴포넌트로 나누지 않고 이 View가 직접 직렬화한다.
        // 원본 CustomFaceCanvas의 고정 외형 조절 항목이다. 슬라이더, 유형 스위치, 색상 선택 행과 부위 탭을 원본 순서로 연결한다.
        // 항목은 미리 저작되어 실행 중에 계층을 만들지 않는다. KeyedView의 _key, _view 이름은 저장된 직렬화 데이터와 맞아야 하므로 유지한다.
        [SerializeField] KeyedView<LabeledSliderUguiView>[] _sliders = Array.Empty<KeyedView<LabeledSliderUguiView>>();
        [SerializeField] KeyedView<CustomizationSwitchUguiView>[] _switches = Array.Empty<KeyedView<CustomizationSwitchUguiView>>();
        [SerializeField] KeyedView<CustomizationColorPickerUguiView>[] _colors = Array.Empty<KeyedView<CustomizationColorPickerUguiView>>();
        [SerializeField] CustomizationTabUguiView[] _tabs = Array.Empty<CustomizationTabUguiView>();
        Dictionary<string, LabeledSliderUguiView>? _sliderIndex;
        Dictionary<string, CustomizationSwitchUguiView>? _switchIndex;
        Dictionary<string, CustomizationColorPickerUguiView>? _colorIndex;
        // 아래 참조는 모두 원본 CustomFaceCanvas 구성의 필수 컨트롤이다.
        // 복사와 붙여넣기 버튼은 제작 도구와 테스트만 직렬화 필드로 읽는다. 그래서 생기는 CS0414만 여기서 끈다.
#pragma warning disable CS0414
        [SerializeField] UnityEngine.UI.Button _exportButton = null!;
        [SerializeField] UnityEngine.UI.Button _importButton = null!;
#pragma warning restore CS0414
        [SerializeField] UnityEngine.UI.Button _completeButton = null!;
        [SerializeField] UnityEngine.UI.Button _cancelButton = null!;
        // 원본에서 CustomFaceUI.RandomPreset을 부르는 DATA_3/YellowDuck 버튼과 캐릭터 회전용 DragHandler 영역이다.
        // 프리팹이 늘 연결하므로 필수다. 복사와 붙여넣기 결과를 알리는 원본 NotificationText는 커스터마이징 씬의 공용 HUD가 표시한다.
        [SerializeField] UnityEngine.UI.Button _yellowDuckButton = null!;
        [SerializeField] CustomizationDragArea _dragArea = null!;

        public UnityEngine.UI.Button CompleteButton => _completeButton;
        public UnityEngine.UI.Button CancelButton => _cancelButton;
        public UnityEngine.UI.Button YellowDuckButton => _yellowDuckButton;
        public CustomizationDragArea DragArea => _dragArea;

        // 완료, 취소, 복사, 붙여넣기, YellowDuck 버튼은 명령이 바뀌지 않으므로 프리팹에 저장한 OnClick이 아래 공개 핸들러를 부른다.
        // CustomizationScreen은 BindCommands로 명령만 넘긴다. 이 규칙은 Docs/rules/ui.md에 있다.
        Action? _complete, _cancel, _copy, _paste, _yellowDuck;

        // 화면이 고정 버튼 명령을 연결한다. null을 넘기면 연결을 끊는다.
        internal void BindCommands(Action? complete, Action? cancel, Action? copy, Action? paste, Action? yellowDuck)
        {
            _complete = complete;
            _cancel = cancel;
            _copy = copy;
            _paste = paste;
            _yellowDuck = yellowDuck;
        }

        // 프리팹에 저장한 OnClick만 부르는 진입점이다. 명령은 화면이 입력 잠금과 종료 여부를 확인한 뒤에만 실행된다.
        public void OnCompleteClicked() => _complete?.Invoke();
        public void OnCancelClicked() => _cancel?.Invoke();
        public void OnCopyClicked() => _copy?.Invoke();
        public void OnPasteClicked() => _paste?.Invoke();
        public void OnYellowDuckClicked() => _yellowDuck?.Invoke();

        // 저장이나 전환 중에는 작업 영역 전체의 포인터와 키보드 입력을 함께 잠근다.
        public void SetWorkspaceInteractable(bool interactable)
        {
            _workspaceGroup.interactable = interactable;
            _workspaceGroup.blocksRaycasts = interactable;
        }

        // 원본 행을 의미 키로 찾는다. 키 연결은 ComponentAuthoring.ConfigureOriginal이 만들고 ProductUiAssetTests가 확인한다.
        public LabeledSliderUguiView Slider(string key) => Find(ref _sliderIndex, _sliders, key);

        public CustomizationSwitchUguiView Switch(string key) => Find(ref _switchIndex, _switches, key);

        public CustomizationColorPickerUguiView ColorPicker(string key) => Find(ref _colorIndex, _colors, key);

        // 화면이 행을 찾을 때 쓰는 의미 키다. 프리팹 KeyedView의 저장된 _key 값과 같아야 하고, 부위와 수치 이름은 enum 이름 그대로라
        // enum 이름을 바꾸면 저작된 키도 함께 바꿔야 한다.
        internal static string MainColorKey => "mainColor";

        internal static string HeadSliderKey(DuckAvatarParameter parameter) => "head" + parameter;

        internal static string PartSliderKey(DuckAvatarPartType type, DuckAvatarParameter parameter) => "value" + type + parameter;

        internal static string SwitchKey(DuckAvatarPartType type) => "switch" + type;

        internal static string ColorKey(DuckAvatarPartType type) => "color" + type;

        // 탭 클릭을 화면 수명 구독에 연결하고, 원본 CustomFaceUI.OnEnable처럼 첫 탭(머리)을 고른다.
        // 탭 구성은 저작 검사가 보장하므로 빈 탭 목록을 검사하지 않는다.
        internal void BindTabs(UiEventSubscriptions subscriptions)
        {
            // 탭마다 대리자를 새로 만들지 않도록 하나를 모든 탭의 구독과 해제에 함께 쓴다.
            Action<CustomizationTabUguiView> clicked = SelectTab;
            foreach (CustomizationTabUguiView tab in _tabs)
            {
                tab.Clicked += clicked;
                subscriptions.Add(() => { if (tab != null) tab.Clicked -= clicked; });
            }
            SelectTab(_tabs[0]);
        }

        // 원본 CustomFaceUI.SelectTab처럼 선택한 탭의 패널만 켜고 탭 배경색을 바꾼다.
        // 탭 패널 참조는 저작 검사가 확인하므로 null을 검사하지 않는다.
        public void SelectTab(CustomizationTabUguiView selected)
        {
            foreach (CustomizationTabUguiView tab in _tabs)
                foreach (GameObject panel in tab.Panels)
                    panel.SetActive(false);
            foreach (GameObject panel in selected.Panels)
                panel.SetActive(true);
            foreach (CustomizationTabUguiView tab in _tabs) tab.SetSelected(tab == selected);
        }

        // 처음 조회할 때 Ordinal 색인을 한 번 만든다. 화면은 코드 상수 키만 조회하고,
        // 키 중복과 연결 누락은 저작 결함이라 ProductUiAssetTests가 프리팹에서 확인한다.
        static T Find<T>(ref Dictionary<string, T>? index, KeyedView<T>[] entries, string key) where T : Component
        {
            if (index == null)
            {
                index = new Dictionary<string, T>(entries.Length, StringComparer.Ordinal);
                foreach (KeyedView<T> entry in entries)
                    index.TryAdd(entry._key, entry._view);
            }
            index.TryGetValue(key, out T view);
            return view;
        }

        // 원본 슬라이더, 유형 스위치, 색상 선택 행 하나를 의미 키와 묶는다.
        [Serializable] sealed class KeyedView<T> where T : Component
        {
            [SerializeField] internal string _key = "";
            [SerializeField] internal T _view = null!;
        }
    }
}
