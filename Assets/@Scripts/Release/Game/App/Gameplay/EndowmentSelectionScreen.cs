#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.GAS;

namespace Game.App
{
    // 원작 ViewsProxy.ShowEndowmentView → EndowmentSelectionPanel.Show와 Execute처럼 공용 HUD의 재능 선택 창을 연다. StockShopScreen과 같은 수명 규칙이다.
    // 열린 동안 게임플레이 입력을 막고, 그 차단 핸들은 화면 공통 바탕(GameplayScreen)이 쥔다. 줄을 고르는 것은 화면 상태이고, 확인하면 고른 재능이
    // 지금과 다를 때만 재능 소유자(EndowmentManager.TrySelect)에 보내고 창을 닫는다. 화면 콜백은 읽기만 하고, 소유자가 확정한 뒤 변경 알림으로 다시 그린다.
    // 원작 차이: 원작은 확인 뒤 기지 씬을 다시 불러오지만, 제품은 효과를 그 자리에서 바꾼다(PlayerVitals 재능 효과 적용).
    internal sealed class EndowmentSelectionScreen : GameplayScreen
    {
        // 원작 ModifierDescription의 백분율 숫자 형식이다. 비율 더하기와 비율 곱하기가 같은 형식을 쓴다.
        const string _percentFormat = "00.#";

        readonly GameplayWorldContext _world;
        readonly OriginalEndowmentSelectionView _view;
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 원작 Selection이다. 열 때 지금 재능을 고른 상태로 시작한다(원작 Setup).
        EndowmentIndex _selected;

        // View는 화면 연결 단계가 공용 HUD에서 꺼내 넘긴다. 이 묶음은 SharedHudView를 고치지 않아서다. 호출자: 2-W 방식의 GameplayScreens.
        internal EndowmentSelectionScreen(GameplayWorldContext world, OriginalEndowmentSelectionView view)
        {
            _world = world;
            _view = view;
        }

        // 원작 EndowmentSelectionPanel.Show다. 공용 HUD가 이 월드에 붙어 있지 않거나, 세션이 없거나, 입력 차단을 못 받으면(정리 중)
        // 열지 않고 아무것도 바꾸지 않는다. 이미 열려 있으면 닫고 다시 연다. 호출자: 화면 연결 단계의 재능 선택 열기(GameplayScreens).
        internal void Show()
        {
            // 연결과 세션 확인, 다시 열기 전 닫기, 입력 차단은 다른 화면과 같은 순서라 공통 바탕이 맡는다.
            if (!TryBeginShow(_world, out _)) return;
            _selected = GameManager.Endowment.Selected;
            Action<EndowmentIndex> select = Select;
            Action confirm = Confirm, close = Close, redraw = Redraw;
            _view.EntrySelected += select;
            _subscriptions.Add(() => _view.EntrySelected -= select);
            _view.ConfirmRequested += confirm;
            _subscriptions.Add(() => _view.ConfirmRequested -= confirm);
            _view.CancelRequested += close;
            _subscriptions.Add(() => _view.CancelRequested -= close);
            // 선택 확정과 공통 파일 해금(퀘스트 완료)은 열린 동안에도 올 수 있어 다시 그린다.
            EndowmentManager.Changed += redraw;
            _subscriptions.Add(() => EndowmentManager.Changed -= redraw);
            GlobalProgress.Changed += redraw;
            _subscriptions.Add(() => GlobalProgress.Changed -= redraw);
            // 로캘 다시 그리기는 공용 구독 도우미가 붙이고 뗀다.
            _subscriptions.BindLocaleChanged(redraw);
            _view.Show();
            Redraw();
            // 원작 View가 열릴 때처럼 기지 씬의 자동 저장을 요청한다. 기지 밖에는 자동 저장이 없어 무시된다.
            BaseAutoSaver.NotifyUiActivity();
        }

        // 구독과 입력 차단을 돌려주고 창을 닫는다. 씬 정리가 무조건 불러도 되고, 여러 번 불러도 된다.
        // ESC(원작 OnCancel)는 공통 바탕의 TryClose로 이 닫기를 부른다.
        internal override void Close()
        {
            if (!IsOpen) return;
            _subscriptions.Clear();
            ReleaseInput();
            if (_world.PresentationAttached) _view.Hide();
            BaseAutoSaver.NotifyUiActivity();
        }

