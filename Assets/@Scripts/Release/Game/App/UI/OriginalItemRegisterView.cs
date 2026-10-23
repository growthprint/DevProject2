#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 MasterKeysRegisterView와 FormulasRegisterView가 같은 모양으로 가진 등록 창이다. 가방과 창고 목록, 등록 칸, 고른 것의 상세
    // (OriginalItemDetailsView 재사용), 이미 등록됨 표시(recordExistsIndicator), 등록 성공 표시(succeedIndicator), 등록 버튼, 닫기 버튼을 보이고
    // 값만 받아 의도 이벤트만 낸다. 저장된 SharedHUD 프리팹은 창마다 파생 형식을 가리키므로 공통 필드와 몸통만 여기 둔다. 등록 소유자와 저장 상태는
    // 읽지 않고, 열고 닫기, 입력 차단, 명령은 창마다 화면 컨트롤러가 맡는다. 원작 FadeGroup 전환은 활성 여부로 보인다(StockShopView 선례, 원작 차이).
    public abstract class OriginalItemRegisterView : MonoBehaviour, IOriginalRegisterCellOwner
    {
        // 목록 번호다. 원작 inventoryDisplay(가방), playerStorageInventoryDisplay(창고) 순서다. 등록 칸은 목록이 아니라 칸 View가 정한 -1이다.
        internal const int _bagList = 0, _storageList = 1, _listCount = 2, _slotList = OriginalRegisterCellView._slotList;

        // 목록 칸 템플릿이다. InventoryEntry.prefab 중첩 인스턴스이고 꺼진 채 가방 목록 격자 안에 있다. 창마다 제 파생 칸 형식을 가리킨다.
        [SerializeField] OriginalRegisterCellView _cellTemplate = null!;
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
        // 원작 registerSlotDisplay(SlotDisplay)다.
        [SerializeField] OriginalRegisterCellView _slot = null!;
        [SerializeField] GameObject _recordExists = null!;
        [SerializeField] GameObject _success = null!;
        // 원작 successIndicationTime(초)이다. 제작 도구가 원본 값을 옮긴다.
        [SerializeField] float _successTime = 1.5f;

        // 목록은 처음 쓸 때 만든다. EditMode 테스트와 공용 HUD 초기화가 Awake 없이 부르기 때문이다.
        OriginalInventoryDisplayList<OriginalRegisterCellView>[]? _displays;
        // 성공 표시가 남은 시간이다. 음수면 표시가 꺼져 있다.
        float _successLeft = -1f;

        // 원작 ItemUIUtilities.Select(목록·등록 칸 클릭), InventoryDisplay.onDisplayDoubleClicked(목록 칸 → 등록 칸),
        // SlotDisplay.onSlotDisplayDoubleClicked(등록 칸 → 가방), 등록 버튼, 닫기 버튼이다. 목록 칸은 아이템 인스턴스 ID를 넘긴다.
        internal event Action<string>? CellSelected;
        internal event Action<string>? CellActivated;
        internal event Action? SlotSelected;
        internal event Action? SlotActivated;
        internal event Action? SubmitRequested;
        internal event Action? CloseRequested;

        internal bool IsShown => gameObject.activeSelf;
        internal OriginalItemDetailsView Details => _details;

        // 목록마다 같은 칸 템플릿과 그 목록 번호의 직렬화 참조로 만든다.
        OriginalInventoryDisplayList<OriginalRegisterCellView> Lists(int list)
        {
            if (_displays == null)
            {
                _displays = new OriginalInventoryDisplayList<OriginalRegisterCellView>[_listCount];
                for (int index = 0; index < _listCount; index++)
                    _displays[index] = new OriginalInventoryDisplayList<OriginalRegisterCellView>(_cellTemplate,
                        static cell => cell.ResetBinding(), _grids[index], _placeholders[index], _capacities[index], _layouts[index], _heights[index]);
            }
            return _displays[list];
        }

        // 원작 OnOpen이다. 원작 Awake의 succeedIndicator.SkipHide처럼 성공 표시를 끈 채 연다.
        internal void Show()
        {
            HideSuccess();
            _recordExists.SetActive(false);
            gameObject.SetActive(true);
        }

        // 공용 HUD 초기화도 무조건 부르므로 숨어 있어도 된다. 칸 연결을 모두 끊어 닫힌 뒤 눌린 칸이 의도를 보내지 않게 한다.
        internal void Hide()
        {
            for (int list = 0; list < _listCount; list++) Lists(list).Clear();
            _slot.ResetBinding();
            HideSuccess();
            gameObject.SetActive(false);
        }

        // 원작 InventoryDisplay.Setup이다. shown이 거짓이면 원작처럼 그 목록 전체를 숨긴다(창고 없음).
        // 칸 풀, 수용량 글자, 격자 높이는 다른 목록 창과 같은 목록 도우미가 맡는다. AddCell 뒤 EndList로 닫는다.
        internal void BeginList(int list, bool shown, int itemCount, int capacity)
        {
            _lists[list].SetActive(shown);
            Lists(list).Begin(itemCount, capacity);
        }

        // 원작 InventoryEntry 하나다. highlight는 원작 EntryFunc_ShouldHighligh, operable은 EntryFunc_CanOperate 값이다.
        internal void AddCell(int list, string? itemId, Sprite? icon, string itemName, int count, bool selected, bool highlight, bool operable) =>
            Lists(list).Add().BindCell(this, list, itemId, icon, itemName, count, selected, highlight, operable);

        // 원작 RefreshGridLayoutPreferredHeight다. 쓰지 않은 칸 숨기기와 격자 높이 맞춤은 목록 도우미가 맡는다.
        internal void EndList(int list) => Lists(list).End();

        // 원작 registerSlotDisplay.Setup과 RefreshRecordExistsIndicator다. recordExists는 창마다 원작 판정(이미 등록된 열쇠 종류,
        // 이미 해금된 제조식)을 화면 컨트롤러가 계산해 넘긴다.
        internal void ShowSlot(string label, string? itemId, Sprite? icon, string itemName, int count, bool selected, bool recordExists)
        {
            _slot.BindSlot(this, label, itemId, icon, itemName, count, selected);
            _recordExists.SetActive(recordExists);
        }

        // 원작 SuccessIndicationTask다. 시간 배율과 상관없이 정해진 시간 뒤 이 View의 Update가 끈다.
        internal void ShowSuccess()
        {
            _success.SetActive(true);
            _successLeft = _successTime;
        }

        // 원작 HideSuccessIndication이다. 원작처럼 등록 칸 내용이 바뀌면 화면 컨트롤러가 부른다.
        internal void HideSuccess()
        {
            _success.SetActive(false);
            _successLeft = -1f;
        }

        // 칸 View가 넘긴 클릭이다. 등록 칸은 목록 번호가 _slotList다. 칸만 부르도록 계약 형식으로만 드러낸다.
        void IOriginalRegisterCellOwner.CellClicked(int list, string itemId, bool activate)
        {
            if (list == _slotList)
            {
                if (activate) SlotActivated?.Invoke();
                else SlotSelected?.Invoke();
                return;
            }
            if (activate) CellActivated?.Invoke(itemId);
            else CellSelected?.Invoke(itemId);
        }

        // 고정 버튼 두 개는 제작 도구가 Runtime Only 영속 OnClick으로 이 공개 핸들러에 연결한다(Docs/rules/ui.md).
        public void OnSubmitClicked() => SubmitRequested?.Invoke();

        public void OnExitClicked() => CloseRequested?.Invoke();

        // 파생 창은 Update를 따로 두지 않는다. 두면 Unity가 파생 쪽만 불러 성공 표시가 꺼지지 않는다.
        void Update()
        {
            if (_successLeft < 0f) return;
            _successLeft -= Time.unscaledDeltaTime;
            if (_successLeft <= 0f) HideSuccess();
        }
    }
}
