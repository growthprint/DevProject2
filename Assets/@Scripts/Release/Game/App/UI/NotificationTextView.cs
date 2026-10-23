#nullable enable annotations
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원본 Duckov.UI.NotificationText인 상단 중앙의 검은 알약 알림이다.
    // 원본처럼 표시를 시작할 때 대기 알림이 있으면 0.65초, 없으면 1.2초 뒤 숨기고, 나타나고 사라질 때 0.12초 페이드한다.
    // 대기열 맨 앞과 같은 문구는 다시 넣지 않으며, 시간 배율과 무관하게 진행한다.
    [DisallowMultipleComponent]
    public sealed class NotificationTextView : MonoBehaviour
    {
        [SerializeField] CanvasGroup _group = null!;
        [SerializeField] TMP_Text _text = null!;
        [SerializeField, Min(0f)] float _duration = 1.2f;
        [SerializeField, Min(0f)] float _durationIfPending = .65f;
        // 페이드 시간으로 나누므로 다른 페이드 View와 같은 UiFade 최소 시간 아래로 내리지 않는다.
        [SerializeField, Min(UiFade._minimumDuration)] float _fadeDuration = .12f;

        readonly Queue<string> _pending = new();
        float _elapsed;
        float _hold;
        bool _showing;

        // 대기열에만 넣고 코루틴을 시작하지 않으므로 꺼진 오브젝트에서 불러도 예외도 로그도 없다.
        // 표시는 켜진 동안 Update가 대기열을 비우며 진행하고, 공용 HUD의 소유자가 바뀌면 Clear가 남은 문구를 버린다.
        public void Push(string text)
        {
            if (_pending.Count > 0 && _pending.Peek() == text) return;
            _pending.Enqueue(text);
        }

        // 공용 HUD 소유자 전환 때 대기 알림과 표시 중인 알림을 즉시 지운다.
        internal void Clear()
        {
            _pending.Clear();
            _showing = false;
            _elapsed = 0f;
            _group.alpha = 0f;
        }

        void OnEnable()
        {
            _group.alpha = 0f;
            _showing = false;
        }

        void Update()
        {
            float delta = Time.unscaledDeltaTime;
            if (!_showing)
            {
                if (_pending.Count == 0) return;
                _text.text = _pending.Dequeue();
                _showing = true;
                _elapsed = 0f;
                _hold = _pending.Count > 0 ? _durationIfPending : _duration;
            }
            _elapsed += delta;
            // 나타나고 사라지는 페이드는 다른 페이드 View와 같은 UiFade 규칙을 쓴다.
            if (_elapsed < _hold)
            {
                _group.alpha = UiFade.Step(_group.alpha, 1f, delta, _fadeDuration);
                return;
            }
            _group.alpha = UiFade.Step(_group.alpha, 0f, delta, _fadeDuration);
            if (_group.alpha <= 0f) _showing = false;
        }
    }
}
