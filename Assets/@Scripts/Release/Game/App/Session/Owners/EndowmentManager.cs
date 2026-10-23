#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Game.Core;
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원작 EndowmentManager처럼 선택한 재능을 소유한다. GameManager 프리팹 자식의 컴포넌트이고 GameManager.Endowment로 찾는다.
    // 재능 정의는 GameplayDataSettings.Progression이 갖고, 해금 기록은 슬롯 밖 공통 파일(GlobalProgress)이 갖는다. 메인 스레드에서만 쓴다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed class EndowmentManager : SessionSectionOwner
    {
        // 값이 바뀌었다는 신호만 보낸다. 선택 창(EndowmentSelectionScreen)과, 연결 뒤 월드의 재능 효과 적용(PlayerVitals.SyncEndowmentEffect)이 듣는다.
        public static event Action? Changed;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => Changed = null;

        // 아래 둘은 명령 관문과 함께 런타임 구성이 잇는다. 다른 소유자 형식을 모르게 공통 파일 해금 조회와 퀘스트 변경 때 돌 해금 명령을 받는다.
        // 잇기 전에는 선택을 모두 거절하고, 기본 해금 재능만 열린 것으로 보며, 퀘스트가 바뀌어도 아무것도 하지 않는다.
        Func<EndowmentIndex, bool> _globalUnlocked = _ => false;
        Action _questsChanged = () => { };

        // 원작 Endowment_SelectedIndex다. 기록 전에는 원작 기본값 None(알바 덕)이다. 3-E의 PlayerVitals와 선택 패널이 읽는다.
        internal EndowmentIndex Selected { get; private set; } = EndowmentIndex.None;

        // 원작 Current의 효과 에셋이다. 수정치가 없는 재능(알바 덕)은 비어 있다.
        // 3-E 적용 요청(PlayerVitals.SyncEndowmentEffect와 월드 초기화·변경 알림 연결)이 읽는다.
        internal GameplayEffectDefinition? SelectedEffect => TryGetDefinition(Selected, out EndowmentDefinition? definition) ? definition.Effect : null;

        // GameRuntime이 구성 때 한 번 부른다. globalUnlocked는 GlobalProgress.IsEndowmentUnlocked이고,
        // questsChanged는 퀘스트 변경 알림마다 GameplayCommands.UnlockEndowmentsOfFinishedQuests를 부른다.
        internal void Configure(Func<bool> acceptsCommands, Func<EndowmentIndex, bool> globalUnlocked, Action questsChanged)
        {
            base.Configure(acceptsCommands);
            _globalUnlocked = globalUnlocked;
            _questsChanged = questsChanged;
        }

        // 원작 UnlockEndowmentWhenQuestComplete는 퀘스트 완료와 시작 때 이미 완료된 경우에 연다. 불러오기도 퀘스트 변경 알림을 내므로
        // 그 알림 하나를 듣는다. 기반 클래스의 저장 구독 뒤에 Awake에서 걸고, 저장 구독을 끊은 뒤 OnDestroy에서 끊는다.
        protected override void Awake()
        {
            base.Awake();
            QuestManager.Changed += QuestsChanged;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            QuestManager.Changed -= QuestsChanged;
        }

        // 원작 GetEndowmentUnlocked다. 기본 해금 재능이거나 공통 파일에 해금 기록이 있으면 참이다. 정의 없는 번호는 거짓이다.
        internal bool IsUnlocked(EndowmentIndex index) =>
            TryGetDefinition(index, out EndowmentDefinition? definition) && (definition.UnlockedByDefault || _globalUnlocked(index));

        // 원작 SelectIndex와 선택 창의 확인이다. 원작 SelectIndex는 잠금을 보지 않고 창이 막지만, 제품은 소유자도 잠긴 재능과
        // 같은 번호를 거절한다. 관문이 닫혀도 기록 없이 거절한다. 바꾸면 알리고, 슬롯 파일은 다음 저장 지점이 쓴다.
        internal bool TrySelect(EndowmentIndex index)
        {
            if (!AcceptsCommands || index == Selected || !IsUnlocked(index)) return false;
            Selected = index;
            ObserverDispatch.InvokeEach(Changed);
            return true;
        }

        // 원작 GetEntry다. 원작 entries 자리인 진행 데이터 등록에서 번호로 찾는다. 등록 데이터는 EndowmentTests가 확인한
        // 재능 다섯 개(빈 칸 없음, 번호 중복 없음)라 빈 칸을 거르지 않는다.
        internal static bool TryGetDefinition(EndowmentIndex index, [NotNullWhen(true)] out EndowmentDefinition? definition)
        {
            IReadOnlyList<EndowmentDefinition> definitions = GameplayDataSettings.Progression.Endowments;
            for (int position = 0; position < definitions.Count; position++)
            {
                if (definitions[position].Index != index) continue;
                definition = definitions[position];
                return true;
            }
            definition = null;
            return false;
        }

        void QuestsChanged() => _questsChanged();

        // 기반 클래스의 저장 수집이 부른다.
        private protected override void WriteSection(GameSaveData data) => data.SetEndowment(Selected);

        // 불러온 기록으로 바꾼 뒤 알린다. 기록은 코덱이 범위를 검증한 값이다.
        private protected override void ReadSection(GameSaveData data)
        {
            Selected = data.Endowment;
            ObserverDispatch.InvokeEach(Changed);
        }
    }
}
