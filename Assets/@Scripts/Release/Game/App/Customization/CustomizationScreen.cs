#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Globalization;
using DuckCharacter;
using DuckCharacter.AvatarParts;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 편집 세션과 uGUI 구독을 화면 하나의 수명에 묶는다. 게임 세션에 확정하는 일은 완료 콜백을 받은 Runtime이 맡는다.
    public sealed class CustomizationScreen : IDisposable
    {
        // 이름으로 찾지 않고 제작 단계에서 검증된 View 참조만 쓴다. 제품 화면에는 PlayerPrefs 외형 슬롯이 없다.
        readonly CustomizationUguiView _view;
        readonly DuckCustomizationSession _session;
        readonly Transform _pivot;
        readonly DuckCharacterAnimation _animation;
        readonly Action _complete;
        readonly Action _cancel;
        readonly Func<bool> _canComplete;
        // 원본 NotificationText 알림은 씬 소유자가 넘긴 공용 HUD의 Notify로 보낸다. 공용 HUD가 필수라 이 함수도 필수다.
        readonly Action<string> _notify;
        // 화면 수명의 구독을 모아 Dispose에서 한 번에 해제한다.
        readonly UiEventSubscriptions _detach = new UiEventSubscriptions();
        // 원본 CustomFaceUI.rotateSpeed.
        const float _rotateSpeed = 15f;
        readonly List<Action<DuckAvatarPreset>> _refresh = new List<Action<DuckAvatarPreset>>();
        // 마지막으로 표시한 외형이다. 세션은 같은 값이어도 확정마다 Changed를 알리므로 늘 세션 현재값과 같다.
        // 부품 순환과 거절된 입력의 되돌림이 스냅샷 복제 대신 이 값을 읽는다. 생성자의 첫 Refresh가 채운다.
        DuckAvatarPreset _shown = null!;
        readonly System.Random _random = new System.Random();
        // 입력 잠금과 화면 종료 상태다. 잠금은 명령을 수락한 Runtime이 SetBusy로 걸고 푼다.
        bool _busy;
        bool _disposed;

        // 원본 구성의 고정 컨트롤에 세션 명령을 연결한다. 부품과 팔레트는 세션의 라이브러리에서 읽어 편집 기준과 어긋나지 않게 한다.
        // 제작 참조와 원본 탭 구성은 제작 도구와 EditMode 저작 검사가 확인하므로 런타임에서 다시 검증하지 않는다.
        // 열기에 실패하면 CustomizationFlow.OpenSpaceAsync가 컨텍스트 ClearAsync로 정리한다.
        public CustomizationScreen(CustomizationUguiView view,
            DuckCustomizationSession session, Transform pivot, DuckCharacterAnimation animation,
            Action complete, Action cancel, Func<bool> canComplete, Action<string> notify)
        {
            _view = view;
            _notify = notify;
            _session = session;
            _pivot = pivot;
            _animation = animation;
            _complete = complete;
            _cancel = cancel;
            _canComplete = canComplete;
            // 원본 구성은 CustomFaceUI.OnEnable처럼 첫 탭(머리)을 선택한 상태로 시작한다.
            // 고정 컨트롤은 View가 직접 직렬화하므로 별도 묶음 컴포넌트를 거치지 않는다.
            _view.BindTabs(_detach);
            BindOriginalAppearance();
            BindStorage();
            BindPreview();
            // 완료와 취소는 씬 소유자 콜백을 바로 부른다. 결과 안내는 소유자가, 입력 잠금은 명령을 수락한 Runtime이 맡으므로
            // 거절된 클릭은 화면을 잠그지 않는다. ESC 취소는 GameUiController가 처리한다. 고정 버튼은 프리팹의 영속 OnClick이 View 핸들러를
            // 부르므로 여기서는 명령만 넘기고 화면이 끝날 때 끊는다.
            _view.BindCommands(
                () => Run(() => { if (_canComplete()) _complete(); }),
                () => Run(_cancel),
                () => Run(Copy),
                () => Run(Paste),
                () => Run(() => _session.ApplyRandomPreset(_random)));
            _detach.Add(() => _view.BindCommands(null, null, null, null, null));
            _session.Changed += Refresh;
            _detach.Add(() => _session.Changed -= Refresh);
            Refresh(_session.Snapshot());
            RefreshCompletion();
        }

        // 원본 CustomFaceUI.Init/LoadValues처럼 머리 설정과 부위별 부품, 색, 수치 컨트롤을 세션 명령에 연결하는 유일한 곳이다.
        // 행 제목은 프리팹의 LocalizedUguiText가 원본 UI_CustomFace_* 키로 표시하고, 조절 항목과 범위는 원본 부위 제한표를 따른다.
        // 원본 행은 View가 직접 직렬화하므로 키 조회도 별도 묶음 컴포넌트 없이 View에서 한다.
        void BindOriginalAppearance()
        {
            // 세션이 ID 보정에 쓰는 라이브러리를 읽어 화면 선택지와 보정 기준을 맞춘다.
            DuckAvatarPartLibrary library = _session.Library;
            // 원본처럼 피부색과 모든 부위가 같은 색 목록 하나를 쓴다. 팔레트는 라이브러리 에셋의 직렬화 값이라 세션처럼 바로 읽는다.
            // 세션 색상 명령은 결과(bool)를 돌려주므로 선택 대리자에는 람다로 연결한다. 행 키는 View의 키 함수가 프리팹 키와 같게 만든다.
            IReadOnlyList<DuckColorObject> palette = library.Palette!.Common;
            BindColorPicker(_view.ColorPicker(CustomizationUguiView.MainColorKey), palette,
                appearance => appearance._headSetting._mainColor, index => _session.SelectMainColor(index));
            foreach (DuckAvatarParameter parameter in DuckAvatarPartLimitRange.HeadParameters)
            {
                // 저장 필드 읽기는 쓰기와 같은 세션 대응 함수를 쓴다. foreach 변수는 반복마다 새로 잡혀 람다가 바로 붙잡아도 된다.
                AddSlider(_view.Slider(CustomizationUguiView.HeadSliderKey(parameter)), DuckAvatarPartLimitRange.GetHeadLimit(parameter),
                    appearance => DuckCustomizationSession.GetHeadSettingValue(appearance._headSetting, parameter),
                    number => _session.SetHeadValue(parameter, number),
                    text => _session.TrySetHeadValue(parameter, text));
            }
            for (DuckAvatarPartType type = 0; type < DuckAvatarPartType.Count; type++)
            {
                DuckAvatarPartType category = type;
                // GetCategory는 범위 안 부위에 항상 카테고리를 돌려주므로 null을 검사하지 않는다.
                IReadOnlyList<int> ids = library.GetCategory(category)!.AllIDs();
                // 부위 행도 View의 키 함수로 찾아 프리팹 키와 어긋나지 않게 한다.
                BindSwitch(_view.Switch(CustomizationUguiView.SwitchKey(category)), category, ids);
                DuckAvatarPartLimitRange rule = DuckAvatarPartLimitRange.For(category);
                // 부위 색도 피부색과 같은 공통 목록에서 고른다.
                if (rule.SupportsColor)
                    BindColorPicker(_view.ColorPicker(CustomizationUguiView.ColorKey(category)), palette,
                        appearance => appearance.GetInfo(category)._color,
                        index => _session.SelectColor(category, index));
                foreach (DuckAvatarParameter parameter in rule.Parameters)
                {
                    // 저장 필드 읽기는 쓰기와 같은 세션 대응 함수를 쓴다. foreach 변수는 반복마다 새로 잡혀 람다가 바로 붙잡아도 된다.
                    if (rule.TryGetLimit(parameter, out DuckAvatarPartLimitRange.Limit limit))
                        AddSlider(_view.Slider(CustomizationUguiView.PartSliderKey(category, parameter)), limit,
                            appearance => DuckCustomizationSession.GetPartValue(appearance.GetInfo(category), parameter),
                            number => _session.SetValue(category, parameter, number),
                            text => _session.TrySetValue(category, parameter, text));
                }
            }
        }

        // 원본 CustomFaceUISwitch처럼 좌우 버튼으로 같은 부위의 부품을 순환하고(끝에서 처음으로), 현재 부품 ID를 표시한다.
        void BindSwitch(CustomizationSwitchUguiView row, DuckAvatarPartType category, IReadOnlyList<int> ids)
        {
            row.Previous.interactable = ids.Count > 1;
            row.Next.interactable = ids.Count > 1;
            // 부품이 두 개 미만이면 좌우 버튼이 비활성이라 입력이 닿지 않으므로 빈 목록을 검사하지 않는다.
            void Step(int direction)
            {
                int current = IndexOf(ids, _shown.GetID(category));
                int next = current < 0 ? 0 : (current + direction + ids.Count) % ids.Count;
                _session.SelectPart(category, ids[next]);
            }
            // 좌우 버튼은 프리팹 영속 OnClick이 행 View 핸들러를 부르므로 명령만 넘기고 화면 종료 때 끊는다.
            row.BindCommands(() => Run(() => Step(-1)), () => Run(() => Step(1)));
            _detach.Add(() => row.BindCommands(null, null));
            // 슬라이더 드래그처럼 다른 값만 바뀔 때마다 ID 문자열을 새로 만들지 않도록, ID가 바뀐 경우에만 텍스트를 쓴다.
            // 이 텍스트는 이 갱신만 쓰고 번역 구독도 없어서 마지막 표시 ID와 어긋나지 않는다.
            bool shown = false;
            int shownId = 0;
            _refresh.Add(appearance =>
            {
                int id = appearance.GetID(category);
                if (shown && id == shownId) return;
                shown = true;
                shownId = id;
                row.Value.text = ids.Count == 0 ? "-" : id.ToString(CultureInfo.InvariantCulture);
            });
        }

        // 원본 CustomFaceUIColorPicker처럼 현재 색 막대 버튼으로 팔레트 격자를 펼치고, 칸을 누르면 색을 적용한 뒤 격자를 닫는다.
        // 격자 펼침도 공통 조작 판정(CanInteract)을 따른다.
        void BindColorPicker(CustomizationColorPickerUguiView row, IReadOnlyList<DuckColorObject> palette,
            Func<DuckAvatarPreset, Color> read, Action<int> select)
        {
            var colors = new List<Color>();
            // 저작된 팔레트는 늘 있고 EditMode 테스트가 확인한다. 빈 목록이면 아래에서 펼침 버튼만 꺼진다.
            foreach (DuckColorObject entry in palette) colors.Add(entry.Color);
            // 격자를 먼저 닫아 두어 팔레트를 받은 행 View가 칸을 처음 펼칠 때까지 만들지 않게 한다(Docs/runtime-boundaries.md#main-thread-split).
            row.SetGridOpen(false);
            row.SetPalette(colors);
            row.Toggle.interactable = colors.Count > 0;
            // 펼침 버튼은 프리팹 영속 OnClick이 행 View 핸들러를 부르므로 명령만 넘기고 화면 종료 때 끊는다.
            row.BindToggle(() => { if (CanInteract()) row.SetGridOpen(!row.IsGridOpen); });
            _detach.Add(() => row.BindToggle(null));
            // 팔레트 칸은 행 View가 만들며 구독하고 누른 칸의 팔레트 번호를 넘긴다. 칸마다 구독을 등록하지 않는다.
            row.BindSelect(paletteIndex =>
            {
                if (!CanInteract()) return;
                select(paletteIndex);
                row.SetGridOpen(false);
            });
            _detach.Add(() => row.BindSelect(null));
            _refresh.Add(appearance => row.SetCurrent(read(appearance)));
        }

        // 슬라이더와 선택적 숫자 입력이 같은 세션 값을 바꾼다. 숫자 입력은 세션 Try 명령이 문화권과 무관하게 점 소수점 유한수만 받는다.
        // 수락된 값은 Changed 뒤 Refresh가 행을 맞추므로, 거절된 경우에만 마지막 표시 외형(_shown)으로 되돌린다.
        // 원본처럼 조작한 행만 다시 그리도록 BindSwitch와 같이 행마다 마지막 표시 값을 기억하고, 값이 같으면 숫자 문자열을 만들지 않는다.
        void AddSlider(LabeledSliderUguiView row, DuckAvatarPartLimitRange.Limit limit,
            Func<DuckAvatarPreset, float> read, Func<float, bool> write, Func<string, bool> writeText)
        {
            row.Slider.minValue = limit._min;
            row.Slider.maxValue = limit._max;
            bool shown = false;
            float shownValue = 0f;
            void Show(float value)
            {
                shown = true;
                shownValue = value;
                SetSliderValue(row, value);
            }
            // 사용자가 행을 움직이면 표시가 기억과 달라지므로, 잠금으로 명령이 막혀도 먼저 기억을 버려 다음 갱신이 이 행을 다시 쓰게 한다.
            // 콜백마다 Run용 클로저를 만들지 않도록 CanInteract를 대리자 안에서 직접 검사한다.
            _detach.Bind(row.Slider.onValueChanged, value =>
            {
                shown = false;
                if (CanInteract() && !write(value)) Show(read(_shown));
            });
            if (row.Input != null)
                _detach.Bind(row.Input.onEndEdit, text =>
                {
                    shown = false;
                    if (CanInteract() && !writeText(text)) Show(read(_shown));
                });
            _refresh.Add(appearance =>
            {
                float value = read(appearance);
                if (shown && value == shownValue) return;
                Show(value);
            });
        }

        // 원본 DATA_3의 YellowDuck, 복사, 붙여넣기 명령은 생성자가 연결한다. 여기서는 YellowDuck 버튼의 사용 가능 여부만 정한다.
        void BindStorage()
        {
            // 원본 DATA_3/YellowDuck은 CustomFaceUI.RandomPreset(presets = [CustomFacePreset_YellowDuck])을 부른다.
            // 라이브러리 프리셋 목록이 그 원본 목록이며, EditMode 테스트가 목록 구성을 고정한다.
            // 라이브러리는 편집 세션이 이미 들고 있으므로 화면이 따로 보관하지 않고 세션에서 읽는다.
            _view.YellowDuckButton.interactable = _session.Library.Presets.Count > 0;
        }

        // 원본 Interact_CustomFace.CopyToClipboard처럼 검증된 초안을 원본 JSON으로 복사하고 알린다. 실패하는 경로는 없다.
        void Copy()
        {
            GUIUtility.systemCopyBuffer = _session.ExportOriginalJson(true);
            Notify("UI_CustomFace_OnCopy");
        }

        // 원본 PastyDataAndApply처럼 붙여넣기 결과를 알린다. 실패하면 초안을 그대로 둔다.
        // 손상된 입력의 원인은 세션 읽기 경계가 한 번 기록한다.
        void Paste() =>
            Notify(_session.TryImportOriginalJson(GUIUtility.systemCopyBuffer) ? "UI_CustomFace_OnPasteSuccess" : "UI_CustomFace_OnPasteFailed");

        // 원본 DragHandler처럼 화면 드래그로 캐릭터를 회전한다.
        void BindPreview()
        {
            CustomizationDragArea drag = _view.DragArea;
            // 원본 CustomFaceUI.OnDrag처럼 왼쪽 버튼 드래그의 -Δx × rotateSpeed × 프레임 시간만큼 위쪽 축으로 돌린다.
            // 편집 중에는 timeScale이 0일 수 있어 unscaledDeltaTime을 쓴다.
            Action<PointerEventData> dragged = eventData =>
            {
                if (!CanInteract() || eventData.button != PointerEventData.InputButton.Left) return;
                _pivot.Rotate(Vector3.up, -eventData.delta.x * _rotateSpeed * Time.unscaledDeltaTime);
            };
            drag.Dragged += dragged;
            _detach.Add(() => { if (drag != null) drag.Dragged -= dragged; });
        }

        // 원본 NotificationText.Push처럼 현재 로캘 문구(GameLocalization.Text)를 씬 소유자의 알림 함수로 보낸다.
        void Notify(string key) => _notify(GameLocalization.Text(key));

        // 미리보기 애니메이션을 진행하며, 공통 조작 판정이 거부하는 동안은 멈춘다.
        public void Tick()
        {
            if (!CanInteract()) return;
            _animation.TickPreview();
        }

        // 전환 중에는 uGUI 입력뿐 아니라 이미 큐에 들어온 조작 콜백도 막는다. 컨텍스트가 종료한 화면의 참조를 먼저 끊으므로
        // 이 공개 메서드들은 종료 여부를 검사하지 않는다.
        public void SetBusy(bool busy)
        {
            _busy = busy;
            _view.SetWorkspaceInteractable(!busy);
            _view.CancelButton.interactable = !busy;
            RefreshCompletion();
        }

        // 실패한 미리보기는 수정하거나 취소할 수 있지만 완료할 수 없다.
        public void RefreshCompletion() => _view.CompleteButton.interactable = !_busy && _canComplete();

        // 확인, 취소, 미리보기 실패 안내 키를 원본 NotificationText 알림으로 띄우고 초안은 유지한다.
        public void ShowError(string? key)
        {
            if (string.IsNullOrEmpty(key)) return;
            Notify(key!);
        }

        // 입장할 때 취소 버튼을 EventSystem 선택 대상으로 두어 키보드와 패드 입력이 돌아올 곳을 만든다.
        public void Focus() => UiFocus.TryFocus(_view.CancelButton);

        // 모든 화면 조작이 거치는 공통 판정이다.
        bool CanInteract() => !_disposed && !_busy;

        // 변경 알림은 값만 갱신하여 슬라이더 조작 중 동적 컨트롤을 재생성하지 않는다.
        void Refresh(DuckAvatarPreset appearance)
        {
            // 갱신 함수가 던져도 되돌림과 부품 순환이 최신 외형을 읽도록 루프보다 먼저 대입한다.
            _shown = appearance;
            foreach (Action<DuckAvatarPreset> refresh in _refresh) refresh(appearance);
        }

        // 화면 명령은 모두 여기를 지나며, 입력 잠금 중이거나 종료한 화면에서는 실행하지 않는다.
        // 세션 명령은 실패를 반환값으로 알리므로 여기서 예외를 잡지 않는다.
        void Run(Action action)
        {
            if (CanInteract()) action();
        }

        // 세션 값으로 되돌릴 때 슬라이더와 숫자 입력을 알림 없이 함께 맞춘다. 숫자는 원본 CustomFaceSlider 형식(0.##)이다.
        static void SetSliderValue(LabeledSliderUguiView row, float value)
        {
            row.Slider.SetValueWithoutNotify(value);
            TMP_InputField? input = row.Input;
            if (input != null) input.SetTextWithoutNotify(value.ToString("0.##", CultureInfo.InvariantCulture));
        }

        // 여러 번 불러도 안전하며, 종료한 화면이 새 편집을 바꾸지 않게 한다. 구독 해제를 끝까지 시도하고 실패는 Cleanup이
        // 기록하며 던지지 않는다. 프리팹의 고정 UI 계층은 파괴하지 않는다. 해제 뒤에는 Refresh가 불리지 않으므로 _refresh는 비우지 않는다.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cleanup.Run(_detach.Dispose);
        }

        // IReadOnlyList에는 IndexOf가 없어 직접 찾는다. 없으면 -1이다.
        static int IndexOf(IReadOnlyList<int> ids, int id)
        {
            for (int index = 0; index < ids.Count; index++)
                if (ids[index] == id) return index;
            return -1;
        }
    }
}
