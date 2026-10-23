#nullable enable annotations
using System;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 Duckov.UI.SleepView 창(LevelManager GameplayUICanvas/SleepView)이다. 분 슬라이더, 깨는 시각, 잘 시간, 다음 날 표시를 보인다.
    // 값은 화면 컨트롤러(SleepScreen)가 넣고, 이 View는 분 바꾸기, 확인, 닫기 의도만 이벤트로 낸다. 보이기와 숨기기는 원작 FadeGroup 대신 활성 여부로 한다.
    // 원작 BlackScreen(GameManager 소속)은 제품에 공용 덮개가 없어, 제작 도구가 이 창 바로 뒤 형제로 만든 전체 화면 검정 덮개를 이 View가 함께 다룬다.
    public sealed class OriginalSleepView : MonoBehaviour
    {
        // 원작 slider다. 범위 0~1440 정수는 원작 프리팹 값 그대로 저장된다.
        [SerializeField] Slider _slider = null!;
        // 원작 willWakeUpAtText, sleepTimeSpanText, nextDayIndicator다.
        [SerializeField] TMP_Text _willWakeUpAt = null!;
        [SerializeField] TMP_Text _sleepTimeSpan = null!;
        [SerializeField] GameObject _nextDayIndicator = null!;
        // 원작 BlackScreen 대신 쓰는 덮개다. 창 밖 형제라 창을 닫아도 남고, 알파가 0이면 꺼져 입력을 막지 않는다.
        [SerializeField] CanvasGroup _blackScreen = null!;

        // UI 규칙대로 값 컨트롤(슬라이더)은 코드로 구독한다. 창이 열린 동안만 묶는다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        bool _bound;

        // 원작 OnSliderValueChanged의 Mathf.RoundToInt 결과다.
        internal event Action<int>? MinutesChanged;
        // 원작 confirmButton과 View 공통 exitButton이다. ESC 닫기는 화면 연결(GameplayScreens)이 TryClose로 처리한다.
        internal event Action? ConfirmRequested;
        internal event Action? CloseRequested;

        internal bool IsShown => gameObject.activeSelf;

        // 원작 OnEnable의 InitializeUI처럼 슬라이더를 알림 없이 마지막으로 고른 분에 맞춘다.
        internal void Show(int minutes)
        {
            gameObject.SetActive(true);
            _slider.SetValueWithoutNotify(minutes);
            if (_bound) return;
            _subscriptions.Bind<float>(_slider.onValueChanged, SliderChanged);
            _bound = true;
        }

        // 수면 순서가 검은 화면을 남긴 채 창만 닫을 때 부른다(원작 Close 뒤 BlackScreen.HideAndReturnTask). 이미 숨었어도 불러도 된다.
        internal void HideWindow()
        {
            _subscriptions.Clear();
            _bound = false;
            gameObject.SetActive(false);
        }

        // 화면 닫기와 공용 HUD 분리가 부른다. 창과 검은 화면을 모두 숨긴다. 이미 숨었어도 불러도 된다.
        internal void Hide()
        {
            HideWindow();
            SetBlackScreen(0f);
        }

        // 원작 Refresh의 세 표시다. 문구는 컨트롤러가 원작 형식("hh:mm", "hh h mm min")으로 만든다.
        internal void SetTimes(string willWakeUpAt, string sleepTimeSpan, bool nextDay)
        {
            _willWakeUpAt.text = willWakeUpAt;
            _sleepTimeSpan.text = sleepTimeSpan;
            _nextDayIndicator.SetActive(nextDay);
        }

        // 원작 BlackScreen 페이드의 한 단계다. 컨트롤러가 시간 배율과 무관하게 알파를 넘긴다. 보이는 동안 뒤의 입력을 막는다.
        internal void SetBlackScreen(float alpha)
        {
            _blackScreen.alpha = alpha;
            _blackScreen.blocksRaycasts = alpha > 0f;
            _blackScreen.gameObject.SetActive(alpha > 0f);
        }

        // 고정 버튼(확인, 닫기)의 저장된 Runtime Only OnClick이 부른다. 버튼은 열린 창 안에만 있다.
        public void OnConfirmClicked()
        {
            Log.Instance.Assert(IsShown, "수면 창이 열린 동안에만 확인합니다.", this);
            ConfirmRequested?.Invoke();
        }

        public void OnExitClicked()
        {
            Log.Instance.Assert(IsShown, "수면 창이 열린 동안에만 닫습니다.", this);
            CloseRequested?.Invoke();
        }

        void SliderChanged(float value) => MinutesChanged?.Invoke(Mathf.RoundToInt(value));
    }
}
