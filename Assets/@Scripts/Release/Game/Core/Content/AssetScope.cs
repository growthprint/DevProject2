#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;

namespace Game.Core
{
    // 에셋 하나와 그 반납 동작을 묶는 봉투다. 소비자는 Addressables 핸들을 몰라도 반납할 수 있다.
    // Addressables 반납은 동기라 ReleaseAsync는 바로 끝난다. 메인 스레드에서만 다룬다.
    public sealed class AssetScope<T> : IAsyncReleasable where T : UnityEngine.Object
    {
        // 실제 반납 동작이다. 제품은 Addressables 핸들을 반납하고, 테스트 대역은 반납을 기록한다.
        readonly Action _release;
        bool _released;

        // 공급자가 로드에 성공한 에셋과 반납 동작을 받는다. Addressables는 null 결과를 로드 실패로 끝내므로 에셋은 늘 있다.
        public AssetScope(T asset, Action release)
        {
            Asset = asset;
            _release = release!;
        }

        // 봉투가 열려 있는 동안 쓰는 에셋이다.
        public T Asset { get; }

        // 한 번만 반납한다. 해제는 던지지 않는 계약이라 실패는 기록만 하고 완료로 끝난다.
        public UniTask ReleaseAsync()
        {
            if (_released)
                return UniTask.CompletedTask;
            _released = true;
            Cleanup.Run(_release, Asset);
            return UniTask.CompletedTask;
        }
    }
}
