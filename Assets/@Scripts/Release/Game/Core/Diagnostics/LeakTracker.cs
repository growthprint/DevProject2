#nullable enable annotations

namespace Game.Core
{
    // 수명 소유자가 항목을 남긴 채 닫히면 누수 후보로 알리는 개발용 진단이다. 동작은 바꾸지 않고 키마다 경고를 한 번만 남긴다.
    // 호출은 에디터와 DEBUG 빌드에만 남고 릴리즈 빌드에서는 인수 계산까지 사라지므로, 인수에 꼭 실행돼야 하는 코드를 넣지 않는다.
    // 앱 종료 중에는 Unity가 씬 객체를 먼저 파괴하고 정리가 뒤따르므로 종료 순서 때문에 생기는 보고는 남기지 않는다. 메인 스레드에서 부른다.
    internal static class LeakTracker
    {
#if UNITY_EDITOR || DEBUG
        // 이미 경고한 키다. 플레이 세션마다 비운다. 테스트가 이 필드 이름으로 리플렉션해 비우므로 이름을 바꾸지 않는다.
        static readonly Log.Once _reported = new Log.Once();
        // 모든 누수 경고 앞에 붙는 머리말이다. 콘솔에서 누수 추적 경고만 골라 볼 수 있게 한 곳에서 붙인다.
        const string _prefix = "[누수 추적] ";
#if !UNITY_EDITOR
        // 플레이어에서 Application.quitting이 왔는지 표시한다. 에디터는 Play Mode가 끝나도 정적 값이 남아 에디터 상태로 판정한다.
        static bool _quitting;
#endif

        // 도메인 재로드 없이 Play Mode에 들어와도 기록이 남지 않게 세션 시작마다 비운다. 플레이어는 종료 알림도 여기서 구독한다.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void BeginSession()
        {
            _reported.Reset();
#if !UNITY_EDITOR
            _quitting = false;
            UnityEngine.Application.quitting -= MarkQuitting;
            UnityEngine.Application.quitting += MarkQuitting;
#endif
        }

#if !UNITY_EDITOR
        static void MarkQuitting() => _quitting = true;
#endif

        // 앱 종료가 진행 중인지 판정한다. 에디터에서 정지 버튼을 누른 뒤 정리 중이면 isPlaying은 true이고
        // isPlayingOrWillChangePlaymode는 false다. 테스트를 포함한 Edit Mode는 종료 중으로 보지 않는다.
        static bool IsQuitting
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorApplication.isPlaying && !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
#else
                return _quitting;
#endif
            }
        }

        static void Warn(string key, string message, UnityEngine.Object? context)
        {
            if (_reported.First(key))
                Log.Instance.Warning(_prefix + message, context);
        }
#endif

        // 수명 범위 owner가 열린 항목 count개를 남긴 채 닫혀 대신 반납했음을 알린다. 소유자마다 한 번이고, 종료 중에는 남기지 않는다.
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEBUG")]
        internal static void ScopeClosedWithEntries(string owner, int count)
        {
#if UNITY_EDITOR || DEBUG
            if (count <= 0 || IsQuitting)
                return;
            Warn("scope:" + owner,
                owner + "이(가) 열린 항목 " + count + "개를 남긴 채 닫혀 대신 반납했습니다. 항목 소유자가 먼저 닫아야 합니다.", null);
#endif
        }

        // Spawner가 사용 중 인스턴스 count개를 남긴 채 종료되어 함께 파괴했음을 알린다. 세션마다 한 번이다. 종료 중에는 남기지 않는다.
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEBUG")]
        internal static void SpawnerDisposedWithLiveInstances(int count)
        {
#if UNITY_EDITOR || DEBUG
            if (count <= 0 || IsQuitting)
                return;
            Warn("spawner",
                "Spawner가 사용 중 인스턴스 " + count + "개를 남긴 채 종료되어 함께 파괴했습니다. 월드 소유자가 먼저 반납해야 합니다.", null);
#endif
        }

        // Spawner 인스턴스가 Despawn이나 월드 종료를 거치지 않고 파괴됐음을 알린다. 이름마다 한 번이고, 종료 중에는 남기지 않는다.
        // 이미 파괴돼 이름을 읽을 수 없으면 호출자가 생성 규칙 같은 대표 객체를 넘긴다.
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEBUG")]
        internal static void PooledInstanceDestroyedExternally(UnityEngine.Object instance)
        {
#if UNITY_EDITOR || DEBUG
            if (IsQuitting)
                return;
            bool alive = instance != null;
            string name = alive ? instance.name : "(파괴된 객체)";
            Warn("destroyed:" + name,
                "Spawner 인스턴스(" + name + ")가 Spawner 밖에서 파괴되었습니다. Despawn이나 월드 종료로 정리해야 합니다.",
                alive ? instance : null);
#endif
        }
    }
}
