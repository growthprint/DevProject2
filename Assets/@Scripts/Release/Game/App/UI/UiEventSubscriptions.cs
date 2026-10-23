#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace Game.App
{
    // 화면이 등록한 이벤트 구독을 모아 한 번에 해제한다. 한 해제의 실패가 나머지 해제를 막지 않는다.
    internal sealed class UiEventSubscriptions : IDisposable
    {
        readonly List<Action> _detach = new List<Action>();
        bool _clearing;

        // 해제 중에 등록된 항목도 진행 중인 Clear가 같은 반복에서 해제한다.
        public void Add(Action detach)
        {
            _detach.Add(detach);
        }

        // 해제 동작 등록은 Add가 맡는다.
        public void Bind(UnityEvent source, UnityAction callback)
        {
            source.AddListener(callback);
            Add(() => source.RemoveListener(callback));
        }

        public void Bind<T>(UnityEvent<T> source, UnityAction<T> callback)
        {
            source.AddListener(callback);
            Add(() => source.RemoveListener(callback));
        }

        // UnityEvent가 아닌 C# 이벤트처럼 구독과 해제를 호출자가 정하는 구독이다. 구독한 뒤 해제 동작을 등록한다.
        public void Bind(Action subscribe, Action unsubscribe)
        {
            subscribe();
            Add(unsubscribe);
        }

        // 순서만 다른 버튼 묶음(탭, 선택지)을 한 콜백에 연결한다. 각 버튼은 배열 위치를 넘긴다.
        public void BindIndexed(Button[] buttons, Action<int> clicked)
        {
            for (int index = 0; index < buttons.Length; index++)
            {
                int position = index;
                Bind(buttons[index].onClick, () => clicked(position));
            }
        }

        // 열린 화면이 세션 변경마다 다시 그리는 구독이다. 붙인 delegate를 그대로 떼어야 해제가 맞으므로 한 변수로 붙이고 뗀다.
        public void BindSessionChanged(GameSessionManager session, Action redraw)
        {
            Action<GameSessionSnapshot> changed = snapshot => redraw();
            session.Changed += changed;
            Add(() => session.Changed -= changed);
        }

        // 열린 화면이 언어가 바뀔 때마다 다시 그리는 구독이다. 해제 규칙은 세션 구독과 같다.
        public void BindLocaleChanged(Action redraw)
        {
            Action<Locale> changed = locale => redraw();
            LocalizationSettings.SelectedLocaleChanged += changed;
            Add(() => LocalizationSettings.SelectedLocaleChanged -= changed);
        }

        // performed를 구독한 뒤 꺼져 있던 액션만 켠다. 해제는 구독을 먼저 끊고, 이 묶음이 켠 액션만 끈다.
        // 다른 소비자가 이미 켠 입력은 끄지 않는다. 액션이 있는지는 ProductUiLocalizationTests가 제품 입력 에셋에서 확인한다.
        public void Bind(InputAction action, Action<InputAction.CallbackContext> callback)
        {
            action.performed += callback;
            Add(() => action.performed -= callback);
            Enable(action);
        }

        // 콜백 없이 다른 컴포넌트가 직접 읽는 입력을 켠다. 꺼져 있던 액션만 켜고 그 액션만 끄는 규칙은
        // 다른 컨트롤러와 같은 InputActionEnableLease가 맡고, 실제로 켠 임대만 해제 목록에 넣는다.
        public void Enable(InputAction action)
        {
            InputActionEnableReleaseHandle lease = InputActionEnableReleaseHandle.Acquire(action);
            if (lease.OwnsEnable) Add(lease.Dispose);
        }

        // 동기 정리 경계다. 해제 동작마다 Cleanup.Run으로 실행해 한 실패가 나머지를 막지 않게 하고, 실패 기록은 Cleanup이 맡는다.
        // 정리는 예외를 던지지 않는다. 해제 중 재등록된 항목도 같은 반복에서 해제하도록 사본 없이 색인으로 돌고,
        // 모든 항목을 실행한 뒤 finally에서 목록을 비운다.
        public void Clear()
        {
            if (_clearing) return;
            _clearing = true;
            try
            {
                for (int index = 0; index < _detach.Count; index++)
                    Cleanup.Run(_detach[index]);
            }
            finally
            {
                _detach.Clear();
                _clearing = false;
            }
        }

        public void Dispose() => Clear();
    }
}
