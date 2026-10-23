#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 CraftView다. 분류 단추 줄(OriginalCraftFilterButtonView), 제조식 목록(OriginalCraftListEntryView), 고른 결과의 상세(OriginalItemDetailsView 재사용),
    // 비용(OriginalCostDisplayView 재사용), 제조 단추와 그 색, 닫기 단추를 보인다. 원작 filters 9개는 이 View에 직렬화한다(S6 4.7절).
    // 값만 받고 의도 이벤트만 낸다. 목록 거르기, 지불 가능 판정, 제조 명령, 알림, 열고 닫기, 입력 차단은 화면 컨트롤러(CraftScreen)가 맡는다.
    // 원작 FadeGroup 전환은 옮기지 않고 활성 여부로 보인다(OriginalItemRepairView 선례, 원작 차이). 결과 아이템을 비동기로 만들지 않으므로
    // 원작 loadingIndicator는 제작 도구가 꺼 둔다.
    public sealed class OriginalCraftView : MonoBehaviour
    {
        // 원작 FilterInfo다. 이름 키, 아이콘, 결과 아이템이 하나라도 가져야 하는 태그(원본 Tag 자산 이름)다. 태그가 없으면 모든 제조식이 든다.
        [Serializable]
        internal sealed class Filter
        {
            [SerializeField] string _nameKey = "";
            [SerializeField] Sprite? _icon;
            [SerializeField] string[] _requireTags = Array.Empty<string>();

            internal string NameKey => _nameKey;
            internal Sprite? Icon => _icon;
            internal IReadOnlyList<string> RequireTags => _requireTags;
        }

        // 원작 listEntryTemplate과 filterBtnTemplate이다. 둘 다 꺼진 채 원본 자리에 있고, 줄은 템플릿과 같은 부모 아래에 복제한다.
        [SerializeField] OriginalCraftListEntryView _entryTemplate = null!;
        [SerializeField] OriginalCraftFilterButtonView _filterTemplate = null!;
        [SerializeField] Filter[] _filters = Array.Empty<Filter>();
        // 원작 detailsFadeGroup(상세, 비용, 제조 단추 묶음)과 placeHolderFadeGroup의 루트다. 고른 것이 있으면 앞을, 없으면 뒤를 켠다.
        [SerializeField] GameObject _detailsGroup = null!;
        [SerializeField] GameObject _placeholder = null!;
        [SerializeField] OriginalItemDetailsView _details = null!;
        [SerializeField] OriginalCostDisplayView _cost = null!;
        // 원작 buttonImage, crafableColor, notCraftableColor다. 비용을 낼 수 있으면 앞 색으로 제조 단추를 칠한다.
        [SerializeField] Image _buttonImage = null!;
        [SerializeField] Color _craftableColor;
        [SerializeField] Color _notCraftableColor;

        // 줄 모음은 처음 쓸 때 만든다. EditMode 테스트와 공용 HUD 초기화가 Awake 없이 부르기 때문이다.
        UiTemplateCollection<OriginalCraftListEntryView>? _entries;
        UiTemplateCollection<OriginalCraftFilterButtonView>? _filterButtons;
        // 줄 클릭을 이 View의 의도 이벤트로 넘기는 대리자다. 줄마다 할당하지 않도록 한 번만 만든다.
        Action<string>? _selectEntry;
        Action<int>? _selectFilter;

        // 원작 SetSelection(목록 줄 클릭), SetFilter(분류 단추 클릭), 제조 단추, 닫기 단추다. 줄은 제조식 ID를, 분류 단추는 분류 번호를 넘긴다.
        internal event Action<string>? EntrySelected;
        internal event Action<int>? FilterSelected;
        internal event Action? CraftRequested;
        internal event Action? CloseRequested;

        internal IReadOnlyList<Filter> Filters => _filters;
        internal OriginalItemDetailsView Details => _details;
        internal OriginalCostDisplayView Cost => _cost;

        UiTemplateCollection<OriginalCraftListEntryView> Entries =>
            _entries ??= new UiTemplateCollection<OriginalCraftListEntryView>(_entryTemplate, static entry => entry.ResetBinding());

        UiTemplateCollection<OriginalCraftFilterButtonView> FilterButtons =>
            _filterButtons ??= new UiTemplateCollection<OriginalCraftFilterButtonView>(_filterTemplate, static button => button.ResetBinding());

        Action<string> SelectEntry => _selectEntry ??= formulaId => EntrySelected?.Invoke(formulaId);
        Action<int> SelectFilter => _selectFilter ??= index => FilterSelected?.Invoke(index);

        // 원작 SetupAndOpen의 detailsFadeGroup.SkipHide, placeHolderFadeGroup.SkipShow와 Open이다. 곧바로 컨트롤러가 다시 그린다.
        internal void Show()
        {
            ShowNothingSelected();
            gameObject.SetActive(true);
        }

        // 원작 OnClose다. 공용 HUD 초기화도 무조건 부르므로 숨어 있어도 된다. 줄 연결을 모두 끊어 닫힌 뒤 눌린 줄이 의도를 보내지 않게 한다.
        internal void Hide()
        {
            Entries.Clear();
            FilterButtons.Clear();
            gameObject.SetActive(false);
        }

        // 원작 RefreshFilterButtons다. 항목이 하나 이상인 분류만 AddFilter로 넣고 EndFilters로 닫는다.
        internal void BeginFilters() => FilterButtons.Begin();

        internal void AddFilter(int index, string displayName, Sprite? icon, bool selected) =>
            FilterButtons.Acquire(_filterTemplate.transform.parent).Bind(SelectFilter, index, displayName, icon, selected);

        internal void EndFilters() => FilterButtons.End();

        // 원작 RefreshList다. 제조식 모음 순서로 AddEntry를 부른 뒤 EndEntries로 닫는다.
        internal void BeginEntries() => Entries.Begin();

        internal void AddEntry(string formulaId, Sprite? icon, string label, bool enough, bool selected) =>
            Entries.Acquire(_entryTemplate.transform.parent).Bind(SelectEntry, formulaId, icon, label, enough, selected);

        internal void EndEntries() => Entries.End();

        // 원작 RefreshTask의 고른 것 없음 경로다. 상세 묶음을 끄고 자리표시를 켠다.
        internal void ShowNothingSelected()
        {
            _detailsGroup.SetActive(false);
            _placeholder.SetActive(true);
        }

        // 원작 RefreshTask의 고른 제조식 경로다. 상세와 비용은 이 뒤에 컨트롤러가 Details와 Cost로 채운다.
        internal void ShowSelected(bool craftable)
        {
            _placeholder.SetActive(false);
            _detailsGroup.SetActive(true);
            _buttonImage.color = craftable ? _craftableColor : _notCraftableColor;
        }

        // 고정 버튼 두 개는 제작 도구가 Runtime Only 영속 OnClick으로 이 공개 핸들러에 연결한다(Docs/rules/ui.md).
        public void OnCraftClicked() => CraftRequested?.Invoke();

        public void OnExitClicked() => CloseRequested?.Invoke();
    }
}
