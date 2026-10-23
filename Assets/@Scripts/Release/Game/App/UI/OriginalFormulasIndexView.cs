#nullable enable annotations
using System;
// 닫힌 창의 버튼 입력 확인에 프로젝트 Log를 쓴다.
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 Duckov.UI.FormulasIndexView(GameplayUICanvas/FormulasIndexView, 레시피 도감 창)다. 왼쪽에 제조식 칸 격자(OriginalFormulasIndexEntryView),
    // 오른쪽에 고른 제조식 결과의 이름, 설명, 아이콘, 비용(원작 FormulasDetailsDisplay, 비용은 OriginalCostDisplayView 재사용)을 보인다.
    // 값만 받고 의도 이벤트만 낸다. 목록, 해금 판정, 비용 판정, 열고 닫기, 입력 차단은 화면 컨트롤러(FormulasIndexScreen)가 맡는다.
    // 원작 FadeGroup 전환 대신 활성 여부로 보이고 숨긴다(열쇠 도감 선례, 원작 차이). 닫기 버튼은 저장된 Runtime Only OnClick이 부르고, 칸 클릭만 코드로 받는다.
    public sealed class OriginalFormulasIndexView : MonoBehaviour
    {
        // 원작 entryTemplate이다. 꺼진 채 원본 칸 격자 안에 있고, 칸은 원작 PrefabPool처럼 템플릿과 같은 부모 아래에 복제한다.
        [SerializeField] OriginalFormulasIndexEntryView _entryTemplate = null!;
        // 원작 FormulasDetailsDisplay의 contentFadeGroup과 placeHolderFadeGroup 루트, nameText, descriptionText, image, costDisplay, unknownImage다.
        [SerializeField] GameObject _content = null!;
        [SerializeField] GameObject _placeholder = null!;
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] TMP_Text _description = null!;
        [SerializeField] Image _image = null!;
        [SerializeField] OriginalCostDisplayView _cost = null!;
        [SerializeField] Sprite _unknownImage = null!;

        // 칸 모음은 처음 쓸 때 만든다. EditMode 테스트와 공용 HUD 초기화가 Awake 없이 부르기 때문이다.
        UiTemplateCollection<OriginalFormulasIndexEntryView>? _entries;
        // 칸 클릭을 이 View의 의도 이벤트로 넘기는 대리자다. 칸마다 할당하지 않도록 한 번만 만든다.
        Action<string>? _selectEntry;

        // 원작 OnEntryClicked(칸 클릭)와 View.exitButton이다. 칸은 제조식 ID를 넘긴다.
        internal event Action<string>? EntrySelected;
        internal event Action? CloseRequested;

        internal bool IsShown => gameObject.activeSelf;
        internal OriginalCostDisplayView Cost => _cost;

        UiTemplateCollection<OriginalFormulasIndexEntryView> Entries =>
            _entries ??= new UiTemplateCollection<OriginalFormulasIndexEntryView>(_entryTemplate, static entry => entry.ResetBinding());

        Action<string> SelectEntry => _selectEntry ??= formulaId => EntrySelected?.Invoke(formulaId);

        // 원작 OnOpen의 fadeGroup.Show다. 목록과 상세는 곧바로 컨트롤러가 채운다.
        internal void Show()
        {
            ShowNothing();
            gameObject.SetActive(true);
        }

        // 원작 OnClose다. 공용 HUD 초기화도 무조건 부르므로 숨어 있어도 된다. 칸 연결을 모두 끊어 닫힌 뒤 눌린 칸이 의도를 보내지 않게 한다.
        internal void Hide()
        {
            Entries.Clear();
            gameObject.SetActive(false);
        }

        // 원작 OnOpen의 Pool.ReleaseAll과 Pool.Get().Setup이다. 컨트롤러가 제조식 모음 순서로 AddEntry를 부른 뒤 EndEntries로 닫는다.
        internal void BeginEntries() => Entries.Begin();

        internal void AddEntry(string formulaId, bool valid, bool unlocked, string resultName, int amount, Sprite? icon)
        {
            OriginalFormulasIndexEntryView entry = Entries.Acquire(_entryTemplate.transform.parent);
            entry.Bind(SelectEntry, formulaId, valid, unlocked, resultName, amount, icon);
            entry.gameObject.SetActive(true);
        }

        internal void EndEntries() => Entries.End();

        // 원작 FormulasDetailsDisplay.SetupEmpty다. 고른 칸이 없거나 결과 ID가 음수인 칸을 골랐으면 자리표시를 켠다.
        internal void ShowNothing()
        {
            _content.SetActive(false);
            _placeholder.SetActive(true);
        }

        // 원작 SetupUnknown이다. 해금하지 않은 제조식은 ???와 unknownImage를 보인다. 비용은 원작 Setup(default(Cost))처럼
        // 돈 줄과 아이템 줄을 모두 숨기고, 빈 비용은 원작 Cost.Enough와 돈 비교가 늘 참이라 넉넉한 색으로 칠한다.
        internal void ShowUnknown()
        {
            _name.text = "???";
            _description.text = "???";
            _image.sprite = _unknownImage;
            _cost.BeginItems(0, true, true, false);
            _cost.EndItems();
            ShowContent();
        }

        // 원작 SetupFormula와 RefreshContent의 이름, 설명, 아이콘이다. 비용은 컨트롤러가 이어서 Cost로 채운다.
        internal void ShowFormula(string resultName, string description, Sprite? icon)
        {
            _name.text = resultName;
            _description.text = description;
            _image.sprite = icon;
            ShowContent();
        }

        // 원작 View.exitButton이다. 닫기는 컨트롤러가 입력 차단과 함께 정한다.
        public void OnExitClicked()
        {
            Log.Instance.Assert(IsShown, "닫힌 레시피 도감의 닫기가 눌렸습니다.", this);
            CloseRequested?.Invoke();
        }

        void ShowContent()
        {
            _placeholder.SetActive(false);
            _content.SetActive(true);
        }
    }
}
