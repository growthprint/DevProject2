#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Playables;

namespace Game.App
{
    // 인트로 Timeline을 재생하고, 정상 종료나 원본처럼 2초 누른 스킵에서 한 번만 다음 진행을 요청한다.
    // 앱 준비 중에는 재생하지 않고, Timeline 정지 콜백은 완료로 쓰지 않는다. 원본 LevelManager의 레벨 초기화 알림은 공통 수명이 받는다.
    public sealed class IntroSceneController : SceneLevelController, ISceneCancelHandler
    {
        [SerializeField] PlayableDirector _director = null!;
        [SerializeField] IntroUguiView _view = null!;
        // 기본값은 원본 Timeline이 쓰는 FMOD 이벤트 경로다.
        [SerializeField] string _audioEvent = "event:/CG/cg_opening";
        GameRuntime? _runtime;
        // 레벨 초기화가 적용 알림을 구독한 앱의 옵션 관리자다. 레벨 정리는 앱이 먼저 사라졌을 수 있어 정적 속성 대신 이 참조로 푼다.
        GameOptionsManager? _options;
        // 원본 cg_opening 연출음의 재시작, 일시정지, 정지는 SceneLoopAudio가 맡는다. 이 컨트롤러는 오디오 관리자를 직접 들지 않는다.
        SceneLoopAudio? _playingAudio;
        InputAction? _interact;
        IDisposable? _anyButton;
        string _binding = "";
        // _started는 첫 재생 준비를 마쳤는지, _completionRequested는 다음 진행을 한 번 요청했는지다. 테스트가 _started를 리플렉션으로 읽는다.
        // 요청이 거절되거나 취소돼도 인트로를 다시 재생하지 않는다.
        bool _started, _completionRequested;
        float _pressTime, _hideTimer, _alpha;
        // 원본 스킵 유지 시간과, 입력 뒤 스킵 안내를 보이는 시간이다. 둘 다 2초지만 뜻이 달라 상수를 나눈다.
        const float _skipHoldSeconds = 2f;
        const float _skipHintSeconds = 2f;
        // 스킵 안내가 나타나고 사라지는 초당 투명도 변화량이다.
        const float _skipHintFadeSpeed = 10f;
        // Timeline 끝 시각과의 부동소수 오차를 정상 종료로 보는 허용 범위(초)다.
        const double _endTolerance = .0001;

        // SceneContext가 캐릭터와 월드를 준비한 뒤 공통 수명이 한 번만 부른다. 재생 위치는 저장하지 않으므로 다시 불러오면 인트로를 처음부터
        // 재생한다. 대입, 구독 순서로 준비하며, Runtime을 대입한 뒤부터는 레벨 정리가 구독과 연출음 소유자를 되돌린다.
        protected override void OnInitializeLevel()
        {
            // SceneContext는 Gameplay를 채운 뒤에만 레벨 초기화를 알리고 LevelInited를 세운다.
            SceneGameplayContext context = SceneContext.Instance!.Gameplay!;
            // 입력은 저장한 키 설정을 따르는 앱의 입력 실행 사본에서 찾는다. 레벨 초기화는 앱이 있을 때만 온다. 경로는 공용 상수다.
            InputAction interact = GameManager.InputActions.FindAction(GameInputPaths._playerInteract, true);
            GameRuntime runtime = context.Runtime;
            _interact = interact;
            // 스킵 안내 키캡은 다른 씬과 같은 키보드 표기로 계산하고, 옵션이 적용될 때마다 다시 계산한다.
            _binding = GameLocalization.KeyboardBindingLabel(interact);
            _director.playOnAwake = false;
            _director.timeUpdateMode = DirectorUpdateMode.Manual;
            _director.extrapolationMode = DirectorWrapMode.Hold;
            // 연출음 소유자는 준비 때 앱 오디오 관리자로 만들고, StartPlayback에서 처음부터 재생한다. 캐릭터 숨김은 SceneContext 설정이 맡는다.
            _playingAudio = new SceneLoopAudio(GameManager.Audio, _audioEvent);
            // Runtime은 연출음 소유자를 만든 뒤 대입한다. 재생과 상태 알림 경로는 _runtime 하나로 연출음 소유자가 있는지까지 본다.
            _runtime = runtime;
            // 옵션 관리자는 구독 전에 필드에 잡아 두어, 레벨 정리가 정적 속성 없이 같은 관리자에서 구독을 푼다.
            GameOptionsManager options = GameManager.Options;
            _options = options;
            options.Applied += RefreshBinding;
            runtime.StateChanged += OnRuntimeChanged;
            _view.ShowSkip(0, 0, _binding);
            // 루트가 로드 때부터 켜져 있으면 OnEnable은 초기화 전에 지나갔으므로 아무 키 구독을 여기서 건다.
            if (isActiveAndEnabled) ListenAnyButton();
        }

