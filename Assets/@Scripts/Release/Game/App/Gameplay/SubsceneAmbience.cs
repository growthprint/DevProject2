#nullable enable annotations

namespace Game.App
{
    // 원작 AudioManager.OnSubSceneLoaded의 하위 씬 앰비언스다. 하위 씬을 켤 때마다 지금 소리를 바로 멈추고 그 하위 씬의 소리를 낸다.
    // 원작 OnStartedLoadingScene처럼 씬 로드가 시작되거나 레벨을 정리할 때도 멈춘다. 전투 월드 컨트롤러가 준비 때 만들어 소유한다.
    internal sealed class SubsceneAmbience
    {
        // 원작 경로 형식 "Amb/amb_{soundkey}"의 FMOD 이벤트 경로 앞부분이다.
        const string _eventPrefix = "event:/Amb/amb_";

        readonly GameAudioManager _audio;
        GameAudioEvent? _playing;

        // 레벨 정리 때는 앱이 먼저 사라졌을 수 있어, 레벨 초기화가 앱에서 읽은 오디오 관리자를 받아 둔다.
        internal SubsceneAmbience(GameAudioManager audio) => _audio = audio;

        // 원작처럼 하위 씬 키를 소문자로 바꿔 이벤트 경로를 만든다. 로캘과 상관없이 같은 경로가 나오게 불변 문화 규칙을 쓴다.
        internal static string EventPath(string key) => _eventPrefix + key.ToLowerInvariant();

        // 원작 ambientSource.StopAll()은 즉시 멈춤이라 페이드 없이 멈춘 뒤 새 소리를 낸다. 오디오가 꺼져 있으면 관리자가 null을 돌려준다.
        internal void Play(string key)
        {
            Stop();
            _playing = _audio.Play(EventPath(key));
        }

        // 내는 소리가 없으면 아무것도 하지 않아 여러 번 불러도 된다. 관리자가 먼저 해제됐으면 핸들이 명령을 무시한다.
        internal void Stop()
        {
            GameAudioEvent? playing = _playing;
            _playing = null;
            if (playing != null) playing.Stop(false);
        }
    }
}