        // 원작 EndowmentEntry.DescriptionAndEffects다. 설명 한 줄 뒤에 수정치마다 "- 스탯 이름 값" 줄을 붙인다.
        // 원작 AppendLine 대신 '\n'으로 줄을 나눠 실행 환경마다 같은 문자열을 만든다. 숫자는 원작 형식을 고정 문화권으로 쓴다.
        internal static string DescriptionAndEffects(EndowmentDefinition definition)
        {
            var text = new StringBuilder();
            // 원작 ToPlainText처럼 비어 있는 키는 번역을 찾지 않는다.
            text.Append(GameLocalization.TextOrEmpty(definition.DescriptionKey)).Append('\n');
            IReadOnlyList<EndowmentModifier> modifiers = definition.Modifiers;
            // 스탯 표시 이름 키 규칙(Stat_ 접두사)은 현지화 키 도우미가 정한다.
            for (int position = 0; position < modifiers.Count; position++)
                text.Append("- ").Append(GameLocalization.TextOrEmpty(GameLocalization.StatNameKey(modifiers[position].StatKey))).Append(' ')
                    .Append(ModifierValue(modifiers[position])).Append('\n');
            return text.ToString();
        }

        // 원작 ModifierDescription.DescriptionText의 값 부분이다. 더하기는 부호와 값, 비율 더하기는 백분율, 비율 곱하기는 x(1+값)%다.
        static string ModifierValue(EndowmentModifier modifier)
        {
            CultureInfo culture = CultureInfo.InvariantCulture;
            float value = modifier.Value;
            switch (modifier.Type)
            {
                case AttributeModifierOp.Add:
                    return value >= 0f ? "+" + value.ToString(culture) : value.ToString(culture);
                // 두 백분율 방식은 원작과 같은 숫자 형식 상수를 쓴다.
                case AttributeModifierOp.PercentageAdd:
                    return value >= 0f ? "+" + (value * 100f).ToString(_percentFormat, culture) + "%" : "-" + (-value * 100f).ToString(_percentFormat, culture) + "%";
                case AttributeModifierOp.PercentageMultiply:
                    return "x" + ((1f + value) * 100f).ToString(_percentFormat, culture) + "%";
                default:
                    // 원작 switch에 없는 방식은 값 문구가 비어 있다.
                    return "";
            }
        }

        // 원작 OnEntryClicked와 Select다. 잠긴 줄은 줄 View가 클릭을 보내지 않고, 잠금 표시는 소유자 변경마다 Redraw가 다시 넣는다.
        void Select(EndowmentIndex index)
        {
            if (!IsOpen) return;
            _selected = index;
            Redraw();
        }

        // 원작 Execute의 확인 뒤 단계다. 선택 명령을 보내고 결과와 상관없이 창을 닫는다. 지금과 같은 번호와 잠긴 번호는
        // 소유자가 바꾸지 않고 거절한다. 그 밖의 거절은 관문이 닫힌 전환 중뿐이고 그때 창도 정리된다.
        void Confirm()
        {
            if (!IsOpen) return;
            GameManager.Endowment.TrySelect(_selected);
            Close();
        }

        // 원작 Setup과 RefreshDescription이다. 원작 entries 순서로 줄을 만들고, 잠긴 줄은 "- 해금 조건 -"을 보인다.
        // 고른 재능이 없으면 설명은 원작 기본값 "-"이다.
        void Redraw()
        {
            if (!IsOpen || !GameManager.HasInstance) return;
            EndowmentManager endowment = GameManager.Endowment;
            IReadOnlyList<EndowmentDefinition> definitions = GameplayDataSettings.Progression.Endowments;
            EndowmentDefinition? described = null;
            _view.BeginEntries();
            for (int position = 0; position < definitions.Count; position++)
            {
                EndowmentDefinition definition = definitions[position];
                bool selected = definition.Index == _selected;
                if (selected) described = definition;
                // 원작 ToPlainText처럼 비어 있는 키(알바 덕의 해금 조건)는 번역을 찾지 않는다.
                _view.AddEntry(definition.Index, definition.Icon, GameLocalization.TextOrEmpty(definition.DisplayNameKey),
                    "- " + GameLocalization.TextOrEmpty(definition.RequirementTextKey) + " -", !endowment.IsUnlocked(definition.Index), selected);
            }
            _view.EndEntries(described != null ? DescriptionAndEffects(described) : "-");
        }
    }
}
