#nullable enable annotations
using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 Duckov.UI.ItemRepairView다. 캐릭터 장비 칸(slotDisplay)과 가방(inventoryDisplay), 고른 것의 상세(OriginalItemDetailsView 재사용),
    // 고른 아이템의 이름, 아이콘, 내구도 막대, 수리 표시와 수리 단추, 가격, 모두 수리 패널(OriginalRepairAllPanelView), 닫기 단추를 보인다.
    // 값만 받고 의도 이벤트만 낸다. 수리 판정, 가격, 명령, 열고 닫기, 입력 차단은 화면 컨트롤러(RepairScreen)가 맡는다.
    // 원작 FadeGroup 전환은 옮기지 않고 활성 여부로 보인다(OriginalMasterKeysRegisterView 선례, 원작 차이). 고른 아이템 품질 그림자도 옮기지 않는다.
    public sealed class OriginalItemRepairView : MonoBehaviour
    {
        // 원작 slotDisplay의 장비 칸이다. 순서는 OriginalCharacterSlots.All이고 제작 도구가 칸마다 하나씩 만든다.
        [SerializeField] OriginalItemRepairCellView[] _slots = Array.Empty<OriginalItemRepairCellView>();
        // 원작 inventoryDisplay의 entryPrefab(InventoryEntry), entriesParent, placeHolder, capacityText, contentLayout, gridLayoutElement다.
        // 칸 템플릿은 InventoryEntry.prefab 중첩 인스턴스이고 꺼진 채 가방 격자 안에 있다.
        [SerializeField] OriginalItemRepairCellView _cellTemplate = null!;
        [SerializeField] RectTransform _grid = null!;
        [SerializeField] GameObject _placeholder = null!;
        [SerializeField] TMP_Text _capacity = null!;
        [SerializeField] GridLayoutGroup _layout = null!;
        [SerializeField] LayoutElement _height = null!;
        // 원작 detailsDisplay다. 원작 itemDetailsFadeGroup의 보이기와 숨기기는 상세 View가 맡는다.
        [SerializeField] OriginalItemDetailsView _details = null!;
        [SerializeField] OriginalRepairAllPanelView _repairAll = null!;
        // 원작 repairButtonFadeGroup과 placeHolderFadeGroup의 루트다. 고칠 수 있으면 단추 묶음을, 아니면 자리표시 묶음을 켠다.
        [SerializeField] GameObject _repairGroup = null!;
        [SerializeField] GameObject _repairPlaceholder = null!;
        [SerializeField] Button _repairButton = null!;
        [SerializeField] TMP_Text _price = null!;
        [SerializeField] TMP_Text _selectedName = null!;
        [SerializeField] Image _selectedIcon = null!;
        // 원작 noItemSelectedNameText와 noItemSelectedIconSprite다. 제작 도구가 원본 값을 옮긴다.
        [SerializeField] string _noItemName = "-";
        [SerializeField] Sprite? _noItemIcon;
        [SerializeField] GameObject _noNeedToRepair = null!;
        [SerializeField] GameObject _broken = null!;
        [SerializeField] GameObject _cannotRepair = null!;
        [SerializeField] TMP_Text _durabilityText = null!;
        [SerializeField] TMP_Text _willLose = null!;
        [SerializeField] Image _barFill = null!;
        [SerializeField] Image _lossBarFill = null!;
        // 원작 barFillColorOverT다. 최대 대비 현재 비율로 채움 색을 고른다.
        [SerializeField] Gradient _barColor = new Gradient();

        // 가방 목록은 처음 쓸 때 만든다. EditMode 테스트와 공용 HUD 초기화가 Awake 없이 부르기 때문이다.
        OriginalInventoryDisplayList<OriginalItemRepairCellView>? _bag;
        // 칸 클릭을 이 View의 선택 이벤트로 넘기는 대리자다. 칸마다 할당하지 않도록 한 번만 만든다.
        Action<string>? _select;

        // 원작 ItemUIUtilities.Select(장비 칸, 가방 칸 클릭), 수리 단추, 닫기 단추다. 칸은 아이템 인스턴스 ID를 넘긴다.
        internal event Action<string>? CellSelected;
        internal event Action? RepairRequested;
        internal event Action? CloseRequested;

        internal OriginalItemDetailsView Details => _details;
        internal OriginalRepairAllPanelView RepairAll => _repairAll;

        // 칸 풀, 수용량 글자, 격자 높이는 다른 목록 창과 같은 목록 도우미가 맡는다.
        OriginalInventoryDisplayList<OriginalItemRepairCellView> Bag =>
            _bag ??= new OriginalInventoryDisplayList<OriginalItemRepairCellView>(_cellTemplate, static cell => cell.ResetBinding(), _grid, _placeholder,
                _capacity, _layout, _height);

        Action<string> SelectCell => _select ??= itemId => CellSelected?.Invoke(itemId);

        // 원작 OnOpen이다. 원작 repairButtonFadeGroup.SkipHide, placeHolderFadeGroup.SkipHide처럼 두 묶음을 끈 채 연다.
        // 곧바로 컨트롤러가 다시 그려 고른 것 없음 상태를 보인다.
        internal void Show()
        {
            _repairGroup.SetActive(false);
            _repairPlaceholder.SetActive(false);
            gameObject.SetActive(true);
        }

        // 원작 OnClose다. 공용 HUD 초기화도 무조건 부르므로 숨어 있어도 된다. 칸 연결을 모두 끊어 닫힌 뒤 눌린 칸이 의도를 보내지 않게 한다.
        internal void Hide()
        {
            Bag.Clear();
            foreach (OriginalItemRepairCellView slot in _slots) slot.ResetBinding();
            _repairAll.Clear();
            gameObject.SetActive(false);
        }

        // 원작 SlotDisplay.Setup이다. index는 OriginalCharacterSlots.All 순서이고 호출자는 저작된 칸 범위만 넘긴다. itemId가 null이면 빈 칸이다.
        internal void ShowSlot(int index, string label, string? itemId, Sprite? icon, string itemName, int count)
        {
            OriginalItemRepairCellView slot = _slots[index];
            slot.Bind(SelectCell, itemId, icon, itemName, count, false);
            slot.SetLabel(label);
        }

        // 원작 InventoryDisplay.Setup이다. 수용량 글자는 목록 도우미가 원작 형식으로 만든다. AddCell 뒤 EndBag으로 닫는다.
        internal void BeginBag(int itemCount, int capacity) => Bag.Begin(itemCount, capacity);

        // 원작 InventoryEntry 하나다. itemId가 null이면 빈 칸이다.
        internal void AddCell(string? itemId, Sprite? icon, string itemName, int count, bool selected) =>
            Bag.Add().Bind(SelectCell, itemId, icon, itemName, count, selected);

        // 원작 RefreshGridLayoutPreferredHeight다. 쓰지 않은 칸 숨기기와 격자 높이 맞춤은 목록 도우미가 맡는다.
        internal void EndBag() => Bag.End();

        // 원작 RefreshSelectedItemInfo의 고른 것 없음 경로다. 이름은 원작 기본 문구, 아이콘은 투명, 표시와 막대는 모두 끈다.
        internal void ShowNothingSelected()
        {
            ShowRepairable(false);
            _willLose.text = "";
            _selectedName.text = _noItemName;
            _selectedIcon.sprite = _noItemIcon;
            _selectedIcon.color = Color.clear;
            _noNeedToRepair.SetActive(false);
            _broken.SetActive(false);
            _cannotRepair.SetActive(false);
            HideDurability();
        }

        // 원작 RefreshSelectedItemInfo의 고른 아이템 경로다. 세 표시 조건은 원작 식을 컨트롤러가 RepairRules로 계산해 넘긴다.
        // 수리 단추와 내구도는 이 뒤에 ShowRepair와 ShowDurability 또는 HideDurability로 채운다.
        internal void ShowSelected(Sprite? icon, string itemName, bool canRepair, bool noNeedToRepair, bool cannotRepair, bool broken)
        {
            ShowRepairable(canRepair);
            _willLose.text = "";
            _selectedIcon.color = Color.white;
            _selectedName.text = itemName;
            _selectedIcon.sprite = icon;
            _noNeedToRepair.SetActive(noNeedToRepair);
            _cannotRepair.SetActive(cannotRepair);
            _broken.SetActive(broken);
        }

        // 원작 CanRepair일 때의 가격, 최대 내구도 감소 문구("UI_MaxDurability -" + 감소량), 단추 상태다. 문구 앞부분은 컨트롤러가 번역해 넘긴다.
        internal void ShowRepair(int price, string maxDurabilityLabel, float lost, bool affordable)
        {
            _price.text = price.ToString();
            _willLose.text = maxDurabilityLabel + " -" + lost.ToString("0.#", CultureInfo.InvariantCulture);
            _repairButton.interactable = affordable;
        }

        // 원작 내구도 막대다. 채움은 최대 대비 현재 비율, 손실 막대는 누적 손실이고 글자는 "현재 / 손실 반영 최대 "다(원작 형식 그대로 끝에 공백).
        internal void ShowDurability(float current, float maximum, float loss)
        {
            float ratio = current / maximum;
            _barFill.fillAmount = ratio;
            _lossBarFill.fillAmount = loss;
            _durabilityText.text = current.ToString("0.#", CultureInfo.InvariantCulture) + " / "
                + (maximum * (1f - loss)).ToString("0.#", CultureInfo.InvariantCulture) + " ";
            _barFill.color = _barColor.Evaluate(ratio);
        }

        // 원작에서 고른 것이 없거나 내구도를 쓰지 않는 아이템일 때다.
        internal void HideDurability()
        {
            _barFill.fillAmount = 0f;
            _lossBarFill.fillAmount = 0f;
            _durabilityText.text = "-";
        }

        // 고정 버튼 두 개는 제작 도구가 Runtime Only 영속 OnClick으로 이 공개 핸들러에 연결한다(Docs/rules/ui.md).
        public void OnRepairClicked() => RepairRequested?.Invoke();

        public void OnExitClicked() => CloseRequested?.Invoke();

        // 원작처럼 고칠 수 있으면 자리표시 대신 수리 단추 묶음을 보인다.
        void ShowRepairable(bool canRepair)
        {
            _repairPlaceholder.SetActive(!canRepair);
            _repairGroup.SetActive(canRepair);
        }
    }
}
