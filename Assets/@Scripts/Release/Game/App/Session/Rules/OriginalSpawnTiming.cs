#nullable enable
using System;
using UnityEngine;

namespace Game.App
{
    // 원작 CharacterSpawnerRoot의 시각, 레벨 시간, 날씨 조건이다. 스폰 루트가 직렬화해 들고, 판정은 원작 CheckTiming과 같은 식이다.
    // 날씨는 원작 Weather 정수(OriginalWeather와 같은 값)로 받는다. 세션 규칙이 게임플레이 형식에 기대지 않게 하기 위해서다.
    [Serializable]
    public sealed class OriginalSpawnTiming
    {
        [SerializeField] bool _useTimeOfDay;
        // 원작 whenToSpawn이다. 시각을 쓰지 않을 때만 보는 레벨 경과 초다.
        [SerializeField] float _whenToSpawn;
        // 원작 spawnTimeRangeFrom, spawnTimeRangeTo다. 하루 시각(시)이며 From이 To보다 크면 자정을 넘는 범위다.
        [SerializeField] float _from, _to;
        [SerializeField] bool _checkWeather;
        [SerializeField] int[] _targetWeathers = Array.Empty<int>();
        [SerializeField] bool _despawnIfTimingWrong;

        // 원작 Awake처럼 시각도 날씨도 보지 않는 스포너는 사라지기 규칙을 끈 것으로 본다.
        internal bool DespawnIfTimingWrong => _despawnIfTimingWrong && (_useTimeOfDay || _checkWeather);

        // 원작 CheckTiming이다. 시각 범위는 양끝을 포함하고, 시각을 쓰면 레벨 시간은 보지 않는다. 날씨를 보면 목록에 든 날씨일 때만 참이다.
        // 원작처럼 시각을 float로 바꾼 뒤 24로 나눈 나머지를 쓴다.
        internal bool IsDue(double hour, float levelTime, int weather)
        {
            bool time;
            if (_useTimeOfDay)
            {
                float now = (float)hour % 24f;
                time = (now >= _from && now <= _to) || (_to < _from && (now >= _from || now <= _to));
            }
            else time = levelTime >= _whenToSpawn;
            return time && (!_checkWeather || Array.IndexOf(_targetWeathers, weather) >= 0);
        }
    }
}
