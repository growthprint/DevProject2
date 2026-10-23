#nullable enable annotations
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 HUDCanvas/EvacuationCountDown의 EvacuationCountdownUI를 재구성한 철수 진행 게이지와 남은 시간 숫자다.
    // 원본 FadeGroup처럼 CanvasGroupFade의 fadeDuration 동안 선형으로 나타나고 사라진다. 철수 판정은 컨트롤러가 맡는다.
    [DisallowMultipleComponent]
    public sealed class EvacuationCountdownView : MonoBehaviour
    {
        [SerializeField] CanvasGroup _group = null!;
        [SerializeField] Image _fill = null!;
        [SerializeField] TMP_Text _digits = null!;
        // 원본 LevelManager에 직렬화된 형식을 제작 도구가 그대로 옮긴다. 코드 기본값도 원본과 SharedHUD 프리팹 값과 같다.
        [SerializeField] string _digitFormat = "{0:00}:{1:00}.<sub> {2:000}</sub>";
        // 최소 페이드 시간은 Editor 제작 도구와 같은 UiFade 상수를 쓴다.
        [SerializeField, Min(UiFade._minimumDuration)] float _fadeDuration = .2f;

        public bool IsShown { get; private set; }

        // 매 프레임 string.Format이 만드는 박싱과 새 문자열을 피하려고 재사용 글자 배열에 숫자를 직접 쓰는 도우미다.
        // _digitFormat 참조가 바뀌면 도우미가 다시 분해한다.
        readonly UiDigitFormat _digitWriter = new UiDigitFormat();

        // 원본 Refresh처럼 진행률과 남은 시간을 매 프레임 갱신한다.
        public void Show(float remaining, float progress)
        {
            IsShown = true;
            _fill.fillAmount = Mathf.Clamp01(progress);
            // 보통은 할당 없이 재사용 배열에 쓰고, 그럴 수 없을 때만 Format으로 같은 글자열을 만든다.
            // 무한대 같은 비유한 시간을 정수로 바꿔 인수가 음수가 되면 재사용 경로가 거절한다.
            float seconds = Mathf.Max(0f, remaining);
            Split(seconds, out int minutes, out int whole, out int milliseconds);
            if (!_digitWriter.TryWrite(_digits, _digitFormat, minutes, whole, milliseconds)) _digits.text = Format(_digitFormat, seconds);
        }

        public void Hide() => IsShown = false;

        // 공용 HUD 소유자 전환 때 철수 표시를 페이드 없이 즉시 숨긴다.
        internal void ResetImmediate()
        {
            IsShown = false;
            _group.alpha = 0f;
        }

        // 원본 ToDigitString처럼 분, 초, 1/1000초를 형식 문자열에 넣는다. 재사용 배열에 쓸 수 없을 때와 테스트가 쓴다.
        internal static string Format(string format, float seconds)
        {
            Split(seconds, out int minutes, out int whole, out int milliseconds);
            return string.Format(CultureInfo.InvariantCulture, format, minutes, whole, milliseconds);
        }

        // 원본 ToDigitString의 분, 초, 1/1000초 계산이다. 1/1000초는 999를 넘지 않는다. Format과 Show가 함께 쓴다.
        static void Split(float seconds, out int minutes, out int wholeSeconds, out int milliseconds)
        {
            int whole = (int)seconds;
            milliseconds = Mathf.Min(999, Mathf.RoundToInt((seconds - whole) * 1000f));
            minutes = whole / 60;
            wholeSeconds = whole - minutes * 60;
        }

        // 원본 FadeGroup처럼 시간 배율과 상관없이 나타나고 사라진다. 다른 페이드 View와 같은 UiFade 규칙을 쓴다.
        internal void Tick(float deltaTime) => _group.alpha = UiFade.Step(_group.alpha, IsShown ? 1f : 0f, deltaTime, _fadeDuration);

        void Update() => Tick(Time.unscaledDeltaTime);
    }
}
