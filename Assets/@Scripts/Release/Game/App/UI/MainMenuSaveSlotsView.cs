#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace Game.App
{
    // 원본의 여섯 저장 칸 화면이다. 런타임이 준 저장 요약을 표시한다.
    [DisallowMultipleComponent]
    public sealed class MainMenuSaveSlotsView : MonoBehaviour
    {
        // 아래 직렬화 참조는 Editor 제작기가 원본 Canvas의 패널과 버튼에 연결한다.
        [SerializeField] GameObject _panel = null!;
        // 메뉴에 보이는 원본 슬롯 열기 버튼이다. 그 라벨(_openLabel)에 현재 슬롯 번호를 보여 준다.
        [SerializeField] Button _open = null!;
        // 닫기는 슬롯 창만 닫고 게임 시작과는 상관없는 화면 명령이다.
        [SerializeField] Button _close = null!;
        // 원본 복구 버튼이다. 선택한 슬롯을 백업으로 복구할 수 있을 때만 보인다.
        [SerializeField] Button _restore = null!;
        [SerializeField] TMP_Text _openLabel = null!;
        // 원본의 여섯 칸을 슬롯 번호 순서대로 제작 때 연결한다. 인덱스 0이 1번 슬롯이다.
        [SerializeField] SlotEntry[] _entries = Array.Empty<SlotEntry>();
        // 추가한 클릭 대리자를 그대로 해제할 수 있도록 보관한다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 열린 창이 언어 변경 때 다시 그리도록 마지막 Render의 요약을 보관한다. 창이 열린 동안에는 메뉴 갱신이 매번 Render해 최신이다.
        IReadOnlyList<GameSaveSlotSummary> _summaries = Array.Empty<GameSaveSlotSummary>();
        // 컨트롤러가 넘긴 선택 슬롯 번호를 표시용으로 보관한다. 1부터 센다.
        int _selected = 1;
        // 마지막 Render의 잠금 상태다. 언어가 바뀌어 다시 그릴 때도 같은 잠금을 쓴다.
        bool _busy;
        // 슬롯 선택은 이벤트로만 알리고, 명령 실행은 컨트롤러가 맡는다.
        public event Action<int>? Selected;
        // 창을 열고 닫는 일은 공통 모달 스택(GameModalStack)이 맡는다.
        public GameObject Panel => _panel;
        // 메뉴 입력이 막힌 동안(menuReady가 false) 컨트롤러가 이 버튼도 잠근다. 창을 닫으면 포커스가 이 버튼으로 돌아온다.
        public Button OpenButton => _open;
        public Button CloseButton => _close;
        // 컨트롤러가 복구 버튼을 선택 슬롯의 백업 복구 명령(RecoverBackup)에 연결한다.
        public Button RestoreButton => _restore;
        // 창을 열 때 현재 선택 슬롯에 포커스를 둔다.
        public Selectable InitialFocus => _entries[_selected - 1].Button;

        // 같은 클릭을 두 번 연결하지 않도록 먼저 해제한다.
        public void Bind()
        {
            Unbind();
            // 여섯 칸과 하위 참조는 제작기가 연결하고 ProductUiLocalizationTests가 확인한다.
            for (int index = 0; index < _entries.Length; index++)
            {
                int slot = index + 1;
                // busy일 때 Render가 슬롯 버튼을 잠그므로 콜백은 _busy를 다시 검사하지 않는다.
                _subscriptions.Bind(_entries[index].Button.onClick, () => Selected?.Invoke(slot));
            }
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        }
        // 클릭과 언어 변경 구독을 푼다. 보관한 요약은 다음 Render가 덮으므로 비우지 않는다.
        public void Unbind()
        {
            // 이 뷰가 추가한 리스너만 해제하고 다른 리스너는 남긴다.
            _subscriptions.Clear();
            LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        }
        // 언어 변경 이벤트는 전역이므로 파괴될 때도 구독을 푼다.
        void OnDestroy() => Unbind();
        // 열린 창만 마지막 Render의 요약과 잠금으로 다시 그린다. 닫힌 창의 사본은 낡을 수 있어 메뉴에 보이는 열기 버튼
        // 라벨만 새 언어로 바꾸고, 숨은 행은 창을 열 때 컨트롤러가 최신 요약으로 다시 그린다.
        void OnLocaleChanged(Locale locale)
        {
            if (_panel.activeSelf) Render(_summaries, _selected, _busy);
            else RenderLabel(_selected);
        }
        // 정상 저장의 실제 정보만 표시하고, 알 수 없는 시간은 추정하지 않는다.
        // 컨트롤러가 Bind한 뒤에만 부르므로 준비 여부를 따로 확인하지 않는다.
        public void Render(IReadOnlyList<GameSaveSlotSummary> summaries, int selected, bool busy)
        {
            _summaries = summaries;
            _busy = busy;
            // 선택 번호와 열기 버튼 라벨은 RenderLabel 한 곳에서만 갱신한다.
            RenderLabel(selected);
            // 여섯 칸이 같은 번역 키를 쓰므로 반복 앞에서 한 번만 찾는다.
            string slotTitle = GameLocalization.Text("MainMenu_SaveSelection_Slot");
            for (int index = 0; index < _entries.Length; index++)
            {
                SlotEntry entry = _entries[index];
                GameSaveSlotSummary? summary = index < summaries.Count ? summaries[index] : null;
                entry.Button.interactable = !busy && summary != null;
                entry.Active.SetActive(index + 1 == selected);
                entry.Title.text = slotTitle + " " + (index + 1);
                bool saved = summary?.State == GameSaveState.Available;
                entry.Difficulty.text = saved && summary!.Difficulty != null
                    // 저장한 난이도 규칙을 원본과 같은 번역 키로 표시한다. 키에는 저장 번호가 아니라 규칙 이름이 들어간다.
                    ? GameLocalization.Text(GameLocalization.DifficultyNameKey(summary.Difficulty.RuleIndex))
                    : GameLocalization.Text(summary?.State switch
                    {
                        GameSaveState.Missing => "MainMenu_NewGame",
                        // 주 파일 없이 백업만 남은 슬롯은 복구 대신 새 게임으로 표시한다.
                        GameSaveState.Recoverable => summary.PrimaryMissing ? "MainMenu_NewGame" : "menu.recover",
                        // 손상과 오류는 메뉴 상태 줄과 같은 StatusKey를 쓴다.
                        // 난이도 없는 정상 저장과 아직 조회하지 않은 칸은 확인 중으로 표시한다.
                        GameSaveState.Corrupt or GameSaveState.Error => StatusKey(summary.State),
                        _ => StatusKey(GameSaveState.Unknown)
                    });
                // TimeSpan의 hh 형식은 24시간 이상에서 일을 버리므로 총 시간을 직접 출력한다.
                entry.PlayTime.text = saved ? summary!.ElapsedSeconds.HasValue
                    ? PlayTime(TimeSpan.FromSeconds(summary.ElapsedSeconds.Value))
                    : "—" : "";
                // 빈 슬롯은 원본 자리표시로 행 높이를 유지한다. 저장 시각을 모르면 추정하지 않고 ???로 둔다.
                entry.SaveTime.text = saved
                    ? summary!.SavedAtUtc.HasValue
                        ? summary.SavedAtUtc.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)
                        : "???"
                    : "----/--/-- --:--";
            }
            bool recoverable = selected <= summaries.Count && summaries[selected - 1].State == GameSaveState.Recoverable;
            _restore.gameObject.SetActive(recoverable);
            _restore.interactable = recoverable && !busy;
            _close.interactable = !busy;
        }

        // 창이 닫혀 있을 때 메뉴에 보이는 열기 버튼 라벨과 InitialFocus가 쓰는 선택 번호만 갱신한다.
        // 숨은 행은 창을 열 때 컨트롤러가 Render로 다시 그린다.
        public void RenderLabel(int selected)
        {
            _selected = selected;
            _openLabel.text = GameLocalization.Text("MainMenu_SaveSlot") + " : " + selected;
        }

        // 누적 플레이 시간을 "총시간:분:초"로 표시한다(25시간 → 25:00:00).
        static string PlayTime(TimeSpan span) => string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}",
            (int)span.TotalHours, span.Minutes, span.Seconds);

        // 저장 상태를 번역 키로 바꾸는 유일한 매핑이다. 메뉴 상태 줄(GameUiController)과 슬롯 행이 함께 쓴다.
        internal static string StatusKey(GameSaveState state) => state switch
        {
            GameSaveState.Missing => "save.missing",
            GameSaveState.Available => "save.available",
            GameSaveState.Error => "save.error",
            GameSaveState.Corrupt => "save.corrupt",
            GameSaveState.Recoverable => "save.recoverable",
            _ => "save.checking"
        };

        // 원본 칸 하나의 참조다. 텍스트와 강조 객체를 직렬화해 두어 이름으로 찾지 않는다.
        [Serializable]
        sealed class SlotEntry
        {
            // _title은 슬롯 번호 라벨이고, _active는 현재 슬롯을 강조하는 객체다.
            [SerializeField] Button _button = null!;
            [SerializeField] TMP_Text _title = null!;
            [SerializeField] TMP_Text _difficulty = null!;
            [SerializeField] TMP_Text _playTime = null!;
            [SerializeField] TMP_Text _saveTime = null!;
            [SerializeField] GameObject _active = null!;
            public Button Button => _button;
            public TMP_Text Title => _title;
            public TMP_Text Difficulty => _difficulty;
            public TMP_Text PlayTime => _playTime;
            public TMP_Text SaveTime => _saveTime;
            public GameObject Active => _active;
        }
    }
}
