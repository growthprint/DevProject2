#nullable enable

namespace Game.App
{
    // 적 처치 하나를 처치 수(KillCounter), 처치 사건(세션), 경험치(ExperienceManager)에 확정하는 명령이다.
    // 원작 CharacterMainControl.OnDead의 처치 수와 Health.Hurt의 처치 경험치를 한 명령으로 묶어, 사건이 거절되거나 이미 완료됐을 때
    // 처치 수만 오르는 일을 막는다.
    internal static partial class GameplayCommands
    {
        // 세 소유자를 모두 확인한 뒤 처치 수, 사건, 경험치 순서로 적용한다. 처치 수를 사건 커밋보다 먼저 올려,
        // 커밋이 동기로 내는 세션 변경 알림을 받는 쪽도 새 처치 수를 읽는다. 원작처럼 플레이어가 죽인 적만 프리셋 경험치를 주고 처치 수를 센다.
        // 이미 완료한 처치 사건은 아무것도 바꾸지 않고 성공한다. 관문이 닫혔거나, 튜토리얼과 출격 밖이거나, 경험치가 넘치면 기록 없이 false다.
        internal static bool TryRecordKill(GameSessionManager session, ExperienceManager experience, KillCounter kills, string eventId,
            string enemyKey, bool fromPlayer, long presetExperience)
        {
            long reward = fromPlayer ? presetExperience : 0;
            if (!experience.CanAdd(reward)) return false;
            // 사건 소유자의 확인 명령을 써서, 기록 명령과 거절 조건이 어긋나 사건 없이 처치 수만 오르는 일을 막는다.
            if (!session.World.CanCompleteEvent(eventId, out bool alreadyComplete)) return false;
            if (alreadyComplete) return true;
            // 원작은 이름 키가 빈 프리셋의 처치를 세지 않고 경험치는 준다. 그래서 처치 수 소유자가 거절해도 결과를 보지 않고 계속한다.
            if (fromPlayer) kills.TryAddKill(enemyKey);
            // 같은 호출 안에서 사건 확인을 통과했고 처치 수 기록은 알림을 내지 않으므로, 사건은 늘 처음 완료로 기록된다.
            session.World.TryCompleteEvent(eventId, out _);
            // 같은 호출 안에서 CanAdd를 통과했고 세션 변경 알림의 구독자는 명령 관문을 닫지 않으므로 경험치는 늘 더해진다.
            // 보상 0은 경험치 소유자가 알림 없이 넘긴다.
            experience.Add(reward);
            return true;
        }
    }
}
