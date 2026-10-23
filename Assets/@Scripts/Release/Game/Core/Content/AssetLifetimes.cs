#nullable enable annotations
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;

namespace Game.Core
{
    // 에셋 봉투를 소유자별 서랍에 넣어 두는 서랍장이다. 앱 서랍 하나와 씬마다 서랍 하나를 두고, 호출자는 소유자만 정한다.
    // 씬이 내려가면 그 씬 서랍을 등록 역순으로 반납하므로, 씬 인스턴스가 먼저 파괴되고 에셋은 그 뒤에 반납된다.
    // 다음 씬은 현재 씬이 활성인 동안 준비되므로 소유 씬을 늘 명시한다. 메인 스레드 전용이다.
    public sealed class AssetLifetimes : IAsyncReleasable
    {
        // 실제 로드를 맡는 교체 가능한 공급자다.
        readonly IAssetProvider _provider;
        // 앱 수명 봉투 서랍이다. 모든 씬 서랍 뒤에 반납한다.
        readonly ResourceScope _app = new ResourceScope();
        // 씬 수명 봉투 서랍이다. 같은 논리 씬이 두 번 열려도 Scene 값이 달라 섞이지 않는다.
        readonly Dictionary<Scene, ResourceScope> _scenes = new Dictionary<Scene, ResourceScope>();
        // 언로드 알림이 반납하지 않고 소유자가 ReleaseSceneAsync로 반납할 씬이다. 씬 전환이 다음 씬 초기화까지 서랍을 남길 때 쓴다.
        readonly HashSet<Scene> _held = new HashSet<Scene>();
        // 여러 번 불러도 서랍장 전체는 한 번만 반납한다.
        readonly ReleaseOnce _release;

        // 씬 언로드 알림은 직접 구독하지 않는다. 만들고 반납하지 않은 서랍장이 정적 알림에 남지 않도록,
        // 서랍장을 쓰는 컴포넌트가 자기 수명 동안 구독해 NotifySceneUnloaded로 넘긴다.
        public AssetLifetimes(IAssetProvider provider)
        {
            _provider = provider!;
            _release = new ReleaseOnce(ReleaseAllAsync);
        }

        // 앱 수명으로 로드한다. 반납은 서랍장 반납 때 한다.
        public UniTask<T> LoadAsync<T>(AssetReference key) where T : UnityEngine.Object =>
            LoadIntoAsync<T>(key, _app);

        // owner 씬 수명으로 로드하고, owner가 내려가면 자동 반납한다. owner는 로드된 씬이어야 한다.
        public UniTask<T> LoadAsync<T>(AssetReference key, Scene owner) where T : UnityEngine.Object
        {
            // 반납이 시작된 뒤 씬 서랍을 만들면 서랍장 반납이 이미 목록을 비운 뒤라 반납되지 않을 수 있다.
            // 그래서 닫힌 앱 서랍으로 보내 봉투를 바로 반납하고 ObjectDisposedException으로 끝낸다.
            if (_release.Started)
                return LoadIntoAsync<T>(key, _app);
            if (!_scenes.TryGetValue(owner, out ResourceScope drawer))
            {
                drawer = new ResourceScope();
                _scenes.Add(owner, drawer);
            }
            return LoadIntoAsync<T>(key, drawer);
        }

        // 씬이 소유한 리소스를 반납(해제)한다.
        // 이미 반납됐거나 등록되지 않은 씬이면 아무 일도 하지 않고 즉시 완료된다.
        public UniTask ReleaseSceneAsync(Scene scene)
        {
            // 보류한 씬 목록에서 씬을 제거한다. (없어도 문제없으므로 반환값은 무시)
            _held.Remove(scene);

            // 관리 목록에서 씬을 제거하면서, 해당 씬의 ResourceScope를 꺼낸다.
            // 주의: Remove는 딕셔너리의 참조만 끊을 뿐, 리소스를 해제하지는 않는다.
            if (!_scenes.Remove(scene, out ResourceScope drawer))
                return UniTask.CompletedTask; // 등록된 적 없거나 이미 반납된 씬 → 할 일 없음

            // 실제 리소스 해제는 여기서 수행되며, 호출자는 await로 완료를 기다릴 수 있다.
            return drawer.ReleaseAsync();
        }

        // 원작은 카탈로그를 앱 수명 정적 캐시로 들고 있어 씬을 바꿔도 다시 읽지 않는다. 씬 서랍에 둔 에셋도 매번 다시 읽지 않도록,
        // 씬 전환은 내려가는 씬의 서랍을 다음 씬이 같은 에셋을 다시 잡을 때까지 남긴다. 보류한 서랍은 언로드 알림이 반납하지 않으며,
        // 소유자가 ReleaseSceneAsync로 반납하거나 서랍장 반납 때 함께 반납된다. 서랍이 없는 씬은 보류할 것이 없다.
        public void HoldSceneRelease(Scene scene)
        {
            if (_scenes.ContainsKey(scene)) _held.Add(scene);
        }

        // 남은 씬 서랍을 먼저, App 서랍을 마지막에 반납한다. 던지지 않는다.
        public UniTask ReleaseAsync() => _release.ReleaseAsync();

        // 봉투를 서랍에 넣고 에셋만 돌려준다. 서랍이 이미 닫혔으면 봉투를 반납하고 ObjectDisposedException으로 끝낸다.
        async UniTask<T> LoadIntoAsync<T>(AssetReference key, ResourceScope drawer) where T : UnityEngine.Object
        {
            AssetScope<T> scope = await _provider.LoadAsync<T>(key);
            await drawer.RegisterReleaseHandleAsync(scope);
            return scope.Asset;
        }

        // 소유 컴포넌트가 SceneManager.sceneUnloaded를 받아 부른다. 이 알림이 올 때 씬 인스턴스는 이미 파괴됐으므로
        // 그 씬 서랍을 반납해도 안전하다. 반납은 던지지 않는다. 반납을 보류한 씬은 소유자가 반납하므로 여기서는 건너뛴다.
        public void NotifySceneUnloaded(Scene scene)
        {
            if (_held.Contains(scene)) return;
            ReleaseSceneAsync(scene).Forget();
        }

        // 언로드에 실패해 남은 씬 서랍까지 반납한 뒤 앱 서랍을 마지막에 반납한다.
        async UniTask ReleaseAllAsync()
        {
            // 아직 열린 씬 서랍은 누수 후보라 개발 빌드에 알린다. 앱 종료 중이면 LeakTracker가 보고를 거르고, 반납은 그대로 한다.
            LeakTracker.ScopeClosedWithEntries("에셋 수명 관리자(씬 서랍)", _scenes.Count);
            var scenes = new List<ResourceScope>(_scenes.Values);
            _scenes.Clear();
            // 보류한 서랍도 위 목록에 들어 있어 함께 반납된다.
            _held.Clear();
            foreach (ResourceScope drawer in scenes)
                await drawer.ReleaseAsync();
            await _app.ReleaseAsync();
        }
    }
}
