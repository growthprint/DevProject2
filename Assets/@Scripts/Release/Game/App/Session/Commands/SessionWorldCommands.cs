#nullable enable

namespace Game.App
{
    // 처치나 대사 완료처럼 같은 씬 안에서 메모리에만 확정하는 월드 기록 명령이다. 파일은 다음 저장 지점이 쓴다.
    // 시계, 지도 마커, 경험치는 각 상태 소유자가 맡는다. 게이트나 게임 규칙에 걸리면 기록 없이 false다.
    internal sealed class SessionWorldCommands
    {
        readonly GameSessionManager _session;

        internal SessionWorldCommands(GameSessionManager session) => _session = session;

        // 처치나 대사 완료를 세션 메모리에 확정한다. 경험치는 GameplayCommands가 newlyCompleted를 보고 경험치 관리자에 한 번만 준다.
        // 사건 키는 씬 데이터 테스트가 형식을 확인한 저작 데이터라 재검증 없는 경로로 확정한다.
        internal bool TryCompleteEvent(string eventId, out bool newlyCompleted)
        {
            newlyCompleted = false;
            // 거절 조건을 CanCompleteEvent 한곳에 두어, 미리 확인하는 명령과 실제 기록이 같은 결과를 낸다.
            if (!CanCompleteEvent(eventId, out bool alreadyComplete)) return false;
            if (alreadyComplete) return true;
            GameSessionSnapshot snapshot = _session.Snapshot();
            CampaignState campaign = snapshot.Campaign;
            // Add는 단계를 그대로 둔 진행 값을 주므로 WithProgress는 출격 불변식에 걸리지 않는다.
            _session.Commit(snapshot.WithCampaign(campaign.WithProgress(CampaignEventLog.Add(campaign, eventId))));
            newlyCompleted = true;
            return true;
        }

        // 여러 소유자를 함께 확정하는 명령(GameplayCommands.TryRecordKill)이 적용 전에 사건 거절을 기록 없이 미리 본다.
        // TryCompleteEvent도 이 조건을 그대로 쓰므로, 확인을 통과한 같은 호출 안의 기록은 거절되지 않는다.
        internal bool CanCompleteEvent(string eventId, out bool alreadyComplete)
        {
            alreadyComplete = false;
            if (!_session.AcceptsCommands) return false;
            CampaignState campaign = _session.Snapshot().Campaign;
            // 튜토리얼이나 출격 중이 아니면 사건 집합이 없어 기록 없이 거절한다.
            if (!CampaignEventLog.IsRecording(campaign)) return false;
            // 이미 완료된 사건은 다시 기록하지 않고 성공한다. 좌표만 다른 같은 원본 처치도 완료로 본다.
            alreadyComplete = CampaignEventLog.IsComplete(campaign, eventId);
            return true;
        }
    }
}
