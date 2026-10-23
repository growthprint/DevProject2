#nullable enable
namespace Game.App
{
    // 출격 방식이다. 기지 벙커 문(SceneLoaderTarget.Sortie)은 씬에 저작된 캐릭터 자리(원작 Special/BunkerEntry 위치)에서 시작하고,
    // 텔레포터 지도 선택은 원작 LevelManager.GetPlayerStartLocation처럼 비콘 번호의 출발점 목록에서 무작위로 고른다.
    internal enum RaidEntryKind
    {
        BunkerDoor = 0,
        MapSelection = 1,
    }

    // 원작 LevelManager.loadLevelBeaconIndex 자리의 값이다. 원작처럼 정적 메모리 값이라 저장하지 않는다.
    // SceneFlow.BeginRaidAsync(RaidEntry)가 출격 준비를 통과할 때 넣고, 출격 월드 초기화가 SceneFlow.TakeRaidEntry로 한 번 꺼낸다.
    // 기본값은 벙커 문 출격이다.
    internal readonly struct RaidEntry
    {
        RaidEntry(RaidEntryKind kind, int beaconIndex)
        {
            Kind = kind;
            BeaconIndex = beaconIndex;
        }

        internal RaidEntryKind Kind { get; }
        // 원작 MapSelectionEntry.beaconIndex다. 0이면 StartPoints, n이면 StartPoints_n을 먼저 찾는다. 벙커 문은 0이다.
        internal int BeaconIndex { get; }

        internal static RaidEntry BunkerDoor => default;

        // 번호는 지도 선택 화면이 저작 데이터(MapSelectionData)에서 그대로 넘기는 0 이상의 값이라 다시 고치지 않는다.
        internal static RaidEntry MapSelection(int beaconIndex) => new RaidEntry(RaidEntryKind.MapSelection, beaconIndex);
    }
}
