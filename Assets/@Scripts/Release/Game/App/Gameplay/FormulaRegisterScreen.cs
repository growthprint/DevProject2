#nullable enable annotations
using System;
using System.Collections.Generic;

namespace Game.App
{
    // 원작 FormulasRegisterView(설계도 연구)다. 등록 칸 흐름은 등록 화면 바탕(ItemRegisterScreen)이 맡고, 이 화면은 등록대 태그,
    // 설계도 판정, 등록 명령과 해금 알림, 도감 단추만 정한다. 설계도를 등록 칸으로 옮기고, 등록 버튼이 그 설계도를 제출한다.
    internal sealed class FormulaRegisterScreen : ItemRegisterScreen
    {
        // 원작 등록 칸(keySlotItem의 SubmitItem 칸)의 표시 이름은 첫 requireTags인 formulaTag(Formula)의 표시 이름이다.
        const string _slotLabelKey = "Tag_Formula";
        // 원작 formulaUnlockedFormatKey 직렬화 값이다. {itemDisplayName}에 결과 아이템 이름이 들어간다.
        const string _succeedFormatKey = "UI_Formulas_RegisterSucceedFormat";

        // 연 등록대의 원작 additionalTags다. 칸 판정과 등록 명령이 Formula 태그와 함께 요구한다. 창이 열린 동안만 쓴다.
        IReadOnlyList<string> _stationTags = Array.Empty<string>();

        // 월드 문맥과 상세 규칙은 등록 화면 바탕이 가진다.
        internal FormulaRegisterScreen(GameplayWorldContext world) : base(world)
        {
        }

        // 원작 도감 단추(ViewsProxy.ShowFormulasIndexView)의 의도다. 화면 순서를 정하는 GameplayScreens가 받아 이 창을 닫고 도감을 연다.
        internal event Action? IndexRequested;

        // 공용 HUD의 설계도 연구 창과 그 등록 칸 이름이다.
        private protected override OriginalItemRegisterView View => _world.Hud.FormulasRegister;
        private protected override string SlotLabelKey => _slotLabelKey;

        // 원작 FormulasRegisterView.Show(requireTags)와 OnOpen이다. 공용 HUD가 이 월드에 붙어 있지 않거나 입력 차단을 못 받으면(정리 중) 열지 않는다.
        // 등록대 태그는 다시 열기 전 닫기가 비운 뒤, 첫 그리기 전에 정한다. 호출자: GameplayScreens.ShowFormulaRegister(FormulasRegisterViewInvoker).
        internal void Show(FormulasRegisterViewInvoker invoker)
        {
            if (!TryBeginShow(_world, out GameSessionManager? session)) return;
            _stationTags = invoker.AdditionalTags;
            Open(session);
        }

        // 도감 단추 의도도 열린 동안만 듣는다. 해금 변경은 제조 소유자 알림으로 받는다.
        private protected override void BindOwnerEvents(UiEventSubscriptions subscriptions, Action redraw)
        {
            OriginalFormulasRegisterView view = _world.Hud.FormulasRegister;
            Action index = RequestIndex;
            view.IndexRequested += index;
            subscriptions.Add(() => view.IndexRequested -= index);
            CraftingManager.Changed += redraw;
            subscriptions.Add(() => CraftingManager.Changed -= redraw);
        }

        // 등록대 태그는 연 창에서만 쓰므로 닫을 때 비운다.
        private protected override void ResetOpenState() => _stationTags = Array.Empty<string>();

        // 칸 판정은 테스트도 쓰는 정적 판정과 같다.
        private protected override bool IsOperable(OriginalItemDefinition definition, out bool highlight) =>
            Operable(GameManager.Crafting, definition, _stationTags, out highlight);

        // 원작 RefreshRecordExistsIndicator는 칸 설계도의 제조식이 이미 해금됐는지 본다.
        private protected override bool IsRecorded(OriginalItemDefinition definition) => GameManager.Crafting.IsItemRegistered(definition.Id);

        // 원작 OnSubmitButtonClicked의 제출이다. 명령이 확인, 설계도 제거, 해금을 한 전이로 확정한다. 이미 해금한 설계도처럼 거절되면
        // 원작처럼 화면과 소리는 그대로다. 원작 결함 그대로 없는 제조식을 가리키는 설계도는 사라지고 해금 알림 없이 성공 표시와 소리만 낸다.
        private protected override bool TryRegister(GameSessionManager session, string itemId, out string? notice)
        {
            notice = null;
            OriginalGameplayCatalog catalog = _world.Catalog;
            if (!GameplayCommands.TryRegisterFormula(session, GameManager.Crafting, catalog, itemId, _stationTags, out CraftingFormula? unlocked)) return false;
            // 원작 StrongNotification(결과 아이템 아이콘이 있는 큰 알림)이 제품에 없어 공용 HUD 알림 대기열에 같은 문구를 넣는다(원작 차이).
            // 원작은 결과 아이템 메타데이터의 이름을 쓴다. 결과 정의가 카탈로그에 없으면 원작 IDValid 거짓처럼 알리지 않는다.
            if (unlocked != null && catalog.TryGet(unlocked.ResultId, out OriginalItemDefinition? result))
                notice = SucceedText(GameLocalization.Text(_succeedFormatKey), GameLocalization.Text(result.NameKey));
            return true;
        }

        // 원작 EntryFunc_CanOperate(칸 태그 판정)를 돌려주고 EntryFunc_ShouldHighligh를 highlight로 낸다. 아직 해금하지 않은 설계도만 강조한다.
        // 이미 해금한 설계도는 강조하지 않지만 칸에는 넣을 수 있다. 없는 제조식을 가리키는 설계도는 해금될 수 없어 늘 강조된다(원작 그대로).
        internal static bool Operable(CraftingManager crafting, OriginalItemDefinition definition, IReadOnlyList<string> stationTags, out bool highlight)
        {
            bool registrable = CraftingManager.IsRegistrable(definition, stationTags);
            highlight = registrable && !crafting.IsItemRegistered(definition.Id);
            return registrable;
        }

        // 원작 FormulaUnlockedNotificationFormat.Format(new { itemDisplayName })다. 번역 문구의 자리표시 하나만 바꾸는 규칙은
        // 제조 완료 알림과 같은 현지화 도우미가 맡는다.
        internal static string SucceedText(string format, string itemName) => GameLocalization.WithItemName(format, itemName);

        // 원작 도감 단추다. 원작 View.OnOpen이 연구 창을 닫듯, 받는 쪽이 이 창을 닫으므로(칸 설계도는 Close가 가방으로 돌려보낸다) 여기서는 의도만 넘긴다.
        void RequestIndex()
        {
            if (!IsOpen) return;
            IndexRequested?.Invoke();
        }
    }
}
