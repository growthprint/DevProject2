#nullable enable annotations
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;

namespace Game.Core
{
    // Addressables를 준비하고 성공 여부만 알려 준다. 초기화 핸들은 Addressables가 스스로 반납한다.
    public static class AddressablesInitializer
    {
        // 초기화가 성공하면 true, 실패하면 한 번 기록하고 false다. 취소되면 기록 없이 OperationCanceledException을 던진다. 메인 스레드에서 끝난다.
        public static async UniTask<bool> InitializeAsync(CancellationToken token)
        {
            try
            {
                await Addressables.InitializeAsync(autoReleaseHandle: true).ToUniTask(cancellationToken: token, cancelImmediately: true);
                return true;
            }
            // 예외 경계: 초기화 실패는 결과(false)로만 알리고 원인은 여기서 한 번 기록한다. 취소는 실패가 아니라 호출자에게 그대로 넘긴다.
            catch (Exception error) when (!(error is OperationCanceledException))
            {
                Log.Instance.Exception(error);
                return false;
            }
        }
    }
}
