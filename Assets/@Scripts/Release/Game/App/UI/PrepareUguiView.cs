#nullable enable annotations
using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 난이도 화면의 컨트롤을 다룬다. 원본 표시 순서는 제작된 배열 순서를 따르며, enum을 순회해 화면을 만들지 않는다.
    public sealed class PrepareUguiView : MonoBehaviour
    {
        // 이 화면이 건 구독을 모아 두었다가 파괴될 때 함께 푼다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        void OnDestroy() => _subscriptions.Dispose();

        [SerializeField] Button[] _choices = Array.Empty<Button>();
        [SerializeField] GameObject[] _selected = Array.Empty<GameObject>();
        [SerializeField] TMP_Text _description = null!;
        [SerializeField] Button _confirm = null!;
        [SerializeField] GameObject _customPanel = null!;
        [SerializeField] Button _closeCustom = null!;
        [SerializeField] Slider[] _numbers = Array.Empty<Slider>();
        // 원본 사용자 지정 패널의 드롭다운과 수치 입력 칸이다. _values[i]는 슬라이더 _numbers[i]와 짝이다.
        [SerializeField] TMP_Dropdown[] _flags = Array.Empty<TMP_Dropdown>();
        [SerializeField] TMP_InputField[] _values = Array.Empty<TMP_InputField>();
        [SerializeField] CanvasGroup _controls = null!;
        readonly LocalizedTextBindings _text = new LocalizedTextBindings();
        static readonly string[] _flagKeys = { "Options_Off", "Options_On" };
        // 잠긴 선택지다(원작 DifficultySelection_Entry.locked). 위치는 _choices와 같고, 컨트롤러가 준비 때 SetLocked로 한 번 넣는다.
        // 원작 잠금 표는 첫 선택에서 분노만, 기지 뒤 재설정에서 현재 규칙에 따라 여러 선택지를 잠근다.
        bool[] _locked = Array.Empty<bool>();
        // 사용자 지정 패널을 여는 선택지 위치다. 패널을 닫으면 선택을 이 버튼으로 돌려준다.
        const int _customChoice = 6;
        const int _spawnDeadBodyFlag = 0, _fogOfWarFlag = 1, _advancedDebuffFlag = 2;
        // 사용자 지정 수치 슬라이더 _numbers와 입력 칸 _values의 위치다. 저작된 두 배열의 순서와 같아야 한다.
        const int _damageToPlayerNumber = 0, _enemyHealthNumber = 1, _savedBodiesNumber = 2, _recoilNumber = 3,
            _reactionTimeNumber = 4, _attackIntervalNumber = 5, _attackTimeNumber = 6;
        // 드롭다운의 끔과 켬 선택지 위치로, _flagKeys 순서와 같아야 한다.
        const int _flagOffChoice = 0, _flagOnChoice = 1;

        // View는 사용자 입력만 알리고 저장이나 진행 명령은 실행하지 않는다. 명령은 PrepareSceneController가 맡는다.
        public event Action<int>? Selected;
        public event Action? Confirmed;

        // 콜백만 연결하고 화면을 열거나 선택 이벤트를 일으키지 않는다. PrepareSceneController.Prepare가 준비마다 한 번만 부르고,
        // 컨트롤 배열 크기와 참조는 EditMode 저작 검사가 확인하므로 중복 연결이나 참조 누락을 검사하지 않는다.
        public void Bind()
        {
            // 선택지 버튼은 순서만 다르므로 위치를 넘기는 공용 묶음 구독을 쓴다. 구독자는 누를 때 읽는다.
            _subscriptions.BindIndexed(_choices, index => Selected?.Invoke(index));
            for (int i = 0; i < _numbers.Length; i++)
            {
                int index = i;
                // 슬라이더와 입력 칸이 서로를 갱신한다. 입력 값은 슬라이더에 넣어 범위 안으로 맞추고, 숫자가 아니면 슬라이더 값으로 되돌린다.
                // 숫자 읽기는 옵션 감도 입력과 같은 UiNumbers 규칙을 쓴다.
                _subscriptions.Bind(_numbers[i].onValueChanged, value => _values[index].SetTextWithoutNotify(Format(value)));
                _subscriptions.Bind(_values[i].onEndEdit, text =>
                {
                    if (UiNumbers.TryParseFinite(text, out float value)) _numbers[index].value = value;
                    _values[index].SetTextWithoutNotify(Format(_numbers[index].value));
                });
            }
            _subscriptions.Bind(_confirm.onClick, () => Confirmed?.Invoke());
            // 패널을 닫으면 선택이 꺼진 닫기 버튼에 남아 키보드와 패드 탐색이 멈추므로, 사용자 지정 선택지로 선택을 돌려준다.
            _subscriptions.Bind(_closeCustom.onClick, () => { _customPanel.SetActive(false); UiFocus.TryFocus(_choices[_customChoice]); });
            // 사용자 지정 드롭다운의 끔과 켬 선택지를 번역 키로 만들고 로캘 변경을 구독한다. 선택 인덱스는 유지되며,
            // 구독은 화면이 파괴될 때 함께 푼다.
            _subscriptions.Add(_text.Dispose);
            for (int i = 0; i < _flags.Length; i++) _text.BindChoices("flag." + i, _flags[i], _flagKeys);
            _customPanel.SetActive(false);
        }

        // 현재 화면 값으로 사용자 지정 난이도 후보를 만든다. 슬라이더 범위는 프리팹 빌드 데이터라 런타임에 검증하지 않고,
        // 최솟값이 0 이상인지는 ProductUiLocalizationTests가 프리팹에서 확인한다. 입력 칸은 유한 수만 슬라이더에 넣으므로 검증 없는 생성자를 쓴다.
        public DifficultyState ReadCustom()
        {
            // 수치 위치는 SetCustom과 같은 위치 상수로 읽는다.
            return new DifficultyState(DifficultyRuleIndex.Custom,
                _numbers[_damageToPlayerNumber].value, _numbers[_enemyHealthNumber].value,
                _flags[_spawnDeadBodyFlag].value == _flagOnChoice,
                _flags[_fogOfWarFlag].value == _flagOnChoice, _flags[_advancedDebuffFlag].value == _flagOnChoice,
                Mathf.RoundToInt(_numbers[_savedBodiesNumber].value), _numbers[_recoilNumber].value,
                _numbers[_reactionTimeNumber].value, _numbers[_attackIntervalNumber].value, _numbers[_attackTimeNumber].value);
        }

        // 값 변경 알림 없이 화면만 맞춰, 초기값을 넣는 동안 입력 콜백이 돌지 않게 한다. 수치 위치는 ReadCustom과 같은 상수를 쓴다.
        public void SetCustom(DifficultyState value)
        {
            SetNumber(_damageToPlayerNumber, value.DamageFactorToPlayer);
            SetNumber(_enemyHealthNumber, value.EnemyHealthFactor);
            SetNumber(_savedBodiesNumber, value.SaveDeadbodyCount);
            SetNumber(_recoilNumber, value.RecoilMultiplier);
            SetNumber(_reactionTimeNumber, value.EnemyReactionTimeFactor);
            SetNumber(_attackIntervalNumber, value.EnemyAttackTimeSpaceFactor);
            SetNumber(_attackTimeNumber, value.EnemyAttackTimeFactor);
            _flags[_spawnDeadBodyFlag].SetValueWithoutNotify(value.SpawnDeadBody ? _flagOnChoice : _flagOffChoice);
            _flags[_fogOfWarFlag].SetValueWithoutNotify(value.FogOfWar ? _flagOnChoice : _flagOffChoice);
            _flags[_advancedDebuffFlag].SetValueWithoutNotify(value.AdvancedDebuffMode ? _flagOnChoice : _flagOffChoice);
        }

        // 슬라이더와 입력 칸을 알림 없이 같은 값으로 맞춘다.
        void SetNumber(int index, float value)
        {
            _numbers[index].SetValueWithoutNotify(value);
            _values[index].SetTextWithoutNotify(Format(value));
        }

        // 사용자 지정 수치를 소수 둘째 자리까지, 로캘과 무관한 소수점으로 표시한다.
        static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        // 선택 테두리와 설명을 함께 갱신한다. 선택지 잠금은 SetInteractive가, 끔과 켬 라벨은 Bind의 번역 구독이 맡는다.
        // 선택 테두리는 공용 ShowOnly로 고른 위치 하나만 켠다.
        public void ShowSelection(int index, string description, bool openCustom)
        {
            UiVisibility.ShowOnly(_selected, index);
            _description.text = description;
            // 패널을 열면 선택을 패널의 첫 슬라이더로 옮겨, 선택이 패널 뒤 선택지에 남지 않게 한다.
            if (openCustom)
            {
                _customPanel.SetActive(true);
                UiFocus.TryFocus(_numbers[0]);
            }
        }

        // 컨트롤러가 원작 CheckUnlocked 표로 정한 잠금을 받는다. 다음 SetInteractive부터 버튼에 반영한다.
        public void SetLocked(bool[] locked) => _locked = locked;

        // 일시정지, 저장, 전환 중에는 포인터와 키보드 선택을 모두 막는다. 선택지 잠금은 SetLocked로 받은 표를 따르고, 받기 전 위치는 잠그지 않는다.
        // 안개와 고급 디버프 드롭다운도 늘 잠근다. 원본 FogOfWarManager, Health 버프 확률, 상점, 루트 태그에 대응하는 시스템이 없어서다.
        // 저장 코덱이 난이도 키를 빠짐없이 요구하므로 두 값은 프리셋 값 그대로 저장한다.
        public void SetInteractive(bool interactive)
        {
            _controls.interactable = interactive;
            _controls.blocksRaycasts = interactive;
            for (int i = 0; i < _choices.Length; i++) _choices[i].interactable = i >= _locked.Length || !_locked[i];
            _flags[_fogOfWarFlag].interactable = false;
            _flags[_advancedDebuffFlag].interactable = false;
        }
    }
}
