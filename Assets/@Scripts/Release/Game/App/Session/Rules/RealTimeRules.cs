#nullable enable
using System;
using Game.Core;

namespace Game.App
{
    // 원작이 실제 UTC로 세는 대기(특성 연구, 상점 재고 새로고침)의 시계 역행 규칙이다. 원작 Perk.UnlockingBeginTime처럼
    // 기록한 시각이 지금보다 미래면 지금으로 당겨 대기를 처음부터 다시 세고, 원작 GameManager.TimeTravelDetected처럼 한 줄을 남긴다.
    // 시계를 앞으로 돌려 대기를 끝내는 것은 원작처럼 막지 않는다. 당긴 값을 기록에 다시 쓰는 일은 호출자가 맡는다. 메인 스레드에서만 쓴다.
    internal static class RealTimeRules
    {
        // recordedUtc가 utcNow보다 미래면 utcNow로 바꾸고 true다. 아니면 그대로 두고 false다. 기록하지 않는다.
        // 매 프레임 남은 시간을 묻는 화면 경로도 쓰므로, 당겼을 때만 호출자가 ReportTimeTravel로 대상 이름을 만들어 남긴다.
        internal static bool ClampToNow(ref DateTime recordedUtc, DateTime utcNow)
        {
            if (recordedUtc <= utcNow) return false;
            recordedUtc = utcNow;
            return true;
        }

        // 원작 TimeTravelDetected의 한 줄 기록이다. 당긴 값이 기록에 다시 쓰여 같은 대상은 다음 확인에서 미래가 아니므로, 역행 한 번에 한 번 남는다.
        // subject는 당긴 대상(트리/특성 이름, 상점 키), context는 그 소유 컴포넌트다.
        internal static void ReportTimeTravel(string subject, UnityEngine.Object context) =>
            Log.Instance.Warning("기록한 시각이 지금보다 미래라 지금으로 당겼습니다(시계 역행): " + subject, context);
    }
}
