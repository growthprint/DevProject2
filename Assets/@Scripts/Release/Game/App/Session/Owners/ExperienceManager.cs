#nullable enable
using System;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 EXPManager처럼 누적 경험치를 소유한다. 레벨은 저장하지 않고 OriginalExperience 규칙으로 계산한다.
    // GameManager 프리팹 자식의 컴포넌트이고 GameManager.Experience로 찾는다. 저장할 때 경험치 구역을 써 넣고, 기록을 불러오면 읽어 간다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed class ExperienceManager : SessionSectionOwner
    {
        // 값이 바뀌었다는 신호만 보낸다. 구독자는 Experience를 직접 읽는다.
        public static event Action? Changed;
        // 원작 EXPManager.onLevelChanged(이전 레벨, 새 레벨)다. 원작처럼 Add가 레벨을 바꿀 때만 내고 불러오기는 내지 않는다.
        // 기지와 제로존 월드가 구독해 레벨업 알림을 띄운다. 퀘스트 경험치 보상도 Add를 거쳐 같은 알림이 난다.
        internal static event Action<int, int>? LevelChanged;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            Changed = null;
            LevelChanged = null;
        }

        // 0 이상이다. 더하는 쪽은 CanAdd로 넘침을 먼저 거절하고, 불러온 값은 저장 값 생성자가 검증했다.
        long _experience;
        // 원작 cachedExp다. 불러오기와 출격 시작 때만 기록하므로 출격 중 월드를 다시 초기화해도 결과 화면의 증가량 기준이 그대로다.
        long _raidStartExperience;

        // 누적 경험치와, 원본 레벨 규칙으로 그 값에서 계산한 레벨이다.
        public long Experience => _experience;
        public int Level => OriginalExperience.Level(_experience);
        // 결과 화면이 이 값과 현재 경험치의 차를 이번 출격에서 얻은 경험치로 보인다.
        internal long RaidStartExperience => _raidStartExperience;

        // 저장마다 경험치 구역을 한 번 쓴다.
        private protected override void WriteSection(GameSaveData data) => data.SetExperience(_experience);

        // 불러온 기록의 경험치로 바꾸고 원작 Start의 CacheExp처럼 증가량 기준도 맞춘 뒤 알린다.
        // 원작 SetupSaveData처럼 레벨 알림은 내지 않는다.
        private protected override void ReadSection(GameSaveData data)
        {
            _experience = data.Experience;
            _raidStartExperience = _experience;
            ObserverDispatch.InvokeEach(Changed);
        }

        // 원작 RaidUtilities.OnNewRaid의 CacheExp다. 출격 시작 명령(SceneFlow.BeginRaidAsync)이 출격 준비를 통과한 뒤 부른다.
        internal void RecordRaidStart() => _raidStartExperience = _experience;

        // 여러 소유자에 걸친 명령이 다른 상태를 바꾸기 전에 확인하는 조건이다. 음수와 long 넘침은 기록 없이 거절한다.
        public bool CanAdd(long amount) => AcceptsCommands && amount >= 0 && amount <= long.MaxValue - _experience;

        // 호출 쪽이 같은 호출에서 CanAdd로 확인한 양만 넘기므로 늘 더해져 결과를 돌려주지 않는다. 0은 바뀐 것이 없어 알리지 않는다.
        // 원작 EXP 설정자처럼 값 알림 뒤에, 레벨이 바뀌었으면 레벨 알림을 한 번 낸다. 여러 레벨을 넘어도 한 번이다.
        public void Add(long amount)
        {
            if (amount == 0) return;
            int level = Level;
            _experience += amount;
            ObserverDispatch.InvokeEach(Changed);
            int next = Level;
            if (next != level) ObserverDispatch.InvokeEachValue(LevelChanged, level, next);
        }
    }
}
