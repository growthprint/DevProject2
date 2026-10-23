#nullable enable annotations
using System;
using UnityEngine;

namespace Game.App
{
    // 원본 GameCamera의 RainyFX, SnowFX, StormFX, StormFX_2에 붙은 WeatherFxControl이다. 맡은 날씨가 되면 파티클을 켜고
    // 방출량을 0에서 원본 값까지 올리며 반복음을 시작한다. 날씨가 끝나면 방출량을 내리고, 0이 된 뒤 원본 지연만큼 지나면 파티클을 끈다.
    // 시간대 볼륨(OriginalTimeOfDay)이 첫 월드 초기화 때 Begin을, 매 프레임 Tick을 부른다. 필드는 Editor의 ComponentAuthoring이 쓴다.
    public sealed class OriginalWeatherFx : MonoBehaviour
    {
        [SerializeField] ParticleSystem[] _particles = Array.Empty<ParticleSystem>();
        [SerializeField] OriginalWeather _targetWeather;
        [SerializeField] float _lerpTime = 5f;
        [SerializeField] float _deactiveDelay = 10f;
        // 원본 rainSoundKey의 FMOD 이벤트 경로다. 원본 SnowFX처럼 비어 있으면 소리 없이 파티클만 다룬다.
        [SerializeField] string _soundEvent = "";
        [SerializeField] bool _onlyOutDoor = true;

        // 원본 Init이 읽어 둔 파티클별 원래 방출량이다. 켜는 동안 이 값까지 보간한다.
        float[] _rates = Array.Empty<float>();
        float _lerp;
        float _overTimer;
        bool _active;
        bool _soundPlaying;
        GameAudioEvent? _sound;
        // 반복음을 내는 앱 오디오 관리자다. 직렬화하지 않고 시간대 볼륨이 Begin에서 넘긴다.
        GameAudioManager _audio = null!;

        // 원본 Init이다. 반복음을 낼 오디오 관리자와 원래 방출량을 기억하고 파티클을 모두 끈다.
        internal void Begin(GameAudioManager audio)
        {
            _audio = audio;
            _rates = new float[_particles.Length];
            for (int i = 0; i < _particles.Length; i++) _rates[i] = _particles[i].emission.rateOverTime.constant;
            SetActive(false);
        }

        // 원본 Update다. 실내 하위 씬이면 원본처럼 방출량을 바로 0으로 두고, 지연이 지나면 파티클을 끈다.
        // 시간대 볼륨이 자기 Begin에서 이 Begin을 부른 뒤에만 부른다.
        internal void Tick(OriginalWeather weather, bool indoor, float deltaTime)
        {
            bool on = weather == _targetWeather;
            if (_onlyOutDoor && indoor)
            {
                on = false;
                _lerp = 0f;
            }
            if (on)
            {
                _overTimer = _deactiveDelay;
                if (!_active) SetActive(true);
            }
            else if (_lerp <= .01f)
            {
                _overTimer -= deltaTime;
                if (_overTimer <= 0f) SetActive(false);
            }
            if (!_active) return;
            // 보간 시간은 저작값이라 나누기 전에 검사하지 않는다. 씬 값이 0보다 큰지는 EditMode 날씨 테스트가 본다.
            _lerp = Mathf.MoveTowards(_lerp, on ? 1f : 0f, deltaTime / _lerpTime);
            for (int i = 0; i < _particles.Length; i++)
            {
                ParticleSystem.EmissionModule emission = _particles[i].emission;
                emission.rateOverTime = Mathf.Lerp(0f, _rates[i], _lerp);
            }
            if (on == _soundPlaying) return;
            _soundPlaying = on;
            if (!on) StopSound();
            // 원본 AudioManager.Post처럼 Begin이 받은 오디오 관리자로 이 FX 객체를 따라가게 재생한다.
            // 오디오가 꺼져 있으면 관리자가 null을 돌려준다.
            else if (_soundEvent.Length > 0) _sound = _audio.PlayAttached(_soundEvent, transform);
        }

        void SetActive(bool active)
        {
            foreach (ParticleSystem particle in _particles) particle.gameObject.SetActive(active);
            _active = active;
        }

        // 원본처럼 페이드아웃을 허용해 멈춘다. 참조를 먼저 비워 같은 핸들을 두 번 멈추지 않는다.
        void StopSound()
        {
            GameAudioEvent? sound = _sound;
            _sound = null;
            sound?.Stop(true);
        }

        void OnDestroy() => StopSound();
    }
}