        // 활성 화면에서 Runtime의 게임플레이 활성 판정이 참일 때만 인트로를 재생한다.
        bool CanPlay => isActiveAndEnabled && _runtime != null && _runtime.IsGameplayActive;

        // 씬 로드 때 켜지는 첫 OnEnable은 초기화 전이라 건너뛰고, 초기화를 마친 뒤 다시 켜질 때만 아무 키 구독을 건다.
        void OnEnable()
        {
            if (_runtime != null) ListenAnyButton();
        }

        // 아무 키나 누르면 스킵 안내를 보인다. 이 구독은 켜진 동안만 유지하고 OnDisable이 푼다.
        void ListenAnyButton()
        {
            _anyButton = InputSystem.onAnyButtonPress.Call(_ => { if (CanPlay) _hideTimer = _skipHintSeconds; });
        }

        // Timeline 시계를 직접 흘려, 일시정지나 비활성화로 멈춘 것을 완료로 오인하지 않는다. 초기화 전에는 CanPlay가 거짓이다.
        void Update()
        {
            if (!CanPlay) return;
            if (_completionRequested) return;
            if (!_started)
                // 초기화와 활성화 순서와 상관없이 첫 프레임 준비는 늘 StartPlayback 한 곳에서 한다.
                StartPlayback();
            if (_director.state != PlayState.Playing) _director.Play();
            double duration = _director.duration;
            float delta = Time.deltaTime;
            _director.time = Math.Min(duration, _director.time + delta);
            _director.Evaluate();
            bool pressing = Application.isFocused && _interact!.IsPressed();
            _pressTime = pressing ? Mathf.Min(_skipHoldSeconds, _pressTime + delta) : Mathf.MoveTowards(_pressTime, 0f, delta);
            if (pressing) _hideTimer = _skipHintSeconds;
            // 안내가 보일 시간 동안은 나타나고, 지나면 같은 속도로 사라진다.
            _alpha = Mathf.MoveTowards(_alpha, _hideTimer > 0f ? 1f : 0f, _skipHintFadeSpeed * delta);
            _hideTimer = Mathf.Max(0f, _hideTimer - delta);
            _view.ShowSkip(_pressTime / _skipHoldSeconds, _alpha, _binding);
            // 2초 누른 스킵이나 Timeline 끝 도달(부동소수 오차 허용)에서 진행을 한 번 요청한다.
            if (_pressTime >= _skipHoldSeconds || _director.time >= duration - _endTolerance)
            {
                _completionRequested = true;
                _director.Pause();
                CompleteAsync().Forget();
            }
        }

        // 정상 종료와 스킵이 같은 프레임에 와도 여기서 한 번만 다음 진행을 요청한다. 실패 처리는 SceneCommand가 맡고,
        // 거절이나 취소로 끝나도 재생을 반복하지 않는다.
        async UniTask CompleteAsync()
        {
            // Forget으로 시작하는 루트이므로 모든 문장을 try 안에 두고, 취소는 기록 없이 끝내며 그 밖의 실패는 기록한다.
            try
            {
                // 캠페인 단계 진행은 씬 흐름(GameManager.Scenes)이 맡는다. 거절되거나 실패해도 인트로를 다시 재생하지 않는다.
                // Update가 CanPlay로 Runtime 활성을 확인한 프레임에서 시작하므로 앱이 살아 있다.
                await SceneCommand.TryRunAsync(() => GameManager.Scenes.AdvanceAsync(CampaignStage.Prologue), this);
            }
            // 예외 경계: Forget으로 시작한 루트라, SceneCommand 밖의 예상하지 못한 실패를 잃지 않도록 기록하고 끝낸다.
            catch (Exception error) { Log.Instance.Exception(error, this); }
        }

