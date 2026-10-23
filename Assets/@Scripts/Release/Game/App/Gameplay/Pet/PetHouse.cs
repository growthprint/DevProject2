#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 PetHouse다. 레벨이 초기화되면 펫을 PetMarker에 두고 그 자리에서 대기시키며, 꺼질 때 대기를 푼다.
    // 원작은 기지 하위 씬을 내리고 다시 불러 Awake와 OnDestroy가 돌지만, 제품은 하위 씬 루트를 끄고 켜므로 OnEnable과 OnDisable이 그 자리다.
    // 펫은 SceneContext의 레벨 의존성 묶음에서 읽으므로 펫은 개집을 모른다.
    [DisallowMultipleComponent]
    public sealed class PetHouse : MonoBehaviour
    {
        // 원작 petMarker다. 기지의 Building_PetHouse/Graphics/PetMarker를 제작 도구가 연결한다.
        [SerializeField] Transform _petMarker = null!;
        // 대기를 건 펫이다. 원작 petTarget처럼 꺼질 때 이 펫의 대기만 푼다.
        PetCharacter? _pet;

        // 원작 Awake처럼 레벨이 이미 초기화됐으면 바로 두고, 아니면 초기화 알림을 기다린다. 씬 로드 때는 초기화 전이라 알림만 받고,
        // 지하에서 돌아와 하위 씬이 다시 켜지면 바로 둔다. 켜질 때마다 한쪽 길만 타서 처음 배치가 두 번 일어나지 않는다.
        void OnEnable()
        {
            if (SceneContext.LevelInited) OnLevelInitialized();
            else SceneContext.LevelInitialized += OnLevelInitialized;
        }

        // SceneContext는 의존성 묶음을 채운 뒤에만 초기화를 알리고, 개집은 펫을 저작한 기지 씬에만 둔다.
        void OnLevelInitialized()
        {
            PetCharacter pet = SceneContext.Instance!.Gameplay!.Pet!;
            Vector3 marker = _petMarker.position;
            pet.PlaceAt(marker);
            pet.SetStandBy(true, marker);
            _pet = pet;
        }

        // 원작 OnDestroy처럼 펫을 지금 자리에서 풀어 준다. 하위 씬이 꺼지면 풀린 펫이 40m 규칙으로 플레이어를 따라온다.
        // 씬 언로드에서는 펫이 먼저 파괴됐을 수 있어 Unity null 비교로 건너뛴다.
        void OnDisable()
        {
            SceneContext.LevelInitialized -= OnLevelInitialized;
            PetCharacter? pet = _pet;
            _pet = null;
            if (pet != null) pet.SetStandBy(false, pet.transform.position);
        }
    }
}
