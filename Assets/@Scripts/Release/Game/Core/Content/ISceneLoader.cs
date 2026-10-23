#nullable enable annotations
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace Game.Core
{
    // 씬을 불러오고 내리는 계약이다. 테스트가 대역으로 바꿔 끼우는 경계라 인터페이스로 둔다.
    public interface ISceneLoader
    {
        // 씬을 추가 로드하고 메인 스레드에서 끝난다. 취소는 없어 로드가 끝날 때까지 기다린다.
        // 로드 실패는 예외로 알린다. 활성 씬은 호출자가 정한다.
        UniTask<Scene> LoadAsync(AssetReference scene);

        // 원작 SceneLoader의 allowSceneActivation = false처럼 씬을 켜기 직전까지만 올린다. 켜지 않은 씬은 내릴 수 없으므로
        // 받은 씬은 반드시 ActivateAsync로 켠다. 그동안 다른 씬 로드와 언로드는 Unity가 뒤로 미룬다. 로드 실패는 예외로 알린다.
        UniTask<Scene> PreloadAsync(AssetReference scene);

        // PreloadAsync로 올린 씬을 켜고 Awake가 끝난 뒤 그 씬을 돌려준다. 이미 켜진 씬이나 모르는 씬은 그대로 돌려준다.
        UniTask<Scene> ActivateAsync(Scene scene);

        // Unity의 미사용 에셋 정리를 한 번 돌리고 끝날 때까지 기다린다. 씬을 내린 뒤 다음 씬을 올리기 전에 부른다. 던지지 않는다.
        UniTask UnloadUnusedAssetsAsync();

        // 이 로더가 연 씬을 한 번만 내린다. 모르는 씬은 무시하고, 실패는 기록만 하며 던지지 않는다.
        UniTask UnloadAsync(Scene scene);
    }
}
