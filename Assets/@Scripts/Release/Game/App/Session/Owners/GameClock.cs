#nullable enable
using System;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 GameClock처럼 월드 시각, 날짜, 플레이 시간을 소유한다. GameManager 프리팹 자식의 컴포넌트이고 GameManager.Clock으로 찾는다.
    // 저장할 때 시계 구역을 써 넣고, 기록을 불러오면 그 값을 읽어 간다. 메인 스레드에서만 쓴다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed class GameClock : SessionSectionOwner
    {
        // 값이 바뀌었다는 신호만 보낸다. 구독자는 Current를 직접 읽는다.
        public static event Action? Changed;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => Changed = null;

        // 기록을 불러오기 전에는 새 게임 시계다. 읽는 쪽은 GameRuntime.HasSession으로 세션이 있는지 먼저 본다.
        GameplayClockState _clock = GameplayPersistenceState.NewGame().Clock;

        // 불변 값이라 복사하지 않고 그대로 준다.
        public GameplayClockState Current => _clock;

        // 원본 WeatherManagerData의 강수 시드(0 이상 100000 미만)다. 사용자 결정 D1로 시계 구역에 함께 저장한다.
        // 원본이 날씨 기록 없는 파일을 불러올 때처럼, 기록에 시드가 없으면 불러올 때 새로 뽑고 다음 저장에 쓴다.
        public int WeatherSeed { get; private set; }
        // 원작 FirstTimeToBaseTimeSetted다. 기지 첫 방문 시각 맞춤(TryApplyFirstBaseVisit)을 저장마다 한 번만 하게 하는 기록이다.
        bool _firstBaseVisitTimeSet;
        // 원작 SleepView 슬라이더 범위(LevelManager.prefab, 0~1440, 정수)다. 이 밖의 분은 수면 명령이 거절한다.
        internal const int _maxSleepMinutes = 1440;

        // 기반 클래스가 저장 구독을 건 뒤, 기록을 불러오기 전의 날씨 시드를 뽑는다.
        protected override void Awake()
        {
            base.Awake();
            WeatherSeed = UnityEngine.Random.Range(0, CampaignRecordsState._weatherSeedLimit);
        }

        // 저장마다 시계 구역을 한 번 쓴다. 날씨 시드와 기지 첫 방문 플래그도 늘 함께 쓴다.
        private protected override void WriteSection(GameSaveData data) => data.SetClock(_clock, WeatherSeed, _firstBaseVisitTimeSet);

        // 불러온 기록의 시계, 날씨 시드, 첫 방문 플래그로 바꾼 뒤 알린다. 시드가 없던 기록이면 새로 뽑는다.
        // 기록은 코덱이 검증한 값이라 다시 검사하지 않는다.
        private protected override void ReadSection(GameSaveData data)
        {
            _clock = data.Clock;
            WeatherSeed = data.WeatherSeed ?? UnityEngine.Random.Range(0, CampaignRecordsState._weatherSeedLimit);
            _firstBaseVisitTimeSet = data.FirstBaseVisitTimeSet;
            ObserverDispatch.InvokeEach(Changed);
        }

        // 원본 GameClock처럼 게임 시간을 실제의 60배로 흘리고 하루 86300초마다 날짜를 넘긴다. 큰 증가량도 반복 없이 날짜를 넘기되,
        // 정확히 하루 길이인 시각은 다음 날로 넘기지 않는다. 날짜나 플레이 시간이 long을 넘으면 기록 없이 거절한다. 성공했을 때만 알린다.
        // 증가량은 호출 쪽이 넘기는 0 이상의 유한한 값이다.
        public bool TryAdvance(double seconds, double realSeconds)
        {
            if (!AcceptsCommands) return false;
            // 2^63이다. 이 값 이상인 double은 long으로 바꿀 수 없다.
            const double longLimit = 9223372036854775808d;
            GameplayClockState old = _clock;
            // 시각을 아직 포착하지 않은 시계는 원본 시작 시각에서 흐른다. 수면과 시각 맞춤도 같은 기본값을 쓴다.
            double time = old.SecondsOfDayOrDefault() + seconds * GameplayClockState._worldTimeScale;
            long days = old.Days ?? 0;
            if (time > GameplayClockState._dayLengthSeconds)
            {
                double carry = Math.Ceiling(time / GameplayClockState._dayLengthSeconds) - 1;
                // 날짜 수가 long을 넘으면 기록 없이 거절한다. 아래 넘침 검사도 모두 같다.
                if (!(carry < longLimit) || (long)carry > long.MaxValue - days) return false;
                time -= carry * GameplayClockState._dayLengthSeconds;
                days += (long)carry;
            }
            long realTicks = TimeSpan.FromSeconds(realSeconds).Ticks;
            if (realTicks > long.MaxValue - old.RecordedPlayTicks) return false;
            long recorded = old.RecordedPlayTicks + realTicks;
            // GameplayClockState는 기록 시간과 과거 시간의 합이 long 안에 있어야 한다. 이 넘침도 입력이 만든 것이라 거절한다.
            if (old.HistoricalPlayTicks.HasValue && old.HistoricalPlayTicks.Value > long.MaxValue - recorded)
                return false;
            // 위 계산은 규칙 안의 날짜, 시각, 플레이 시간만 만들므로 믿을 수 있는 입력용 생성자를 쓴다.
            _clock = new GameplayClockState(recorded, old.HistoricalPlayTicks, days, time);
            ObserverDispatch.InvokeEach(Changed);
            return true;
        }

        // 원본 GameClock.StepTimeTil이다. 원본 LevelConfig가 시각을 고정한 레벨(튜토리얼 08:00)에 들어올 때 시간대 볼륨이 부른다.
        // 그 시각이 오늘 아직 오지 않았으면 그때까지, 지났거나 같으면 원본처럼 TimeSpan.FromDays(1)인 86400초를 더한 차이만큼 진행한다.
        // 원본 하루 길이(86300초)와 다른 이 86400초도 원본 그대로다. 플레이 시간은 늘리지 않는다.
        public bool TryStepTimeTil(double secondsOfDay)
        {
            double now = _clock.SecondsOfDayOrDefault();
            double step = secondsOfDay > now ? secondsOfDay - now : secondsOfDay + 86400d - now;
            return TryAdvance(step / GameplayClockState._worldTimeScale, 0d);
        }

        // 원작 SleepView.Sleep의 GameClock.Step(분 × 60)이다. 게임 초를 그만큼 더하므로 배속 60(GameplayClockState._worldTimeScale)으로 나눠 넘긴다.
        // 원작 Step처럼 플레이 시간은 늘리지 않는다. 슬라이더 밖의 분은 기록 없이 거절한다(C1). 관문이 닫혀도 TryAdvance가 거절한다.
        internal bool TrySleep(int minutes)
        {
            if (minutes < 0 || minutes > _maxSleepMinutes) return false;
            return TryAdvance(minutes * 60d / GameplayClockState._worldTimeScale, 0d);
        }

        // 원작 SetTimeOnEnterBaseFirstTime.Start다. 플래그가 없을 때만 hour시까지 StepTimeTil하고 플래그를 세운다. 원작은 플래그를 먼저 저장하지만
        // 제품은 시계 진행이 성공했을 때만 세워, 관문이 닫힌 거절 뒤 다음 진입에서 다시 맞춘다. hour는 제작 도구가 원작 setTimeTo(8)에서 옮긴 값이다.
        internal bool TryApplyFirstBaseVisit(int hour)
        {
            if (_firstBaseVisitTimeSet) return false;
            if (!TryStepTimeTil(hour * GameplayClockState._secondsPerHour)) return false;
            _firstBaseVisitTimeSet = true;
            return true;
        }
    }
}
