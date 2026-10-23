#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 MapMarkerManager처럼 플레이어가 지도에 찍은 마커를 모든 씬에 걸쳐 소유한다. GameManager 프리팹 자식의 컴포넌트이고
    // GameManager.Markers로 찾는다. 저장할 때 마커 구역을 써 넣고, 기록을 불러오면 읽어 간다. 메인 스레드에서만 쓴다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed class MapMarkers : SessionSectionOwner
    {
        // 목록이 바뀌었다는 신호만 보낸다. 구독자는 All을 직접 읽는다.
        public static event Action? Changed;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => Changed = null;

        // 추가, 삭제, 불러오기에서만 새 목록으로 갈아 끼운다. 그래서 밖에 준 목록은 바뀌지 않는다. 기록을 불러오기 전에는 비어 있다.
        IReadOnlyList<MapMarkerState> _markers = Array.Empty<MapMarkerState>();

        // 모든 씬의 마커다. 지도가 매 프레임 읽고 참조가 바뀔 때만 다시 그리므로, 목록이 바뀔 때까지 같은 참조를 준다.
        public IReadOnlyList<MapMarkerState> All => _markers;

        // 저장마다 마커 구역을 한 번 쓴다.
        private protected override void WriteSection(GameSaveData data) => data.SetMapMarkers(_markers);

        // 불러온 기록의 마커로 바꾼 뒤 알린다. 기록의 목록은 검증된 불변 값이라 복사하지 않는다.
        private protected override void ReadSection(GameSaveData data)
        {
            _markers = data.MapMarkers;
            ObserverDispatch.InvokeEach(Changed);
        }

        // 지도 마커를 추가한다. 유일한 호출자인 지도 화면이 열린 지도의 씬과 새 GUID로 만든 마커만 넘긴다.
        public bool TryAdd(MapMarkerState marker)
        {
            if (!AcceptsCommands) return false;
            // 새 목록을 배열 하나로 만들어 그대로 보관한다. 밖에 준 목록은 그대로 두고 참조만 바꿔 지도가 다시 그리게 한다.
            Commit(ReadOnlyLists.Appended(_markers, marker));
            return true;
        }

        // 지도 마커를 삭제한다. 없는 ID는 바꿀 것이 없어 성공이다.
        // 지도 화면은 열린 지도의 씬 마커만 보여 주고 그중에서 고르게 한다.
        public bool TryRemove(string id)
        {
            if (!AcceptsCommands) return false;
            IReadOnlyList<MapMarkerState> markers = _markers;
            MapMarkerState? target = markers.FirstOrDefault(value => value.Id == id);
            if (target == null) return true;
            Commit(markers.Where(value => value.Id != id).ToArray());
            return true;
        }

        // 마커는 지도 화면이 만든 값이라 다시 검증하지 않는다. 호출자가 방금 만든 배열이라 복사하지 않고 읽기 전용으로 감싸 보관한 뒤 알린다.
        void Commit(MapMarkerState[] markers)
        {
            _markers = Array.AsReadOnly(markers);
            ObserverDispatch.InvokeEach(Changed);
        }
    }
}
