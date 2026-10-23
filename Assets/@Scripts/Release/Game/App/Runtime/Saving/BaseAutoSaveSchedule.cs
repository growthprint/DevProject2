#nullable enable
namespace Game.App
{
    // 원본 BaseSceneUtilities의 기지 자동 저장 일정을, 호출자가 넘기는 실시간(초)만 받는 순수 상태로 재현한다.
    // 기지 진입이나 저장 직후부터 쿨다운이 지나야 저장한다. 쿨다운 동안의 요청은 하나로 모여 만료 뒤 한 번만 저장된다.
    internal sealed class BaseAutoSaveSchedule
    {
        // 원본 saveCoolTime이다.
        internal const double _cooldown = 5d;
        // 원본 TrySetDirty의 무시 구간(saveCoolTime - saveCoolTimer < 0.5)이다.
        internal const double _ignoreAfterReset = .5d;

        // 마지막 초기화 시각과 저장 요청 여부다.
        double _resetAt;
        bool _requested;

        internal bool IsRequested => _requested;
        // 쌓인 요청을 저장할 수 있는 가장 이른 시각이다.
        internal double DueAt => _resetAt + _cooldown;

        // 쿨다운을 지금부터 다시 시작하고 쌓인 요청을 버린다. 기지 월드를 확정할 때와 TryTake가 부른다.
        internal void Reset(double now)
        {
            _resetAt = now;
            _requested = false;
        }

        // UI 활동의 저장 요청이다. 초기화 뒤 0.5초 안이면 무시하고 false를 돌려준다.
        internal bool Request(double now)
        {
            if (now - _resetAt < _ignoreAfterReset) return false;
            _requested = true;
            return true;
        }

        // 요청이 있고 쿨다운이 끝났으면 요청을 소비하고 true다. 원본처럼 저장 직후 쿨다운을 다시 시작한다.
        internal bool TryTake(double now)
        {
            if (!_requested || now < DueAt) return false;
            Reset(now);
            return true;
        }
    }
}
