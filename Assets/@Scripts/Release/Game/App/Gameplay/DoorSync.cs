#nullable enable annotations
using System;

namespace Game.App
{
    // 원본 Door처럼 저작한 문 지점의 열림을 확정된 사건에 맞추고 문 소리를 낸다. 전투 월드 컨트롤러가 준비 때 만들어 소유한다.
    // 첫 월드 초기화가 Restore로 저장된 상태를 복원한 뒤 문 소리를 구독하고, 레벨 정리가 Dispose로 그 구독을 푼다.
    internal sealed class DoorSync : IDisposable
    {
        readonly GameplayWorldContext _world;
        // Restore가 건 문 소리 구독의 해제 동작이다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        // 문 열림과 닫힘의 FMOD 이벤트 경로다. 여닫을 때마다 경로를 잇지 않도록 완성된 문자열로 둔다.
        const string _openSound = "event:/SFX/Actions/door_normal_open";
        const string _closeSound = "event:/SFX/Actions/door_normal_close";

        internal DoorSync(GameplayWorldContext world)
        {
            _world = world!;
        }

        // 저장된 문 상태는 즉시 모드로 소리 없이 복원한다. 문 소리는 그 뒤에 구독하므로 실제로 열고 닫을 때만 난다.
        // 첫 월드 초기화가 한 번 부른다. 오디오 관리자는 앱 수명이라 한 번 읽어 문 소리 콜백이 함께 쓰며, 꺼져 있으면 소리를 조용히 무시한다.
        internal void Restore()
        {
            GameAudioManager audio = _world.Audio;
            foreach (Door door in _world.Doors)
            {
                // 복원과 동기화가 같은 열림 조건을 쓴다. 조건이 없는 문은 원작 closed 초기값을 따른다.
                door.SetOpen(door.StartOpen || RequiredEventComplete(door), true);
                // 원작 hasSound가 꺼진 문은 소리를 구독하지 않는다.
                if (!door.HasSound) continue;
                // 열림 여부로 두 경로 중 하나를 고른다. 원작 문마다 다른 소리(스위치 등)가 있으면 그 경로를 쓴다.
                string open = door.OpenSound.Length != 0 ? door.OpenSound : _openSound;
                string close = door.CloseSound.Length != 0 ? door.CloseSound : _closeSound;
                Action<bool> sound = opened => audio.PlayOneShot(opened ? open : close, door.transform.position);
                door.Changed += sound;
                _subscriptions.Add(() => door.Changed -= sound);
            }
        }

        // 첫 복원 뒤로 조건부 문을 여는 유일한 경로다. 확정된 세션만 근거로 삼아, 커밋이 거절된 사건으로는 문이 열리지 않는다.
        // 세션 변경 알림마다 불리므로 이미 열린 문은 다시 평가하지 않는다. 커밋이 알림을 동기로 내서 처치한 프레임에 바로 열린다.
        internal void Sync()
        {
            foreach (Door door in _world.Doors)
                // 복원과 동기화가 같은 열림 조건을 쓴다.
                if (!door.Opened && RequiredEventComplete(door)) door.SetOpen(true);
        }

        // 조건 사건이 있는 문만 그 사건이 확정됐을 때 연다. 조건 없는 문은 F 손잡이로만 여닫는다.
        bool RequiredEventComplete(Door door) =>
            door.RequiredEvent.Length > 0 && _world.TutorialEventComplete(door.RequiredEvent);

        // 레벨 정리가 부른다. 해제마다 실패를 기록하고 계속하므로 던지지 않으며, 여러 번 불러도 된다.
        public void Dispose() => _subscriptions.Clear();
    }
}
