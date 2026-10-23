#nullable enable
using System;
using Game.GAS;
using UnityEngine;

namespace Game.App
{
    // 원본 LevelManager의 StormI, StormII 오브젝트에 붙은 StormWeather 둘이다. OriginalTimeOfDay가 소유하고 매 프레임 Tick을 부른다.
    // 원본 TimeOfDayController가 날씨에 맞는 오브젝트만 켜므로, 그 단계의 날씨인 동안만 그 단계의 타이머가 돈다. 타이머는 0에서 시작해
    // 켜진 첫 프레임에 바로 걸고, 원본 오브젝트를 껐다 켜도 private 타이머가 남듯 단계별 타이머는 레벨 수명 동안 이어진다.
    // 값은 원본 LevelManager.prefab의 StormWeather 값이고, 버프 참조는 원본 buff 필드처럼 저작 도구(StormBuffAuthoring.WireStorm)가 넣는다.
    // 불변식: 폭풍 버프는 주인공에게만 걸고, 실내 하위 씬에서는 새로 걸지 않는다. 이미 걸린 층은 효과 시간이 다할 때까지 남는다.
    [Serializable]
    internal sealed class OriginalStormWeather
    {
        // 원본 StormI은 1111_Buff_Space를 3초마다, StormII는 1112_Buff_Space2를 1.5초마다 건다. 면제 기준은 0.9와 1.9다.
        [SerializeField] GameplayEffectDefinition _stormIBuff = null!;
        [SerializeField, Min(0f)] float _stormIInterval = 3f;
        [SerializeField] float _stormIProtectionThreshold = .9f;
        [SerializeField] GameplayEffectDefinition _stormIIBuff = null!;
        [SerializeField, Min(0f)] float _stormIIInterval = 1.5f;
        [SerializeField] float _stormIIProtectionThreshold = 1.9f;

        float _stormITimer;
        float _stormIITimer;

        // 원본 StormWeather.Update다. 폭풍이 아니면 아무것도 하지 않는다. 대상은 생존 값 소유자의 플레이어 아바타 하나라 펫과 적은 받지 않는다.
        // 면제, 사망, 아바타 확인은 소유자가 한다. 거절은 원본처럼 알림 없이 다음 간격을 기다리는 규칙 거절이라 결과를 보지 않는다.
        internal void Tick(OriginalWeather weather, bool indoor, float deltaTime, PlayerVitals vitals, DifficultyState? difficulty)
        {
            if (weather == OriginalWeather.StormyI)
            {
                if (Due(ref _stormITimer, _stormIInterval, indoor, deltaTime)) vitals.TryApplyStormBuff(_stormIBuff, _stormIProtectionThreshold, difficulty);
            }
            else if (weather == OriginalWeather.StormyII)
            {
                if (Due(ref _stormIITimer, _stormIIInterval, indoor, deltaTime)) vitals.TryApplyStormBuff(_stormIIBuff, _stormIIProtectionThreshold, difficulty);
            }
        }

        // 원본처럼 실내면 타이머를 줄이기 전에 돌아가 타이머가 멈춘다. 타이머가 0 이하가 되면 남은 몫을 버리고 간격으로 되돌린다.
        static bool Due(ref float timer, float interval, bool indoor, float deltaTime)
        {
            if (indoor) return false;
            timer -= deltaTime;
            if (timer > 0f) return false;
            timer = interval;
            return true;
        }
    }
}
