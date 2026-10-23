#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 ItemRepair_RepairAllPanel(수리 창 Content/VertLayout/RepairAll)이다. 고칠 장비 목록, 가격 합계, 모두 수리 단추, 빈 목록 자리표시를 보인다.
    // 값만 받고 단추 의도만 낸다. 목록과 가격 규칙은 RepairRules.RepairAll, 명령은 RepairScreen이 맡는다.
    public sealed class OriginalRepairAllPanelView : MonoBehaviour
    {
        // 원작 priceDisplay, itemDisplayTemplate, button, placeholder다. 템플릿은 원작 Awake처럼 꺼진 채 목록 부모 안에 있다.
        [SerializeField] TMP_Text _price = null!;
        [SerializeField] OriginalItemRepairCellView _entryTemplate = null!;
        [SerializeField] Button _button = null!;
        [SerializeField] GameObject _placeholder = null!;

        // 줄 모음은 처음 쓸 때 만든다. EditMode 테스트와 공용 HUD 초기화가 Awake 없이 부르기 때문이다.
        UiTemplateCollection<OriginalItemRepairCellView>? _entries;
        int _count;

        // 원작 OnButtonClicked다. 수리 화면 컨트롤러가 받아 GameplayCommands.TryRepairAll을 부른다.
        internal event Action? RepairAllRequested;

        UiTemplateCollection<OriginalItemRepairCellView> Entries =>
            _entries ??= new UiTemplateCollection<OriginalItemRepairCellView>(_entryTemplate, static entry => entry.ResetBinding());

        // 원작 Refresh의 Pool.ReleaseAll이다. AddEntry 뒤 End로 닫는다.
        internal void Begin()
        {
            _count = 0;
            Entries.Begin();
        }

        // 원작 Pool.Get().Setup(item)이다. 목록 칸은 클릭을 받지 않고 아이콘, 이름, 수량, 내구도 막대만 보인다.
        internal void AddEntry(Sprite? icon, string itemName, int count, float current, float maximum, float loss)
        {
            OriginalItemRepairCellView entry = Entries.Acquire(_entryTemplate.transform.parent);
            entry.Bind(null, "", icon, itemName, count, false);
            entry.ShowDurability(current, maximum, loss);
            _count++;
        }

        // 원작 Refresh의 나머지다. 목록이 비면 자리표시를 켜고 단추를 끈다. 아니면 지불할 수 있을 때만 단추를 켠다. 가격은 비어도 0을 보인다.
        internal void End(long price, bool affordable)
        {
            Entries.End();
            bool listed = _count > 0;
            _placeholder.SetActive(!listed);
            _button.interactable = listed && affordable;
            _price.text = price.ToString();
        }

        // 수리 창을 닫거나 공용 HUD를 준비할 때 칸 연결을 끊고 숨긴다. 여러 번 불러도 된다.
        internal void Clear()
        {
            _count = 0;
            Entries.Clear();
        }

        // 고정 버튼은 제작 도구가 Runtime Only 영속 OnClick으로 이 공개 핸들러에 연결한다(Docs/rules/ui.md).
        public void OnRepairAllClicked() => RepairAllRequested?.Invoke();
    }
}
