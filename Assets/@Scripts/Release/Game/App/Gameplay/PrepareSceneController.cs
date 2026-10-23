#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace Game.App
{
    // 난이도 선택 씬의 입력을 받아 고른 난이도를 Runtime의 진행 후보로 제출한다.
    // 이 씬은 캐릭터를 숨기지만 Runtime과 입력은 필요하다. 원본 LevelManager의 레벨 초기화 알림은 공통 수명이 받는다.
    public sealed class PrepareSceneController : SceneLevelController
    {
        [SerializeField] PrepareUguiView _view = null!;
        GameRuntime? _runtime;
        // 첫 선택은 원작처럼 표준 난이도다. 재설정이면 레벨 초기화가 현재 규칙의 위치로 바꾼다.
        int _selection = _defaultSelection;
        bool _submitting;
        // 기지 뒤 난이도 재설정으로 열렸는지다. 원작 GamePrepareProcess는 같은 화면을 쓰고, 기지를 방문했으면 확정 뒤 기지로 돌아간다.
        // 레벨 초기화 때 확정 세션으로 한 번 정한다.
        bool _reselect;
        // 원작 DifficultySelection_Entry.locked다. 위치는 _order와 같고, 레벨 초기화 때 원작 CheckUnlocked 표로 한 번 채운다.
        readonly bool[] _locked = new bool[_order.Length];
        // 원본 PREPARE 779의 표시 순서다. 화면 위치는 저장 ID와 별개이므로 이 배열로 규칙 ID를 찾는다.
        static readonly DifficultyRuleIndex[] _order = { DifficultyRuleIndex.ExtraEasy,
            DifficultyRuleIndex.Easy, DifficultyRuleIndex.Standard, DifficultyRuleIndex.StandardChallenge,
            DifficultyRuleIndex.ExtraHard, DifficultyRuleIndex.Rage, DifficultyRuleIndex.Custom };
        // 원작 첫 선택인 표준 난이도의 화면 위치다. 표시 순서 배열에서 찾아 순서가 바뀌어도 어긋나지 않게 한다.
        static readonly int _defaultSelection = Array.IndexOf(_order, DifficultyRuleIndex.Standard);

        // SceneContext가 캐릭터와 월드를 준비한 뒤 공통 수명이 한 번만 부른다. View 연결과 기본 선택을 준비하고, 켜져 있으면 선택 설명과
        // 입력 상태까지 표시한다. 캐릭터 숨김은 SceneContext가 맡는다.
        protected override void OnInitializeLevel()
        {
            // SceneContext는 Gameplay를 채운 뒤에만 레벨 초기화를 알리고 LevelInited를 세운다.
            GameRuntime runtime = SceneContext.Instance!.Gameplay!.Runtime;
            _runtime = runtime;
            _view.Bind();
            // 준비 씬은 세션 안에서만 열린다. 재설정이면 원작처럼 현재 규칙을 처음 선택으로 두고, 사용자 지정이면 저장된 값을 패널에 넣는다.
            // 레벨 초기화는 앱이 살아 있는 프레임에만 오므로 난이도와 공통 파일 소유자를 GameManager에서 바로 읽는다. 재설정 판정은 캠페인 규칙이 한다.
            _reselect = runtime.HasSession && CampaignRules.IsDifficultyReselectSession(runtime.Snapshot());
            DifficultyRules difficulty = GameManager.Difficulty;
            DifficultyState? current = difficulty.Current;
            _view.SetCustom(_reselect && current != null && current.RuleIndex == DifficultyRuleIndex.Custom
                ? current : DifficultyState.CreatePreset(DifficultyRuleIndex.Custom));
            for (int i = 0; i < _order.Length; i++)
            {
                _locked[i] = !GameplayCommands.CanSelectDifficulty(difficulty, GameManager.GlobalProgress, _order[i], !_reselect);
                if (_reselect && _order[i] == difficulty.CurrentRuleIndex) _selection = i;
            }
            _view.SetLocked(_locked);
            _view.Selected += Select;
            _view.Confirmed += Confirm;
            runtime.StateChanged += Refresh;
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
            // 루트가 로드 때부터 켜져 있으면 OnEnable은 초기화 전에 지나갔으므로 여기서 표시를 맞춘다.
            // 원작 NotifySelected처럼 재설정의 처음 선택이 사용자 지정이면 패널을 연다.
            if (isActiveAndEnabled) { ShowSelection(_reselect && _order[_selection] == DifficultyRuleIndex.Custom); Refresh(); }
        }

        // 초기화를 마친 뒤 켜질 때만 선택 설명과 입력 상태를 표시한다. 드롭다운의 끔과 켬 라벨은 View의 번역 구독이 갱신한다.
        void OnEnable() { if (_runtime != null) { ShowSelection(false); Refresh(); } }
        // 씬 언로드 중에는 파괴 순서가 정해져 있지 않아, 먼저 파괴된 View는 Unity null 검사로 건너뛴다.
        void OnDisable() { if (_runtime != null && _view != null) _view.SetInteractive(false); }
        bool CanInput => isActiveAndEnabled && _runtime != null && !_submitting && _runtime.IsGameplayActive;
        // Runtime 상태가 바뀔 때와 제출 전후에 부른다.
        void Refresh() => _view.SetInteractive(CanInput);
        // 로캘이 바뀌면 선택 설명을 다시 번역한다.
        void OnLocaleChanged(Locale locale) => ShowSelection(false);

        // 잠긴 Rage는 고를 수 없고, Custom을 고르면 사용자 지정 패널을 연다. 화면 위치가 아니라 규칙 ID로 판정한다.
        // 원작 DifficultySelection_Entry.OnPointerClick처럼 잠긴 선택지는 무시한다. 잠금은 첫 선택과 재설정 모두 원작 CheckUnlocked 표다.
        void Select(int index)
        {
            if (!CanInput || !Selectable(index)) return;
            _selection = index;
            ShowSelection(_order[index] == DifficultyRuleIndex.Custom);
        }

        // View가 넘긴 위치가 표시 범위 안이고 잠기지 않았는지 본다.
        bool Selectable(int index) => index >= 0 && index < _order.Length && !_locked[index];

        // 선택 설명은 이 컨트롤러가 원본 난이도 설명 키로 번역하고 View에는 표시할 글만 넘긴다.
        void ShowSelection(bool openCustom) => _view.ShowSelection(_selection,
            GameLocalization.Text(GameLocalization.DifficultyDescriptionKey(_order[_selection])), openCustom);

        void Confirm() { if (CanInput) SubmitAsync().Forget(); }
        // 제출 중에는 같은 버튼 입력을 막는다. 명령 실패의 기록 여부는 SceneCommand가 정하고, 실패해도 다시 시도하지 않는다.
        async UniTask SubmitAsync()
        {
            // Forget으로 시작하는 루트라서 입력을 되살리는 finally까지 모두 바깥 try 안에 둔다.
            try
            {
                _submitting = true;
                try
                {
                    Refresh();
                    // Custom은 화면 값을, 그 밖의 규칙은 원본 프리셋을 후보로 쓴다. 화면 값은 슬라이더 범위가 막아 주므로
                    // ReadCustom은 검사만 하고 거부하지 않는다.
                    DifficultyState candidate = _order[_selection] == DifficultyRuleIndex.Custom
                        ? _view.ReadCustom()
                        : DifficultyState.CreatePreset(_order[_selection]);
                    // 명령 결과는 쓰지 않는다. 난이도 선택은 씬 흐름(GameManager.Scenes)이 맡고, 거절과 실패의 기록은 SceneCommand와 Runtime이 맡는다.
                    // 제출은 CanInput이 Runtime 활성을 확인한 입력에서만 시작하므로 앱이 살아 있다.
                    // 재설정 화면은 진행 단계를 바꾸지 않는 기지 복귀 명령으로, 첫 선택은 인트로 진행 명령으로 보낸다.
                    await SceneCommand.TryRunAsync(() => _reselect
                        ? GameManager.Scenes.ReselectDifficultyAsync(candidate)
                        : GameManager.Scenes.SelectDifficultyAsync(candidate), this);
                }
                finally { _submitting = false; if (this != null) Refresh(); }
            }
            // 예외 경계: Forget으로 시작한 제출의 루트라, SceneCommand 밖에서 생긴 뜻밖의 실패도 잃지 않도록 기록하고 끝낸다.
            catch (Exception error) { Log.Instance.Exception(error, this); }
        }

        // 원작 onStartedLoadingScene 구독자처럼 씬을 내리기 직전에 레벨 초기화가 건 Runtime 상태, 정적 로캘, View 구독을 끊는다.
        // 공통 수명이 한 번만 부르며 던지지 않고, 단계마다 Cleanup.Run으로 계속한다. 먼저 파괴된 Runtime도 C# 이벤트가 이 핸들러를 들고 있어
        // 참조만 보고 풀고, 먼저 파괴된 View는 알림을 낼 수 없어 건너뛴다. 정적 로캘 구독은 그와 관계없이 푼다.
        // 구독 전이면 해제는 아무 일도 하지 않는다. 정리 뒤에는 _runtime이 비어 입력을 받지 않는다.
        protected override void OnReleaseLevel()
        {
            GameRuntime? runtime = _runtime;
            _runtime = null;
            // 늦은 상태 알림이 파괴된 이 객체에 오지 않도록 Unity null 비교 대신 참조로 본다.
            if (!ReferenceEquals(runtime, null)) Cleanup.Run(() => runtime.StateChanged -= Refresh, this);
            Cleanup.Run(() => LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged, this);
            PrepareUguiView view = _view;
            if (view != null)
            {
                Cleanup.Run(() => view.Selected -= Select, this);
                Cleanup.Run(() => view.Confirmed -= Confirm, this);
            }
        }
    }
}
