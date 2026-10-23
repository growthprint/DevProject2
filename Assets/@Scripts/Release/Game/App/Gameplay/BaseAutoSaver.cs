#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원본 BaseSceneUtilities처럼 기지 씬에만 두는 자동 저장 컴포넌트다. 기지 UI 활동이 저장을 요청하면 쿨다운(BaseAutoSaveSchedule)이
    // 끝난 뒤 아무도 기다리지 않는 저장 지점(SaveManager.SaveAtPointAsync)으로 쓴다. 레벨 초기화 알림을 받으면 일정을 시작하고,
    // 씬이 내려가 파괴되면 멈춘다. 그래서 기지 밖에는 인스턴스가 없고, 그곳의 UI 활동 알림은 아무 일도 하지 않는다.
    [DisallowMultipleComponent]
    public sealed class BaseAutoSaver : MonoBehaviour
    {
        // 쿨다운이 끝났어도 전환이나 편집이나 다른 저장 때문에 바로 저장할 수 없을 때 다시 확인하는 간격(초)이다.
        const double _retrySeconds = .25d;

        // 원작 LevelManager.Instance처럼 지금 로드된 기지 씬의 자동 저장이다. Awake가 채우고 OnDestroy가 비운다.
        static BaseAutoSaver? _current;

        readonly BaseAutoSaveSchedule _schedule = new BaseAutoSaveSchedule();
        // 레벨 초기화로 일정을 시작한 Runtime이다. 시작 전이나 파괴 뒤에는 null이라 요청을 받지 않는다.
        GameRuntime? _runtime;
        // 요청을 처리하는 루프가 도는지와 그 세대다. 취소 토큰 대신 루프가 대기 뒤마다 세대를 비교해, 멈춘 뒤의 루프는 조용히 끝난다.
        bool _looping;
        int _loopGeneration;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 값이 남으므로 세션 시작마다 비운다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => _current = null;

        // 기지 UI 활동을 알린다. 모달과 인벤토리와 창고를 열고 닫을 때, ESC와 Alt 입력, 외형 편집 복귀가 부른다.
        // 지금 기지 씬의 자동 저장에 넘기고, 기지 밖이라 인스턴스가 없으면 아무 일도 하지 않는다. 파괴된 인스턴스는 Unity null 비교로 거른다.
        public static void NotifyUiActivity()
        {
            BaseAutoSaver? current = _current;
            if (current != null) current.Request();
        }

        // 앱 종료가 기다리던 요청의 루프만 멈춘다. 이미 시작한 쓰기는 저장 관리자가 끝까지 진행한다.
        internal static void CancelCurrentLoop()
        {
            BaseAutoSaver? current = _current;
            if (current != null) current.CancelLoop();
        }

        // 씬이 로드되며 지금 기지의 자동 저장이 된다. 원본 매니저처럼 Awake에서 레벨 알림을 구독하고 OnDestroy에서 끊는다.
        void Awake()
        {
            _current = this;
            SceneContext.LevelInitialized += OnLevelInitialized;
        }

        // 구독보다 레벨 초기화가 먼저 끝났으면 바로 일정을 시작한다.
        void Start()
        {
            if (SceneContext.LevelInited) OnLevelInitialized();
        }

        // 레벨이 준비된 기지 월드의 Runtime으로 일정을 시작한다. 레벨 컨텍스트는 Unity 객체라 Unity null 비교로 확인한다.
        void OnLevelInitialized()
        {
            SceneContext? level = SceneContext.Instance;
            SceneGameplayContext? gameplay = level != null ? level.Gameplay : null;
            if (gameplay != null) Arm(gameplay.Runtime);
        }

        // 돌던 루프를 멈추고 일정을 지금 시각으로 초기화한다. 쌓인 요청은 버리고, 첫 자동 저장은 5초 뒤부터 가능하다.
        // 레벨 초기화 알림과, 실제 씬 대신 기지를 흉내 내는 테스트 씬 흐름이 부른다.
        internal void Arm(GameRuntime runtime)
        {
            CancelLoop();
            _runtime = runtime;
            _schedule.Reset(runtime.AutoSaveNow());
        }

        // 씬 언로드로 파괴될 때 구독과 루프를 끝내 다음 세션이나 다른 씬에 저장이 닿지 않게 한다.
        void OnDestroy()
        {
            SceneContext.LevelInitialized -= OnLevelInitialized;
            CancelLoop();
            _runtime = null;
            if (ReferenceEquals(_current, this)) _current = null;
        }

        // 원본처럼 기지에서 실행 중일 때만 저장을 요청하고, 쿨다운이 끝나 저장할 수 있게 되면 막지 않고 저장한다.
        // 일정 초기화 뒤 0.5초 안의 요청은 버린다. 일정 시작 전, 종료 중, 결과 화면에서는 무시한다.
        void Request()
        {
            GameRuntime? runtime = _runtime;
            if (runtime == null || runtime.State != GameRuntimeState.Running || runtime.SessionState == GameSessionState.Closure)
                return;
            if (!_schedule.Request(runtime.AutoSaveNow()) || _looping) return;
            _looping = true;
            RunAsync(runtime, _loopGeneration).Forget();
        }

        // 루프만 멈춘다. 이미 시작한 쓰기는 저장 관리자가 끝까지 진행하고, 다른 작업이 SaveManager.CurrentSave로 그 끝을 기다린다.
        void CancelLoop()
        {
            _looping = false;
            _loopGeneration++;
        }

        // 요청이 남아 있는 동안 쿨다운이 끝나기를 기다렸다가, 저장할 수 있으면 요청을 소비하고 저장한다.
        // 저장할 수 없으면 짧게 기다려 다시 확인한다. 요청이 없거나, 루프가 멈췄거나, Runtime이 버려지면 끝난다.
        // 파괴는 루프 세대를 올리므로 시작할 때의 Runtime을 붙잡아 써도 파괴 뒤에는 쓰지 않는다.
        async UniTaskVoid RunAsync(GameRuntime runtime, int generation)
        {
            try
            {
                while (IsCurrent(runtime, generation) && _schedule.IsRequested)
                {
                    double now = runtime.AutoSaveNow();
                    // 쿨다운이 끝났는지는 TryTake만 판정한다. 저장 실패는 저장 관리자가 한 번 기록하고 다음 요청이 다시 쓴다.
                    // 아무도 기다리지 않는 저장이라 플레이를 막지 않으며, 끝난 뒤 다음 요청을 확인한다. 저장 관리자는 GameManager가 내주고,
                    // 루프 조건이 Runtime이 버려지지 않았음을 보므로 같은 루트의 앱도 살아 있다.
                    if (CanStart(runtime) && _schedule.TryTake(now))
                    {
                        await GameManager.Saves.SaveAtPointAsync();
                        continue;
                    }
                    await runtime.AutoSaveDelayAsync(TimeSpan.FromSeconds(now < _schedule.DueAt ? _schedule.DueAt - now : _retrySeconds));
                }
            }
            // 예외 경계: Forget으로 시작한 루프의 맨 바깥이라, 대기나 조건 판정의 예상 밖 실패를 잃지 않도록 기록하고 끝낸다.
            catch (Exception error) { Log.Instance.Exception(error); }
            finally
            {
                if (generation == _loopGeneration) _looping = false;
            }
        }

        // 이 루프가 아직 지금 세대이고 Runtime이 버려지지 않았는지 본다.
        bool IsCurrent(GameRuntime runtime, int generation) => generation == _loopGeneration && !runtime.IsAbandoned;

        // 쿨다운이 끝났을 때 저장을 시작해도 되는지 본다. 실행 중, 전환 없음, 플레이나 일시정지는 CanMutateSession이 보고,
        // 편집 중은 Customizing 상태라 거기서 걸러진다. 쓰기가 진행 중이면 겹치지 않게 다음 확인까지 미룬다. 쓰기 상태는 GameManager의 저장 관리자가 가진다.
        static bool CanStart(GameRuntime runtime) => runtime.CanMutateSession && !GameManager.Saves.IsSaving;
    }
}
