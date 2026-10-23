#nullable enable annotations
using System;
// 단추 핸들러의 상태 확인을 프로젝트 Log로 남긴다.
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 탭 단추 줄(GameplayUICanvas/Tabs/ViewButtons)의 단추다. 값은 원작 단추 순서(BackpackButton, PlayerStatsButton, QuestsButton,
    // MapButton, KeyButton, NoteIndexButton)이고 OriginalViewButtonsView._indicators의 색인이다. None은 강조할 탭 화면이 없다는 뜻이다.
    public enum ViewTab
    {
        None = -1,
        Inventory = 0,
        PlayerStats = 1,
        Quest = 2,
        Map = 3,
        Key = 4,
        Notes = 5,
    }

    // 원작 GameplayUICanvas/Tabs/ViewButtons 탭 단추 줄이다. 원작 GenericButton.onPointerClick → ViewsProxy.Show*처럼 단추가 눌리면 그 탭만
    // 의도로 알리고, 원작 ViewTabDisplayEntry처럼 컨트롤러가 넘긴 지금 탭의 단추에만 SelectionIndicator를 켠다. 어떤 화면을 닫고 열지는
    // 공용 HUD를 붙인 월드 컨트롤러(OriginalGameplayController, 프롤로그는 가방 단추만 받는 OriginalPrologueController)가 정한다.
    // 단추 줄은 Tabs 아래라 탭 화면(인벤토리, 쪽지, 일지, 열쇠 도감)이 열린 동안에만 보인다.
    // 고정 단추는 저장된 Runtime Only OnClick이 아래 핸들러를 부른다(Docs/rules/ui.md). 제품 화면이 없는 능력치 단추만
    // 핸들러 없이 누를 수 없는 Button으로 둔다(제작 도구 SharedHudAuthoring.ViewButtons).
    // 쪽지 단추도 제품 쪽지 창(NoteScreen)이 있어 "제품 화면이 있는 단추는 잇는다"는 사용자 규칙대로 잇는다.
    public sealed class OriginalViewButtonsView : MonoBehaviour
    {
        // 원작 ViewTabDisplayEntry.indicator(각 단추의 SelectionIndicator)를 ViewTab 순서로 둔다. 원작 Awake의 HideIndicator처럼 꺼진 채 저장한다.
        [SerializeField] GameObject[] _indicators = Array.Empty<GameObject>();

        // 마지막으로 켠 탭이다. 저장된 표시는 모두 꺼져 있으므로 None에서 시작하고, 바뀔 때만 SetActive를 부른다.
        ViewTab _selected = ViewTab.None;

        public event Action<ViewTab>? TabRequested;

        // 원작 ViewTabDisplayEntry.OnViewOpen과 OnViewClose다. 컨트롤러가 매 프레임 지금 탭을 넘기므로 같은 값이면 아무것도 쓰지 않는다.
        // 원작 ShowIndicator의 PunchReceiver 흔들림은 옮기지 않는다(원작 차이). None(-1)은 어느 색인과도 같지 않아 모두 끈다.
        internal void SetSelected(ViewTab tab)
        {
            if (tab == _selected) return;
            _selected = tab;
            UiVisibility.ShowOnly(_indicators, (int)tab);
        }

        // 원작 BackpackButton(ViewsProxy.ShowInventoryView), QuestsButton(ShowQuestView), MapButton(ShowMapView), KeyButton(ShowKeyView)이다.
        public void OnInventoryClicked() => Request(ViewTab.Inventory);
        public void OnQuestClicked() => Request(ViewTab.Quest);
        public void OnMapClicked() => Request(ViewTab.Map);
        public void OnKeyClicked() => Request(ViewTab.Key);
        // 원작 NoteIndexButton(NoteIndexView.DoOpen)이다. 제품 쪽지 창이 있어 다른 단추처럼 의도만 알린다.
        public void OnNotesClicked() => Request(ViewTab.Notes);

        // 입력은 단추 줄이 보이는 동안에만 온다(Docs/rules/ui.md의 핸들러 상태 확인). 클릭마다 문자열을 만들지 않게 메시지는 고정한다.
        void Request(ViewTab tab)
        {
            Log.Instance.Assert(gameObject.activeInHierarchy, "숨은 탭 단추 줄의 단추가 눌렸습니다.", this);
            TabRequested?.Invoke(tab);
        }
    }
}
