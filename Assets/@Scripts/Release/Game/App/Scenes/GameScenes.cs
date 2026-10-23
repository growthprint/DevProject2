using System;
using System.Collections.Generic;

namespace Game.App
{
    // 논리 씬과 저장 문자열 사이의 변환을 한곳에 둔다.
    public static class GameScenes
    {
        // GameScene 값 순서대로 둔 영속 문자열이다. 진행 저장과 지도 표식에 기록되므로 바꾸지 않는다.
        static readonly string[] _ids = { "base-camp", "main-menu", "customization", "prepare", "intro", "prologue",
            "tutorial", "zero-zone" };

        public static IReadOnlyList<GameScene> All { get; } =
            Array.AsReadOnly((GameScene[])Enum.GetValues(typeof(GameScene)));

        // 넘어오는 값은 저장 경계의 TryCreate를 거친 상태나 코드 상수뿐이라 범위를 다시 보지 않는다.
        public static string Id(GameScene scene) => _ids[(int)scene];

        // 저장이나 씬 저작에서 온 문자열을 서수 비교로 논리 씬으로 바꾼다. null이나 모르는 문자열은 로그 없이 false다.
        public static bool TryParse(string id, out GameScene scene)
        {
            for (int i = 0; i < _ids.Length; i++)
            {
                if (string.Equals(_ids[i], id, StringComparison.Ordinal))
                {
                    scene = (GameScene)i;
                    return true;
                }
            }
            scene = default;
            return false;
        }
    }
}
