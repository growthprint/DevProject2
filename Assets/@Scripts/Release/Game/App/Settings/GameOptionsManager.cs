#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원본 OptionsManager처럼 옵션 파일, 화면 적용, 변경 알림을 한 컴포넌트가 맡는다. GameManager 프리팹의 자식이고 메인 스레드에서만 쓴다.
    // 저장 요청은 화면에 바로 적용하고 파일 쓰기는 하나씩 줄 세운다. 쓰기나 적용이 실패하면 후보를 남기고 확정값으로 되돌린다.
    // Runtime은 Configure로 입력을 넘기고, 시작 초기화, 종료 Flush, 저장 허용 판정, 앱 자원 등록만 맡는다.
    [DisallowMultipleComponent]
    public sealed class GameOptionsManager : MonoBehaviour, IAsyncReleasable
    {
        // 옵션 파일을 두는 키 저장소다. null이면 옵션을 저장하지 않고, 입력에 기본값만 적용하며 표시 알림도 하지 않는다.
        ISaveStorage? _storage;
        // 옵션 파일 형식은 GameOptionsJson 코덱 하나가 정한다.
        readonly GameOptionsJson _json = new();
        // 옵션 파일과 백업을 모두 쓸 수 없을 때의 기본값이다. null이면 GameOptions 기본값을 쓴다.
        // 제품 구성은 원본처럼 장치의 가장 큰 해상도를 넣는다.
        GameOptions? _defaults;
        // 파일에서 읽었거나 쓰기에 성공한 확정 옵션이다. 쓰는 중인 후보는 _writing에 따로 두어 확정값으로 보이지 않는다.
        GameOptions _current = new GameOptions();
        // 옵션 파일 읽기를 마쳤는지 나타낸다. 읽기 전에는 화면 모드를 저장하지 않아, 읽지 못한 파일을 기본값으로 덮어쓰지 않는다.
        bool _initialized;
        // 품질 설정과 키 설정을 실제로 쓰는 객체다. 해제는 앱 자원 범위가 맡고, 여기서는 적용만 한다.
        GameGraphicsOptions? _graphics;
        GameInputOptions? _input;
        // Runtime이 정한 저장 허용 판정이다. 실행 중이고 씬 전환이 없으며 메뉴나 일시정지 화면일 때만 참이다.
        Func<bool> _canSave;
        // 해제를 한 번만 실행한다. Configure가 만들므로 null이면 구성 전이다. 다시 불러도 같은 완료를 돌려준다.
        ReleaseOnce? _release;
        // 빌드에서만 참이다. Editor는 화면에 적용하지 않으므로 실제 화면 모드를 감시하지 않는다.
        bool _watchScreenMode;
        // 저장부터 표시 적용 완료까지를 묶은 공유 작업이다. 진행 중이면 모든 저장 호출자가 같은 완료를 기다리고,
        // 그사이 들어온 후보는 마지막 것만 _queued에 남긴다.
        readonly TrackedOperation _save = new TrackedOperation();
        GameOptions? _queued;
        // 지금 파일에 쓰는 중인 후보다. Pending이 쓰는 동안에도 후보를 보여 주도록 두고, 쓰기가 끝나거나 실패하면 비운다.
        GameOptions? _writing;
        // 표시 적용은 파일 쓰기와 따로 줄 세운다. 연속 입력이 디스크 쓰기를 기다리지 않게 하기 위해서다.
        Task? _applyTask;
        GameOptions? _queuedApply;
        // 실패 복원 중이면 참이다. 이때 들어온 입력은 화면에 적용하지 않고 재시도 후보로만 남긴다.
        bool _rollingBack;
        // 적용이나 쓰기에 실패한 마지막 후보와 원인이다. 재시도 때까지 남겨 Flush가 실패를 성공으로 보지 않게 한다.
        GameOptions? _failed;
        Exception? _failure;
        // 화면 적용과 화면 모드 확인 시점을 맡는다. 바뀐 화면 모드를 저장할지는 SyncScreenMode가 정한다.
        readonly GameScreenOptions _screen = new();
        // 묶음별로 마지막에 실제로 적용을 마친 값이다. 원본 OptionsManager가 바뀐 키의 설정만 적용하듯, ApplyAsync는 이 값과
        // 다른 묶음만 다시 적용한다. null이면 그 묶음을 아직 적용하지 않았다. 관리자는 후보 사본을 고치지 않으므로 사본을 그대로 담는다.
        // 그래픽은 옵션에 없는 고정값이라 적용을 마쳤는지만 둔다. 화면 묶음은 _screen이 같은 방식으로 기록한다.
        string? _appliedLanguage;
        GameOptions? _appliedInput;
        bool _graphicsApplied;

        // 원본 LevelManager 연결처럼 GameRuntime이 직렬화할 수 없는 입력을 한 번 넘긴다. 어댑터는 앱 자원 범위에 이미 등록된 것을 받는다.
        // 저장소나 어댑터를 넘기지 않으면 그 저장이나 적용을 건너뛴다. watchScreenMode는 빌드에서 Alt+Enter 같은 화면 모드 변경을 저장할 때 켠다.
        internal void Configure(Func<bool> canSave, ISaveStorage? storage = null,
            GameOptions? defaults = null, GameGraphicsOptions? graphics = null, GameInputOptions? input = null,
            bool watchScreenMode = false)
        {
            _storage = storage;
            _defaults = defaults?.Clone();
            _graphics = graphics;
            _input = input;
            _watchScreenMode = watchScreenMode;
            // 저장 허용 판정은 GameRuntime 구성과 테스트 구성이 늘 람다로 넘긴다.
            _canSave = canSave;
            _release = new ReleaseOnce(ReleaseCoreAsync, this);
        }

        // 옵션 저장소가 있어 옵션 화면을 만들고 저장할 수 있는지 알린다.
        public bool IsPersistent => _storage != null;
        // 호출자가 고쳐도 확정값이 바뀌지 않도록 늘 사본을 돌려준다. 초기화 전에는 GameOptions 기본값이다.
        public GameOptions Current => _current.Clone();
        // 저장 중에는 기다리는 최신 후보나 쓰는 중인 후보를, 실패 뒤에는 재시도 후보를 사본으로 반환한다.
        public GameOptions? Pending => (_queued ?? _writing ?? _failed)?.Clone();
        // 즉시 적용한 후보와 실패 시 복원한 확정값을 구독자마다 새 사본으로 씬과 UI에 전달한다.
        public event Action<GameOptions>? Applied;

        // 화면 모드 감시가 매 프레임 부르므로 옵션 전체를 복사하지 않고 확정값의 화면 모드만 돌려준다.
        internal int CurrentWindowMode => _current.WindowMode;
        // 옵션 파일 읽기를 마쳤는지 알린다. 저장소가 없는 구성은 읽을 파일이 없어 늘 false다.
        internal bool Initialized => _initialized;

        // 시작할 때 한 번 옵션을 읽어 적용한다. 손상된 옵션 파일은 백업이나 기본값으로 복구해 시작을 멈추지 않는다.
        // 다시 불려도 파일을 또 읽지 않고 확정값을 다시 적용한다. 표시 구독자의 실패는 기록만 하고 메뉴 공개를 계속한다.
        internal async UniTask InitializeAsync()
        {
            if (_storage == null)
            {
                _current = DefaultOptions();
                _input?.Apply(_current);
                return;
            }
            if (!_initialized) await LoadAsync();
            // 읽는 동안 파괴됐으면 적용하지 않는다. 옵션 저장소가 메인 스레드에서 끝나므로 메인 스레드로 따로 돌아가지 않는다.
            if (this == null) return;
            await ApplyAsync(Current, reportSubscriberFailures: true);
        }

        // 검증과 공유 저장 작업 예약을 await 없이 바로 끝내, 연속 클릭이 같은 저장소에 겹쳐 쓰지 않게 한다.
        // 연속 변경은 진행 중인 저장 작업에 마지막 후보만 예약한다. 창이 닫혀도 이미 접수한 저장은 멈추지 않는다.
        public UniTask SaveAsync(GameOptions candidate)
        {
            // 거부는 결함으로 보고한다. 에디터와 DEBUG 빌드는 같은 예외를 던져 옵션 화면의 catch가 받고,
            // 릴리즈 빌드는 한 번 기록한 뒤 아무것도 바꾸지 않고 끝난다. 해제 여부는 ReleaseOnce.Started 하나로 본다.
            // 해제는 Started를 먼저 세운 뒤 정리를 시작하고, 옵션 화면보다 먼저 해제되므로 해제 중에도 이 호출이 올 수 있다.
            // 호출자는 모두 Configure 뒤에만 부르므로 _release는 null이 아니다.
            if (_release!.Started)
            {
                Log.Instance.Fail(new ObjectDisposedException(nameof(GameOptionsManager)));
                return UniTask.CompletedTask;
            }
            if (!_canSave())
            {
                Log.Instance.Fail(new InvalidOperationException("환경 설정을 저장할 수 없는 실행 상태입니다."));
                return UniTask.CompletedTask;
            }
            GameOptions copy = candidate.Clone();
            // 표시 적용과 파일 쓰기 전에 옵션을 검증하는 유일한 관문이라, 파일 쓰기는 다시 검증하지 않는다.
            // TryValidate는 이유를 주지 않으므로 고정 문구로 보고하고, 릴리즈 빌드는 적용도 쓰기도 하지 않고 끝난다.
            if (!copy.TryValidate())
            {
                Log.Instance.Fail(new ArgumentException("옵션 후보가 올바르지 않습니다.", nameof(candidate)));
                return UniTask.CompletedTask;
            }
            _queued = copy;
            _failed = null;
            _failure = null;
            // 진행 중인 저장 작업이 이 후보까지 쓰므로 같은 완료를 돌려준다. 적용은 진행 중인 파일 쓰기를 기다리지 않는다.
            Task? running = _save.Current;
            if (running != null)
            {
                if (!_rollingBack) QueueApply(copy);
                return running.AsUniTaskOnCurrentContext();
            }
            // 적용 구독자가 다시 저장을 불러도 겹치지 않게, 저장 작업이 자리를 잡은 뒤 started에서 적용을 예약한다.
            // 창이 닫혀도 접수한 변경을 쓴다. 복원은 저장 작업이 도는 동안에만 일어나 여기서 보지 않는다.
            return _save.Start(SaveCoreAsync, started: () => QueueApply(copy)).AsUniTaskOnCurrentContext();
        }

        // 창 닫기와 정상 종료가 부른다. 기다리는 사이 새로 시작된 저장 작업까지 모두 기다린 뒤,
        // 실패는 원래 예외로, 남은 후보는 재시도 요청으로 알린다.
        public async UniTask FlushAsync()
        {
            while (_save.Current != null) await _save.Current.AsUniTaskOnCurrentContext();
            if (_failure != null) ExceptionDispatchInfo.Capture(_failure).Throw();
            // 남은 후보가 있는지만 알면 되므로 Pending처럼 사본을 만들지 않는다.
            if (HasPendingCandidate) throw new InvalidOperationException("저장하지 못한 설정이 있습니다. 다시 시도해 주세요.");
        }

        // 새 저장을 거부하고 진행 중인 저장과 적용이 끝나기를 한 번 기다린다. 구성 전에는 정리할 작업이 없다.
        public UniTask ReleaseAsync() => _release != null ? _release.ReleaseAsync() : UniTask.CompletedTask;

        // 저장과 적용의 실패는 저장 호출자와 Flush가 받으므로 해제는 진행 중인 두 작업이 끝나기만 기다린다.
        // ReleaseOnce가 Started를 먼저 세운 뒤 부르므로, 이때부터 새 저장과 화면 모드 저장은 거부된다.
        async UniTask ReleaseCoreAsync()
        {
            await TrackedTask.ObservePreviousAsync(_save.Current);
            await TrackedTask.ObservePreviousAsync(_applyTask);
        }

        // 적용은 앞 작업을 앞지르지 않고, 아직 시작하지 않은 후보만 최신 값으로 바꾼다.
        // _applyTask가 null이 아니면 진행 중이거나, 저장 쪽이 복원할 때까지 남겨 둔 실패한 작업이다.
        // 그래서 새 입력이 아직 처리하지 않은 실패를 덮지 않는다. 성공한 작업은 ApplyQueuedAsync가 스스로 비운다.
        void QueueApply(GameOptions candidate)
        {
            _queuedApply = candidate;
            if (_applyTask != null) return;
            TaskCompletionSource<bool> completion = SharedCompletion.Create<bool>();
            _applyTask = completion.Task;
            ApplyQueuedAsync(completion).Forget();
        }

        // 적용 작업의 실패는 SaveCoreAsync가 받아 저장 실패와 같은 복원과 재시도 규칙으로 처리한다. 이 작업은 메인 스레드에서 끝난다.
        async UniTask ApplyQueuedAsync(TaskCompletionSource<bool> completion)
        {
            Exception? failure = null;
            try
            {
                while (_queuedApply != null)
                {
                    GameOptions candidate = _queuedApply;
                    _queuedApply = null;
                    await ApplyAsync(candidate, latestOnly: true);
                }
            }
            // 예외 경계: 적용 실패를 작업 결과로 넘기고, 표시 복원은 그 결과를 받는 저장 작업이 맡는다.
            catch (Exception error) { failure = error; }
            finally
            {
                // 성공한 작업은 여기서 참조를 비운다. 실패한 작업은 SaveCoreAsync가 받아 복원한 뒤 비우도록 남긴다.
                // 결과를 알리기 전에 비워도 안전하다. 대기자는 비동기로 재개되고, AwaitApplyAsync는 참조가 바뀌면 다시 확인한다.
                if (failure == null && ReferenceEquals(_applyTask, completion.Task)) _applyTask = null;
                completion.TrySetOutcome(failure, true);
            }
        }

        // 기다리는 사이 새 적용 작업이 생기면 그 작업까지 기다린다. 대기가 끝나면 메인 스레드로 돌아온다.
        async UniTask AwaitApplyAsync()
        {
            Task? applying;
            do
            {
                applying = _applyTask;
                if (applying != null) await applying.AsUniTaskOnCurrentContext();
            }
            while (!ReferenceEquals(applying, _applyTask));
        }

        // 적용에 성공한 후보만 하나씩 파일에 쓰고, 쓰는 동안에도 표시 적용은 계속 받는다. 모든 적용과 복원이 끝난 뒤에만
        // 대기자를 풀어 준다. 옵션 저장소가 메인 스레드에서 끝나므로 메인 스레드로 따로 돌아가지 않는다.
        async Task SaveCoreAsync()
        {
            try
            {
                while (_queued != null)
                {
                    await AwaitApplyAsync();
                    // 쓰는 동안에도 Pending이 후보를 보이도록 _writing에 옮겨 둔다. 쓰기가 성공해야 확정값이 된다.
                    GameOptions candidate = _queued!;
                    _queued = null;
                    _writing = candidate;
                    await WriteAsync(candidate);
                    _writing = null;
                }
            }
            // 예외 경계: 실패 후보를 남기고 확정값으로 표시를 되돌린 뒤 원래 저장 실패만 다시 던진다. 복원 실패는 기록만 한다.
            catch (Exception error)
            {
                // 적용 실패면 _queued가, 쓰기 실패면 더 새 입력이 없을 때 _writing이 실패 후보다.
                _failed = (_queued ?? _writing)?.Clone();
                _queued = null;
                _writing = null;
                // 이미 시작한 적용이 복원 뒤에 늦게 반영되지 않게, 새 적용 예약을 막고 진행 중인 적용부터 기다린다.
                _rollingBack = true;
                _queuedApply = null;
                try { await AwaitApplyAsync(); }
                // 예외 경계: 저장 실패와 같은 예외는 원래 실패로 넘어가므로, 그와 다른 적용 실패만 여기서 한 번 기록한다.
                catch (Exception applyError) { if (!ReferenceEquals(applyError, error)) Log.Instance.Exception(applyError); }
                // 확정값으로 표시를 되돌린다. 구독자 실패는 구독자마다, 그 밖의 실패는 Cleanup이 기록하고
                // 원래 저장 실패를 그대로 넘긴다.
                await Cleanup.RunAsync(() => ApplyAsync(Current, reportSubscriberFailures: true), this);
                // 복원하는 동안 들어온 마지막 입력도 잃지 않도록 실패 후보로 남긴다.
                if (_queued != null) _failed = _queued.Clone();
                _queued = null;
                _failure = error;
                // TrackedOperation이 이 예외를 공유 완료로 게시해, 같은 저장을 기다리던 호출자와 Flush가 원래 실패를 받는다.
                throw;
            }
            finally
            {
                // 복원 표시와 적용 참조를 여기서 비운 뒤에 TrackedOperation이 저장 자리를 비우고 결과를 게시한다.
                _rollingBack = false;
                _applyTask = null;
            }
        }

        // 언어 테이블을 준비한 뒤 품질, 입력, 화면 설정에 적용하고 구독자에게 알린다. 적용 큐에서 온 후보는
        // 준비하는 사이 더 새 후보가 오거나 복원이 시작되면 적용하지 않는다. 한 구독자가 실패해도 나머지에 알린다.
        // 저장 경로는 실패를 다시 던지고, 시작과 복원 경로는 실패마다 기록만 한다.
        // 품질은 고정값이라 처음 한 번만, 언어, 입력, 화면 묶음은 마지막으로 적용한 값과 다를 때만 적용한다. 그래서 음량 슬라이더를 끄는 동안
        // 언어 선택, 키 재정의, 해상도 재요청이 반복되지 않는다. 구독자에게는 늘 알린다.
        async UniTask ApplyAsync(GameOptions options, bool latestOnly = false, bool reportSubscriberFailures = false)
        {
            if (!string.Equals(_appliedLanguage, options.Language, StringComparison.Ordinal))
            {
                await GameLocalization.SelectAsync(options.Language, default );
                // 언어 준비를 기다리는 사이 이 컴포넌트가 파괴됐으면 화면과 구독자에 적용하지 않는다.
                if (this == null) return;
                _appliedLanguage = options.Language;
            }
            if (latestOnly && (_queuedApply != null || _rollingBack)) return;
            // 적용 객체가 해제 뒤라 거부하면(false) 적용한 것으로 기록하지 않는다. 그래픽은 고정값이라 한 번 성공하면 다시 부르지 않는다.
            if (_graphics != null && !_graphicsApplied) _graphicsApplied = _graphics.Apply();
            if (_input != null && !SameInput(_appliedInput, options) && _input.Apply(options)) _appliedInput = options;
            // 해상도, 화면 모드, 수직 동기화, 프레임 제한은 화면 설정 객체가 마지막 적용값과 비교해 적용한다. Editor에서는 적용하지 않는다.
            _screen.Apply(options);
            var failures = new List<Exception>();
            // onFailure가 null이면 ObserverDispatch가 실패마다 기록한다. 저장 경로만 실패를 모아 다시 던진다.
            Action<Exception>? onFailure = null;
            if (!reportSubscriberFailures) onFailure = failures.Add;
            ObserverDispatch.InvokeEach(Applied, options.Clone, onFailure);
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("옵션 표시 구독자 여러 개가 실패했습니다.", failures);
        }

        // GameInputOptions가 쓰는 키 바인딩이 마지막 적용값과 순서까지 같은지다. 검증을 통과한 옵션만 오므로 배열은 null이 아니다.
        static bool SameInput(GameOptions? applied, GameOptions options)
        {
            if (applied == null || applied.KeyBindings.Length != options.KeyBindings.Length) return false;
            for (int index = 0; index < options.KeyBindings.Length; index++)
                if (!string.Equals(applied.KeyBindings[index], options.KeyBindings[index], StringComparison.Ordinal)) return false;
            return true;
        }

        // 원본 ResolutionSetter.Update처럼 빌드에서 매 프레임 실제 화면 모드를 확인한다. 감시는 Configure가 켠 구성에서만 한다.
        void Update()
        {
            if (_watchScreenMode) SyncScreenMode(Time.unscaledDeltaTime);
        }

        // 앱 자원 범위가 해제하기 전에 파괴돼도 새 저장을 막고 진행 중인 쓰기를 마치게 한다. 해제는 한 번만 실행되고 던지지 않는다.
        void OnDestroy()
        {
            if (_release != null) Cleanup.RunAsync(ReleaseAsync, this).Forget();
        }

        // 원본 ResolutionSetter.UpdateFullScreenCheck를 따른다. Alt+Enter 등으로 실제 화면 모드가 확정값과 달라지면 그 모드를 저장한다.
        // Update가 매 프레임 부른다. 원본과 달리 저장할 수 없는 상태이거나 저장할 후보가 남아 있으면 다음 확인으로 미룬다.
        internal void SyncScreenMode(float deltaTime)
        {
            // 화면 적용 직후의 대기 시간이 지나야 확인한다. 저장할 수 없는 상태이거나 저장할 후보가 남아 있으면
            // 확정값이 곧 바뀔 수 있어 이번 확인을 건너뛰고, 실제 화면 모드는 그 뒤에만 읽는다.
            if (!_screen.TickModeCheck(deltaTime)) return;
            if (!CanSyncScreenMode) return;
            if (HasPendingCandidate) return;
            bool fullScreen = GameScreenOptions.IsFullScreen;
            // 확정값의 화면 모드만 먼저 비교하고, 다를 때만 옵션 전체 사본을 만든다.
            if (fullScreen == (CurrentWindowMode == GameOptions._borderlessWindowMode)) return;
            GameOptions current = Current;
            // 실제 창이 전체 화면 계열이면 테두리 없음, 아니면 창 모드로 저장한다. 저장 요청이 반영될 때까지 다음 확인을 미룬다.
            current.WindowMode = fullScreen ? GameOptions._borderlessWindowMode : GameOptions._windowedWindowMode;
            _screen.DelayModeCheck();
            SaveScreenModeAsync(current).Forget();
        }

        // 화면 모드 감시가 저장을 요청해도 되는 상태인지다. 옵션 파일을 읽기 전에는 읽지 못한 파일을 덮어쓰지 않으려고,
        // 저장 작업이 도는 동안에는 확정값이 곧 바뀔 수 있어 거부한다. 파일 읽기는 저장소가 있을 때만 하므로 _initialized가
        // 저장소 확인을 겸하고, 감시를 켜는 Configure가 _release도 만든다.
        bool CanSyncScreenMode => _initialized && !_release!.Started && !_save.IsRunning && _canSave();

        // 기다리는 후보, 쓰는 중인 후보, 실패한 재시도 후보 중 하나라도 있는지다. Pending != null과 같은 조건이지만,
        // 매 프레임 화면 모드 감시도 부르므로 사본을 만들지 않는다.
        bool HasPendingCandidate => _queued != null || _writing != null || _failed != null;

        // 화면 모드 저장은 기다려 줄 호출자가 없어 여기서 실패를 받는다. 실패 후보 보존과 표시 복원은 SaveAsync가 맡는다.
        async UniTask SaveScreenModeAsync(GameOptions candidate)
        {
            try
            {
                await SaveAsync(candidate);
            }
            // 예외 경계: 기다리는 호출자가 없는 저장 실패를 한 번 기록한다.
            catch (Exception error)
            {
                Log.Instance.Exception(error);
            }
        }

        // 원본처럼 옵션 파일이 없거나 손상되면 백업을 읽고, 쓸 수 있는 백업은 확정값으로 삼아 주 파일에 다시 쓴다. 그런 백업이 없으면 기본값이다.
        // 손상된 파일을 찾았을 때만 경고를 한 번 남긴다. 에디터에서도 복구가 멈추지 않도록 InvalidData 대신 경고로 남긴다.
        // 파일이 없는 첫 실행은 경고 없이 기본값이다. 저장소 입출력 실패는 잡지 않고 시작 실패로 올린다.
        async UniTask LoadAsync()
        {
            var (_, loaded, primaryError) = await ReadOptionsAsync(_storage!.ReadAsync);
            string? warning = null;
            if (loaded == null)
            {
                var (backupText, backup, backupError) = await ReadOptionsAsync(_storage.ReadBackupAsync);
                if (backup != null)
                {
                    loaded = backup;
                    // 검증한 백업이 그사이 바뀌었으면 주 파일을 그대로 두고, 다음 저장이 확정값으로 다시 쓴다.
                    bool restored = await _storage.RestoreBackupAsync(SaveManager.OptionsKey, backupText!);
                    if (primaryError != null)
                        warning = restored
                            ? "옵션 파일이 손상되어 백업으로 복원했습니다. " + primaryError
                            : "옵션 파일이 손상되어 백업 값을 적용했습니다. 백업이 확인 뒤 바뀌어 옵션 파일은 다음 저장 때 다시 씁니다. " + primaryError;
                }
                else
                {
                    loaded = DefaultOptions();
                    if (primaryError != null)
                        warning = "옵션 파일이 손상되었고 쓸 수 있는 백업이 없어 기본값을 적용했습니다. " + primaryError
                            + (backupError != null ? " 백업: " + backupError : string.Empty);
                    else if (backupError != null)
                        warning = "옵션 파일이 없고 백업이 손상되어 기본값을 적용했습니다. " + backupError;
                }
            }
            // 원본처럼 저장된 해상도를 목록 값으로 맞추지 않고 그대로 확정한다. 화면비 보정은 적용할 때만 한다.
            _current = loaded;
            _initialized = true;
            if (warning != null) Log.Instance.Warning(warning, this);
        }

        // 옵션 파일 하나를 읽어 해석한다. 없으면 모두 null이고, 손상됐으면 오류 문구만 있다. 입출력 실패는 그대로 올린다.
        async UniTask<(string? text, GameOptions? options, string? error)> ReadOptionsAsync(Func<string, UniTask<string?>> read)
        {
            string? text;
            try { text = await read(SaveManager.OptionsKey); }
            // 예외 경계: 저장소의 잘못된 UTF-8 판정을 파일 손상으로 분류해 백업 복구로 넘긴다. 다른 실패는 잡지 않는다.
            catch (DecoderFallbackException decodeError)
            {
                return (null, null, "옵션 저장 파일이 올바른 UTF-8이 아닙니다: " + decodeError.Message);
            }
            if (text == null) return (null, null, null);
            if (!_json.TryRead(text, out GameOptions? options, out string reason)) return (text, null, reason);
            return (text, options, null);
        }

        // 주입된 기본값은 코덱을 거치지 않지만, 제품 구성의 GameResolutions.CreateDefaultOptions가 검증을 통과한 값만 만든다.
        GameOptions DefaultOptions() => _defaults?.Clone() ?? new GameOptions();

        // 후보는 SaveAsync가 검증한 사본이라 다시 검증하지 않는다. 기다리는 동안 다른 참조가 고쳐도 영향이 없게 복제해 쓰고,
        // 쓰기가 성공해야 확정한다. 매 쓰기는 덮어쓰는 옵션 파일을 백업으로 남겨, 다음 시작에 손상된 파일을 복구할 수 있게 한다.
        async UniTask WriteAsync(GameOptions candidate)
        {
            GameOptions frozen = candidate.Clone();
            string payload = _json.Write(frozen);
            await _storage!.WriteAsync(SaveManager.OptionsKey, payload, keepBackup: true);
            _current = frozen;
        }
    }
}
