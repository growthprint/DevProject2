#nullable enable annotations
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // Bootstrap 호스트에서 앱 종료를 맡는다. Unity 종료 신호와 호스트 파괴를 받아 저장 없이 Runtime을 중단하고 GameManager를 파괴한다.
    // 시작은 같은 호스트의 GameBootstrap이 맡고, 앱 종료가 시작되면 진행 중인 시작은 Application.exitCancellationToken으로 멈춰 앱을 만들지 않는다.
    [DisallowMultipleComponent]
    public sealed class GameShutdown : MonoBehaviour
    {
        // 종료 결과다. null이 아니면 종료가 접수된 것이다.
        // 정리 전에 만들어 두므로 반복 호출과 재진입 호출도 같은 결과를 받는다.
        UniTaskCompletionSource<bool>? _stopped;

        // 앱이 끝날 때 저장 없이 종료를 시작하고 기다리지 않는다. Unity는 정리를 기다려 주지 않아 이번 호출 안에서는
        // 강제 종료의 동기 구간만 돌고, 현재 레벨은 그 구간에서 끝난다. 남은 정리는 객체가 파괴된 뒤에 돌거나 버려진다.
        void OnApplicationQuit() => StopAsync().Forget();

        // 호스트 파괴도 같은 종료로 받는다. 앱 종료가 먼저 접수했으면 같은 결과를 공유한다.
        void OnDestroy() => StopAsync().Forget();

        // 저장 없이 Runtime을 중단하고 GameManager를 파괴한다. 실패는 기록하고 false로 돌려주며, 반복 호출도 같은 결과를 받는다.
        // 본문의 정리 단계는 Cleanup.RunAsync가 실패를 기록하고 계속해 던지지 않으므로, 아래 처리기는 예상 밖 예외를 기록만 한다.
        public UniTask<bool> StopAsync()
        {
            if (_stopped == null)
            {
                _stopped = new UniTaskCompletionSource<bool>();
                StopCoreAsync().Forget(exception =>{ Log.Instance.Exception(exception, this); });
            }
            return _stopped.Task;
        }

        // 앱은 하나뿐이라 GameManager.Live로 찾는다. 시작이 아직 만들기 전이면 Live가 없으므로 HasInstance를 먼저 보고,
        // 그 시작은 스스로 멈춰 만들지 않는다. Runtime이 앱 자원을 모두 정리한 뒤에 그 루트를 파괴하며, 실패는 Cleanup이 기록만 한다.
        async UniTask StopCoreAsync()
        {
            bool stopped = true;
            if (GameManager.HasInstance)
            {
                GameManager manager = GameManager.Live;
                GameObject root = manager.gameObject;
                bool aborted = await Cleanup.RunAsync(manager.Runtime.AbortAsync, this);
                bool destroyed = await Cleanup.RunAsync(() => UnityObjects.DestroyAndWaitAsync(root), this);
                stopped = aborted && destroyed;
            }
            _stopped!.TrySetResult(stopped);
        }
    }
}
