#nullable enable
using System;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 GameRulesManager처럼 고른 난이도 규칙을 소유한다. 고르는 규칙은 캠페인 쪽이 정하고, 여기서는 결과 값만 보관한다.
    // GameManager 프리팹 자식의 컴포넌트이고 GameManager.Difficulty로 찾는다. 저장할 때 난이도 구역을 써 넣고, 기록을 불러오면 읽어 간다.
    [DisallowMultipleComponent]
    public sealed class DifficultyRules : MonoBehaviour
    {
        // 값이 바뀌었다는 신호만 보낸다. 구독자는 Current를 직접 읽는다.
        public static event Action? Changed;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => Changed = null;

        // null은 아직 난이도를 고르지 않았다는 저장 값이다. 불변 값이라 복사하지 않는다.
        // 플레이어 명령이 없어 다른 상태 소유자와 달리 명령 허용 조건을 받지 않는다.
        DifficultyState? _difficulty;

        // 피해와 적 체력 배율을 계산하는 쪽은 이 값을 DamageRules에 넘긴다.
        public DifficultyState? Current => _difficulty;

        // 난이도 재설정의 선택 제한이 현재 규칙 번호로 정해진다. 원작 GameRulesManager.SelectedRuleIndex처럼 아직 고르지 않았으면 표준으로 본다.
        // DifficultyState는 Unity 객체가 아니라 ?.와 ??를 써도 된다.
        internal DifficultyRuleIndex CurrentRuleIndex => _difficulty?.RuleIndex ?? DifficultyRuleIndex.Standard;

        // 원작 DifficultySelection.CheckUnlocked와 GetRageUnlocked의 선택 가능 표다. 화면 잠금 표시와 선택 명령의 검증이 같은 표를 쓴다.
        // firstSelection은 원작의 '기지 미방문'이고, 제품에서는 준비 단계의 첫 선택이다. 그때는 분노만 공통 파일 해금을 본다.
        // 기지 뒤 재설정은 현재가 사용자 지정이면 사용자 지정만, 분노면 분노만(해금됐을 때), 그 밖이면 일반 여섯 단계만 고를 수 있다.
        internal static bool CanSelect(DifficultyRuleIndex rule, bool firstSelection, DifficultyRuleIndex current, bool rageUnlocked) => rule switch
        {
            DifficultyRuleIndex.Standard or DifficultyRuleIndex.Easy or DifficultyRuleIndex.ExtraEasy or DifficultyRuleIndex.StandardChallenge
                or DifficultyRuleIndex.Hard or DifficultyRuleIndex.ExtraHard =>
                firstSelection || (current != DifficultyRuleIndex.Custom && current != DifficultyRuleIndex.Rage),
            DifficultyRuleIndex.Rage => rageUnlocked && (firstSelection || current == DifficultyRuleIndex.Rage),
            DifficultyRuleIndex.Custom => firstSelection || current == DifficultyRuleIndex.Custom,
            // 원작 default 분기처럼 정의 밖 번호는 고를 수 없다.
            _ => false
        };

        // 원작 매니저처럼 Awake에서 저장 수집과 불러오기를 구독하고 OnDestroy에서 끊는다.
        void Awake()
        {
            SaveManager.CollectSaveData += WriteSection;
            SaveManager.FileLoaded += ReadSection;
        }

        void OnDestroy()
        {
            SaveManager.CollectSaveData -= WriteSection;
            SaveManager.FileLoaded -= ReadSection;
        }

        // 저장마다 난이도 구역을 한 번 쓴다.
        void WriteSection(GameSaveData data) => data.SetDifficulty(_difficulty);

        // 불러온 기록의 난이도로 바꾼 뒤 알린다.
        void ReadSection(GameSaveData data)
        {
            _difficulty = data.Difficulty;
            ObserverDispatch.InvokeEach(Changed);
        }

        // 캠페인 전환이 난이도 선택을 확정할 때 부르는 Runtime 전환이라 명령 관문을 보지 않는다.
        // 난이도 화면이 고른 프리셋이나 사용자 값만 오고, 호출 쪽이 null이 아닐 때만 부른다.
        // 기지 뒤 재설정도 같은 전환으로 온다. 선택 제한(CanSelect)은 후보를 만들 때 GameplayCommands가 이미 확인했다.
        internal void Apply(DifficultyState difficulty)
        {
            _difficulty = difficulty;
            ObserverDispatch.InvokeEach(Changed);
        }
    }
}
