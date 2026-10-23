#nullable enable annotations
using System;

namespace Game.App
{
    // 씬이 소유한 반복음 하나를 시작, 재시작, 일시정지, 정지한다. 프롤로그 배경음과 인트로 연출음이 쓴다.
    // 오디오 관리자 명령은 던지지 않고 실패를 스스로 기록하므로, 이 타입에는 예외 경계가 없다.
    internal sealed class SceneLoopAudio : IDisposable
    {
        // 앱 수명 오디오 관리자다. 오디오가 꺼져 있으면 재생 요청에 null을 돌려준다.
        readonly GameAudioManager _audio;
        readonly string _eventPath;
        GameAudioEvent? _event;
        bool _disposed;

        // 오디오가 꺼져 있으면 핸들이 생기지 않아 모든 명령이 아무 일도 하지 않는다. 이벤트 경로는 씬 컨트롤러의 직렬화 값이나 상수다.
        internal SceneLoopAudio(GameAudioManager audio, string eventPath)
        {
            _audio = audio;
            _eventPath = eventPath;
        }

        // 이벤트가 없을 때만 시작하므로, 다시 켜질 때는 같은 이벤트를 이어 쓴다.
        // 시작에 실패하면 핸들이 null로 남아 다음 Start에서 다시 시도한다.
        internal void Start() { if (_event == null) Restart(); }

        // 현재 이벤트를 즉시 정지하고 처음부터 다시 재생한다.
        internal void Restart()
        {
            if (_disposed) return;
            StopCurrent();
            // 오디오가 꺼져 있으면 관리자가 null을 돌려주므로, 핸들이 비어 다음 Start가 다시 시도한다.
            _event = _audio.Play(_eventPath);
        }

        // 재생 위치를 유지한 채 멈추거나 재개한다. 이미 끝난 이벤트는 GameAudioEvent가 무시한다.
        internal void SetPaused(bool paused)
        {
            GameAudioEvent? current = _event;
            if (_disposed || current == null) return;
            current.SetPaused(paused);
        }

        // 여러 번 불러도 한 번만 정지한다.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopCurrent();
        }

        // 참조를 먼저 비운 뒤 즉시 정지한다. 정지가 실패해도 같은 핸들을 다시 쓰지 않게 하기 위해서다.
        void StopCurrent()
        {
            GameAudioEvent? current = _event;
            _event = null;
            current?.Stop(false);
        }
    }
}
