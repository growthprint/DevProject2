#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Game.App
{
    // 도입부 진행 단계다. 파일명이나 배열 순서에 기대지 않도록 각 값을 명시한다.
    public enum CampaignStage { Prepare = 0, Intro = 1, Prologue = 2, Tutorial = 3, BaseUnlocked = 4 }

    // 도입부 진행 단계와 체크포인트다. 체크포인트는 월드가 실제로 확정한 것만 담는다.
    public sealed class CampaignProgress
    {
        // 단계 상수나 규칙 계산처럼 코드가 만든 값용 생성자다. 검증하지 않으며, 저장 파일에서 읽은 값은 TryCreate를 쓴다.
        public CampaignProgress(CampaignStage stage, string? checkpointId)
        {
            Stage = stage;
            _checkpointId = checkpointId;
        }

        // CampaignEventLog가 사건을 더한 진행 값이다. 처치와 루팅마다 집합 전체를 다시 잇지 않도록 사건 집합만 들고,
        // 저장 문자열은 CheckpointId를 처음 읽을 때 만든다. 단계는 이미 검사한 진행 값에서 그대로 받으므로 다시 검사하지 않는다.
        internal static CampaignProgress FromEvents(CampaignStage stage, CampaignEventLog.Parsed events) =>
            new CampaignProgress(stage, events);

        // FromEvents만 쓴다. 밖에 열면 체크포인트 없이 만드는 'new CampaignProgress(단계, null)' 호출이 모호해진다.
        CampaignProgress(CampaignStage stage, CampaignEventLog.Parsed events)
        {
            Stage = stage;
            _events = events;
            _eventCache = events;
        }

        // TryCreate가 쓰는 검사다. 체크포인트 ID는 null을 허용하지만 빈 값이나 공백은 거절한다.
        static bool Check(CampaignStage stage, string? checkpointId) =>
            stage >= CampaignStage.Prepare && stage <= CampaignStage.BaseUnlocked &&
            (checkpointId == null || !string.IsNullOrWhiteSpace(checkpointId));

        // 저장 파일에서 읽은 값용 생성 경로다. 규칙에 어긋나면 던지지 않고 false를 돌려준다.
        public static bool TryCreate(CampaignStage stage, string? checkpointId, [NotNullWhen(true)] out CampaignProgress? value)
        {
            value = Check(stage, checkpointId) ? new CampaignProgress(stage, checkpointId) : null;
            return value != null;
        }

        public CampaignStage Stage { get; }

        // 체크포인트를 기록하지 않았으면 null이다. 사건 집합으로 만든 진행 값은 저장이나 비교로 처음 읽을 때 문자열을 만든다.
        // 저장 직렬화는 스레드 풀에서 읽으므로, 두 스레드가 함께 만들어도 먼저 넣은 하나만 남도록 CompareExchange로 넣는다.
        public string? CheckpointId
        {
            get
            {
                if (_checkpointId == null && _events != null)
                    Interlocked.CompareExchange(ref _checkpointId, CampaignEventLog.Format(_events), null);
                return _checkpointId;
            }
        }

        // 체크포인트 문자열이다. 사건 집합으로 만든 진행 값은 CheckpointId를 처음 읽기 전까지 null이다.
        internal string? _checkpointId;
        // 사건 집합으로 만든 진행 값의 원래 집합이다. 캐시와 달리 다른 출격 범위로 조회해도 바뀌지 않아, 문자열을 늘 같게 만든다.
        readonly CampaignEventLog.Parsed? _events;

        // 프롤로그 거울에서 외형 편집을 닫았다는 체크포인트다. 출구 개방과 튜토리얼 진행의 조건이다.
        // 저장 파일에 이 문자열이 남으므로 값을 바꾸지 않는다.
        internal const string _prologueMirrorClosed = "prologue:mirror-closed";
        // 튜토리얼 탈출로 기지 해금 단계에 들어갈 때 남기는 체크포인트다. 저장 파일에 이 문자열이 남으므로 값을 바꾸지 않는다.
        internal const string _tutorialCompleted = "tutorial:completed";

        // CampaignEventLog가 쓰는 파싱 캐시다. 값의 일부가 아니며, CheckpointId 내용이 불변이라 언제든 다시 만들 수 있다.
        internal CampaignEventLog.Parsed? _eventCache;
    }
}
