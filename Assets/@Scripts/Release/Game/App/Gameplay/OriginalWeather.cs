#nullable enable annotations
using System;
using UnityEngine;

namespace Game.App
{
    // 원본 Duckov.Weathers.Weather다. 저작 도구가 원본 정수(targetWeather, forceSetWeatherTo)를 그대로 쓰므로 값을 지킨다.
    public enum OriginalWeather { Sunny = 0, Cloudy = 1, Rainy = 2, StormyI = 3, StormyII = 4, Snow = 22 }

    // 원본 Duckov.Weathers.Seasons다. 씬 저작값과 원본 LevelConfig.season의 정수를 그대로 쓰므로 값을 지킨다.
    public enum OriginalSeason { Spring = 0, Summer = 1, Autumn = 2, Winter = 3 }

    // 원본 WeatherManager의 Storm과 Precipitation이다. 폭풍은 게임 시각의 고정 주기로, 강수는 시드를 쓴 Perlin 잡음으로 정한다.
    // 값은 원본 LevelManager 프리팹의 WeatherManager 값이고, 저작 도구가 쓴다. 기본값도 같은 원본 값이다.
    [Serializable]
    internal sealed class OriginalWeatherCycle
    {
        // 원본 Storm 필드다. 단위는 TimeSpan 틱이다. 원본 값은 잠잠 9일, 1단계 12시간, 2단계 12시간이다.
        [SerializeField] long _stormOffset;
        [SerializeField] long _stormSleepTime = 7776000000000;
        [SerializeField] long _stormStage1Time = 432000000000;
        [SerializeField] long _stormStage2Time = 432000000000;
        // 원본 Precipitation 필드다. 시드는 원본처럼 저장 기록마다 따로 정하므로 여기 두지 않고 호출자가 넘긴다.
        [SerializeField] float _cloudyThreshold = .66f;
        [SerializeField] float _rainyThreshold = .68f;
        [SerializeField] float _frequency = .05f;
        [SerializeField] float _offset = .13f;
        [SerializeField] float _contrast = 1.17f;

        // 원본 GameClock.Now다. 날짜는 원본처럼 TimeSpan.FromDays로 더하므로, 하루 86300초 시계여도 날짜마다 86400초씩 늘어난다.
        // 시각을 아직 기록하지 않았으면 시계 상태의 기본 시각을 쓴다.
        internal static TimeSpan Now(GameplayClockState clock) =>
            TimeSpan.FromSeconds(clock.SecondsOfDayOrDefault()) + TimeSpan.FromDays(clock.Days ?? 0);

        // 폭풍 I과 II 단계인지다. 폭풍 알림, 폭풍 표시, 폭풍 효과음이 같은 판정을 쓴다.
        internal static bool IsStorm(OriginalWeather weather) => weather == OriginalWeather.StormyI || weather == OriginalWeather.StormyII;

        // 원본 WeatherManager.M_GetWeather다. 폭풍이 강수보다 먼저이고, 비 문턱을 넘으면 겨울에만 눈이다.
        internal OriginalWeather Weather(TimeSpan now, int seed, OriginalSeason season)
        {
            int storm = StormLevel(now);
            if (storm > 0) return storm == 1 ? OriginalWeather.StormyI : OriginalWeather.StormyII;
            float precipitation = Precipitation(now, seed);
            if (precipitation > _rainyThreshold) return season == OriginalSeason.Winter ? OriginalWeather.Snow : OriginalWeather.Rainy;
            return precipitation > _cloudyThreshold ? OriginalWeather.Cloudy : OriginalWeather.Sunny;
        }

        // 원본 Storm.GetStormLevel이다. 0은 잠잠, 1과 2는 폭풍 단계다. 주기 안의 위치는 ETA 함수와 같은 StormPhase로 구한다.
        internal int StormLevel(TimeSpan now)
        {
            long phase = StormPhase(now);
            if (phase < _stormSleepTime) return 0;
            return phase < _stormSleepTime + _stormStage1Time ? 1 : 2;
        }

        // 원본 Storm의 ETA와 비율 함수다. 공용 HUD 폭풍 표시(원본 TimeOfDayDisplay.RefreshStormText)가 0.5초마다 읽는다.
        // 원본처럼 주기 안의 위치를 매번 다시 구하고, 잠잠 중이 아닐 때 StormEta는 0이다.
        internal TimeSpan StormEta(TimeSpan now)
        {
            long phase = StormPhase(now);
            return phase < _stormSleepTime ? TimeSpan.FromTicks(_stormSleepTime - phase) : TimeSpan.Zero;
        }

        internal TimeSpan StormIOverEta(TimeSpan now) => TimeSpan.FromTicks(_stormSleepTime + _stormStage1Time - StormPhase(now));

        internal TimeSpan StormIIOverEta(TimeSpan now) => TimeSpan.FromTicks(StormPeriod - StormPhase(now));

        // 원본 GetSleepPercent와 GetStormRemainPercent다. 부동소수 변환 순서를 원본과 같게 둔다. 단계 시간은 저작값이라 0으로 나누는지 보지 않는다.
        internal float SleepPercent(TimeSpan now) => (float)StormPhase(now) / (float)_stormSleepTime;

        internal float StormRemainPercent(TimeSpan now) =>
            1f - (float)(StormPhase(now) - _stormSleepTime) / ((float)_stormStage1Time + (float)_stormStage2Time);

        long StormPeriod => _stormSleepTime + _stormStage1Time + _stormStage2Time;

        long StormPhase(TimeSpan now) => (now.Ticks + _stormOffset) % StormPeriod;

        // 원본 Precipitation.Get이다. 가로축은 10년 안의 시각(시간 단위), 세로축은 시드와 10년 단위 번호다.
        // 부동소수 연산 순서를 원본과 같게 두어 같은 시각과 시드에서 같은 값을 낸다.
        internal float Precipitation(TimeSpan now, int seed)
        {
            float hours = (float)(now.Days % 3650) * 24f + (float)now.Hours + (float)now.Minutes / 60f;
            var coord = new Vector2(hours * _frequency, seed + now.Days / 3650);
            float noise = Mathf.PerlinNoise(coord.x, coord.y) + Mathf.PerlinNoise(coord.x + .5f + 123.4f, coord.y - 567.8f);
            return Mathf.Clamp01((noise / 2f - .5f) * _contrast + .5f + _offset);
        }
    }
}
