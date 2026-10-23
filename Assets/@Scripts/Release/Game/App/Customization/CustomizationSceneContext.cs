#nullable enable annotations

using System;
using Cysharp.Threading.Tasks;
using DuckCharacter;
using DuckCharacter.AvatarParts;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 외형 편집 씬의 진입 준비와 퇴장 정리를 맡는다. 게임 세션을 참조하지 않고,
    // 편집 중인 임시 외형과 장비 없는 미리보기만 소유한다.
    public sealed class CustomizationSceneContext : MonoBehaviour
    {
        // 편집 화면의 씬 콘텐츠다. Activate가 켤 때까지 비활성으로 둔다.
        [SerializeField] GameObject _contentRoot = null!;
        // 게임 바인딩과 장비가 없는 미리보기 전용 프리팹이다.
        [SerializeField] GameObject _previewPrefab = null!;
        // 편집 화면의 uGUI View다. 편집 씬과 함께 생기고 사라진다.
        [SerializeField] CustomizationUguiView _view = null!;
        // 앱 전체가 함께 쓰는 HUD다. 편집 씬에는 HUD가 없으므로 Prepare로 받아 두고 정리 때 놓는다.
        // 복사와 붙여넣기 결과를 원본 NotificationText처럼 이 HUD로 알린다.
        SharedHudView? _hud;
        // 미리보기를 붙이는 위치다. 화면에서 끌면 이 축이 돌아 미리보기가 회전한다.
        [SerializeField] Transform _previewMount = null!;
        // 저작 도구가 이 이름으로 값을 쓰고 테스트가 읽지만 C# 코드는 읽지 않는다.
        // 그래서 생기는 CS0414 경고만 이 필드에서 끈다.
#pragma warning disable CS0414
        [SerializeField] Camera _previewCamera = null!;
#pragma warning restore CS0414
        // 편집 초안과 미리보기 캐릭터다. 실제로 적용된 외형은 캐릭터가 갖고 있어 완료 때 캐릭터에서 읽는다.
        DuckCustomizationSession? _session;
        DuckCharacterInstance? _character;
        // 미리보기 리그의 애니메이션 어댑터다. 화면에는 리그 전체 대신 이것만 넘긴다.
        DuckCharacterAnimation? _animation;
        GameObject? _preview;
        CustomizationScreen? _screen;
        Action? _complete;
        Action? _cancel;
        bool _busy;
        bool _previewFailed;
        // 정리를 시작하면 켠다. 그 뒤로는 세션 편집 잠금이 편집을 막고, 다시 부른 정리는 아무것도 하지 않는다.
        // 컨텍스트는 다시 준비하지 않고 편집 씬과 함께 내려간다.
        bool _closing;

        // 편집 초안이다. 게임에 실제로 반영하는 일은 런타임의 완료 경로가 맡는다. 미리보기 적용과 테스트가 읽는다.
        internal DuckCustomizationSession Session
        {
            get
            {
                return _session!;
            }
        }

        // 편집 세션과 미리보기를 만들고 첫 외형을 적용한다. 카메라와 화면은 Activate 전까지 켜지 않는다.
        // 공용 HUD는 보관만 하고 Activate에서 쓴다. 화면은 라이브러리를 세션에서 읽고 원본 구성의 고정 에셋만 쓴다.
        // 씬 연결과 장비 없는 미리보기 프리팹은 EditMode 저작 테스트(CustomizationPresentationTests)가 확인한다.
        public void Prepare(DuckAvatarPreset initial, DuckAvatarPartLibrary library, SharedHudView hud, Action complete, Action cancel)
        {
            _hud = hud;
            _contentRoot.SetActive(false);
            _complete = complete;
            _cancel = cancel;
            // Session으로 초안을 직접 고쳐도 같은 잠금이 걸리도록, 처리 중이거나 정리 중이면 세션이 편집을 거부한다.
            // 원작 JSON 코덱과 그 직렬화 인스턴스는 이 세션만 쓴다.
            _session = new DuckCustomizationSession(library, initial,
                new DuckovAppearanceJson(new DevProject.Serialization.JsonSerializer()), () => !_busy && !_closing);
            _preview = Instantiate(_previewPrefab, _previewMount, false);
            _preview.name = "CustomizationPreview";
            DuckCharacterRig rig = _preview.GetComponentInChildren<DuckCharacterRig>(true);
            // Animator를 미리보기용으로 설정하고 되돌리는 일은 애니메이션 어댑터가 맡는다.
            _animation = rig.Animation;
            _animation.ConfigurePreview();
            // 첫 적용이 실패하면 던져서 CustomizationFlow.OpenSpaceAsync의 열기 실패 정리로 넘긴다.
            // 실패 원인은 외형 적용 쪽이 이미 보고하므로 예외에는 고정 문구만 쓴다.
            _character = new DuckCharacterInstance(rig);
            if (!TryApplyPreview(_session.Snapshot()))
                throw new InvalidOperationException("미리보기 외형을 적용하지 못했습니다.");
            _session.Changed += ApplyPreview;
        }

        // 준비에 성공한 뒤 한 번만 화면을 켠다. 화면 생성에는 실패 검사가 없어 스스로 되돌리지 않는다.
        // 열기가 실패하면 HUD 숨김 해제와 콘텐츠 끄기는 CustomizationFlow.OpenSpaceAsync가 부르는 ClearAsync가 맡는다.
        public void Activate()
        {
            _contentRoot.SetActive(true);
            // 원본 HUDManager처럼 커스터마이징(CustomFaceUI) 중에는 공용 HUD의 HUDCanvas를 숨긴다.
            // HUD는 Prepare가 받아 둔 앱 수명 객체라 여기서 null을 다시 보지 않는다.
            _hud!.RegisterHideToken(this);
            // 완료 버튼은 마지막 미리보기 적용이 성공했을 때만 쓸 수 있다. 알림은 원본 NotificationText처럼 공용 HUD로 띄운다.
            var screen = new CustomizationScreen(_view, _session!, _previewMount, _animation!,
                () => _complete?.Invoke(), () => _cancel?.Invoke(), () => !_previewFailed,
                _hud.Notify);
            screen.SetBusy(_busy);
            screen.Focus();
            _screen = screen;
        }

        // 완료 때 캐릭터에 실제로 적용된 외형의 사본을 한 번 읽는다. 실패한 미리보기는 화면이 완료 버튼을 막고
        // 준비는 첫 적용이 성공해야 끝나므로, 정리 전의 미리보기 캐릭터에는 늘 적용된 외형이 있다.
        public DuckAvatarPreset CaptureAppliedAppearance()
        {
            DuckAvatarPreset? applied = _character!.SnapshotAppearance();
            return applied!;
        }

        // 확인이나 취소를 처리하는 동안 편집 입력과 완료, 취소의 중복 요청을 함께 막는다.
        // 편집 흐름은 Activate 뒤부터 정리가 화면을 놓기 전까지만 부르므로 화면이 늘 있다.
        public void SetBusy(bool busy)
        {
            _busy = busy;
            _screen.SetBusy(busy);
        }

        // 확인이나 취소 실패 안내를 초안은 그대로 둔 채 화면에 넘기고, 화면은 HUD 알림으로 띄운다. key는 번역 키다.
        // 미리보기 실패 안내는 TryApplyPreview가 따로 띄우므로 상태로 남기지 않는다.
        // SetBusy처럼 편집 흐름이 화면을 켠 뒤에만 부르므로 화면이 늘 있다.
        public void ShowError(string? key)
        {
            _screen.ShowError(key);
        }

        // 슬라이더를 끌면 거의 매 프레임 적용하므로, 같은 정리 경고는 경고 없는 적용이 나올 때까지 한 번만 기록한다.
        readonly Log.Once _reportedPreviewProblems = new Log.Once();

        // 초안이 바뀔 때마다 미리보기에 적용한다. 정리는 _closing을 켠 같은 동기 구간에서 이 구독을 끊고
        // 세션 편집도 막으므로, 여기서 정리 중인지 따로 보지 않는다.
        void ApplyPreview(DuckAvatarPreset appearance) => TryApplyPreview(appearance);

        // 적용 결과로 완료 가능 여부를 갱신한다. 실패해도 계속 편집할 수 있고, 정리 경고만 있으면 미리보기와 완료 버튼을 유지한다.
        // 실패 원인은 외형 적용 쪽이 보고한다.
        bool TryApplyPreview(DuckAvatarPreset appearance)
        {
            DuckAvatarApplyResult result = _character!.ApplyAppearance(Session.Library, appearance);
            // 실패 안내는 미리보기가 실패로 바뀔 때만 띄운다. 확인이나 취소 실패 안내와 섞이지 않게 직전 미리보기 결과와만 비교한다.
            bool wasFailed = _previewFailed;
            _previewFailed = !result.IsApplied;
            // 첫 경고 메시지를 키로 같은 정리 경고는 한 번만 보고한다. 경고 없이 적용에 성공해야 기록을 비운다.
            if (result.CleanupErrors.Count == 0)
            {
                if (result.IsApplied) _reportedPreviewProblems.Reset();
            }
            else if (_reportedPreviewProblems.First("cleanup:" + result.CleanupErrors[0].Message))
                result.ReportCleanupWarnings(this, "미리보기 적용 완료 후 정리 경고");
            if (_previewFailed && !wasFailed) _screen?.ShowError("customization.previewFailed");
            _screen?.RefreshCompletion();
            return !_previewFailed;
        }

        // 첫 호출만 정리를 실행한다. 정리는 동기로 끝나고 단계별 실패를 기록만 하므로 모든 호출이 이미 끝난 성공 작업을 받는다.
        // 미리보기 객체는 뒤이은 편집 씬 언로드가 파괴한다.
        public UniTask ClearAsync()
        {
            if (_closing) return UniTask.CompletedTask;
            _closing = true;
            ClearCore();
            return UniTask.CompletedTask;
        }

        // 구독, 화면, HUD 숨김, 콘텐츠, 캐릭터 순으로 각 단계를 Cleanup으로 한 번씩 끝까지 시도한다.
        // 실패는 단계마다 기록만 하고 던지지 않는다. 캐릭터 해제가 미리보기 애니메이션 설정도 되돌린다.
        void ClearCore()
        {
            _busy = true;
            _complete = null;
            _cancel = null;
            Cleanup.Run(() => { if (_session != null) _session.Changed -= ApplyPreview; }, this);
            CustomizationScreen? screen = _screen;
            _screen = null;
            if (screen != null) Cleanup.Run(screen.Dispose, this);
            Cleanup.Run(() => { if (_hud != null) _hud.UnregisterHideToken(this); }, this);
            // HUD는 앱이 소유하므로 파괴하지 않고 참조만 놓는다.
            _hud = null;
            _animation = null;
            Cleanup.Run(() => { if (_contentRoot != null) _contentRoot.SetActive(false); }, this);
            DuckCharacterInstance? character = _character;
            _character = null;
            if (character != null) Cleanup.Run(character.Dispose, this);
            // 이 컨텍스트는 다시 준비하지 않으므로 정리 경고 기록은 비우지 않는다.
            _session = null;
            // 미리보기는 편집 씬 언로드가 파괴한다. 파츠 라이브러리는 앱 수명 게임플레이 데이터라 미리보기보다 오래 남는다.
            _preview = null;
        }

        // 활성 화면이 있을 때만 미리보기 애니메이션 틱을 전달한다.
        void Update() => _screen?.Tick();
    }
}
