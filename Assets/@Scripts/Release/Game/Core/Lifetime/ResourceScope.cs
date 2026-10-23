using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Game.Core
{
    // 등록된 해제 대상을 등록 역순으로 한 번 해제하는 수명 범위다. 메인 스레드 전용이라 잠금이 없다.
    // 해제는 던지지 않고, 단계마다 실패를 기록한 뒤 계속한다.
    public sealed class ResourceScope : IAsyncReleasable
    {
        // 등록 순서대로 보관하고, 해제가 시작되면 한 번에 비운다.
        private readonly List<IAsyncReleasable> _registered = new List<IAsyncReleasable>();

        private readonly ReleaseOnce _release;

        public ResourceScope()
        {
            _release = new ReleaseOnce(ReleaseRegisteredAsync);
        }

        // 해제 대상을 이 범위에 등록한다. 해제가 시작된 범위는 false를 돌려주며, 그때는 호출자가 직접 해제해야 한다.
        // null이나 자기 자신, 중복 등록은 내부 호출자의 결함이라 예외 대신 검사로 알린다.
        public bool TryRegisterReleaseHandle(IAsyncReleasable releaseHandle)
        {
            // 릴리즈 빌드는 검사 실패 뒤에도 계속 실행되므로 같은 조건으로 false를 돌려준다.
            // null은 해제 때 NRE를 내고, 자기 자신은 자기 완료를 기다리느라 끝나지 않기 때문이다.
            bool registrable = releaseHandle != null && !ReferenceEquals(releaseHandle, this);
            Log.Instance.Assert(registrable, "releaseHandle: 등록할 수 없는 해제 대상입니다.");
            Log.Check(!_registered.Contains(releaseHandle), "이미 이 범위에 등록된 해제 대상입니다.");
            if (!registrable || _release.Started)
                return false;

            _registered.Add(releaseHandle);
            return true;
        }

        // 등록을 시도하고, 범위가 거부하면 대상을 해제한 뒤 ObjectDisposedException으로 구성 작업을 끝낸다.
        // 어느 쪽이든 호출자는 이 대상을 직접 해제하지 않는다. 거부된 대상은 이 메서드가 이미 해제했다.
        public async UniTask RegisterReleaseHandleAsync(IAsyncReleasable releaseHandle)
        {
            if (!TryRegisterReleaseHandle(releaseHandle))
            {
                await Cleanup.RunAsync(releaseHandle.ReleaseAsync);
                throw new ObjectDisposedException(nameof(ResourceScope));
            }
        }

        // 종료를 한 번 실행하고 모든 호출자가 같은 완료를 기다린다. 던지지 않는다.
        public UniTask ReleaseAsync() => _release.ReleaseAsync();

        // 목록을 복사해 비운 뒤 등록 역순으로 해제한다. 계약을 어기고 던지는 대상이 있어도 Cleanup이 기록하고 다음 대상을 계속한다.
        private async UniTask ReleaseRegisteredAsync()
        {
            IAsyncReleasable[] registered = _registered.ToArray();
            _registered.Clear();
            for (int index = registered.Length - 1; index >= 0; index--)
                await Cleanup.RunAsync(registered[index].ReleaseAsync);
        }

    }
}
