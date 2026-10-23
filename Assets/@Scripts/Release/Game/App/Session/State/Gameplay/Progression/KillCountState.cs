#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 원작 SavesCounter의 처치 수 하나(원작 키 Count/Kills/<key>)다. 적 키와 영구 처치 수를 보존한다.
    public sealed class KillCountState
    {
        // 믿을 수 있는 입력용 생성자다. 믿을 수 없는 입력은 TryCreate를 쓴다.
        public KillCountState(string enemyKey, int count)
        {
            EnemyKey = enemyKey; Count = count;
        }

        // 믿을 수 없는 입력용 생성 경로다. 빈 키와 음수를 거부한다. 규칙에 어긋나면 던지지 않고 false를 돌려주며, 실패 보고는 호출자가 맡는다.
        public static bool TryCreate(string enemyKey, int count, [NotNullWhen(true)] out KillCountState? value)
        {
            value = !string.IsNullOrWhiteSpace(enemyKey) && count >= 0 ? new KillCountState(enemyKey, count) : null;
            return value != null;
        }

        public string EnemyKey { get; }
        public int Count { get; }
    }
}
