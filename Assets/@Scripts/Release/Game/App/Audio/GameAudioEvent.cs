#nullable enable annotations
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 반복 재생하거나 제어할 FMOD 이벤트의 핸들이다. 호출자가 일시정지와 정지를 고르고, ReleaseAsync는 즉시 정지한다.
    // 마지막 해제는 GameAudioManager 해제가 보장한다. 3D 위치는 시작할 때 정하고, PlayAttached로 시작했으면 관리자가 대상을 따라 옮긴다.
    public sealed class GameAudioEvent : IAsyncReleasable
    {
        // 원본 AudioObject처럼 소리가 따라갈 대상이다. 없으면 시작 위치에 머문다.
        Transform? _follow;
        // 오디오 관리자가 해제된 뒤 이 이벤트로 명령하지 않도록 소유 관리자를 기억한다.
        readonly GameAudioManager _owner;
        // 호출자가 FMOD 원시 핸들을 직접 해제하지 못하게 내부에 보관한다.
        FMOD.Studio.EventInstance _instance;
        // 종료와 네이티브 핸들 반환을 한 번만 한다. 종료가 시작되면 이후 명령은 무시한다.
        readonly ReleaseOnce _release;
        // 첫 종료 요청이 고른 정지 방식이다. Stop만 페이드아웃을 고를 수 있고, ReleaseAsync와 앱 종료는 즉시 정지한다.
        bool _allowFadeout;
        // FMOD 이벤트 경로다. 실패 기록을 동작과 경로마다 한 번으로 묶고, 메시지에 어느 이벤트인지 남긴다.
        readonly string _path;

        // GameAudioManager는 시작에 성공한 인스턴스만 이 핸들로 감싸 공개한다.
        internal GameAudioEvent(GameAudioManager owner, FMOD.Studio.EventInstance instance, string path)
        {
            _owner = owner;
            _instance = instance;
            _path = path;
            _release = new ReleaseOnce(ReleaseCore);
        }

        // 종료 뒤에는 예외 없이 무시하고, FMOD 오류는 동작과 경로마다 한 번만 기록한다.
        public void SetPaused(bool paused)
        {
            if (CanCommand())
                _owner.Succeeded(_instance.setPaused(paused), "Event.setPaused", _path);
        }

        // 페이드아웃 여부와 관계없이 네이티브 핸들을 반환하고 관리자 추적에서 제거한다. 예외를 던지지 않는다.
        // 종료가 이미 시작됐으면 무시하므로 첫 요청의 정지 방식만 쓰인다.
        public void Stop(bool allowFadeout = true)
        {
            if (_release.Started)
                return;
            _allowFadeout = allowFadeout;
            _release.ReleaseAsync().Forget();
        }

        // 리소스 범위나 앱 종료가 부르는 종료로, 즉시 정지한다. 반복 요청도 같은 완료를 받고 예외를 던지지 않는다.
        public UniTask ReleaseAsync() => _release.ReleaseAsync();

        // GameAudioManager.PlayAttached만 부른다. 이후 위치 갱신은 관리자의 LateUpdate가 한 번에 한다.
        internal void Attach(Transform target) => _follow = target;

        // 따라갈 대상 위치로 3D 위치를 옮긴다. 이벤트가 끝났거나 대상이 파괴됐으면 false라 관리자가 목록에서 뺀다.
        // FMOD 오류는 동작과 경로마다 한 번 기록하고 계속 따라간다.
        internal bool UpdateFollow()
        {
            if (!CanCommand() || _follow == null) return false;
            _owner.Succeeded(_instance.set3DAttributes(FMODUnity.RuntimeUtils.To3DAttributes(_follow.position)), "Event.set3DAttributes", _path);
            return true;
        }

        // ReleaseOnce가 한 번만 부르는 정리로, 동기로 끝난다. 추적에서 제거한 뒤 정지와 반환을 따로 실행해
        // 정지가 실패해도 반환은 진행한다. 각 실패는 동작과 경로마다 한 번 기록한다.
        UniTask ReleaseCore()
        {
            _owner.Forget(this);
            FMOD.Studio.STOP_MODE mode = _allowFadeout ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE;
            _owner.ReleaseStep(() => _instance.stop(mode), "Event.stop", _path);
            _owner.ReleaseStep(() => _instance.release(), "Event.release", _path);
            _instance.clearHandle();
            return UniTask.CompletedTask;
        }

        // 개별 종료나 앱 종료 뒤 남은 핸들로 네이티브 명령을 보내지 않도록 예외 없이 판정한다.
        bool CanCommand() => _owner.CanCommand() && !_release.Started;
    }
}

