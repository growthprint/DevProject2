#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 수리 창의 아이템 칸 하나다. 장비 칸은 원작 SlotDisplay(SlotDisplay.prefab 중첩 인스턴스), 가방 칸은 원작 InventoryEntry(InventoryEntry.prefab
    // 중첩 인스턴스), 모두 수리 목록 칸은 원작 ItemDisplay(itemDisplayTemplate)다. 셋 다 원작 ItemDisplay의 아이콘, 이름, 수량을 쓰므로 한 View로 둔다.
    // 없는 표시 참조는 비어 있어 != null로 거른다. 장비 칸만 칸 이름과 빈 칸 아이콘을, 모두 수리 목록 칸만 원작 ItemDisplay 내구도 막대를 가진다.
    // 클릭은 의도만 넘긴다. 원작 수리 창의 두 목록은 편집할 수 없어(editable 0) 왼쪽 클릭 선택만 있고 끌기와 더블클릭 동작은 없다.
    // 모두 수리 목록 칸은 원작 OnPointerClickEntry(data.Use)처럼 클릭을 받지 않는다. 칸 안의 OriginalInventoryCell, OriginalSlotCell은 제작 도구가 꺼 둔다.
    public sealed class OriginalItemRepairCellView : OriginalItemDisplayCellView, IPointerClickHandler
    {
        [SerializeField] Image _icon = null!;
        // 이름과 수량은 원작 ItemDisplay처럼 글자 뒤 띠(nameContainer, countGameObject)째 숨긴다.
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] GameObject _nameBox = null!;
        [SerializeField] TMP_Text _count = null!;
        [SerializeField] GameObject _countBox = null!;
        // 원작 selectionIndicator다. 장비 칸은 제품 인벤토리처럼 선택 테두리가 없다. 호버 표시는 공통 칸 기반이 맡는다.
        [SerializeField] GameObject? _selection;
        // 가방 칸의 원본 버튼이다. 원작처럼 빈 칸 버튼은 탐색과 강조 대상이 되지 않게 끈다.
        [SerializeField] Button? _button;
        // 장비 칸만 있다. 원작 SlotDisplay처럼 칸 아이콘은 빈 칸에서만 보인다.
        [SerializeField] TMP_Text? _label;
        [SerializeField] GameObject? _slotIcon;
        // 원작 ItemDisplay의 durabilityGameObject, durabilityFill, durabilityLoss, durabilityZeroIndicator, durabilityFillColorOverT다.
        // 모두 수리 목록 칸만 제작 도구가 원본 값을 옮긴다.
        [SerializeField] GameObject? _durability;
        [SerializeField] Image? _durabilityFill;
        [SerializeField] Image? _durabilityLoss;
        [SerializeField] GameObject? _durabilityZero;
        [SerializeField] Gradient _durabilityColor = new Gradient();

        Action<string>? _clicked;
        string? _itemId;

        // 클릭을 받는 칸에만 호버 표시를 켠다. 모두 수리 목록 칸은 대리자 없이 묶여 켜지지 않는다.
        private protected override bool Hoverable => _clicked != null;

        // 원작 ItemDisplay.Setup과 InventoryEntry.Refresh다. itemId가 null이면 빈 칸이다. clicked가 null이면 클릭을 받지 않는다.
        // 컨트롤러는 화면이 열릴 때 한 번 만든 대리자를 넘겨 칸마다 할당하지 않는다.
        internal void Bind(Action<string>? clicked, string? itemId, Sprite? icon, string itemName, int count, bool selected)
        {
            _clicked = clicked;
            _itemId = itemId;
            bool occupied = itemId != null;
            // 아이템 표시는 다른 창 칸과 같은 원작 ItemDisplay 규칙을 쓴다.
            OriginalItemDisplay.Show(_icon, _name, _nameBox, _count, _countBox, icon, itemName, count, occupied);
            if (_selection != null) _selection.SetActive(selected && occupied);
            if (_button != null) _button.interactable = occupied && clicked != null;
            if (_slotIcon != null) _slotIcon.SetActive(!occupied);
            if (_durability != null) _durability.SetActive(false);
            gameObject.SetActive(true);
        }

        // 원작 SlotDisplay.label이다. 장비 칸 이름은 원작처럼 칸의 첫 태그 표시 이름이다.
        internal void SetLabel(string label)
        {
            if (_label != null) _label.text = label;
        }

        // 원작 ItemDisplay.Refresh의 내구도 막대다. 채움은 최대 대비 현재 비율과 그 비율의 색, 손실 막대는 누적 손실, 0이면 0 표시를 켠다.
        // Bind 뒤에 부른다. 내구도 막대가 없는 칸은 아무것도 하지 않는다.
        internal void ShowDurability(float current, float maximum, float loss)
        {
            if (_durability == null) return;
            float ratio = maximum > 0f ? current / maximum : 0f;
            if (_durabilityFill != null)
            {
                _durabilityFill.fillAmount = ratio;
                _durabilityFill.color = _durabilityColor.Evaluate(ratio);
            }
            if (_durabilityLoss != null) _durabilityLoss.fillAmount = loss;
            if (_durabilityZero != null) _durabilityZero.SetActive(current <= 0f);
            _durability.SetActive(true);
        }

        // 풀로 돌아간 칸이나 닫힌 창의 칸이 앞 의도를 보내지 않게 연결을 끊는다. 표시는 다음 Bind가 덮거나 줄 모음이 숨긴다.
        internal void ResetBinding()
        {
            _clicked = null;
            _itemId = null;
            HideHover();
        }

        // 원작 ItemDisplay.OnPointerClick의 ItemUIUtilities.Select다. 빈 칸과 클릭을 받지 않는 칸은 무시한다.
        public void OnPointerClick(PointerEventData data)
        {
            if (_clicked == null || _itemId == null || data.button != PointerEventData.InputButton.Left) return;
            _clicked(_itemId);
        }
    }
}
