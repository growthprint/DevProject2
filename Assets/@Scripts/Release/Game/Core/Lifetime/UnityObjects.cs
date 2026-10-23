#nullable enable annotations
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game.Core
{
    // 소유한 Unity 객체를 파괴하고 실제 파괴를 기다리는 공용 헬퍼다. 언제 파괴하고 기다릴지는 소유자가 정한다.
    public static class UnityObjects
    {
        // 이미 파괴된 객체도 null로 보고 무시한다. Edit Mode에서는 Destroy를 쓸 수 없으므로 DestroyImmediate를 쓴다.
        public static void DestroyOwned(Object? target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }

        // Play Mode의 Destroy는 지연 실행되므로 Play Mode가 이어질 때만 다음 프레임까지 기다린다. Edit Mode에서는 바로 끝난다.
        public static async UniTask WaitForDestroyAsync()
        {
            if (PlayModeContinues)
                await UniTask.NextFrame();
        }

        // 플레이어는 종료하면 다음 프레임이 오지 않아 대기도 함께 버려진다. 에디터는 Play Mode를 끝내도 Edit Mode 프레임이 돌아
        // 기다린 정리가 그때 재개되고, Unity가 이미 파괴한 객체와 회수한 Addressables를 다룬다. 그래서 정지가 시작되면 기다리지 않는다.
        static bool PlayModeContinues =>
#if UNITY_EDITOR
            Application.isPlaying && UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;
#else
            Application.isPlaying;
#endif

        public static UniTask DestroyAndWaitAsync(Object? target)
        {
            if (target == null)
                return UniTask.CompletedTask;

            DestroyOwned(target);
            return WaitForDestroyAsync();
        }
    }
}
