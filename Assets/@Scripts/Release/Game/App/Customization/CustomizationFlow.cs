#nullable enable annotations
using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using DuckCharacter;
using DuckCharacter.AvatarParts;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.App
{
    // GameManager 프리팹의 자식 컴포넌트로, 원작 Interact_CustomFace 흐름처럼 외형 편집을 열고 확인하고 취소하고 닫는다.
    // 편집 한 번 동안 편집 씬과 미리보기의 수명도 맡는다. 확정 외형은 외형 소유자의 메모리에만 반영하고 파일은 다음 저장 지점이 쓴다.
    // 공간 해제는 GameRuntime이 등록한 CloseSpaceAsync 한 길로만 하므로 OnDestroy에서 정리하지 않는다.
    // sealed가 아닌 것은 테스트 하위 클래스가 편집 공간 멤버(internal virtual)를 대역으로 바꾸기 때문이다.
    [DisallowMultipleComponent]
    public class CustomizationFlow : MonoBehaviour
    {
        // 편집이 끝나는 이유다. 열기 실패, 완료, 취소는 편집 직전 상태로 돌아가고,
        // 앱 정상 종료 때 초안을 버리는 경우는 일시정지 상태가 된다.
        enum ExitReason { OpenFailed, Finish, StopDiscard }

        // 아래 세 참조는 Unity가 컴포넌트를 만든 뒤 GameRuntime이 Configure로 넣으므로 readonly로 둘 수 없다.
        // 로더는 앱 것을 공유하지만 주 월드 SceneFlow를 교체하지 않는다.
        ISceneLoader _loader;
        // 편집 씬(GameScene.Customization)의 씬 참조를 찾는 표다.
        SceneTable _scenes;
        // 앱 수명 공용 HUD다. 편집 씬은 자기 HUD 없이 준비 때 이 참조를 받아 HUD 숨김과 알림에 쓴다.
        SharedHudView _hud;
        // 로더가 연 편집 씬이다. 열린 씬이 없으면 null이고, 해제는 ReleaseSpaceAsync 한 곳에서만 한다.
        Scene? _scene;
        CustomizationSceneContext? _context;
        // 편집 씬을 활성화하기 직전의 활성 씬이다. 활성 씬을 바꾼 이 흐름이 닫을 때와 열기 실패 정리 때 되돌린다.
        Scene _activeBeforeOpen;
        // Opened를 알린 편집만 닫을 때 Closed를 알리게 한다. 화면을 켜기 전에 실패한 열기는 둘 다 알리지 않는다.
        bool _announced;
        // 독점 예약, 세션 상태, 입력 차단을 맡는 Runtime이다. 제품 구성과 테스트 하위 Runtime이 Bind로 넣는다.
        // 저장 관리자, 외형 소유자, 세션 관리자는 이 Runtime이 아닌 GameManager가 내준다.
        GameRuntime _runtime;
        // 편집 공간이 열려 있는 동안 참이다. 편집 중에도 Busy는 아니며, 열기와 완료와 취소 명령만 잠시 전환 잠금을 잡는다.
        bool _editing;
        // 편집을 열기 직전의 세션 상태다. 편집이 끝나면 이 상태로 돌아간다.
        GameSessionState _beforeCustomization;
        // 편집을 열고 닫을 때마다 올리는 세대 번호다. 이미 끝난 편집에서 늦게 온 화면 콜백을 이 값으로 거른다.
        long _editGeneration;
        // 편집하는 동안 주 월드의 플레이어 입력을 막는 핸들이다. 시간은 계속 흐르므로 입력만 막고, 편집 종료가 해제한다.
        IDisposable? _editInputBlock;
        // 편집 확정 동안만 참이다. 확정은 독점 작업 안에서 돌아 CanMutateSession이 거짓이므로, 그동안 이 표시가 소유자 명령을 연다.
        bool _committingCustomization;

        // 편집 화면이 켜지고 닫힐 때 알리는 정적 이벤트다. 게임 씬은 이 알림으로 플레이어 리그와 월드 표시를 멈추고 되살린다.
        public static event Action? Opened, Closed;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            Opened = null;
            Closed = null;
        }

        // 직렬화할 수 없는 입력이라 GameRuntime이 생성 지점에서 이 메서드로 넘긴다.
        // 구성 때는 콘텐츠를 로드하지 않는다. 편집 씬은 OpenAsync가 열고, 파츠 라이브러리는 앱 수명 게임플레이 데이터에서 읽는다.
        internal void Configure(ISceneLoader loader, SceneTable scenes, SharedHudView hud)
        {
            _loader = loader;
            _scenes = scenes;
            _hud = hud;
        }

        // 편집 명령이 쓰는 Runtime을 묶는다. 제품 구성과 테스트 하위 Runtime이 첫 명령 전에 한 번 부른다.
        internal void Bind(GameRuntime runtime)
        {
            _runtime = runtime;
        }

        // 편집 공간이 열려 있는지 알린다. 종료가 편집 초안을 버릴지 정하는 데 쓴다.
        internal bool IsEditing => _editing;
        // 상태 소유자의 명령 관문(GameRuntime.AcceptsSessionCommands)이 편집 확정 동안만 명령을 열도록 읽는다.
        internal bool IsCommittingCustomization => _committingCustomization;

        // 기지나 프롤로그 거울에서 외형 편집을 연다. 예약 직후 Customizing 상태로 일반 명령을 막는다.
        // 편집 중에도 주 월드의 시간은 흐르고, 진행 중인 저장이 끝난 뒤 플레이어 입력만 막는다.
        public UniTask OpenAsync() => _runtime.Operations.Run(() =>
        {
            _beforeCustomization = _runtime.SessionState;
            _runtime.SessionState = GameSessionState.Customizing;
        }, OpenCoreAsync);

        // 편집 취소는 초안을 버린다. 프롤로그 거울은 편집 전 외형으로 커밋해 거울 닫힘을 남기고, 기지는 커밋 없이 닫는다.
        public UniTask CancelAsync() =>
            RunEditCommand(() => CampaignRules.IsPrologueMirrorSession(_runtime.Snapshot())
                ? CommitAsync(false)
                : ExitAsync(ExitReason.Finish));

        // 확인은 미리보기에 실제 적용된 외형을 확정한다. 끝날 때까지 중복 완료와 초안 변경을 거부한다.
        // 제품에서는 화면 콜백만 부르고, 테스트는 이 경로로 확인 버튼을 흉내 낸다.
        internal UniTask CompleteAsync() => RunEditCommand(() => CommitAsync(true));

        // 앱 정상 종료 전에 편집 초안을 버리고 편집 공간을 닫는다. 편집 종료 경로는 던지지 않으므로 종료 쪽은 바로 기다린다.
        internal Task DiscardForStopAsync() => ExitAsync(ExitReason.StopDiscard);

        // 세대를 올려 끝난 편집의 늦은 화면 콜백을 막고 편집 중 표시를 끈다. 강제 종료를 포함한 모든 편집 종료가 함께 쓴다.
        internal void EndEditLifetime()
        {
            ++_editGeneration;
            _editing = false;
        }

        // 열기에 실패하면 공통 종료 경로로 편집 직전 상태를 되살린다. 공간이 이미 닫혔으면 닫기는 아무 일도 하지 않는다.
        // 복구 중 실패는 Cleanup이 기록만 하고, 호출자에게는 원래 열기 실패를 다시 던진다.
        // 기다리는 사이 Runtime이 버려지면(강제 종료나 파괴) 조용히 멈추고, 편집 상태와 입력 차단은 앱 정리가 되돌린다.
        async Task OpenCoreAsync()
        {
            long generation = ++_editGeneration;
            try
            {
                // 진행 중인 저장(기지 자동 저장 포함)이 끝난 뒤에 편집을 열어, 저장과 편집 확정이 겹치지 않게 한다.
                await TrackedTask.ObservePreviousAsync(GameManager.Saves.CurrentSave);
                if (_runtime.IsAbandoned) return;
                // 앱 정리는 독점 작업이 끝난 뒤에만 시작하므로 차단은 항상 걸린다. 결과는 보지 않는다.
                // Customizing은 실행 중으로 치므로 ApplyPlayPause가 시간을 멈추지 않는다.
                _runtime.GameplayInput.TryAcquire(out _editInputBlock);
                _runtime.ApplyPlayPause();
                // 외형 소유자의 현재 외형으로 초안을 만든다. 편집 공간은 성공해도 실패해도 메인 스레드에서 끝나므로 바로 기다린다.
                await OpenSpaceAsync(GameManager.Appearance.Current,
                    () => HandleScreenAction(generation, true),
                    () => HandleScreenAction(generation, false));
                if (_runtime.IsAbandoned) return;
                _editing = true;
                SetSpaceBusy(false);
            }
            // 예외 경계: 열기 실패는 공간이 스스로 되돌린 뒤 진입 직전 상태를 복구하고 원래 실패를 유지한다.
            catch (Exception)
            {
                await Cleanup.RunAsync(() => ExitAsync(ExitReason.OpenFailed).AsUniTask());
                throw;
            }
        }

        // 화면 버튼 콜백이다. 세대가 다르면 끝난 편집이고, IsBusy면 열기나 다른 명령이 진행 중이라 무시한다.
        // 편집 중이 아니거나 실행 중이 아닌 경우도 이 두 검사로 걸러진다. 오류 기록과 안내는 ObserveScreenActionAsync가 맡는다.
        void HandleScreenAction(long generation, bool complete)
        {
            if (generation != _editGeneration || _runtime.IsBusy) return;
            ObserveScreenActionAsync(generation, complete).Forget();
        }

        // 화면 버튼으로 시작한 명령의 오류를 여기서 한 번 기록하고, 완료인지 취소인지에 맞는 안내를 한 번 띄운다.
        // 명령을 이 메서드 안에서 시작하므로 동기 예외도 여기서 잡힌다. ESC와 오류 창 경로의 안내는 GameUiController가 맡는다.
        async UniTask ObserveScreenActionAsync(long generation, bool complete)
        {
            try { await (complete ? CompleteAsync() : CancelAsync()); }
            // 예외 경계: 화면 버튼 이벤트 경계에서 명령 실패의 원인을 기록하고 번역 가능한 복구 안내만 화면에 보낸다.
            catch (Exception error)
            {
                // 늦게 온 오류도 기록하지만, 이미 끝난 편집 화면에는 안내를 띄우지 않는다.
                Log.Instance.Exception(error, this);
                // 확인은 파일을 쓰지 않으므로 완료 실패는 저장 실패가 아닌 일반 변경 실패로 안내한다. 취소 실패는 편집 종료 실패로 안내한다.
                if (generation == _editGeneration)
                    ShowSpaceError(complete ? "customization.failed" : "customization.exitFailed");
            }
        }

        // 완료와 취소의 공통 예약이다. 예약이 받아들여질 때만 화면을 잠근다.
        // 예약이 풀린 뒤에도 편집이 이어지면, 예컨대 커밋이 거절되면, finally가 잠금을 푼다. 예약을 잡다가 난 동기 예외도 이 finally를 거친다.
        async UniTask RunEditCommand(Func<Task> body)
        {
            try
            {
                await _runtime.Operations.Run(null, () =>
                {
                    SetSpaceBusy(true);
                    return body();
                });
            }
            finally { if (_editing) SetSpaceBusy(false); }
        }

        // 확인과 프롤로그 취소가 함께 쓰는 커밋이다. 확인은 미리보기 외형을, 프롤로그 취소는 외형 소유자의 현재 외형을 확정한다.
        // 거울 세션이면 거울 닫힘도 함께 메모리에만 확정하고, 파일은 다음 저장 지점이 쓴다.
        // 커밋 전 거절은 초안과 편집 공간을 남겨 같은 화면에서 다시 시도하게 하고, 커밋 뒤에는 공통 종료 경로로 닫는다.
        async Task CommitAsync(bool applyAppearance)
        {
            // 외형 검증은 외형 소유자가 커밋 때 한 번만 한다. 거절 사유를 돌려받지 못하므로 고정 문구로 던지고, 안내는 호출 경계가 한다.
            if (!TryApplyInExclusive(applyAppearance ? CaptureAppliedAppearance() : GameManager.Appearance.Current))
                throw new InvalidOperationException("편집한 외형을 확정하지 못했습니다.");
            await ExitAsync(ExitReason.Finish);
        }

        // 편집 중에는 Runtime이 세션 명령 게이트를 닫아 둔다. 이 동기 명령 한 번 동안만 허브와 소유 컴포넌트의 게이트를 함께 열고
        // 바로 닫으므로 다른 명령이 끼어들 수 없다. 확정 알림 구독자가 표시만 하고 명령을 내지 않는다는 전제에 기댄다.
        bool TryApplyInExclusive(DuckAvatarPreset appearance)
        {
            GameSessionManager session = GameManager.SessionManager!;
            session.SetCommandsAllowed(true);
            _committingCustomization = true;
            try { return GameplayCommands.TryCommitCustomization(session, GameManager.Appearance, appearance); }
            finally
            {
                session.SetCommandsAllowed(false);
                _committingCustomization = false;
            }
        }

        // 편집을 끝내는 유일한 경로다. 편집 공간을 닫고, 늦은 콜백을 막고, 입력 차단을 푼 뒤 세션 상태를 되돌린다.
        // 기지나 프롤로그의 캐릭터와 월드 등록과 위치는 편집 전 그대로 이어서 쓰고, 이 경로는 던지지 않는다.
        async Task ExitAsync(ExitReason reason)
        {
            // 공간 닫기는 던지지 않고 정리 실패를 스스로 기록한다. 메인 스레드에서 끝나므로 바로 기다린다.
            await CloseSpaceAsync();
            EndEditLifetime();
            // 열기가 차단 전에 실패했으면 핸들이 없다. 필드를 먼저 비워 다음 편집이 끝난 핸들을 다시 보지 않게 한다.
            IDisposable? block = _editInputBlock;
            _editInputBlock = null;
            block?.Dispose();
            // 앱 정상 종료로 버리는 편집은 일시정지 상태로 두고, 그 밖에는 편집 직전 상태로 되돌린다.
            // 종료 중에는 실행 상태가 아니므로 상태 적용이 시간을 멈춘 채 둔다.
            _runtime.ApplySessionState(reason == ExitReason.StopDiscard ? GameSessionState.Paused : _beforeCustomization);
            // 원본 외형 편집 화면(ManagedUIElement)이 닫힐 때처럼 기지 씬의 자동 저장을 요청한다.
            // 기지 밖이거나, 앱 정상 종료처럼 실행 중이 아니면 NotifyUiActivity가 스스로 무시한다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 편집 씬을 열어 컨텍스트를 준비하고 활성 씬으로 만든다. 실패하면 연 공간을 스스로 되돌리고 원래 실패를 그대로 던진다.
        // 끝난 편집의 늦은 완료와 취소 콜백은 편집 세대와 컨텍스트 정리가 거른다. 테스트 하위 클래스만 대역으로 바꾼다.
        // 기다리는 사이 이 컴포넌트가 파괴됐으면(앱 종료) 더 진행하지 않는다. 연 씬은 Unity가 회수한다.
        internal virtual async UniTask OpenSpaceAsync(DuckAvatarPreset appearance, Action complete, Action cancel)
        {
            // 원작 CustomFaceData처럼 파츠 라이브러리는 앱 수명 게임플레이 데이터에서 읽는다.
            // 씬 로드를 기다리는 동안 앱이 사라져도 정적 구역을 다시 읽지 않도록 대기 전에 잡아 둔다.
            DuckAvatarPartLibrary library = GameplayDataSettings.CustomFaceData;
            try
            {
                // 로드한 씬을 바로 보관해야 뒤에서 실패해도 ReleaseSpaceAsync가 이 씬을 내린다.
                Scene scene = await _loader.LoadAsync(_scenes[GameScene.Customization]);
                if (this == null) return;
                _scene = scene;
                // ISceneLoader는 메인 스레드에서 완료되므로 여기서 Unity API를 바로 써도 된다.
                _context = SceneContexts.Single<CustomizationSceneContext>(scene);
                _context.Prepare(appearance, library, _hud, complete, cancel);
                _activeBeforeOpen = SceneManager.GetActiveScene();
                if (!SceneManager.SetActiveScene(scene)) throw new InvalidOperationException("편집 씬을 활성화하지 못했습니다.");
                _context.Activate();
                // 화면을 켠 뒤에 알리고, 닫기가 짝을 맞춰 Closed를 보내도록 알리기 전에 표시해 둔다.
                _announced = true;
                ObserverDispatch.InvokeEach(Opened);
            }
            // 기다린 Core 작업은 모두 메인 스레드에서 끝나므로 실패도 메인 스레드에서 들어온다. 되돌리기는 던지지 않는다.
            // 예외 경계: 열기 실패는 이 흐름이 연 공간을 스스로 되돌린 뒤 원래 실패를 그대로 전달한다.
            catch (Exception)
            {
                await ReleaseSpaceAsync();
                throw;
            }
        }

        // 검증을 통과해 실제로 적용된 미리보기 외형을 확인 후보로 읽는다.
        // 열린 편집의 확인에서만 부르므로 컨텍스트가 늘 있다. 테스트 하위 클래스만 대역으로 바꾼다.
        internal virtual DuckAvatarPreset CaptureAppliedAppearance()
        {
            return _context!.CaptureAppliedAppearance();
        }
        // 확인이나 취소를 처리하는 동안 입력만 막고 초안은 그대로 둔다. 파괴된 컨텍스트는 Unity null 비교로 건너뛴다.
        internal virtual void SetSpaceBusy(bool busy) { if (_context != null) _context.SetBusy(busy); }
        // 확인이나 취소 실패 안내를 번역 키로 띄우고 같은 초안에서 다시 시도하게 한다. 파괴된 컨텍스트는 건너뛴다.
        internal virtual void ShowSpaceError(string? key) { if (_context != null) _context.ShowError(key); }

        // 여러 번 불러도 안전하다. 열린 공간이 있을 때만 한 번 해제하고, 실패는 기록만 하며 던지지 않는다.
        // 편집 종료와 앱 정리가 함께 부르고, 테스트 하위 클래스만 대역으로 바꾼다.
        internal virtual UniTask CloseSpaceAsync() => ReleaseSpaceAsync();

        // 편집 공간을 해제하는 유일한 곳이다. 컨텍스트 정리, 씬 언로드, 활성 씬 복원을 차례로 한 번씩 시도한다.
        // 미리보기는 씬 언로드가 파괴한다. 편집 공간은 씬 서랍에 에셋을 로드하지 않아 반납할 서랍이 없다.
        // 한 단계가 실패해도 기록하고 다음 단계로 가며 던지지 않는다.
        async UniTask ReleaseSpaceAsync()
        {
            CustomizationSceneContext? context = _context;
            _context = null;
            if (context != null)
                await Cleanup.RunAsync(context.ClearAsync, context);

            // 편집 화면을 정리한 뒤, 편집 씬을 내리기 전에 게임 씬이 표시를 되살리게 알린다. Opened를 알린 편집만 알린다.
            if (_announced)
            {
                _announced = false;
                ObserverDispatch.InvokeEach(Closed);
            }

            Scene? scene = _scene;
            _scene = null;
            if (scene.HasValue)
                await _loader.UnloadAsync(scene.Value);
            Cleanup.Run(RestoreActiveScene);
        }

        // 열기가 기억해 둔 활성 씬이 아직 로드되어 있으면 그 씬으로 되돌린다. 기억을 비우므로 다시 불러도 아무것도 하지 않는다.
        // 공간 해제의 마지막 단계라 되돌리지 못해도 결과를 보지 않는다.
        void RestoreActiveScene()
        {
            Scene previous = _activeBeforeOpen;
            _activeBeforeOpen = default;
            SceneContexts.TryMakeActive(previous);
        }
    }
}