        // 로딩 덮개가 사라지는 상태 알림에서 Timeline을 바로 평가해, 원본 초기 UI가 잠깐 보이는 것을 막는다.
        void StartPlayback()
        {
            _director.Play();
            _director.time = 0;
            _director.Evaluate();
            _started = true;
            // 원본 인트로 연출음을 처음부터 다시 재생한다. 던지지 않으며, 실패는 GameAudioManager가 동작과 경로마다 한 번 기록한다.
            // CanPlay가 확인한 _runtime보다 연출음 소유자가 먼저 생기므로 늘 있다.
            _playingAudio!.Restart();
        }

        // Runtime 상태가 바뀌거나 비활성화될 때 다음 Update를 기다리지 않고, 첫 렌더 전에 인트로 재생 상태를 맞춘다.
        // 씬 언로드 중의 OnDisable에서도 불리고 파괴 순서가 정해져 있지 않아, 먼저 파괴된 PlayableDirector는 Unity null 검사로 건너뛴다.
        void OnRuntimeChanged()
        {
            if (CanPlay && !_started && !_completionRequested) StartPlayback();
            if (!CanPlay && _started && _director != null) _director.Pause();
            // 오디오 실패는 GameAudioManager가 던지지 않고 기록하므로, 이 상태 알림이 저장 결과를 흔들지 않는다.
            // 구독과 OnDisable 호출은 연출음 소유자를 만든 뒤에만 오고, 레벨 정리는 구독을 푼 뒤에 비운다.
            _playingAudio!.SetPaused(!CanPlay || _completionRequested);
        }

        // 일시정지나 씬 정리로 멈춘 재생은 진행을 요청하지 않는다.
        void OnDisable()
        {
            _anyButton?.Dispose();
            _anyButton = null;
            if (_runtime != null) OnRuntimeChanged();
        }

        // 인트로 재생 중 ESC가 일시정지 메뉴를 열지 않도록, UI가 일시정지보다 먼저 SceneContext를 거쳐 묻는 이 질의에서 ESC를 소비한다.
        // 재생 중이고 완료 요청 전일 때만 소비하므로, 진행이 거절되어 멈춘 인트로에서는 일시정지 메뉴를 열 수 있다.
        // 전환 중 ESC는 UI가 이 질의 전에 무시한다.
        public bool TryCancelPanel() => CanPlay && !_completionRequested;

        // 키 재바인딩을 포함한 옵션 적용 뒤 스킵 안내 키캡을 다시 계산한다.
        void RefreshBinding(GameOptions options)
        {
            // 초기화가 _interact를 찾은 뒤에만 구독하고, 레벨 정리는 구독을 푼 뒤에 _interact를 비운다.
            _binding = GameLocalization.KeyboardBindingLabel(_interact!);
        }

        // 원작 onStartedLoadingScene 구독자처럼 씬을 내리기 직전에 레벨 초기화가 건 구독과 아무 키 구독을 풀고 연출음 소유자를 정리한다.
        // 공통 수명이 한 번만 부르며 던지지 않는다. 단계마다 Cleanup.Run으로 계속하며, 앱 수명 구독은 Runtime이 먼저 파괴됐어도 푼다.
        // 정리 뒤에는 _runtime이 비어 CanPlay가 거짓이다. 연출음 정리 실패는 GameAudioEvent가 경로마다 한 번 기록한다.
        protected override void OnReleaseLevel()
        {
            IDisposable? anyButton = _anyButton;
            _anyButton = null;
            if (anyButton != null) Cleanup.Run(anyButton.Dispose, this);
            // 앱이 먼저 파괴될 수 있어 정적 속성 대신 구독한 Runtime과 관리자로 푼다. 파괴된 쪽도 C# 이벤트가 이 핸들러를 들고 있어
            // 늦은 알림이 파괴된 이 객체에 올 수 있으므로, Unity null 비교 대신 참조만 보고 푼다.
            GameRuntime? runtime = _runtime;
            GameOptionsManager? options = _options;
            _runtime = null;
            _options = null;
            if (!ReferenceEquals(runtime, null)) Cleanup.Run(() => runtime.StateChanged -= OnRuntimeChanged, this);
            if (!ReferenceEquals(options, null)) Cleanup.Run(() => options.Applied -= RefreshBinding, this);
            SceneLoopAudio? playingAudio = _playingAudio;
            _playingAudio = null;
            if (playingAudio != null) Cleanup.Run(playingAudio.Dispose, this);
            _interact = null;
        }
    }
}
