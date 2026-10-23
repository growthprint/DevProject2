#nullable enable annotations
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Game.Core
{
    // Addressables 씬을 추가 로드하고 내린다. 메인 스레드 전용이다.
    // 같은 씬 에셋을 두 번 열 수 있으므로 핸들은 로드마다 다른 Scene 값으로 구분한다.
    public sealed class SceneLoader : ISceneLoader
    {
        // 이 로더가 연 씬과 그 로드 핸들이다. 앱이 끝나면 남은 씬은 Unity가 회수한다.
        readonly Dictionary<Scene, AsyncOperationHandle<SceneInstance>> _loaded = new Dictionary<Scene, AsyncOperationHandle<SceneInstance>>();

        // 켠 씬을 돌려주는 일반 로드다.
        public UniTask<Scene> LoadAsync(AssetReference scene) => LoadSceneAsync(scene, true);

        // Addressables는 켜지 않은 씬의 로드 핸들을 진행률 0.9에서 끝낸다. 씬 값은 로드를 시작할 때 정해져 켠 뒤에도 같다.
        public UniTask<Scene> PreloadAsync(AssetReference scene) => LoadSceneAsync(scene, false);

        // 켜지 않은 씬의 로드 연산을 이어 끝낸다. 이미 켠 씬에서 다시 불러도 끝난 연산을 기다리므로 바로 끝난다.
        public async UniTask<Scene> ActivateAsync(Scene scene)
        {
            if (!_loaded.TryGetValue(scene, out AsyncOperationHandle<SceneInstance> handle))
                return scene;
            await handle.Result.ActivateAsync().ToUniTask();
            return handle.Result.Scene;
        }

        // 로드가 끝날 때까지 기다린다. 취소는 없다. 실패한 로드는 에셋 로드와 같은 도우미가 핸들을 반납한 뒤 원래 오류를 다시 던진다.
        async UniTask<Scene> LoadSceneAsync(AssetReference scene, bool activate)
        {
            AsyncOperationHandle<SceneInstance> handle = Addressables.LoadSceneAsync(
                scene.RuntimeKey, LoadSceneMode.Additive, SceneReleaseMode.OnlyReleaseSceneOnHandleRelease, activate);
            SceneInstance instance = await AddressablesAssetProvider.AwaitOrReleaseAsync(handle);
            _loaded.Add(instance.Scene, handle);
            return instance.Scene;
        }

        // 원작 커튼 Single 로드가 일으키는 정리와 같다. Addressables가 쥔 에셋은 핸들의 Result가 잡고 있어 남는다.
        public async UniTask UnloadUnusedAssetsAsync() => await Resources.UnloadUnusedAssets().ToUniTask();

        // 이 로더가 연 씬을 한 번 내린다. 모르거나 이미 내린 씬은 무시하고, 실패는 기록만 한다.
        public UniTask UnloadAsync(Scene scene)
        {
            if (!_loaded.Remove(scene, out AsyncOperationHandle<SceneInstance> handle))
                return UniTask.CompletedTask;
            return UnloadHandleAsync(handle);
        }

        // 언로드를 기다린다. Unity가 이미 회수한 핸들은 건너뛴다. 실패는 기록하고 메인 스레드에서 끝난다.
        async UniTask UnloadHandleAsync(AsyncOperationHandle<SceneInstance> handle)
        {
            if (!handle.IsValid())
                return;
            await Cleanup.RunAsync(async () =>
            {
                AsyncOperationHandle<SceneInstance> unload = Addressables.UnloadSceneAsync(handle, autoReleaseHandle: true);
                await unload.ToUniTask();
            });
            await UniTask.SwitchToMainThread();
        }
    }
}
