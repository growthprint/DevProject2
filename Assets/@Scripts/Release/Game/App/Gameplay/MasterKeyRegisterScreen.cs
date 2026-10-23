#nullable enable annotations
using System;

namespace Game.App
{
    // 원작 MasterKeysRegisterView(열쇠 등록)다. 등록 칸 흐름은 등록 화면 바탕(ItemRegisterScreen)이 맡고, 이 화면은 열쇠 판정,
    // 등록 명령, 등록 소유자 알림만 정한다. 원작처럼 열쇠를 등록 칸으로 옮겨 담고, 등록 버튼이 그 열쇠 1개를 써서 등록한다.
    internal sealed class MasterKeyRegisterScreen : ItemRegisterScreen
    {
        // 원작 등록 칸(keySlotItem의 Key 칸)의 표시 이름은 첫 requireTags인 Key 태그의 표시 이름이다.
        const string _slotLabelKey = "Tag_Key";

        // 월드 문맥과 상세 규칙은 등록 화면 바탕이 가진다.
        internal MasterKeyRegisterScreen(GameplayWorldContext world) : base(world)
        {
        }

        // 공용 HUD의 열쇠 등록 창과 그 등록 칸 이름이다.
        private protected override OriginalItemRegisterView View => _world.Hud.MasterKeysRegister;
        private protected override string SlotLabelKey => _slotLabelKey;

        // 원작 MasterKeysRegisterView.Show와 OnOpen이다. 공용 HUD가 이 월드에 붙어 있지 않거나 입력 차단을 못 받으면(정리 중) 열지 않는다.
        // 호출자: GameplayScreens.ShowMasterKeyRegister(Building_MasterKey 안의 MasterKeyRegisterViewInvoker).
        internal void Show()
        {
            if (!TryBeginShow(_world, out GameSessionManager? session)) return;
            Open(session);
        }

        // 등록 변경은 열쇠 등록 소유자 알림으로 받는다.
        private protected override void BindOwnerEvents(UiEventSubscriptions subscriptions, Action redraw)
        {
            MasterKeysManager.Changed += redraw;
            subscriptions.Add(() => MasterKeysManager.Changed -= redraw);
        }

        // 원작처럼 열쇠만 칸에 넣을 수 있고, 아직 등록하지 않은 열쇠만 강조한다.
        private protected override bool IsOperable(OriginalItemDefinition definition, out bool highlight)
        {
            bool registrable = MasterKeysManager.IsRegistrable(definition);
            highlight = registrable && !GameManager.MasterKeys.IsActive(definition.Id);
            return registrable;
        }

        // 원작 RefreshRecordExistsIndicator는 칸 열쇠가 이미 등록됐는지 본다.
        private protected override bool IsRecorded(OriginalItemDefinition definition) => GameManager.MasterKeys.IsActive(definition.Id);

        // 원작 OnSubmitButtonClicked의 등록이다. 명령이 확인, 열쇠 1개 소모, 등록을 한 전이로 확정하고 알림 문구는 없다.
        private protected override bool TryRegister(GameSessionManager session, string itemId, out string? notice)
        {
            notice = null;
            return GameplayCommands.TryRegisterMasterKey(session, GameManager.MasterKeys, _world.Catalog, itemId);
        }
    }
}
