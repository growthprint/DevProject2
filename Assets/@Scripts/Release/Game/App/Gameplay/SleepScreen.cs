#nullable enable annotations
using System;
using System.Globalization;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.UI.SleepView의 컨트롤러다. 창은 공용 HUD의 OriginalSleepView이고 값만 받아 그린다.
    // 기지 Sleep 물체(SleepInteractable)가 끝나면 열린다. 열린 동안 게임플레이 입력을 막고 ESC와 닫기 버튼으로 닫는다(NoteScreen 선례).
    // 확인하면 원작 Sleep 순서(검은 화면 → GameClock.TrySleep → 실제 0.5초 → 창 닫기 → 검은 화면 끔)를 따르고, 진행 중 다시 누른 확인은 무시한다.
    // 날씨와 스폰 시각은 시계 소유자의 Changed 뒤 같은 시계 값을 읽으므로 따로 알리지 않는다. 원작 OnAfterSleep은 듣는 쪽이 없어 옮기지 않는다.
    internal sealed class SleepScreen : GameplayScreen
    {
        // 원작 BlackScreen.ShowAndReturnTask와 HideAndReturnTask의 기본 길이 0.5초다. 원작 곡선 대신 선형으로 걷는다.
        const float _blackScreenSeconds = .5f;
        // 원작 Sleep의 UniTask.WaitForSeconds(0.5f, ignoreTimeScale: true)다.
        const double _afterSleepSeconds = .5d;

        readonly GameplayWorldContext _world;
        // 연 동안 붙인 창과 시계 구독을 창을 닫을 때 한 번에 뗀다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 처음 연 창이다. 공용 HUD의 같은 창이라 월드 수명 동안 바뀌지 않고, 창을 닫은 뒤 검은 화면을 걷을 때도 쓴다.
        OriginalSleepView? _view;
        // 원작 sleepForMinuts다. 원작은 이 값을 되돌리지 않아 다시 열면 마지막으로 고른 분이 보인다. 처음만 0이다.
        int _minutes;
        // 원작 sleeping이다. 검은 화면을 걷을 때까지 참이고, 그동안의 확인과 다시 열기는 무시한다.
        bool _sleeping;
        // 진행 중인 수면 순서의 세대다. 씬 정리나 다른 화면의 Close가 올려, 기다리던 순서가 창과 검은 화면을 다시 만지지 않게 한다.
        int _generation;

        internal SleepScreen(GameplayWorldContext world)
        {
            _world = world;
        }

        // 원작 SleepView.Show다. 원작은 어느 Sleep 물체가 열었는지 쓰지 않아 source를 읽지 않는다. 이미 열려 있으면 다시 그리기만 한다.
        // 검은 화면을 걷는 중이거나, 공용 HUD가 이 월드에 붙어 있지 않거나, 입력 차단을 얻지 못하면(정리 중) 열지 않는다(C2).
        internal void Show(SleepInteractable source)
        {
            if (IsOpen)
            {
                Refresh();
                return;
            }
            if (_sleeping || !_world.CanPresent) return;
            if (!TryBlockInput(_world.Runtime)) return;
            OriginalSleepView view = _world.Hud.Sleep;
            _view = view;
            Subscribe(view);
            view.Show(_minutes);
            Refresh();
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다. 기지 밖에는 자동 저장이 없어 무시된다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 진행 중인 수면 순서를 멈추고 검은 화면을 바로 걷은 뒤 창을 닫는다. 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다.
        // 시계는 이미 진행했으면 그대로 둔다.
        internal override void Close()
        {
            if (_sleeping)
            {
                _generation++;
                _sleeping = false;
                if (_view != null && _world.PresentationAttached) _view.SetBlackScreen(0f);
            }
            CloseWindow();
        }

        // ESC 처리가 묻는다. 열려 있으면 true다. 수면 순서 중에는 원작처럼 순서가 창을 닫으므로 ESC를 받기만 하고 닫지 않는다.
        internal override bool TryClose()
        {
            if (!IsOpen) return false;
            if (!_sleeping) Close();
            return true;
        }

        // 원작 Refresh의 표시 값이다. 깨는 시각은 원작 GameClock.TimeOfDay + 분이고, 그 Days가 0보다 크면 다음 날 표시를 켠다.
        // 원작 문자열 보간("00:00", "00 h 00 min")과 같은 자리 수를 문화권과 무관하게 만든다. 시각이 없는 시계는 시계 상태의 기본 시각을 쓴다.
        internal static void Describe(GameplayClockState clock, int minutes, out string willWakeUpAt, out string sleepTimeSpan, out bool nextDay)
        {
            TimeSpan span = TimeSpan.FromMinutes(minutes);
            TimeSpan wake = TimeSpan.FromSeconds(clock.SecondsOfDayOrDefault()) + span;
            willWakeUpAt = TwoDigits(wake.Hours) + ":" + TwoDigits(wake.Minutes);
            sleepTimeSpan = TwoDigits((int)span.TotalHours) + " h " + TwoDigits(span.Minutes) + " min";
            nextDay = wake.Days > 0;
        }

        // 원작 보간의 "00" 자리 수다. 문화권과 무관한 고정 형식으로 만든다.
        static string TwoDigits(int value) => value.ToString("00", CultureInfo.InvariantCulture);

        // 입력 차단과 구독을 돌려주고 창만 닫는다. 검은 화면은 건드리지 않는다.
        void CloseWindow()
        {
            if (!IsOpen) return;
            _subscriptions.Clear();
            ReleaseInput();
            if (_world.PresentationAttached) _view!.HideWindow();
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 OnEnable과 OnDisable의 GameClock.OnGameClockStep 구독처럼, 열린 동안 시계가 흐르면 깨는 시각을 다시 그린다.
        // 붙인 순서대로 떼도록 해제를 한 번에 등록한다.
        void Subscribe(OriginalSleepView view)
        {
            view.MinutesChanged += MinutesChanged;
            view.ConfirmRequested += ConfirmRequested;
            view.CloseRequested += CloseRequested;
            GameClock.Changed += Refresh;
            _subscriptions.Add(() =>
            {
                view.MinutesChanged -= MinutesChanged;
                view.ConfirmRequested -= ConfirmRequested;
                view.CloseRequested -= CloseRequested;
                GameClock.Changed -= Refresh;
            });
        }

        void MinutesChanged(int minutes)
        {
            _minutes = minutes;
            Refresh();
        }

        void CloseRequested()
        {
            if (!_sleeping) Close();
        }

        // 원작 OnConfirmButtonClicked다. 진행 중 다시 누른 확인은 원작 sleeping 검사처럼 무시한다(C2).
        // 아무도 기다리지 않는 순서라, 예외가 나면 상태를 먼저 되돌린 뒤 한 번 기록한다(K3, GameBootstrap의 Forget 처리기 선례).
        void ConfirmRequested()
        {
            if (_sleeping || !IsOpen) return;
            _sleeping = true;
            int generation = ++_generation;
            SleepAsync(_minutes, generation).Forget(error =>
            {
                if (generation == _generation) Close();
                Log.Instance.Exception(error, _world.LogContext);
            });
        }

        async UniTask SleepAsync(int minutes, int generation)
        {
            if (!await FadeBlackScreenAsync(0f, 1f, generation)) return;
            // 관문이 닫혀 거절되면 시계는 그대로이고, 순서는 원작처럼 끝까지 가서 창과 검은 화면을 걷는다.
            GameManager.Clock.TrySleep(minutes);
            await UniTask.Delay(TimeSpan.FromSeconds(_afterSleepSeconds), true);
            if (!IsCurrent(generation)) return;
            CloseWindow();
            if (!await FadeBlackScreenAsync(1f, 0f, generation)) return;
            _sleeping = false;
        }

        // 원작 FadeGroup 페이드처럼 실제 시간으로 알파를 옮긴다. 순서가 멈췄으면 false를 돌려준다.
        async UniTask<bool> FadeBlackScreenAsync(float from, float to, int generation)
        {
            float elapsed = 0f;
            while (true)
            {
                _view!.SetBlackScreen(Mathf.Lerp(from, to, elapsed / _blackScreenSeconds));
                if (elapsed >= _blackScreenSeconds) return true;
                await UniTask.Yield();
                if (!IsCurrent(generation)) return false;
                elapsed += Time.unscaledDeltaTime;
            }
        }

        // 기다리는 동안 Close가 세대를 올렸으면 멈춘다. 공용 HUD가 떨어졌으면(씬 정리 중) 다른 소유자의 HUD를 만지지 않도록
        // 순서를 끝내고 입력 차단만 돌려준다. 앱 루트와 공용 HUD 연결은 월드 문맥의 표시 가능 판정 하나로 본다.
        bool IsCurrent(int generation)
        {
            if (generation != _generation) return false;
            if (_world.CanPresent) return true;
            Close();
            return false;
        }

        // 앱 루트가 없거나 공용 HUD가 떨어졌으면 그리지 않는다.
        void Refresh()
        {
            if (!IsOpen || !_world.CanPresent) return;
            Describe(GameManager.Clock.Current, _minutes, out string willWakeUpAt, out string sleepTimeSpan, out bool nextDay);
            _view!.SetTimes(willWakeUpAt, sleepTimeSpan, nextDay);
        }
    }
}
