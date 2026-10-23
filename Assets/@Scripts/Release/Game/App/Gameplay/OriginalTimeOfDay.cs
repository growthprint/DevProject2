#nullable enable annotations
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.App
{
    // 원본 TimeOfDayEntry의 단계별 프로필이다. 원본 GetPhase처럼 해질녘 단계가 없는 항목은 저작 때 낮 프로필을 넣는다.
    [Serializable]
    internal sealed class OriginalTimeOfDayEntry
    {
        [SerializeField] VolumeProfile _day = null!;
        [SerializeField] VolumeProfile _dawn = null!;
        [SerializeField] VolumeProfile _night = null!;

        internal VolumeProfile Day => _day;
        internal VolumeProfile Dawn => _dawn;
        internal VolumeProfile Night => _night;

        // 원본 프로필 TimeOfDayPost의 nightViewAngleFactor다. 음수는 그 프로필이 값을 덮지 않는다는 뜻이다. 제품에는 TimeOfDayPost
        // 볼륨 컴포넌트가 없어 Editor의 ComponentAuthoring.ConfigureTimeOfDay가 원본 프로필에서 읽어 쓴다. 기본 1은 저작 전 씬에서 시야를 줄이지 않는다.
        [SerializeField] float _dayViewAngle = 1f;
        [SerializeField] float _dawnViewAngle = 1f;
        [SerializeField] float _nightViewAngle = 1f;

        // 이 항목이 그 프로필을 가지면 저작한 배율을 돌려준다.
        internal bool TryViewAngle(VolumeProfile? profile, out float value)
        {
            value = profile == _day ? _dayViewAngle : profile == _dawn ? _dawnViewAngle : _nightViewAngle;
            return profile != null && (profile == _day || profile == _dawn || profile == _night);
        }
    }

    // 원본 LevelManager 프리팹의 TimeOfDayController와 TimeOfDayVolumeControl을 옮긴 시간대 볼륨이다. 원본 WeatherManager 규칙으로 날씨를,
    // 게임 시계로 낮, 해질녘, 밤 단계를 고르고, 그 날씨 항목의 단계 프로필이 바뀌면 이전 프로필(From, 우선순위 3) 위에서 새 프로필(To, 우선순위 4)의
    // 가중치를 곡선으로 올린다. 날씨 FX와 폭풍 알림도 맡는다. 필드는 Editor의 ComponentAuthoring.ConfigureTimeOfDay와 ConfigureWeather가 쓴다.
    // 월드 컨트롤러(OriginalGameplayController)가 첫 초기화 때 Begin을, 세션이 있는 매 프레임 Tick을, 카메라를 움직인 뒤 PlaceWeatherFx를 부른다.
    // 원본 LevelManager의 StormWeather 두 개(폭풍 피해 버프)와 TimeOfDayDisplay(공용 HUD의 단계, 날씨, 폭풍 표시)도 Tick 안에서 진행한다.
    // 그래서 월드 컨트롤러는 새 호출 없이 이 둘을 얻는다. 버프는 생존 값 소유자(PlayerVitals)에, 표시는 공용 HUD에 넘긴다.
    public sealed class OriginalTimeOfDay : MonoBehaviour
    {
        // 원본 TimeOfDayController가 폭풍 단계에 들어갈 때 내는 효과음이다.
        const string _stormISound = "event:/Music/Stinger/stg_storm_1";
        const string _stormIISound = "event:/Music/Stinger/stg_storm_2";
        // 원본 GameClock.TimeOfDay.TotalHours를 하루 단위로 접는 시간 수다. 폭풍이 하루 안에 오는지도 같은 값으로 잰다.
        const double _hoursPerDay = 24d;
        // 원본 TimeOfDayDisplay의 표시 갱신 간격(실제 시간 초)이다.
        const float _hudRefreshInterval = .5f;
        // Begin이 지금 단계의 프로필을 섞지 않고 바로 적용하도록 섞는 시간보다 충분히 긴 경과 시간을 한 번 넘긴다.
        const float _instantBlendTime = 999f;

        [SerializeField] Volume _fromVolume = null!;
        [SerializeField] Volume _toVolume = null!;
        // 원본 TimeOfDayConfig의 날씨별 항목이다. 원본 GetCurrentEntry처럼 눈은 비 항목을 쓴다.
        [SerializeField] OriginalTimeOfDayEntry _sunny = new OriginalTimeOfDayEntry();
        [SerializeField] OriginalTimeOfDayEntry _cloudy = new OriginalTimeOfDayEntry();
        [SerializeField] OriginalTimeOfDayEntry _rainy = new OriginalTimeOfDayEntry();
        [SerializeField] OriginalTimeOfDayEntry _stormI = new OriginalTimeOfDayEntry();
        [SerializeField] OriginalTimeOfDayEntry _stormII = new OriginalTimeOfDayEntry();
        // 원본 TimeOfDayConfig의 시각과 날씨 고정 값이다. 튜토리얼(Level_Guide_Main)은 08시 맑음으로 고정한다.
        [SerializeField] bool _forceTime;
        [SerializeField] int _forceHour = 8;
        [SerializeField] bool _forceWeather;
        [SerializeField] OriginalWeather _forceWeatherTo;
        // 원본 LevelConfig.season과 원본 LevelManager의 WeatherManager 값이다.
        [SerializeField] OriginalSeason _season;
        [SerializeField] OriginalWeatherCycle _cycle = new OriginalWeatherCycle();
        // 원본 TimeOfDayController의 단계 경계 시각과 TimeOfDayVolumeControl의 섞는 시간, 가중치 곡선이다.
        [SerializeField] float _morningStart = 5f;
        [SerializeField] float _dawnStart = 16f;
        [SerializeField] float _nightStart = 19f;
        [SerializeField] float _blendTime = 2f;
        [SerializeField] AnimationCurve _blendCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        // 원본 MultiSceneCore에서 실내(isInDoor)인 하위 씬 이름이다. 그 안에서는 원본처럼 날씨 FX를 끄고 폭풍 효과음을 내지 않는다.
        // 폭풍 버프도 새로 걸지 않고 그 타이머를 멈춘다.
        [SerializeField] string[] _indoorSubscenes = Array.Empty<string>();
        // 하위 씬 루트를 나누지 않아 활성 하위 씬 이름이 없는 레벨의 실내 여부다. 저작 도구(DuckovCampaignShaderParity)가 원본
        // MultiSceneCore에서 정한다. 기지는 원본처럼 두 하위 씬으로 나뉘어 이름 목록으로 판정하므로 거짓이다.
        [SerializeField] bool _indoorWithoutSubscene;
        // 원본 stormIObject와 stormIIObject의 StormWeather다. 버프 참조와 값은 저작 도구(StormBuffAuthoring.WireStorm)가 쓴다.
        [SerializeField] OriginalStormWeather _storm = new OriginalStormWeather();
        // 원본 GameCamera 아래의 날씨 FX와 그 루트다. 원본 GameCamera처럼 루트를 카메라 추종점에 두고 회전은 월드 기준으로 둔다.
        [SerializeField] Transform _weatherFxRoot = null!;
        [SerializeField] OriginalWeatherFx[] _weatherFx = Array.Empty<OriginalWeatherFx>();

        VolumeProfile? _buffered;
        bool _blending;
        float _blendTimer;
        OriginalWeather _weather;
        // 원본 WeatherManager처럼 시각과 시드가 그대로면 앞서 고른 날씨를 쓴다. 시계 값은 바뀔 때마다 새 객체라 참조로 비교한다.
        GameplayClockState? _weatherClock;
        int _weatherSeed;
        OriginalWeather _clockWeather;
        // 원본 NotificationText.Push 자리다. 월드 컨트롤러가 공용 HUD 알림을 넘긴다.
        Action<string> _notify = null!;
        // 폭풍 버프를 넘길 생존 값 소유자와 폭풍 효과음을 내는 앱 수명 서비스다. 직렬화하지 않고 월드 컨트롤러가 Begin에서 넘긴다.
        GameRuntime _runtime = null!;
        GameAudioManager _audio = null!;
        // Begin이 잡아 두는 공용 HUD 폭풍 표시와 기지 여부, 원본 TimeOfDayDisplay의 0.5초(실제 시간) 갱신 타이머다. 타이머가 0에서 시작해
        // 첫 Tick이 바로 표시한다.
        HudStormDisplay _stormHud = null!;
        bool _baseLevel;
        float _hudTimer;
        // 마지막 Tick이 본 실내 여부와, 이전(From)·새(To) 볼륨 프로필의 원본 야간 시야각 배율이다. 음수는 덮지 않는 프로필이다.
        bool _indoor;
        float _fromViewAngle = -1f, _toViewAngle = -1f;
        // 원본 TimeOfDayPost.nightViewAngleFactor 기본값이다. 어느 프로필도 덮지 않으면 볼륨 스택이 이 값을 가진다.
        const float _defaultViewAngle = .2f;

        // 원본 TimeOfDayController.CurrentWeather다.
        internal OriginalWeather CurrentWeather => _weather;
        // 원작 TimeOfDayController.AtNight다. 낚시 물고기 추첨이 밤 여부를 읽는다.
        internal bool AtNight => PhaseAt(Hour()) == TimePhase.Night;
        // 원작 AICharacterController.Update가 밤 반응 시간을 쓰는 조건(AtNight이고 지금 하위 씬이 실내가 아님)이다. 적 AI가 읽는다.
        internal bool AtNightOutdoors => !_indoor && AtNight;

        // 원작 TimeOfDayController.NightViewAngleFactor다. 원작은 TimeOfDayPost.Override가 볼륨 스택 값을 넣는다. 스택은 기본값에서 시작해
        // 이전 프로필(가중치 1)과 새 프로필(섞는 가중치)이 덮는 값 쪽으로 차례로 섞인다. 장면의 다른 볼륨은 보지 않는다. 적 AI의 시야각이 읽는다.
        internal float NightViewAngleFactor
        {
            get
            {
                float value = _defaultViewAngle;
                if (_fromVolume.gameObject.activeSelf && _fromViewAngle >= 0f) value = _fromViewAngle;
                if (_toViewAngle >= 0f) value = Mathf.Lerp(value, _toViewAngle, _toVolume.weight);
                return value;
            }
        }

        // 원본 TimeOfDayController.Start다. 고정 시각이 있으면 시계를 그 시각까지 진행한 뒤 날씨를 고르고, 지금 단계의 프로필을 섞지 않고 바로 적용한다.
        // 월드 컨트롤러가 같은 동기 흐름에서 명령 관문을 확인한 뒤 부르므로, 시계 진행의 거절은 저장된 날짜 수가 long 끝에 닿은 넘침뿐이다. 그때는 현재 시각의 단계를 쓴다.
        // 원본처럼 이때의 폭풍 알림은 띄우지만 효과음은 레벨 초기화 뒤에만 낸다. 날씨 FX는 원본 Init처럼 꺼 둔다.
        // 앱 Runtime과 오디오 관리자는 월드 컨트롤러가 넘기고, Tick과 폭풍 효과음이 같은 참조를 쓴다.
        internal void Begin(Action<string> notify, GameRuntime runtime, GameAudioManager audio)
        {
            _notify = notify;
            _runtime = runtime;
            _audio = audio;
            // 월드 컨트롤러는 공용 HUD를 붙이고 세션을 확인한 뒤 Begin을 부르므로, 넘겨받은 앱 Runtime의 HUD와 지금 장소를 그대로 읽는다.
            // 원본 LevelManager.IsBaseLevel처럼 기지에서만 폭풍 설명을 켠다.
            _stormHud = runtime.Hud.Storm;
            _baseLevel = runtime.Snapshot().SceneId == GameScene.BaseCamp;
            _stormHud.Begin(ShowsStorm);
            _hudTimer = 0f;
            // 고정 시각은 시계 상태와 같은 시간당 초 수로 바꾼다.
            if (_forceTime) GameManager.Clock.TryStepTimeTil(_forceHour * GameplayClockState._secondsPerHour);
            _weather = SelectWeather();
            WeatherChanged(false);
            _buffered = PhaseProfile();
            StartBlend();
            UpdateBlend(_instantBlendTime);
            // 날씨 FX의 반복음도 같은 앱 오디오 관리자로 낸다.
            foreach (OriginalWeatherFx fx in _weatherFx) fx.Begin(audio);
        }

        // 원본 TimeOfDayController.Update와 TimeOfDayVolumeControl.Update, WeatherFxControl.Update를 한 번에 진행한다. 섞는 중에 단계가 또 바뀌면
        // 원본처럼 다음 목표로 맡아 두었다가 지금 섞기가 끝난 뒤 시작한다. 섞기가 끝나면 이전 프로필 볼륨을 끈다.
        // 월드 컨트롤러는 첫 초기화가 Begin까지 마친 뒤에만 부른다.
        internal void Tick(float deltaTime, string? activeSubscene)
        {
            // 원본처럼 실내 하위 씬에서는 폭풍 피해, 날씨 FX, 폭풍 효과음을 함께 막으므로 판정을 한곳에 둔다.
            bool indoor = Indoor(activeSubscene);
            // 적 AI의 밤 반응 조건이 읽도록 남긴다.
            _indoor = indoor;
            OriginalWeather weather = SelectWeather();
            if (weather != _weather)
            {
                _weather = weather;
                WeatherChanged(!indoor);
            }
            VolumeProfile phase = PhaseProfile();
            // 지금 섞는 목표는 StartBlend만 바꾸는 To 볼륨의 프로필이다. 그와 다른 단계만 다음 목표로 맡기며, 같은 단계를 다시 맡겨도 결과는 같다.
            if (_toVolume.sharedProfile != phase) _buffered = phase;
            if (!_blending && _buffered != null) StartBlend();
            if (_blending) UpdateBlend(deltaTime);
            if (!_blending && _fromVolume.gameObject.activeSelf) _fromVolume.gameObject.SetActive(false);
            foreach (OriginalWeatherFx fx in _weatherFx) fx.Tick(_weather, indoor, deltaTime);
            // 원본 StormWeather.Update는 Time.deltaTime으로 돈다. 일시정지 중에는 deltaTime이 0이라 타이머도 멈춘다.
            // 난이도 배율은 기아 버프와 같은 난이도 소유자의 현재 값이다. 생존 값 소유자는 Begin이 받은 Runtime에서 읽는다.
            _storm.Tick(_weather, indoor, deltaTime, _runtime.Vitals, GameManager.Difficulty.Current);
            // 원본 TimeOfDayDisplay.Update처럼 실제 시간 0.5초마다 표시를 갱신하므로 일시정지 중에도 돈다.
            _hudTimer -= Time.unscaledDeltaTime;
            if (_hudTimer > 0f) return;
            _hudTimer = _hudRefreshInterval;
            RefreshHud();
        }

        // 원본 MultiSceneCore.GetSubSceneInfo().IsInDoor다. 활성 하위 씬 이름이 없는 레벨은 저작한 값을 쓴다.
        bool Indoor(string? activeSubscene) =>
            activeSubscene != null ? Array.IndexOf(_indoorSubscenes, activeSubscene) >= 0 : _indoorWithoutSubscene;

        // 원본 TimeOfDayDisplay.RefreshStormText다. 남은 시간과 막대는 고정 날씨와 상관없이 시계의 폭풍 주기로 정한다.
        // 폭풍 I은 2단계까지, 폭풍 II는 끝까지 남은 시간이고, 그 밖에는 폭풍까지 남은 시간이다. 설명은 폭풍 중이거나 하루 안에 올 때 기지에서만 켠다.
        void RefreshHud()
        {
            TimeSpan now = OriginalWeatherCycle.Now(GameManager.Clock.Current);
            string title;
            TimeSpan eta;
            float fill;
            bool description;
            switch (_weather)
            {
                case OriginalWeather.StormyI:
                    title = "StormPhaseIIETA";
                    eta = _cycle.StormIOverEta(now);
                    fill = _cycle.StormRemainPercent(now);
                    description = _baseLevel;
                    break;
                case OriginalWeather.StormyII:
                    title = "StormOverETA";
                    eta = _cycle.StormIIOverEta(now);
                    fill = _cycle.StormRemainPercent(now);
                    description = _baseLevel;
                    break;
                default:
                    eta = _cycle.StormEta(now);
                    fill = _cycle.SleepPercent(now);
                    // 폭풍까지 하루가 안 남았는지 본다.
                    bool soon = eta.TotalHours < _hoursPerDay;
                    title = soon ? "StormOneDayETA" : "StormETA";
                    description = soon && _baseLevel;
                    break;
            }
            // 폭풍 판정은 날씨 규칙 한곳의 것을 쓴다.
            bool storm = OriginalWeatherCycle.IsStorm(_weather);
            _stormHud.Show(PhaseKey(PhaseAt(Hour())), WeatherKey(_weather), title, eta, fill, !storm, description);
        }

        // 원본 TimeOfDayDisplay.Start의 조건이다. 폭풍이 아닌 날씨로 고정된 레벨이면 폭풍 표시를 끈다.
        bool ShowsStorm => !_forceWeather || OriginalWeatherCycle.IsStorm(_forceWeatherTo);

        // 원본 LevelManager.prefab TimeOfDayController의 timePhaseKey_*와 WeatherKey_* 값이다. 원본 GetWeatherNameByWeather처럼 모르는 값은 맑음이다.
        static string PhaseKey(TimePhase phase) => phase switch
        {
            TimePhase.Dawn => "TOD_Dawn",
            TimePhase.Night => "TOD_Night",
            _ => "TOD_Day",
        };

        static string WeatherKey(OriginalWeather weather) => weather switch
        {
            OriginalWeather.Cloudy => "Weather_Cloudy",
            OriginalWeather.Rainy => "Weather_Rainy",
            OriginalWeather.Snow => "Weather_Snow",
            OriginalWeather.StormyI => "Weather_Storm_I",
            OriginalWeather.StormyII => "Weather_Storm_II",
            _ => "Weather_Sunny",
        };

        // 원본 GameCamera 루트가 카메라 추종점에 있으므로 그 아래 날씨 FX도 같은 점에 둔다. 카메라가 움직인 뒤 같은 프레임에 부른다.
        internal void PlaceWeatherFx(Vector3 followPoint) => _weatherFxRoot.position = followPoint;

        // 원본 GetCurrentEntry와 GetTimePhaseTagByTime이다. 시각은 원본 GameClock.TimeOfDay.TotalHours를 24로 나눈 나머지다.
        internal VolumeProfile Profile(OriginalWeather weather, double hour)
        {
            OriginalTimeOfDayEntry entry = weather switch
            {
                OriginalWeather.Cloudy => _cloudy,
                OriginalWeather.Rainy or OriginalWeather.Snow => _rainy,
                OriginalWeather.StormyI => _stormI,
                OriginalWeather.StormyII => _stormII,
                _ => _sunny,
            };
            // 단계 판정은 공용 HUD 단계 이름도 쓰므로 PhaseAt 하나로 둔다.
            return PhaseAt(hour) switch
            {
                TimePhase.Night => entry.Night,
                TimePhase.Day => entry.Day,
                _ => entry.Dawn,
            };
        }

        // 원본 TimePhaseTags의 낮, 해질녘, 밤이다.
        enum TimePhase { Day, Dawn, Night }

        TimePhase PhaseAt(double hour)
        {
            if (hour < _morningStart || hour >= _nightStart) return TimePhase.Night;
            return hour < _dawnStart ? TimePhase.Day : TimePhase.Dawn;
        }

        VolumeProfile PhaseProfile() => Profile(_weather, Hour());

        // 시각은 원본 GameClock.TimeOfDay.TotalHours를 24로 나눈 나머지다. 기록 전 시각과 시간당 초 수는 시계 상태가 정한다.
        static double Hour() => GameManager.Clock.Current.SecondsOfDayOrDefault() / GameplayClockState._secondsPerHour % _hoursPerDay;

        // 원본 WeatherManager.GetWeather다. 시드는 시계 소유자가 저장 기록마다 정한다.
        OriginalWeather SelectWeather()
        {
            if (_forceWeather) return _forceWeatherTo;
            GameClock clock = GameManager.Clock;
            GameplayClockState state = clock.Current;
            int seed = clock.WeatherSeed;
            if (ReferenceEquals(state, _weatherClock) && seed == _weatherSeed) return _clockWeather;
            _weatherClock = state;
            _weatherSeed = seed;
            _clockWeather = _cycle.Weather(OriginalWeatherCycle.Now(state), seed, _season);
            return _clockWeather;
        }

        // 원본 OnWeatherChanged다. 폭풍 단계에 들어가면 알림을 띄우고, sound가 참이면 이 객체 위치에서 Begin이 받은 오디오 관리자로
        // 원본 효과음을 낸다. 원본 stormIObject와 stormIIObject를 켜고 끄는 일은 Tick이 지금 날씨를 폭풍 버프 타이머(OriginalStormWeather)에
        // 넘기는 것으로 대신한다.
        void WeatherChanged(bool sound)
        {
            if (!OriginalWeatherCycle.IsStorm(_weather)) return;
            bool first = _weather == OriginalWeather.StormyI;
            _notify(first ? "Weather_Storm_I" : "Weather_Storm_II");
            if (sound) _audio.PlayOneShot(first ? _stormISound : _stormIISound, transform.position);
        }

        // 원본 StartBlendToBufferdTarget이다. 원본은 Volume.profile로 실행 중 사본을 쓰지만, 같은 값을 그리므로 공유 프로필을 그대로 넘긴다.
        void StartBlend()
        {
            _blending = true;
            VolumeProfile? target = _buffered;
            _buffered = null;
            _fromVolume.gameObject.SetActive(true);
            _fromVolume.sharedProfile = _toVolume.sharedProfile;
            _fromVolume.weight = 1f;
            _toVolume.sharedProfile = target;
            _toVolume.weight = 0f;
            _blendTimer = 0f;
            // 원작 TimeOfDayPost 값도 프로필과 함께 넘긴다. 이전 볼륨은 지금까지의 새 프로필 값을 이어받는다.
            _fromViewAngle = _toViewAngle;
            _toViewAngle = ViewAngleOf(target);
        }

        // 날씨 항목에서 그 프로필의 저작 배율을 찾는다. 항목에 없는 프로필은 덮지 않는 것으로 본다. 섞기를 시작할 때만 부른다.
        // 섞기마다 배열을 만들지 않도록 맑음, 흐림, 비, 폭풍 I, 폭풍 II 순서로 차례로 묻는다.
        float ViewAngleOf(VolumeProfile? profile)
        {
            if (_sunny.TryViewAngle(profile, out float value) || _cloudy.TryViewAngle(profile, out value)
                || _rainy.TryViewAngle(profile, out value) || _stormI.TryViewAngle(profile, out value)
                || _stormII.TryViewAngle(profile, out value)) return value;
            return -1f;
        }

        void UpdateBlend(float deltaTime)
        {
            _blendTimer += deltaTime;
            // 섞는 시간은 저작값이라 나누기 전에 검사하지 않는다. 씬 값이 0보다 큰지는 EditMode 날씨 테스트가 본다.
            float progress = _blendTimer / _blendTime;
            if (progress > 1f)
            {
                progress = 1f;
                _blending = false;
            }
            _toVolume.weight = _blendCurve.Evaluate(progress);
        }
    }
}
