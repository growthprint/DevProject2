#nullable enable

namespace Game.App
{
    // 원본 누적 경험치와 레벨 규칙이다. 레벨 L은 누적 1000 × (L(L+1)/2 − 1)에서 시작하고 최대 99다.
    // 저장은 누적 경험치만 보관하고, 레벨과 구간 경계는 이 규칙으로 계산한다. 레벨 2는 2000, 레벨 99는 4949000에서 시작한다.
    internal static class OriginalExperience
    {
        // 원본 최대 레벨이다. 이 레벨에는 다음 구간 경계가 없다.
        internal const int _maxLevel = 99;

        // 레벨별 시작 누적 경험치 표다. 색인이 레벨이고 0번은 쓰지 않는다. 고정 값이라 한 번만 만든다.
        static readonly long[] _levelStarts = CreateLevelStarts();

        // 원본 식으로 표를 채운다. 값이 레벨 순으로 커져야 Level의 이진 탐색이 맞게 동작한다.
        static long[] CreateLevelStarts()
        {
            var starts = new long[_maxLevel + 1];
            for (int level = 1; level <= _maxLevel; level++) starts[level] = 1000L * (level * (level + 1) / 2 - 1);
            return starts;
        }

        // 누적 경험치가 도달한 가장 높은 레벨이다. 시작 경험치 표를 이진 탐색하며, 레벨 2에 못 미치면 1이다.
        // 음수 경험치는 저장 값 생성자가 거부한다.
        internal static int Level(long experience)
        {
            int low = 1;
            int high = _maxLevel;
            while (low < high)
            {
                int middle = low + (high - low + 1) / 2;
                if (experience >= _levelStarts[middle]) low = middle;
                else high = middle - 1;
            }
            return low;
        }

        // 그 레벨이 시작하는 누적 경험치다. 레벨 1은 0이다. 인수는 늘 Level 결과나 최대 레벨 미만의 다음 레벨이라
        // 범위를 다시 보지 않는다. 그 범위는 OriginalExperienceSaveTests가 확인한다.
        internal static long LevelStart(int level) => _levelStarts[level];

        // 해당 레벨이 끝나는(다음 레벨이 시작하는) 누적 경험치다. 최대 레벨은 끝이 없어 null이다.
        internal static long? LevelEnd(int level) => level == _maxLevel ? (long?)null : LevelStart(level + 1);
    }
}
