#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 끝났을 때 넘어갈 장면이다. 기지 출격, 제로존 벙커 귀환, 프롤로그에서 튜토리얼로 가는 출구다.
    // 정수로 직렬화되므로 새 값은 끝에 붙인다.
    // 원작 기지 SelectDifficulty(SceneLoaderProxy → Prepare)의 난이도 재설정 출구를 더한다. 기지에만 둔다.
    public enum SceneLoaderTarget { Sortie, ReturnToBase, Tutorial, ReselectDifficulty }

    // 원작 InteractableBase + SceneLoaderProxy처럼 끝나면 다른 장면으로 넘어간다. 실제 전환은 호스트가 씬 흐름 명령으로 한다.
    [DisallowMultipleComponent]
    public sealed class SceneLoaderInteractable : InteractableBase
    {
        [SerializeField] SceneLoaderTarget _target;

        internal SceneLoaderTarget Target => _target;

        internal override void OnInteractFinished(IInteractionHost host) => host.LoadScene(this);

        internal void Configure(string sourceId, Collider? interactCollider, string nameKey, Vector3 markerOffset, float interactTime,
            SceneLoaderTarget target)
        {
            ConfigureInteraction(sourceId, interactCollider, nameKey, markerOffset, interactTime);
            _target = target;
        }
    }
}
