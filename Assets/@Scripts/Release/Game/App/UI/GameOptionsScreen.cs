#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 옵션 창이다. 후보 편집은 이 화면이, 순서 있는 저장과 실패 복원은 GameManager의 옵션 관리자가 맡는다.
    internal sealed class GameOptionsScreen : IDisposable
    {
        readonly GameOptionsUguiView _view;
        readonly GameRuntime _runtime;
        // 만든 쪽이 넘긴 옵션 관리자다. 저장을 기다린 뒤의 결과 처리도 GameManager 접근자를 읽지 않고 이 참조를 쓴다.
        readonly GameOptionsManager _options;
        readonly Action _close;
        readonly LocalizedTextBindings _text = new LocalizedTextBindings();
        readonly List<Vector2Int> _resolutions = new List<Vector2Int>();
        // 이 화면이 연결한 이벤트만 모아 두었다가 한 번에 해제한다. 원본 에셋의 다른 리스너는 건드리지 않는다.
        readonly UiEventSubscriptions _unbind = new UiEventSubscriptions();
        GameOptions _candidate = new GameOptions();
        bool _disposed, _closing;
        // 닫기 요청이 저장 실패를 보여 준 상태다. 옵션 저장 실패가 창 닫기를 막지 않도록 다음 닫기는 기다리지 않고 닫는다.
        // 저장이 성공하거나 창을 다시 열면 지운다.
        bool _closeFailureShown;
        int _saveRevision;
        // 언어 드롭다운에서 한국어와 영어 항목의 위치와, 0~1 음량을 0~100으로 보여 줄 배율이다.
        const int _koreanLanguageChoice = 0, _englishLanguageChoice = 1;
        const float _volumePercentScale = 100f;
        // 모달 소유자가 창의 루트와 처음 포커스할 컨트롤을 읽는다.
        public GameObject Root => _view.gameObject;
        public Selectable InitialFocus => _view.Language;

        // 드롭다운과 슬라이더의 모든 변경은 같은 즉시 적용 경로(Changed)로 보낸다. View 참조는 여기서 검사하지 않는다.
        // 메뉴 연결 중에 던지면 나머지 메뉴 연결이 끊기므로, 참조가 빠짐없는지는 EditMode 테스트가 확인한다.
        public GameOptionsScreen(GameOptionsUguiView view, GameRuntime runtime, GameOptionsManager options, Action close)
        {
            // 옵션 저장소가 있는 구성에서만 이 화면을 만든다. 두 생성 지점(OptionsPanelPresenter의 일시정지와 메뉴)이 보장하므로
            // Open에는 거부 분기를 두지 않는다. 옵션 관리자는 앱 UI가 바인딩 때 잡아 둔 것을 받는다.
            _options = options;
            _view = view;
            _runtime = runtime;
            _close = close;
            // 언어 이름은 번역하지 않는다. 창 모드 선택지는 번역 키로 연결해 번역이 도착하면 글자만 바뀐다.
            // 두 목록의 순서는 _koreanLanguageChoice, _englishLanguageChoice와 GameOptions의 창 모드 상수 값과 같아야 한다.
            LocalizedTextBindings.SetChoices(view.Language, new List<string> { "한국어", "English" });
            _text.BindChoices("window", view.WindowMode, new[] { "options.borderless", "options.windowed" });
            // 화면에 보이는 세 드롭다운의 변경만 저장한다.
            foreach (TMP_Dropdown dropdown in new[] { view.Language, view.Resolution, view.WindowMode })
            {
                _unbind.Bind(dropdown.onValueChanged, _ => Changed());
            }
            foreach (Slider slider in new[] { view.Master, view.Music, view.Sfx, view.Sensitivity })
            {
                slider.interactable = true;
                _unbind.Bind(slider.onValueChanged, _ => { RefreshNumbers(); Changed(); });
            }
            // 감도 슬라이더 범위(0.1~50)는 프리팹에 저작된 값이라 여기서 대입하지 않는다. OptionsInteractionTests가 확인한다.
            _unbind.Bind(view.SensitivityInput.onEndEdit, SensitivityEdited);
            _unbind.Add(_text.Dispose);
            // 옵션 관리자가 Alt+Enter로 바뀐 화면 모드를 따로 저장해도 후보가 낡지 않게 적용 알림을 받는다. 해제는 Dispose가 한다.
            _options.Applied += SyncWindowMode;
            _unbind.Add(() => _options.Applied -= SyncWindowMode);
            for (int index = 0; index < view.Tabs.Length; index++)
            {
                int tab = index;
                _unbind.Bind(view.Tabs[index].onClick, () => SelectTab(tab));
            }
            // [적용] 버튼은 저장에 실패한 후보를 다시 저장하는 재시도 버튼이다.
            _unbind.Bind(view.ApplyButton.onClick, Retry);
            _unbind.Bind(view.DefaultsButton.onClick, Defaults);
            _unbind.Bind(view.CloseButton.onClick, RequestClose);
        }

        // 창을 다시 열면 확정값을 보여 준다. 옵션 관리자에 남은 실패 후보는 그대로 두고 재시도 버튼으로만 알린다.
        public void Open()
        {
            Fill(_options.Current);
            // 메뉴에서는 선택한 슬롯 파일의 난이도를, 게임 중에는 난이도 소유자의 현재 난이도를 보인다. 없으면 배지를 숨긴다.
            // 세션이 없을 때 난이도 소유자의 값은 지금 플레이 중인 난이도가 아니므로 읽지 않는다. 저장 관리자와 난이도 소유자는 GameManager가 내준다.
            DifficultyState? difficulty = _runtime.SessionState == GameSessionState.Menu
                ? GameManager.Saves.SelectedSummary?.Difficulty
                : _runtime.HasSession ? GameManager.Difficulty.Current : null;
            _view.DifficultyBadge.SetActive(difficulty != null);
            // 배지 이름은 저장 칸과 같은 원본 난이도 번역 키로 고른다.
            if (difficulty == null) _text.Clear("difficulty", value => _view.DifficultyLabel.text = value);
            else _text.Set("difficulty", GameLocalization.DifficultyNameKey(difficulty.RuleIndex),
                value => _view.DifficultyLabel.text = value);
            SelectTab(0);
            // 새로 연 창의 첫 닫기는 다시 저장 완료를 기다린다.
            _closeFailureShown = false;
            // 옵션 관리자에 보류 후보가 남아 있으면 실패 상태와 재시도 버튼을 보인다.
            ShowFailure(_options.Pending != null);
        }

        // 값을 이벤트 없이 채우므로 저장을 일으키지 않는다.
        void Fill(GameOptions options)
        {
            // 호출자는 모두 이 화면만 쓰는 새 사본을 넘기므로 복제하지 않고 그대로 후보로 쓴다.
            _candidate = options;
            // 옵션 검증과 같은 지원 로캘 상수를 보고, 선택지 순서와 맞춘 위치 상수로 드롭다운 위치를 고른다.
            _view.Language.SetValueWithoutNotify(
                options.Language == GameLocalization._koreanLocale ? _koreanLanguageChoice : _englishLanguageChoice);
            _view.Master.SetValueWithoutNotify(options.MasterVolume);
            _view.Music.SetValueWithoutNotify(options.MusicVolume);
            _view.Sfx.SetValueWithoutNotify(options.SfxVolume);
            _view.Sensitivity.SetValueWithoutNotify(options.MouseSensitivity);
            _view.WindowMode.SetValueWithoutNotify(options.WindowMode);
            // 원본 ResolutionOptions처럼 장치가 지원하는 해상도 목록을 보인다.
            RebuildResolutions(options.Width, options.Height, options.WindowMode);
            RefreshNumbers();
        }

        // 원본 GetResolutions처럼 목록을 만든다. 테두리 없음 창 모드 상수와 같으면 최대 해상도와 화면비가 같은 것만 넣는다.
        // 원본 OptionsUIEntry_Dropdown처럼 현재 해상도가 목록에 없으면 맨 앞에 넣어 선택한다. 선택지 교체는 저장을 일으키지 않는다.
        void RebuildResolutions(int width, int height, int windowMode)
        {
            _resolutions.Clear();
            _resolutions.AddRange(GameResolutions.Choices(GameResolutions.Device(), GameResolutions.Current,
                windowMode == GameOptions._borderlessWindowMode));
            var current = new Vector2Int(width, height);
            int index = _resolutions.IndexOf(current);
            if (index < 0)
            {
                _resolutions.Insert(0, current);
                index = 0;
            }
            LocalizedTextBindings.SetChoices(_view.Resolution, _resolutions.ConvertAll(value => $"{value.x} x {value.y}"));
            _view.Resolution.SetValueWithoutNotify(index);
        }

        // 적용된 화면 모드가 후보와 다르면 후보와 드롭다운을 맞춘다. 그러지 않으면 다음 변경이 낡은 모드를 보내 Alt+Enter를 되돌린다.
        // 이 화면이 보낸 저장은 같은 값으로 돌아와 아무것도 바꾸지 않는다.
        void SyncWindowMode(GameOptions applied)
        {
            if (applied.WindowMode == _candidate.WindowMode) return;
            _candidate.WindowMode = applied.WindowMode;
            _view.WindowMode.SetValueWithoutNotify(applied.WindowMode);
            RebuildResolutions(_candidate.Width, _candidate.Height, _candidate.WindowMode);
        }

        // 화면에 보이는 항목만 후보에 덮어써서, 화면에 없는 버전 2 값은 그대로 둔다. 후보는 이 화면만 가진 사본이라 제자리에서 갱신한다.
        // 해상도 인덱스가 범위 밖이면 후보를 바꾸기 전에 던지도록 해상도를 먼저 읽는다.
        void ReadCandidate()
        {
            Vector2Int resolution = _resolutions[_view.Resolution.value];
            // 옵션 검증을 통과하도록 지원 로캘 상수를 넣는다.
            _candidate.Language = _view.Language.value == _koreanLanguageChoice ? GameLocalization._koreanLocale : GameLocalization._englishLocale;
            _candidate.MasterVolume = _view.Master.value;
            _candidate.MusicVolume = _view.Music.value;
            _candidate.SfxVolume = _view.Sfx.value;
            _candidate.MouseSensitivity = _view.Sensitivity.value;
            _candidate.Width = resolution.x;
            _candidate.Height = resolution.y;
            _candidate.WindowMode = _view.WindowMode.value;
        }

        // 저장 중이나 닫기 대기 중의 변경도 저장 요청으로 받아 마지막 값을 잃지 않는다. 닫기가 기다리는 FlushAsync는
        // 남은 저장이 없을 때까지 기다리므로 그 변경까지 저장한 뒤 창이 닫힌다. Dispose가 콜백을 모두 해제하고, 패널은 Open이
        // 해상도 목록을 만든 뒤에만 입력을 받으므로 해제 뒤나 열기 전 호출을 막는 분기가 없다.
        void Changed()
        {
            // 후보를 제자리에서 갱신하므로 갱신 전 화면 모드를 먼저 읽어 둔다.
            int previousMode = _candidate.WindowMode;
            ReadCandidate();
            // 원본처럼 화면 모드가 바뀌면 해상도 목록을 다시 만든다. 선택한 해상도는 유지한다.
            if (_candidate.WindowMode != previousMode) RebuildResolutions(_candidate.Width, _candidate.Height, _candidate.WindowMode);
            // 옵션 관리자의 SaveAsync가 첫 await 전에 후보를 복제하므로 여기서는 복제하지 않는다.
            SaveAsync(_candidate, ++_saveRevision).Forget();
        }

        // 수치 형식은 원본처럼 음량 0~100, 감도 소수 한 자리로 표시한다.
        void RefreshNumbers()
        {
            _view.VolumeValues[0].text = (_view.Master.value * _volumePercentScale).ToString("0", CultureInfo.InvariantCulture);
            _view.VolumeValues[1].text = (_view.Music.value * _volumePercentScale).ToString("0", CultureInfo.InvariantCulture);
            _view.VolumeValues[2].text = (_view.Sfx.value * _volumePercentScale).ToString("0", CultureInfo.InvariantCulture);
            // 입력 칸의 반올림 표시는 저장되는 감도 값을 바꾸지 않는다.
            _view.SensitivityInput.SetTextWithoutNotify(_view.Sensitivity.value.ToString("0.0", CultureInfo.InvariantCulture));
        }

        // 직접 입력한 감도도 슬라이더와 같은 범위로 한 번만 제한한다.
        void SensitivityEdited(string text)
        {
            // 범위는 옵션 검증과 같은 GameOptions 상수를 쓰고, 숫자 읽기는 난이도 입력과 같은 UiNumbers 규칙을 쓴다.
            if (UiNumbers.TryParseFinite(text, out float value))
                _view.Sensitivity.value = Mathf.Clamp(value, (float)GameOptions._minMouseSensitivity, (float)GameOptions._maxMouseSensitivity);
            RefreshNumbers();
        }

        // 탭 전환은 저장 후보를 다시 읽지 않고 원본 탭 강조, 페이지, 스크롤 위치만 바꾼다.
        void SelectTab(int index)
        {
            for (int tab = 0; tab < _view.Tabs.Length; tab++)
            {
                _view.Tabs[tab].interactable = true;
                _view.TabIndicators[tab].SetActive(tab == index);
                _view.Pages[tab].SetActive(tab == index);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_view.Scroll.content);
            _view.Scroll.verticalNormalizedPosition = 1f;
        }

        // 기본값은 화면에 보이는 일반과 오디오 항목만 되돌리고, 화면에 없는 버전 2 필드는 유지한다.
        void Defaults()
        {
            GameOptions value = _candidate.Clone();
            // 해상도 기본값은 원본처럼 장치의 가장 큰 해상도라, 옵션 파일이 없을 때의 기본값을 한 번에 넘겨 되돌린다.
            value.ResetVisibleOptions(GameResolutions.CreateDefaultOptions());
            Fill(value);
            Changed();
        }

        // 실패 뒤 화면에는 확정값이 보이지만, 재시도는 옵션 관리자가 보관한 실패 후보를 그대로 다시 저장한다.
        void Retry()
        {
            GameOptions? pending = _options.Pending;
            if (pending == null) return;
            Fill(pending);
            SaveAsync(pending, ++_saveRevision).Forget();
        }

        // 입력마다 옵션 관리자의 공유 저장을 기다리지만 상태 표시는 가장 최근 요청만 갱신한다. 결과는 메인 스레드에서 이어진다.
        // 기다린 뒤에는 앱이 사라졌을 수 있어, 저장과 실패 복원 모두 만들 때 잡은 옵션 관리자를 쓴다.
        async UniTask SaveAsync(GameOptions candidate, int revision)
        {
            Status("options.saving");
            try
            {
                await _options.SaveAsync(candidate);
                // 가장 최근 요청이 성공하면 실패 표시와 닫기 실패 상태를 지운다.
                if (!_disposed && revision == _saveRevision)
                {
                    _closeFailureShown = false;
                    ShowFailure(false);
                }
            }
            // 예외 경계: Forget한 저장의 실패를 가장 최근 요청만 기록하고, 후보를 남긴 채 재시도 표시로 바꾼다.
            catch (Exception error)
            {
                // 옵션 관리자는 실패를 기록하지 않고 넘기므로, 같은 저장을 기다린 요청 중 가장 최근 것만 기록해 누락과 중복을 막는다.
                if (revision == _saveRevision)
                    Log.Instance.Exception(error);
                if (!_disposed && revision == _saveRevision)
                {
                    // 실패 후보는 옵션 관리자에 남기고, 화면은 실제 확정값으로 되돌린다.
                    Fill(_options.Current);
                    ShowFailure(true);
                }
            }
        }

        // ESC가 펼쳐진 드롭다운 목록부터 닫게 한다. 목록을 접었으면 true를 돌려주고, 호출자는 설정 창을 닫지 않는다.
        // 같은 프레임에 드롭다운 항목의 취소 처리가 Hide를 다시 불러도 TMP가 무시한다.
        public bool TryCollapseDropdown() =>
            Collapse(_view.Language) || Collapse(_view.Resolution) || Collapse(_view.WindowMode);

        static bool Collapse(TMP_Dropdown dropdown)
        {
            if (!dropdown.IsExpanded) return false;
            dropdown.Hide();
            return true;
        }

        // ESC와 닫기 버튼은 저장 완료를 기다린 뒤 창을 닫는다. 앞선 닫기가 저장 실패를 보였으면 기다리지 않고 바로 닫는다.
        // 실패 후보는 옵션 관리자에 남고, 사용자는 일시정지나 메뉴의 종료 버튼에 다시 닿을 수 있다.
        public void RequestClose()
        {
            if (_closing) return;
            if (_closeFailureShown) Close();
            else CloseAsync().Forget();
        }

        // 저장이 실패하면 창을 열어 둔 채 실패 상태와 [적용] 재시도 버튼을 보이고, 다음 닫기는 기다리지 않게 한다.
        async UniTask CloseAsync()
        {
            _closing = true;
            try
            {
                // 옵션 관리자의 남은 저장이 모두 끝나기를 기다린다. 저장 실패나 남은 후보가 있으면 던지고 아래 catch가 받는다.
                await _options.FlushAsync();
                Close();
            }
            // 예외 경계: Forget한 닫기의 실패는 창을 연 채 재시도 표시로 바꾼다. 원인은 SaveAsync가 기록하므로 다시 기록하지 않는다.
            catch (Exception)
            {
                if (!_disposed)
                {
                    _closeFailureShown = true;
                    ShowFailure(true);
                }
            }
            finally { _closing = false; }
        }

        // 닫기 실패 표시를 지우고 상위 모달 소유자에게 닫기를 맡긴다.
        void Close()
        {
            if (_disposed) return;
            _closeFailureShown = false;
            _close();
        }

        // 상태 문구는 같은 식별자("status")의 번역 구독을 교체하므로 마지막 상태 하나만 남는다.
        void Status(string key) => _text.Set("status", key, value => _view.Status.text = value);

        // 저장 실패면 [적용] 재시도 버튼과 실패 상태를 보이고, 아니면 둘 다 지운다.
        void ShowFailure(bool failed)
        {
            _view.ApplyButton.gameObject.SetActive(failed);
            if (failed) Status("options.failed");
            else _text.Clear("status", value => _view.Status.text = value);
        }

        // 화면이 사라져도 옵션 관리자가 접수한 저장은 계속하고 UI 콜백만 제거한다.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // 한 구독의 해제가 실패해도 나머지 해제는 계속하고, 실패는 기록만 한다.
            _unbind.Dispose();
        }
    }
}
