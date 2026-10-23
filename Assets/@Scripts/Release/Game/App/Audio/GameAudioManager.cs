#nullable enable annotations
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원본 AudioManager처럼 FMOD 플러그인의 RuntimeManager로 소리를 내고, 버스 볼륨과 리스너 위치, 메뉴 음악을 맡는다.
    // 버튼 소리는 버튼 프리팹에 저작한 UguiAudioFeedback이 이 관리자로 낸다.
    // GameManager 프리팹의 자식이다. FMOD 시스템과 뱅크는 RuntimeManager가 만들고, 이 관리자가 앱 종료가 아닌 때 파괴되면 함께 닫는다.
    // 앱 종료와 Play 종료 때는 RuntimeManager가 스스로 해제한다.
    [DisallowMultipleComponent]
    public sealed class GameAudioManager : MonoBehaviour, IAsyncReleasable
    {
        // 켜지는 순간 RuntimeManager를 건드려 FMOD를 시작하므로, 프리팹에서는 꺼 두고 Open이 성공한 뒤에만 켠다.
        // 켠 뒤에는 해제해도 끄지 않고, 이 관리자가 파괴되거나 앱이 끝날 때 꺼진다.
        [SerializeField] FMODUnity.StudioListener _listener;

        // 플러그인이 FMOD를 닫은 뒤 RuntimeManager.StudioSystem을 읽으면 FMOD를 다시 띄우므로, Open에서 받은 핸들만 쓴다.
        FMOD.Studio.System _system;
        // 원본 뱅크의 Master, Music, SFX 버스 경로다. 볼륨 옵션 세 개가 이 버스에 하나씩 대응한다.
        const string _masterBusPath = "bus:/Master";
        const string _musicBusPath = "bus:/Master/Music";
        const string _sfxBusPath = "bus:/Master/SFX";
        // 원본 타이틀 승인 때 내는 시작음과 메뉴가 열린 동안 트는 제목 음악의 FMOD 경로다.
        const string _gameStartEvent = "event:/UI/game_start";
        const string _titleMusicEvent = "event:/Music/Loop/mus_title";
        // 원본 UpdateListener처럼 리스너를 캐릭터 발밑에서 이만큼 위인 머리 높이에 둔다.
        const float _listenerHeadHeight = 2f;
        // 원본 뱅크의 Master, Music, SFX 버스다.
        FMOD.Studio.Bus _masterBus;
        FMOD.Studio.Bus _musicBus;
        FMOD.Studio.Bus _sfxBus;
        // Open이 성공했는지다. 열지 못했으면 모든 명령을 조용히 무시한다.
        bool _opened;
        // 앱 종료 신호를 받았으면 참이다. 그 뒤의 파괴에서는 RuntimeManager가 스스로 해제하므로 FMOD를 닫지 않는다.
        bool _quitting;
        // 해제를 한 번만 실행하고 모든 호출자가 같은 완료를 받는다. 해제가 시작되면 새 FMOD 명령을 받지 않는다.
        // 꺼진 오브젝트에서는 Awake가 돌지 않으므로 처음 해제할 때 만든다.
        ReleaseOnce? _release;
        // 호출자가 들고 있는 이벤트 핸들도 해제 때 함께 정지하도록 추적한다.
        readonly HashSet<GameAudioEvent> _events = new HashSet<GameAudioEvent>();
        // PlayAttached로 시작해 대상을 따라가는 핸들이다. LateUpdate가 한 번에 옮기고, 끝난 핸들은 그때 뺀다.
        readonly List<GameAudioEvent> _following = new List<GameAudioEvent>();
        // 같은 동작과 경로의 실패는 한 번만 기록해 발소리나 사격음마다 같은 로그가 쌓이지 않게 한다.
        readonly Log.Once _reported = new Log.Once();
        // 소리를 낼 때마다 getEvent와 is3D를 다시 부르지 않도록 경로별로 성공한 조회만 캐시한다.
        // 뱅크는 RuntimeManager가 시작할 때 모두 싣고 내리지 않으므로, 캐시한 설명자는 쓰는 동안 유효하다.
        readonly Dictionary<string, EventInfo> _eventInfos = new Dictionary<string, EventInfo>(StringComparer.Ordinal);

        // Bind가 구독한 옵션 관리자와 현재 메뉴 연출이다. 해제는 앱 정리 중에 GameManager를 다시 읽지 않고 이 대상에서 구독을 끊는다.
        // 메뉴 교체와 리스너 캐릭터는 씬 문맥의 정적 접근자로 읽으므로 씬 흐름 참조를 두지 않는다.
        GameOptionsManager? _options;
        OriginalMenuPresentation? _menu;
        // 메뉴가 열려 있는 동안 트는 제목 음악이다.
        GameAudioEvent? _titleMusic;
        // 매 프레임 Camera.main을 찾지 않도록 리스너 카메라를 캐시하고, 파괴됐거나 꺼졌을 때만 다시 찾는다.
        // Release 씬에는 켜진 MainCamera가 하나이고, 씬 교체는 나가는 씬을 내린 뒤 새 씬을 싣는다.
        // 로드 시작 알림에서 비우므로 씬을 바꾼 뒤 첫 비교는 파괴된 카메라가 아니라 C# null을 본다.
        Camera? _listenerCamera;

        // 앱 구성이 오디오를 켤 때 한 번 부른다. 첫 RuntimeManager 접근이 FMOD를 시작하고 설정대로 모든 뱅크를 싣는다.
        // FMOD가 실패해도 시작을 막지 않고 원본처럼 소리 없이 진행하도록 false를 돌려준다. 해제 뒤에는 기록 없이 false다.
        internal bool Open()
        {
            if (_release != null && _release.Started) return false;

            FMOD.Studio.System system;
            try
            {
                system = FMODUnity.RuntimeManager.StudioSystem;
            }
            // 예외 경계: FMOD 플러그인은 초기화 실패를 예외로만 알린다. 한 번 기록하고 소리 없이 계속한다.
            catch (FMODUnity.SystemNotInitializedException error)
            {
                Log.Instance.Exception(error, this);
                return false;
            }
            // 볼륨을 맞출 세 버스가 없으면 뱅크를 싣지 못한 것이다. 처음 실패한 버스만 기록하고 소리 없이 계속한다.
            if (!TryGetBus(system, _masterBusPath, out _masterBus)
                || !TryGetBus(system, _musicBusPath, out _musicBus)
                || !TryGetBus(system, _sfxBusPath, out _sfxBus))
                return false;

            _system = system;
            _opened = true;
            _listener.gameObject.SetActive(true);
            return true;
        }

        // 버스 하나를 찾는다. 실패는 경로마다 한 번만 기록한다.
        bool TryGetBus(FMOD.Studio.System system, string path, out FMOD.Studio.Bus bus) =>
            Succeeded(system.getBus(path, out bus), "Resolve bus", path);

        // 옵션 초기화 전인 Runtime 구성 단계에서 옵션 적용과 메뉴 교체 알림을 구독해 첫 적용을 놓치지 않는다.
        // Open이 성공한 뒤 한 번만 부른다.
        internal void Bind()
        {
            // 구성의 await 사이에 앱 종료로 해제가 먼저 시작됐으면 끊을 수 없는 구독이 남지 않도록 아무것도 하지 않는다.
            if (!CanCommand()) return;

            // 옵션 관리자는 해제가 같은 대상에서 끊도록 잡아 두고, 적용 알림을 구독한 뒤 현재 확정값을 바로 적용한다.
            // 저장소가 없으면 확정값은 기본값이다.
            GameOptionsManager options = GameManager.Options;
            _options = options;
            options.Applied += ApplyOptions;
            // 메뉴 씬 문맥은 Awake에서 자신을, 로딩 시작에서 null을 알린다. 구성보다 먼저 올라온 메뉴도 바로 연결한다.
            MenuSceneContext.CurrentChanged += BindMenu;
            // 나가는 씬의 카메라와 따라가던 대상을 그 씬이 내려가기 전에 버린다. 이유는 ForgetSceneObjects에 있다.
            SceneFlow.LoadingStarted += ForgetSceneObjects;
            ApplyOptions(options.Current);
        }

        // 반복음과 제어할 단발음은 호출자와 이 관리자가 함께 추적하는 핸들로 돌려준다. 제품 호출이 경로와 위치만 쓰므로 파라미터는 받지 않는다.
        // 열기 전과 해제 뒤에는 조용히 null이다. FMOD 오류도 null이며, 기록은 TryStart가 원인마다 한 번 한다.
        public GameAudioEvent? Play(string eventPath, Vector3? position = null)
        {
            if (!TryStart(eventPath, position, out FMOD.Studio.EventInstance instance))
                return null;
            var handle = new GameAudioEvent(this, instance, eventPath);
            _events.Add(handle);
            return handle;
        }

        // 원본 AudioObject처럼 장전음 같은 긴 소리가 대상을 따라가게 시작한다. 위치는 LateUpdate가 프레임마다 한 번 옮긴다.
        // 실패와 무시는 Play와 같다.
        public GameAudioEvent? PlayAttached(string eventPath, Transform follow)
        {
            GameAudioEvent? handle = Play(eventPath, follow.position);
            if (handle == null) return null;
            handle.Attach(follow);
            _following.Add(handle);
            return handle;
        }

        // 시작 직후 인스턴스를 release해 재생이 끝나면 FMOD가 스스로 해제하므로 단발 이벤트만 받는다.
        // 남은 재생은 RuntimeManager가 FMOD를 닫을 때 회수한다. 사격, 피격, 발소리마다 불리므로 던지지 않고 실패는 동작과 경로마다 한 번 기록한다.
        public void PlayOneShot(string eventPath, Vector3? position = null)
        {
            if (TryStart(eventPath, position, out FMOD.Studio.EventInstance instance))
                Succeeded(instance.release(), "Release one-shot", eventPath);
        }

        // 옵션 검증을 거친 0~1 볼륨을 원본 믹서의 대응 버스에 적용한다.
        // 열기 전과 해제 뒤에는 조용히 무시한다. 버스 하나에서 FMOD 오류가 나도 기록하고 나머지 버스 적용을 계속한다.
        public void ApplyVolumes(float master, float music, float sfx)
        {
            if (!CanCommand())
                return;
            // 원본처럼 볼륨이 0인 버스는 음소거하고, 0보다 크면 음소거를 푼다.
            Succeeded(_masterBus.setVolume(master), "Set master volume");
            Succeeded(_masterBus.setMute(master == 0f), "Set master mute");
            Succeeded(_musicBus.setVolume(music), "Set music volume");
            Succeeded(_musicBus.setMute(music == 0f), "Set music mute");
            Succeeded(_sfxBus.setVolume(sfx), "Set SFX volume");
            Succeeded(_sfxBus.setMute(sfx == 0f), "Set SFX mute");
        }

        // 앱 자원 범위가 부르는 해제다. 열기 전에 불러도 안전하고, 여러 번 불러도 같은 완료를 받으며 던지지 않는다.
        public UniTask ReleaseAsync() => (_release ??= new ReleaseOnce(ReleaseCore, this)).ReleaseAsync();

        // 원본 UpdateListener처럼 현재 레벨에 보이는 플레이어 머리 위, 없으면 카메라 위치에 리스너를 두고 카메라 방향을 따른다.
        // FMOD 반영은 StudioListener가 맡는다. 카메라를 못 찾은 프레임은 리스너를 그대로 두고 다음 프레임에 다시 찾는다.
        void LateUpdate()
        {
            if (!CanCommand()) return;
            // 씬을 읽는 동안에는 씬 카메라와 따라갈 대상을 찾거나 비교하지 않는다. 파괴된 Camera나 Transform의 Unity null 비교는
            // 엔진에 존재를 묻고, 그 질문이 로딩 스레드의 잠금을 기다려 로딩 화면을 멈출 수 있다. 그동안 리스너는 마지막 자세에 머문다.
            // 커튼 카메라도 로드가 끝나기 전에 내려가므로 잡지 않는다. 규칙: Docs/runtime-boundaries.md#unity-objects-during-scene-loads
            if (SceneFlow.IsSceneLoading) return;
            // 붙여 재생한 소리를 대상 위치로 옮긴다. 끝났거나 대상이 사라진 핸들은 여기서 뺀다. 원본 AudioObject도 프레임마다 옮긴다.
            for (int i = _following.Count - 1; i >= 0; i--)
                if (!_following[i].UpdateFollow()) _following.RemoveAt(i);
            Camera? camera = _listenerCamera;
            if (camera == null || !camera.isActiveAndEnabled)
            {
                camera = Camera.main;
                _listenerCamera = camera;
            }
            if (camera == null) return;

            Transform view = camera.transform;
            // 메뉴나 캐릭터를 숨긴 씬처럼 보이는 캐릭터가 없으면 null이라 카메라 위치를 쓴다.
            GameCharacterBinding? character = SceneContext.VisibleCharacter;
            Vector3 position = character == null ? view.position : character.transform.position + Vector3.up * _listenerHeadHeight;
            _listener.transform.SetPositionAndRotation(position, view.rotation);
        }

        // 앱 해제를 거치지 않고 GameManager가 파괴될 때의 백업이다. 해제는 한 번만 실행되므로 앱이 먼저 해제했으면 아무 일도 없다.
        // 기다릴 호출자가 없어 실패는 Cleanup이 기록만 한다. 열지 못했으면 구독도 이벤트도 없어 정리할 것이 없다.
        void OnDestroy()
        {
            if (_opened) Cleanup.RunAsync(ReleaseAsync, this).Forget();
        }

        // 리스너는 이 관리자의 자식이라 함께 꺼진다. GameManager에 Object.Destroy를 부르면 리스너는 그 즉시 꺼지고 OnDestroy는
        // 프레임 끝에야 오므로(2026-10-18 측정), FMOD는 OnDestroy가 아니라 여기서 닫는다. 그 사이 RuntimeManager.Update가 돌면 리스너 누락
        // 경고가 남는다. 해제는 동기로 끝나므로 이벤트를 먼저 멈춰 닫힌 시스템의 핸들을 쓰지 않게 한다. 열지 못했으면 FMOD도 이 관리자가
        // 띄우지 않아 닫지 않고, 앱 종료 중이면 RuntimeManager가 스스로 해제하므로 건드리지 않는다.
        void OnDisable()
        {
            if (!_opened || _quitting) return;
            Cleanup.RunAsync(ReleaseAsync, this).Forget();
            Cleanup.Run(CloseFmod, this);
        }

        // FMOD 수명을 이 관리자에 맞춘다. 리스너 없이 RuntimeManager.Update가 한 프레임이라도 돌면 리스너 누락 경고가 남으므로,
        // 리스너를 먼저 끄고 같은 호출 안에서 RuntimeManager를 파괴한다. 리스너의 OnDisable은 RuntimeManager.StudioSystem을 읽어,
        // RuntimeManager를 먼저 없애면 FMOD를 다시 띄운다. UnityObjects의 지연 파괴는 다음 프레임까지 남을 수 있어 플러그인처럼 즉시 파괴한다.
        // 이미 없으면 IsInitialized가 거짓이라 새로 만들지 않는다. 다음 Open이 FMOD를 다시 시작한다.
        void CloseFmod()
        {
            if (!FMODUnity.RuntimeManager.IsInitialized) return;
            if (_listener != null && _listener.isActiveAndEnabled) _listener.enabled = false;
            // RuntimeManager는 플러그인이 숨김 오브젝트로 만들고 참조를 내주지 않으므로 플러그인과 같은 방법으로 찾는다.
            foreach (FMODUnity.RuntimeManager manager in Resources.FindObjectsOfTypeAll<FMODUnity.RuntimeManager>())
                DestroyImmediate(manager.gameObject);
        }

        // 앱 종료 때 Unity는 RuntimeManager와 이 관리자를 정해지지 않은 순서로 파괴하므로, 파괴가 시작되기 전에 이벤트를 멈추고
        // 리스너를 끈다. 리스너의 OnDisable은 RuntimeManager.StudioSystem을 읽어, RuntimeManager가 먼저 파괴됐으면 FMOD를 다시 띄운다.
        // 해제는 동기로 끝나고 기록만 하므로 결과를 기다리지 않는다.
        void OnApplicationQuit()
        {
            _quitting = true;
            if (!_opened) return;
            ReleaseAsync().Forget();
            if (_listener != null) _listener.gameObject.SetActive(false);
        }

        // ReleaseOnce가 한 번만 부르는 동기 정리다. 구독을 끊고, 보관한 이벤트를 즉시 멈춘다.
        // 리스너는 켜 둔다. FMOD가 도는 동안 리스너가 하나도 없으면 RuntimeManager가 리스너 누락 경고를 남기기 때문이다.
        // 뱅크와 FMOD 시스템은 여기서 닫지 않는다. 이 관리자가 파괴될 때 CloseFmod가, Play 종료와 앱 종료 때는 RuntimeManager가 닫는다.
        UniTask ReleaseCore()
        {
            // 앱 정리 중에는 GameManager가 먼저 사라질 수 있어 Bind가 잡아 둔 옵션 관리자에서 끊고 비운다.
            // 파괴된 관리자도 C# 이벤트가 이 핸들러를 들고 있으므로 Unity null 비교 대신 참조만 본다.
            GameOptionsManager? options = _options;
            _options = null;
            if (!ReferenceEquals(options, null)) options.Applied -= ApplyOptions;
            // 정적 이벤트는 이 관리자보다 오래 살아서 옵션 관리자 생존과 상관없이 끊는다. 구독하지 않았어도 해제는 안전하다.
            MenuSceneContext.CurrentChanged -= BindMenu;
            // Bind가 건 로드 시작 구독도 같은 이유로 여기서 끊는다.
            SceneFlow.LoadingStarted -= ForgetSceneObjects;
            if (_menu != null) _menu.MenuOpened -= OpenMenu;
            StopTitleMusic();
            var handles = new List<GameAudioEvent>(_events);
            _events.Clear();
            // 따라가던 핸들도 아래에서 함께 멈추므로 목록만 비운다.
            _following.Clear();
            // 이벤트 해제는 던지지 않고 동기로 끝나므로 완료를 기다리지 않는다.
            foreach (GameAudioEvent handle in handles)
                handle.ReleaseAsync().Forget();
            _menu = null;
            return UniTask.CompletedTask;
        }

        // 로드 시작 알림은 나가는 씬을 내리기 전에 온다. 그 씬의 리스너 카메라와 따라가던 대상을 지금 버려,
        // 로드가 끝난 뒤 LateUpdate가 파괴된 객체를 Unity null 비교하지 않게 한다. 따라가던 소리는 멈추지 않고 마지막 위치에 남는다.
        // 대상이 파괴됐을 때 UpdateFollow가 목록에서 빼던 결과와 같다. PlayAttached 대상은 모두 씬 객체다.
        void ForgetSceneObjects(GameScene scene)
        {
            _listenerCamera = null;
            _following.Clear();
        }

        // 메뉴 연출이 바뀌면 제목 음악 연결을 옮기되, 같은 메뉴로 돌아오면 음악을 다시 시작하지 않는다.
        // null로 불릴 때 앞 메뉴가 이미 파괴됐을 수 있어, 앞 메뉴의 연출은 Unity null 비교 뒤에만 만진다.
        void BindMenu(MenuSceneContext? context)
        {
            OriginalMenuPresentation? next = context == null ? null : context.Presentation;
            if (ReferenceEquals(_menu, next)) return;
            if (_menu != null) _menu.MenuOpened -= OpenMenu;
            StopTitleMusic();
            _menu = next;
            if (_menu == null) return;
            _menu.MenuOpened += OpenMenu;
            if (_menu.IsMenuOpen) StartTitleMusic();
        }

        // 원본 ContinueTask처럼 타이틀 승인 시점에 시작음과 제목 음악을 함께 재생한다.
        void OpenMenu()
        {
            PlayOneShot(_gameStartEvent);
            StartTitleMusic();
        }

        // 재생 실패로 null이면 Play가 한 번 기록하고, 다음 메뉴 열기에서 다시 시도한다.
        void StartTitleMusic() => _titleMusic ??= Play(_titleMusicEvent);

        // 정지가 실패해도 같은 핸들을 다시 정지하지 않도록 먼저 비운다. 정지는 던지지 않고 실패를 한 번 기록한다.
        void StopTitleMusic()
        {
            GameAudioEvent? music = _titleMusic;
            _titleMusic = null;
            music?.Stop();
        }

        // 옵션 화면에서 확정한 볼륨을 원본 믹서 버스에 맞춘다.
        void ApplyOptions(GameOptions options) => ApplyVolumes(options.MasterVolume, options.MusicVolume, options.SfxVolume);

        // 인스턴스를 만든 뒤 3D 위치와 시작 중 하나라도 실패하면 finally가 공개 전 인스턴스를 즉시 정지하고 해제한다.
        // 던지지 않고 false를 돌려준다. 열기 전과 해제 뒤에는 기록하지 않고, FMOD 오류는 동작과 경로마다 한 번 기록한다.
        bool TryStart(string eventPath, Vector3? position, out FMOD.Studio.EventInstance instance)
        {
            instance = default;
            if (!CanCommand())
                return false;

            EventInfo? info = Describe(eventPath);
            if (info == null)
                return false;
            FMOD.Studio.EventDescription description = info._description;
            FMOD.Studio.EventInstance created = default;
            bool started = false;
            try
            {
                if (!Succeeded(description.createInstance(out created), "Create", eventPath))
                    return false;
                // 3D 여부는 인스턴스를 만든 뒤에 읽고, 처음 성공한 값만 캐시해 다음 호출부터 다시 읽지 않는다.
                if (info._spatial == null)
                {
                    if (!Succeeded(description.is3D(out bool read3D), "Read 3D flag", eventPath))
                        return false;
                    info._spatial = read3D;
                }
                bool spatial = info._spatial.Value;
                if ((spatial || position.HasValue) && !Succeeded(
                        created.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(position ?? Vector3.zero)), "Set position", eventPath))
                    return false;
                if (!Succeeded(created.start(), "Start", eventPath))
                    return false;
                started = true;
                instance = created;
                return true;
            }
            finally
            {
                if (!started && created.hasHandle())
                {
                    Succeeded(created.stop(FMOD.Studio.STOP_MODE.IMMEDIATE), "Stop failed", eventPath);
                    Succeeded(created.release(), "Release failed", eventPath);
                }
            }
        }

        // 이벤트 설명자를 캐시에서 꺼내고, 없으면 getEvent로 조회해 성공한 것만 담는다. 조회 실패는 한 번 기록하고 null을 돌려준다.
        EventInfo? Describe(string eventPath)
        {
            if (!_eventInfos.TryGetValue(eventPath, out EventInfo? info))
            {
                if (!Succeeded(_system.getEvent(eventPath, out FMOD.Studio.EventDescription description), "Resolve", eventPath))
                    return null;
                info = new EventInfo(description);
                _eventInfos.Add(eventPath, info);
            }
            return info;
        }

        // 한 이벤트 경로에서 성공한 조회 결과다. 3D 여부는 null이면 아직 읽지 않은 것이고, 인스턴스를 만든 뒤 처음 성공했을 때 채운다.
        sealed class EventInfo
        {
            internal readonly FMOD.Studio.EventDescription _description;
            internal bool? _spatial;

            internal EventInfo(FMOD.Studio.EventDescription description) => _description = description;
        }

        // 핸들 하나가 끝나면 추적에서만 빼고 다른 이벤트에는 손대지 않는다.
        internal void Forget(GameAudioEvent handle) => _events.Remove(handle);

        // 재생과 설정 명령이 먼저 거치는 판정이다. 열지 못했거나 해제가 시작됐으면 기록 없이 false다. 이벤트 핸들도 이 판정을 먼저 거친다.
        internal bool CanCommand() => _opened && (_release == null || !_release.Started);

        // 정리 단계 하나가 실패해도 뒤 단계의 네이티브 해제를 계속한다. 호출 중 예외는 Cleanup.Run이 기록하고,
        // FMOD 실패 결과는 동기 명령과 같은 형식으로 동작과 경로마다 한 번 기록한다. 던지지 않는다.
        internal void ReleaseStep(Func<FMOD.RESULT> step, string operation, string? path = null)
        {
            FMOD.RESULT result = FMOD.RESULT.OK;
            if (Cleanup.Run(() => result = step()))
                Succeeded(result, operation, path);
        }

        // 동기 명령의 FMOD 결과를 판정한다. 실패면 동작과 경로마다 한 번만 기록하고 false를 돌려준다. 성공 경로는 할당하지 않는다.
        internal bool Succeeded(FMOD.RESULT result, string operation, string? path = null)
        {
            if (result == FMOD.RESULT.OK)
                return true;
            if (ShouldReport(operation, path))
                Log.Instance.Error(path == null
                    ? $"{operation}: {result} ({FMOD.Error.String(result)})"
                    : $"{operation} {Printable(path)}: {result} ({FMOD.Error.String(result)})");
            return false;
        }

        // 이 관리자에서 같은 동작과 경로의 실패를 처음 보면 true다. 호출자는 true일 때만 메시지를 만들어 기록한다.
        // 동작 이름에는 '|'가 없어 첫 '|'가 경계가 되므로 키가 겹치지 않는다. null 경로는 빈 경로와 같은 키다.
        internal bool ShouldReport(string operation, string? path) => _reported.First(operation + "|" + path);

        // 로그에 넣을 경로의 널 문자를 보이는 표기로 바꾼다. 기록할 때만 부른다.
        static string Printable(string? text) => text == null ? "null" : text.Replace("\0", "\\0");
    }
}
