#nullable enable annotations
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 LootView/ItemDetails의 ItemDetailsDisplay다. 원작 순서대로 공통 정보(이름, ID, 무게, 사용과 단축키 표시, 아이콘, 수량, 내구도,
    // 설명, 태그), 아이템 칸(총 부착 칸), 속성 목록(탄종, 변수, 상수, 수치, modifier)을 보인다. 문구와 값은 소유자가 정하고 이 View는 표시만 한다.
    // 원작 UsageUtilitiesDisplay와 ItemEffectEntry는 이 게임에서 채우지 않는다. 직렬화 필드 이름은 SharedHUD 저작 도구가 쓴다.
    public sealed class OriginalItemDetailsView : MonoBehaviour
    {
        // 원본 detailsFadeGroup의 CanvasGroup과 CanvasGroupFade.fadeDuration이다. 시간 하한은 다른 페이드 View와 같은 UiFade 값이다.
        [SerializeField] CanvasGroup _group = null!;
        [SerializeField, Min(UiFade._minimumDuration)] float _fadeDuration = .15f;
        [SerializeField] Image _icon = null!;
        [SerializeField] TMP_Text _displayName = null!, _itemId = null!, _description = null!, _weight = null!;
        [SerializeField] GameObject _countContainer = null!;
        [SerializeField] TMP_Text _count = null!;
        [SerializeField] GameObject _durabilityContainer = null!;
        [SerializeField] TMP_Text _durabilityText = null!;
        [SerializeField] Image _durabilityFill = null!, _durabilityLoss = null!;
        // 원본 durabilityColorOverT다. 남은 내구도 비율로 채움 색을 고른다.
        [SerializeField] Gradient _durabilityColor = new Gradient();
        [SerializeField] GameObject _usable = null!, _registered = null!;
        [SerializeField] RectTransform _tags = null!;
        [SerializeField] OriginalItemTagEntry _tagTemplate = null!;
        // 원본 ItemSlotCollectionDisplay의 칸 격자와 칸 템플릿(SlotDisplay 프리팹)이다.
        [SerializeField] RectTransform _slotGrid = null!;
        [SerializeField] OriginalSlotCell _slotTemplate = null!;
        // 원본 propertiesParent와 BulletTypeDisplay, 세 줄 템플릿(원본 ItemVariableEntry, ItemStatEntry, ItemModifierEntry 프리팹)이다.
        [SerializeField] RectTransform _properties = null!;
        [SerializeField] GameObject _bullet = null!;
        [SerializeField] TMP_Text _bulletName = null!;
        [SerializeField] OriginalItemPropertyEntry _variableTemplate = null!, _statTemplate = null!, _modifierTemplate = null!;
        // 원본 ItemDetailsDisplay.weightFormat이다.
        [SerializeField] string _weightFormat = "{0:0.#} kg";

        UiTemplateCollection<OriginalItemTagEntry>? _tagEntries;
        UiTemplateCollection<OriginalSlotCell>? _slotCells;
        UiTemplateCollection<OriginalItemPropertyEntry>? _variables, _stats, _modifiers;
        // 이번 표시에 쓴 아이템 칸 셀이다. 끌기 중 꽂을 수 있는 칸 표시와 거절 깜박임이 칸 번호로 찾는다.
        readonly List<OriginalSlotCell> _shownSlots = new List<OriginalSlotCell>();
        float _alphaTarget;
        bool _fading;

        // 원본 FadeGroup처럼 보이는 중인지다. 숨기는 페이드 중이면 거짓이다.
        internal bool Shown => gameObject.activeSelf && _alphaTarget > 0f;
        internal IReadOnlyList<OriginalSlotCell> Slots => _shownSlots;
        internal string WeightFormat => _weightFormat;

        // 셀 풀은 EditMode 테스트가 Awake 없이 부르므로 처음 쓸 때 만든다. 칸 셀만 풀로 돌아갈 때 연결을 끊는다.
        UiTemplateCollection<OriginalItemTagEntry> TagEntries => _tagEntries ??= new UiTemplateCollection<OriginalItemTagEntry>(_tagTemplate);
        UiTemplateCollection<OriginalSlotCell> SlotCells =>
            _slotCells ??= new UiTemplateCollection<OriginalSlotCell>(_slotTemplate, cell => cell.ResetBinding());
        UiTemplateCollection<OriginalItemPropertyEntry> Variables =>
            _variables ??= new UiTemplateCollection<OriginalItemPropertyEntry>(_variableTemplate);
        UiTemplateCollection<OriginalItemPropertyEntry> Stats => _stats ??= new UiTemplateCollection<OriginalItemPropertyEntry>(_statTemplate);
        UiTemplateCollection<OriginalItemPropertyEntry> Modifiers =>
            _modifiers ??= new UiTemplateCollection<OriginalItemPropertyEntry>(_modifierTemplate);

        // 새 아이템 표시를 시작한다. 앞 표시의 칸 셀은 연결을 끊어 앞 아이템의 명령을 부르지 않게 하고, 이번에 채우지 않은 항목은 End가 숨긴다.
        internal void Begin(Sprite? icon, string displayName, int typeId, string description, string weight, bool usable, bool registered)
        {
            TagEntries.Begin(); SlotCells.Begin(); Variables.Begin(); Stats.Begin(); Modifiers.Begin();
            _shownSlots.Clear();
            // 아이콘이 없는 아이템은 빈 그림을 그리지 않도록 HUD 칸과 같은 도우미로 끈다.
            UiVisibility.SetIcon(_icon, icon);
            _displayName.text = displayName;
            _itemId.text = "#" + typeId;
            _description.text = description;
            _weight.text = weight;
            UiVisibility.Set(_usable, usable);
            UiVisibility.Set(_registered, registered);
            UiVisibility.Set(_countContainer, false);
            UiVisibility.Set(_durabilityContainer, false);
            UiVisibility.Set(_bullet, false);
        }

        // 원본처럼 쌓이는 아이템만 수량을 보인다.
        internal void ShowCount(int count)
        {
            UiVisibility.Set(_countContainer, true);
            _count.text = count.ToString();
        }

        // 원본 RefreshDurability다. 글자는 현재값과 손실을 뺀 최대치, 채움은 최대치 대비 비율, 손실 막대는 손실 비율이다.
        internal void ShowDurability(float current, float maximum, float loss)
        {
            UiVisibility.Set(_durabilityContainer, true);
            float ratio = maximum > 0f ? current / maximum : 0f;
            _durabilityText.text = current.ToString("0") + " / " + (maximum * (1f - loss)).ToString("0");
            _durabilityFill.fillAmount = ratio;
            _durabilityFill.color = _durabilityColor.Evaluate(ratio);
            _durabilityLoss.fillAmount = loss;
        }

        // 원본 태그 줄 하나다. 줄 풀은 처음 쓸 때 만든다.
        internal void AddTag(string text, Color color)
        {
            OriginalItemTagEntry entry = TagEntries.Acquire(_tags);
            entry.Show(text, color);
            entry.gameObject.SetActive(true);
        }

        // 아이템 칸 셀 하나를 꺼낸다. 칸 연결과 표시는 인벤토리 패널이 맡는다.
        internal OriginalSlotCell AddSlot()
        {
            OriginalSlotCell cell = SlotCells.Acquire(_slotGrid);
            cell.gameObject.SetActive(true);
            _shownSlots.Add(cell);
            return cell;
        }

        // 원본 BulletTypeDisplay다. 총일 때만 보인다.
        internal void ShowBullet(string name)
        {
            UiVisibility.Set(_bullet, true);
            _bulletName.text = name;
        }

        // 원본처럼 줄을 만든 순서대로 속성 목록 끝에 둔다. 줄 풀은 종류마다 처음 쓸 때 만든다.
        internal void AddVariable(string name, string value) => Place(Variables).Show(name, value);
        internal void AddStat(string name, string value) => Place(Stats).Show(name, value);
        internal void AddModifier(string name, string value, float signedValue, OriginalStatPolarity polarity) =>
            Place(Modifiers).ShowModifier(name, value, signedValue, polarity);

        OriginalItemPropertyEntry Place(UiTemplateCollection<OriginalItemPropertyEntry> entries)
        {
            OriginalItemPropertyEntry entry = entries.Acquire(_properties);
            entry.transform.SetAsLastSibling();
            entry.gameObject.SetActive(true);
            return entry;
        }

        // 이번에 채우지 않은 항목을 숨기고 원본 detailsFadeGroup.Show처럼 페이드로 보인다.
        internal void End()
        {
            TagEntries.End(); SlotCells.End(); Variables.End(); Stats.End(); Modifiers.End();
            if (!gameObject.activeSelf)
            {
                _group.alpha = 0f;
                gameObject.SetActive(true);
            }
            _group.blocksRaycasts = true;
            _alphaTarget = 1f;
            _fading = _group.alpha != 1f;
        }

        // 원본 detailsFadeGroup.Hide다. immediate면 인벤토리를 닫을 때처럼 페이드 없이 끈다. 칸 셀 연결은 reset으로 끊는다.
        internal void Hide(bool immediate)
        {
            _slotCells?.Clear();
            _shownSlots.Clear();
            _alphaTarget = 0f;
            _group.blocksRaycasts = false;
            if (immediate || !gameObject.activeInHierarchy)
            {
                _group.alpha = 0f;
                _fading = false;
                UiVisibility.Set(gameObject, false);
                return;
            }
            _fading = _group.alpha != 0f;
            if (!_fading) gameObject.SetActive(false);
        }

        // 페이드가 없으면 바로 돌아간다. 원작 FadeGroup처럼 시간 배율과 무관하게 진행하고, 다 숨으면 끈다.
        // 한 프레임 알파 이동은 다른 페이드 View와 같은 UiFade 규칙이다.
        void Update()
        {
            if (!_fading) return;
            float alpha = UiFade.Step(_group.alpha, _alphaTarget, Time.unscaledDeltaTime, _fadeDuration);
            _group.alpha = alpha;
            _fading = alpha != _alphaTarget;
            if (!_fading && _alphaTarget == 0f) gameObject.SetActive(false);
        }
    }
}
