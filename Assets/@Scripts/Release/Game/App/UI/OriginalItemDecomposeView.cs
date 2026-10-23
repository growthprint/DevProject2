#nullable enable annotations
using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 Duckov.UI.ItemDecomposeView다. 가방과 창고 목록, 고른 것의 상세(OriginalItemDetailsView 재사용), 대상 이름, 수량 슬라이더
    // (원작 DecomposeSlider), 결과 표시(원작 CostDisplay), 분해 불가·선택 없음·작업 중 표시, 분해 버튼, 닫기 버튼을 보인다. 값만 받고 의도 이벤트만 낸다.
    // 분해식과 세션 소지품은 읽지 않는다. 열고 닫기, 입력 차단, 명령은 화면 컨트롤러(DecomposeScreen)가 맡는다.
    // 원작 FadeGroup 전환은 옮기지 않고 활성 여부로 보인다(MasterKeysRegisterView 선례, 원작 차이).
    public sealed class OriginalItemDecomposeView : MonoBehaviour
    {
        // 목록 번호다. 원작 characterInventoryDisplay(가방), storageDisplay(창고) 순서다.
        internal const int _bagList = 0, _storageList = 1, _listCount = 2;

        // 목록 칸 템플릿이다. InventoryEntry.prefab 중첩 인스턴스이고 꺼진 채 가방 목록 격자 안에 있다.
        [SerializeField] OriginalItemDecomposeCellView _cellTemplate = null!;
        // 아래 배열은 목록 번호 순서다. 원작 InventoryDisplay 루트, entriesParent, placeHolder, capacityText,
        // contentLayout, gridLayoutElement다.
        [SerializeField] GameObject[] _lists = Array.Empty<GameObject>();
        [SerializeField] RectTransform[] _grids = Array.Empty<RectTransform>();
        [SerializeField] GameObject[] _placeholders = Array.Empty<GameObject>();
        [SerializeField] TMP_Text[] _capacities = Array.Empty<TMP_Text>();
        [SerializeField] GridLayoutGroup[] _layouts = Array.Empty<GridLayoutGroup>();
        [SerializeField] LayoutElement[] _heights = Array.Empty<LayoutElement>();
        // 원작 detailsDisplay(ItemDetailsDisplay)다. 원작 detailsFadeGroup은 상세 루트에 있어 상세 View가 함께 켜고 끈다.
        [SerializeField] OriginalItemDetailsView _details = null!;
        // 원작 DecomposeSlider의 slider, minText, maxText, valueText다.
        [SerializeField] Slider _countSlider = null!;
        [SerializeField] TMP_Text _minText = null!;
        [SerializeField] TMP_Text _maxText = null!;
        [SerializeField] TMP_Text _valueText = null!;
        // 원작 targetNameDisplay, resultDisplay, cannotDecomposeIndicator, noItemSelectedIndicator, decomposeButton, busyIndicator다.
        [SerializeField] TMP_Text _targetName = null!;
        [SerializeField] OriginalCostDisplayView _result = null!;
        [SerializeField] GameObject _cannotDecompose = null!;
        [SerializeField] GameObject _noItemSelected = null!;
        [SerializeField] Button _decomposeButton = null!;
        [SerializeField] GameObject _busy = null!;

        // 목록은 처음 쓸 때 만든다. EditMode 테스트와 공용 HUD 초기화가 Awake 없이 부르기 때문이다.
        OriginalInventoryDisplayList<OriginalItemDecomposeCellView>[]? _displays;
        // 칸 클릭을 이 View의 선택 이벤트로 넘기는 대리자다. 칸마다 할당하지 않도록 한 번만 만든다.
        Action<string>? _select;
        // 슬라이더는 값 컨트롤이라 코드로 구독한다(Docs/rules/ui.md). 창이 보이는 동안만 듣는다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 화면이 범위와 값을 맞추는 동안 슬라이더 알림을 의도로 내보내지 않는다.
        bool _settingCount;

        // 원작 ItemUIUtilities.Select(목록 칸 클릭), countSlider.OnValueChangedEvent, 분해 버튼, 닫기 버튼이다. 칸은 아이템 인스턴스 ID를 넘긴다.
        internal event Action<string>? CellSelected;
        internal event Action<int>? CountChanged;
        internal event Action? DecomposeRequested;
        internal event Action? CloseRequested;

        internal OriginalItemDetailsView Details => _details;
        // 원작 DecomposeSlider.Value처럼 슬라이더 값을 반올림한 수량이다.
        internal int Count => Mathf.RoundToInt(_countSlider.value);

        // 목록마다 같은 칸 템플릿과 그 목록 번호의 직렬화 참조로 만든다.
        OriginalInventoryDisplayList<OriginalItemDecomposeCellView> Lists(int list)
        {
            if (_displays == null)
            {
                _displays = new OriginalInventoryDisplayList<OriginalItemDecomposeCellView>[_listCount];
                for (int index = 0; index < _listCount; index++)
                    _displays[index] = new OriginalInventoryDisplayList<OriginalItemDecomposeCellView>(_cellTemplate,
                        static cell => cell.ResetBinding(), _grids[index], _placeholders[index], _capacities[index], _layouts[index], _heights[index]);
            }
            return _displays[list];
        }

        // 분해 창은 원작처럼 더블클릭을 구독하지 않아 칸 클릭은 선택 하나뿐이다.
        Action<string> SelectCell => _select ??= itemId => CellSelected?.Invoke(itemId);

        // 원작 OnOpen이다. 원작 Awake의 SetupEmpty처럼 선택 없음 상태로 열고, 화면 컨트롤러가 이어서 목록과 대상을 그린다.
        internal void Show()
        {
            _subscriptions.Clear();
            _subscriptions.Bind(_countSlider.onValueChanged, SliderChanged);
            ShowEmpty();
            gameObject.SetActive(true);
        }

        // 공용 HUD 초기화도 무조건 부르므로 숨어 있어도 된다. 칸 연결과 슬라이더 구독을 끊어 닫힌 뒤 의도를 보내지 않게 한다.
        internal void Hide()
        {
            _subscriptions.Clear();
            for (int list = 0; list < _listCount; list++) Lists(list).Clear();
            gameObject.SetActive(false);
        }

        // 원작 InventoryDisplay.Setup이다. shown이 거짓이면 원작처럼 그 목록 전체를 숨긴다(창고 없음).
        // 칸 풀, 수용량 글자, 격자 높이는 다른 목록 창과 같은 목록 도우미가 맡는다. AddCell 뒤 EndList로 닫는다.
        internal void BeginList(int list, bool shown, int itemCount, int capacity)
        {
            _lists[list].SetActive(shown);
            Lists(list).Begin(itemCount, capacity);
        }

        // 원작 InventoryEntry 하나다. operable은 원작 EntryFunc_CanOperate(빈 칸이거나 분해식이 있음) 값이다.
        // 칸 클릭은 한 번 만든 대리자로 받는다.
        internal void AddCell(int list, string? itemId, Sprite? icon, string itemName, int count, bool selected, bool operable) =>
            Lists(list).Add().Bind(SelectCell, itemId, icon, itemName, count, selected, operable);

        // 원작 RefreshGridLayoutPreferredHeight다. 쓰지 않은 칸 숨기기와 격자 높이 맞춤은 목록 도우미가 맡는다.
        internal void EndList(int list) => Lists(list).End();

        // 원작 SetupEmpty다. 상세는 화면 컨트롤러가 InventoryItemDetails로 숨긴다.
        internal void ShowEmpty()
        {
            _targetName.text = "-";
            ClearResult();
            _cannotDecompose.SetActive(false);
            _decomposeButton.gameObject.SetActive(false);
            _noItemSelected.SetActive(true);
            _busy.SetActive(false);
            SetCount(1, 1);
        }

        // 원작 Setup이다. 분해식이 있으면 분해 버튼을, 없으면 분해 불가 표시를 켠다. 상세는 화면 컨트롤러가 InventoryItemDetails로 보인다.
        // 원작 busyIndicator는 비동기 분해 중에만 켜진다. 이 게임의 분해 명령은 같은 프레임에 끝나 늘 꺼져 있다.
        internal void ShowTarget(string targetName, bool decomposable)
        {
            _noItemSelected.SetActive(false);
            _targetName.text = targetName;
            _decomposeButton.gameObject.SetActive(decomposable);
            _cannotDecompose.SetActive(!decomposable);
            _busy.SetActive(false);
        }

        // 원작 SetupSlider의 SetMinMax와 Value다. 겹친 아이템은 1부터 겹친 수까지이고 기본값은 겹친 수 전체다(원작 ItemDecomposeView.cs:196-205).
        // 범위를 바꾸는 동안 슬라이더가 보내는 알림은 의도로 내보내지 않는다.
        internal void SetCount(int max, int value)
        {
            _settingCount = true;
            _countSlider.minValue = 1;
            _countSlider.maxValue = max;
            _countSlider.SetValueWithoutNotify(value);
            _settingCount = false;
            _minText.text = "1";
            _maxText.text = max.ToString(CultureInfo.InvariantCulture);
            _valueText.text = value.ToString(CultureInfo.InvariantCulture);
        }

        // 원작 resultDisplay.Setup(cost, count)이다. 돈 줄과 아이템 줄을 화면 컨트롤러가 배수를 곱해 채운다.
        internal OriginalCostDisplayView Result => _result;

        // 원작 resultDisplay.Clear다. 돈 줄을 숨기고 아이템 줄을 모두 거둔다.
        internal void ClearResult()
        {
            _result.BeginItems(0, true, false, false);
            _result.EndItems();
        }

        // 고정 버튼 두 개는 제작 도구가 Runtime Only 영속 OnClick으로 이 공개 핸들러에 연결한다(Docs/rules/ui.md).
        public void OnDecomposeClicked() => DecomposeRequested?.Invoke();

        public void OnExitClicked() => CloseRequested?.Invoke();

        // 원작 DecomposeSlider.OnValueChanged와 ItemDecomposeView.OnSliderValueChanged다. 값 글자를 바꾸고 반올림한 수량을 넘긴다.
        void SliderChanged(float value)
        {
            int count = Mathf.RoundToInt(value);
            _valueText.text = count.ToString(CultureInfo.InvariantCulture);
            if (!_settingCount) CountChanged?.Invoke(count);
        }
    }
}
