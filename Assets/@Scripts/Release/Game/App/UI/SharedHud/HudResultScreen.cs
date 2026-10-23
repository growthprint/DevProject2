#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game.App
{
    // 원본 GameplayUICanvas/ClosureView의 사망과 철수 결과 화면이다. 페이드와 경험치 표시가 끝난 뒤에만 계속 버튼을 보이고,
    // 버튼이 눌릴 때까지 기다린다. 프리팹 참조는 생성자로 다시 받지 않고 SharedHudView의 직렬화 필드를 직접 읽는다.
    internal sealed class HudResultScreen
    {
        // 원본 ClosureView의 결과 페이드 시간과 경험치 표시 시간이다.
        const float _resultFadeSeconds = .5f, _resultExperienceSeconds = 2f;
        const string _resultEndOpen = "/<sub>", _resultEndClose = "</sub>", _resultNoEnd = "∞";
        // 사망 결과 제목의 붉은 글자색이다. 철수 결과는 흰색이다.
        static readonly Color _deadTitleColor = new(.9725491f, .33333334f, .40000004f);
        // 경험치가 오르는 동안 효과음을 다시 내는 최소 간격(초)이다.
        const float _experienceSoundInterval = .05f;
        // 결과 화면 효과음의 FMOD 이벤트 경로다. 경험치 상승, 계속 버튼 표시, 계속 클릭 순서로 쓴다.
        const string _experienceUpSound = "event:/UI/exp_up", _continueShownSound = "event:/UI/pop", _continueSound = "event:/UI/confirm";

        readonly SharedHudView _hud;
        // 계속 버튼이면 true, 소유자 전환으로 표시가 끝나면 false로 완료한다. 취소 예외 대신 결과 값으로 알린다.
        UniTaskCompletionSource<bool>? _continueCompletion;
        // 경험치 애니메이션이 매 프레임 문자열을 만들지 않게 한다. 레벨 문구는 레벨이 바뀔 때만 쓰며, 아직 쓰지 않았으면 int.MinValue다.
        // 경험치 문구는 재사용 배열에 숫자를 직접 쓴다. 64자는 long 19자리 두 개와 태그 12자를 담는다.
        int _shownResultLevel = int.MinValue;
        readonly char[] _resultExperienceChars = new char[64];

        internal HudResultScreen(SharedHudView hud) => _hud = hud;

        // 결과 화면 효과음은 씬 오디오 소유자가 재생한다.
        public event Action<string>? PresentationSound;
        // 결과 패널이 켜져 있는 동안을 결과 화면 열림으로 보고, HUD를 처음 준비할 때는 닫아 둔다.
        public bool IsOpen => _hud._resultPanel.activeSelf;
        internal void Initialize() => _hud._resultPanel.SetActive(false);

        // 소유자가 바뀔 때 결과 패널을 닫고 진행 중인 결과 표시를 끝낸다. 완료가 연속을 그 자리에서 실행할 수 있어 상태를 먼저 되돌린다.
        // 표시 중이던 ShowAsync는 남은 표시를 건너뛰고 false로 끝난다.
        internal void Reset()
        {
            UniTaskCompletionSource<bool>? completion = _continueCompletion;
            _continueCompletion = null;
            _hud._resultPanel.SetActive(false);
            completion?.TrySetResult(false);
        }

        // 계속 버튼의 프리팹 영속 OnClick이 SharedHudView를 거쳐 부른다. 결과 화면이 계속을 기다리는 동안만 진행을 풀어 준다.
        internal void Continue() => _continueCompletion?.TrySetResult(true);

        // 원본 ClosureView처럼 페이드와 경험치 표시가 끝난 뒤에만 계속 버튼을 보인다. 버튼이 실제로 눌릴 때까지 기다려,
        // 호출자의 저장과 전환 명령이 그 뒤에만 진행되게 한다. 계속을 눌렀으면 true, 소유자 전환으로 끝났으면 false다.
        public async UniTask<bool> ShowAsync(string title, long fromExperience, long experience, string continueText, bool dead)
        {
            _hud._resultTitle.text = title;
            // 사망과 철수를 제목 색으로도 구분한다.
            _hud._resultTitle.color = dead ? _deadTitleColor : Color.white;
            _hud._continueLabel.text = continueText;
            _hud._continue.gameObject.SetActive(false);
            // 이번 호출의 완료 원본을 지역 변수에도 둔다. 소유자 전환의 Reset으로 필드가 다른 값이 되면 이 표시는 취소된 것이다.
            UniTaskCompletionSource<bool> completion = new();
            _continueCompletion = completion;
            _hud.HideInteraction();
            _hud._resultPanel.SetActive(true);
            // 원본 ClosureView.SetupBeginning처럼 페이드 전에 출격 시작 경험치로 막대와 문구를 채운다.
            // 채우지 않으면 첫 결과 화면은 페이드 동안 프리팹 자리표시 값(513513513, Lv.24)을 보인다.
            _shownResultLevel = int.MinValue;
            PresentExperience(fromExperience, fromExperience);
            try
            {
                float elapsed = 0;
                // 취소된 표시는 다음 소유자의 HUD를 건드리지 않도록 루프를 멈춘다.
                while (elapsed < _resultFadeSeconds && ReferenceEquals(_continueCompletion, completion))
                {
                    elapsed += Time.unscaledDeltaTime;
                    _hud._resultFade.alpha = Mathf.Clamp01(elapsed / _resultFadeSeconds);
                    await UniTask.NextFrame();
                }
                elapsed = 0;
                float soundAt = 0;
                // 페이드 루프처럼 취소된 표시는 여기서도 멈춘다.
                while (fromExperience != experience && elapsed < _resultExperienceSeconds && ReferenceEquals(_continueCompletion, completion))
                {
                    elapsed += Time.unscaledDeltaTime;
                    PresentExperience(fromExperience + (long)((experience - fromExperience) * _hud._resultCurve.Evaluate(Mathf.Clamp01(elapsed / _resultExperienceSeconds))), fromExperience);
                    // 매 프레임 효과음이 겹치지 않도록 정한 간격마다 한 번만 낸다.
                    if (elapsed >= soundAt) { PresentationSound?.Invoke(_experienceUpSound); soundAt = elapsed + _experienceSoundInterval; }
                    await UniTask.NextFrame();
                }
                // 끝난 표시는 계속 버튼과 효과음을 내지 않는다. 아래 대기는 Reset이 false로 완료한 원본에서 바로 끝난다.
                if (ReferenceEquals(_continueCompletion, completion))
                {
                    PresentExperience(experience, fromExperience);
                    _hud._continue.gameObject.SetActive(true);
                    PresentationSound?.Invoke(_continueShownSound);
                }
                bool continued = await completion.Task;
                // 소유자 전환으로 끝난 표시는 클릭 효과음을 내지 않는다.
                if (continued) PresentationSound?.Invoke(_continueSound);
                return continued;
            }
            finally
            {
                // 이 호출이 아직 결과 화면을 소유할 때만 정리한다. 소유자 전환이 이미 정리했으면 다음 표시를 건드리지 않는다.
                if (ReferenceEquals(_continueCompletion, completion))
                {
                    _continueCompletion = null;
                    if (_hud._resultPanel != null) _hud._resultPanel.SetActive(false);
                }
            }
        }

        // 저장된 누적 경험치를 OriginalExperience 레벨 규칙의 현재 레벨 구간으로 표시한다. 최대 레벨은 구간 끝이 없다.
        void PresentExperience(long experience, long from)
        {
            int level = OriginalExperience.Level(experience);
            long minimum = OriginalExperience.LevelStart(level);
            long? end = OriginalExperience.LevelEnd(level);
            long maximum = end ?? long.MaxValue;
            // 음이 아닌 값은 재사용 배열에 숫자를 직접 써서 SetCharArray로 넘긴다. 결과는 long.ToString과 같고, TMP는 리치 텍스트를
            // text 경로와 똑같이 처리한다. 음수는 문화권의 음수 기호를 따르도록 문자열 결합을 쓴다.
            if (experience >= 0 && (!end.HasValue || end.Value >= 0))
            {
                char[] chars = _resultExperienceChars;
                int length = UiDigits.Write(chars, 0, experience, 1);
                _resultEndOpen.CopyTo(0, chars, length, _resultEndOpen.Length);
                length += _resultEndOpen.Length;
                if (end.HasValue) length = UiDigits.Write(chars, length, end.Value, 1);
                else
                {
                    _resultNoEnd.CopyTo(0, chars, length, _resultNoEnd.Length);
                    length += _resultNoEnd.Length;
                }
                _resultEndClose.CopyTo(0, chars, length, _resultEndClose.Length);
                length += _resultEndClose.Length;
                _hud._resultExperience.SetCharArray(chars, 0, length);
            }
            else _hud._resultExperience.text = experience + _resultEndOpen + (end.HasValue ? end.Value.ToString() : _resultNoEnd) + _resultEndClose;
            if (level != _shownResultLevel)
            {
                _shownResultLevel = level;
                _hud._resultLevel.text = "Lv." + level;
            }
            _hud._resultCurrentFill.fillAmount = (float)((double)(experience - minimum) / (maximum - minimum));
            _hud._resultOldFill.fillAmount = from >= minimum ? (float)((double)(from - minimum) / (maximum - minimum)) : 0;
        }
    }
}
