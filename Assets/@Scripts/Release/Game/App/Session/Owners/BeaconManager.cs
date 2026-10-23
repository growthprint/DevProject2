#nullable enable
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 BeaconManager처럼 지도 선택 비콘의 해금을 소유한다. GameManager 프리팹 자식의 컴포넌트이고 GameManager.Beacons로 찾는다.
    // 저장 필드는 unlockedBeacons이고 Beacons 구역으로 쓴다. 해금된 비콘은 다시 잠기지 않는다. 메인 스레드에서만 쓴다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed class BeaconManager : SessionSectionOwner
    {
        // 처음 해금한 비콘의 ID와 번호를 알린다. 2-Q3의 비콘 해금 퀘스트 과제가 듣는다.
        public static event Action<string, int>? Unlocked;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => Unlocked = null;

        // 해금한 순서의 목록이다. 비콘은 원작 범위에서 몇 개뿐이라 색인 없이 훑는다.
        readonly List<BeaconUnlockState> _unlocked = new List<BeaconUnlockState>();
        // 옛 저장의 null(미포착)을 빈 목록으로 바꿔 쓰지 않도록, 첫 해금 전까지는 null로 되돌려 쓴다. 새 게임은 빈 목록이다.
        bool _captured = true;

        // 원작 BeaconManager.GetBeaconUnlocked다. 5-G의 지도 선택 화면과 2-Q3의 과제 판정이 읽는다.
        internal bool IsUnlocked(string beaconId, int beaconIndex)
        {
            foreach (BeaconUnlockState beacon in _unlocked)
                if (beacon.BeaconIndex == beaconIndex && string.Equals(beacon.BeaconId, beaconId, StringComparison.Ordinal)) return true;
            return false;
        }

        // 원작 BeaconManager.UnlockBeacon이다. 5-C의 TeleportBeacon이 부른다. 이미 해금이면 바꾸지 않고 성공한다.
        // 빈 ID나 음수 번호는 저장 규칙에 맞지 않아 기록 없이 거절한다. 처음 해금일 때만 Unlocked를 낸다.
        internal bool TryUnlock(string beaconId, int beaconIndex)
        {
            if (!AcceptsCommands) return false;
            if (IsUnlocked(beaconId, beaconIndex)) return true;
            if (!BeaconUnlockState.TryCreate(beaconId, beaconIndex, out BeaconUnlockState? beacon)) return false;
            _unlocked.Add(beacon);
            _captured = true;
            ObserverDispatch.InvokeEachValue(Unlocked, beaconId, beaconIndex);
            return true;
        }

        // 기반 클래스의 저장 수집이 부른다.
        private protected override void WriteSection(GameSaveData data) => data.SetBeacons(_captured ? _unlocked.ToArray() : null);

        // 불러온 기록의 목록으로 바꾼다. 기록의 목록은 검증된 불변 값이다.
        private protected override void ReadSection(GameSaveData data)
        {
            IReadOnlyList<BeaconUnlockState>? saved = data.UnlockedBeacons;
            _unlocked.Clear();
            _captured = saved != null;
            if (saved != null) _unlocked.AddRange(saved);
        }
    }
}
