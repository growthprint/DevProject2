#nullable enable annotations
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Game.Core
{
    // IAssetProvider의 Addressables 구현이다. 메인 스레드 전용이다.
    public sealed class AddressablesAssetProvider : IAssetProvider
    {
        // 로드를 시작해 끝날 때까지 기다린다. 취소는 없다. 실패한 로드는 반납한 뒤 원래 오류를 전달한다.
        // 앱이 끝나며 버려진 대기는 Unity가 핸들을 회수한다.
        public async UniTask<AssetScope<T>> LoadAsync<T>(AssetReference key) where T : UnityEngine.Object
        {
            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(key.RuntimeKey);
            T asset = await AwaitOrReleaseAsync(handle);
            return new AssetScope<T>(asset, () => Release(handle));
        }

        // 주소로 로드해 끝날 때까지 기다린다. 동기 로드는 그동안 메인 스레드를 멈추므로 쓰지 않는다(Docs/runtime-boundaries.md#blocking).
        // 앱 시작 때 한 번 도는 GameManager.CreateAsync만 쓴다. 실패한 로드는 반납한 뒤 원래 오류를 전달하고,
        // 로드 중에 앱 종료가 시작됐으면 호출자가 아무것도 만들지 않도록 반납한 뒤 취소로 끝낸다.
        public static async UniTask<AssetScope<T>> LoadByAddressAsync<T>(string address, CancellationToken token) where T : UnityEngine.Object
        {
            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(address);
            T asset = await AwaitOrReleaseAsync(handle);
            if (token.IsCancellationRequested) Release(handle);
            token.ThrowIfCancellationRequested();
            return new AssetScope<T>(asset, () => Release(handle));
        }

        // 로드 핸들을 기다린다. 실패하면 핸들을 반납한 뒤 원래 오류를 전달한다. 에셋 로드와 씬 로드가 함께 쓴다.
        internal static async UniTask<T> AwaitOrReleaseAsync<T>(AsyncOperationHandle<T> handle)
        {
            try
            {
                return await handle.ToUniTask();
            }
            // 예외 경계: 실패한 로드의 핸들을 반납하고 원래 오류를 호출자에게 전달한다.
            catch (Exception)
            {
                Release(handle);
                throw;
            }
        }

        // 반납한다. 이미 회수된 핸들은 건너뛴다.
        static void Release<T>(AsyncOperationHandle<T> handle)
        {
            if (handle.IsValid())
                Addressables.Release(handle);
        }
    }
}
