#nullable enable annotations
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;

namespace Game.Core
{
    // 에셋 로드 계약이다. 반납 방법은 결과 봉투에 들어 있어 게임은 로더 구현을 모른다.
    // 테스트가 대역으로 바꿔 끼우는 경계라 인터페이스로 둔다.
    public interface IAssetProvider
    {
        // 메인 스레드에서 시작하고 끝난다. 취소는 없고, 기다리던 소유자가 사라졌으면 받은 봉투를 그 소유자가 반납한다.
        // 로드 실패는 예외로 알린다.
        UniTask<AssetScope<T>> LoadAsync<T>(AssetReference key) where T : UnityEngine.Object;
    }
}
