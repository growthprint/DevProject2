#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 Building처럼 건물 프리팹 루트에 붙는다. 영역 표시가 만든 뒤 설치 기록으로 회전을 넣고, 그 건물이 회수되면 스스로 파괴된다.
    // 그래픽과 기능 묶음은 프리팹 제작 도구가 원작 계층 이름(Graphics, Function)대로 잇는다. 메인 스레드에서만 쓴다.
    [DisallowMultipleComponent]
    public sealed class Building : MonoBehaviour
    {
        [SerializeField] GameObject _graphicsContainer;
        [SerializeField] GameObject _functionContainer;
        // 원작 Building의 id와 dimensions다. 배치 규칙은 프리팹을 로드하지 않으려고 정의(BuildingInfo)의 같은 값을 읽는다.
        // 그래서 실행 코드는 이 값을 읽지 않고, 프리팹 제작 도구가 원작 값을 넣고 데이터 크기 시험이 정의와 같은지 직렬화로 본다.
#pragma warning disable CS0414
        [SerializeField] string _id = "";
        [SerializeField] Vector2Int _dimensions = Vector2Int.zero;
#pragma warning restore CS0414

        // 설치 번호다. Setup 전의 미리보기 사본은 어떤 저장 건물도 아니라 저장 번호에 없는 -1이다.
        int _instanceId = -1;
        // 실행 중에 생긴 건물은 저작 표에 없어서, F 상호작용 대상을 전투 월드의 생성 대상 목록에 넣는다.
        // 넣은 월드와 대상을 기억해 파괴되거나 영역이 월드에서 풀릴 때 같은 대상을 뺀다.
        GameplayWorldContext? _world;
        readonly List<InteractableBase> _interactables = new List<InteractableBase>();

        public int InstanceId => _instanceId;

        // 원작 Setup이다. 회전은 영역 아래 지역 회전으로 넣고, 위치는 부른 쪽이 월드 좌표로 놓는다.
        internal void Setup(BuildingInstallationState installation)
        {
            _instanceId = installation.InstanceId;
            transform.localRotation = Quaternion.Euler(0f, installation.Rotation * 90, 0f);
            BuildingManager.Destroyed += OnBuildingDestroyed;
            // 실행 중에 지은 건물도 첫 메뉴 전에 표시 조건(작업대 수리·분해 항목 같은 것)이 맞도록 생길 때 바로 맞춘다.
            // 레벨 준비 전에 그린 건물이면 조건 컴포넌트가 아무것도 하지 않고 준비 뒤 자기 Update에서 맞춘다. 미리보기 사본은 Setup을 부르지 않는다.
            foreach (SetActiveByCondition setter in GetComponentsInChildren<SetActiveByCondition>(true)) setter.TryBegin();
        }

        // 구독하지 않은 미리보기 사본에서 빼도 아무 일이 없다. 회수, 다시 그리기, 씬 정리로 파괴될 때 월드 목록에서도 뺀다.
        void OnDestroy()
        {
            BuildingManager.Destroyed -= OnBuildingDestroyed;
            UnregisterInteractables();
        }

        // 건설 영역이 표시한 건물을 월드에 연결할 때 부른다. 조건으로 꺼진 기능도 넣어 두어 켜지면 바로 찾힌다.
        // 기능 묶음이 꺼진 미리보기 사본은 넣지 않고, 같은 월드에는 두 번 넣지 않는다.
        internal void RegisterInteractables(GameplayWorldContext world)
        {
            if (_instanceId < 0 || _world == world) return;
            UnregisterInteractables();
            GetComponentsInChildren(true, _interactables);
            for (int index = 0; index < _interactables.Count; index++) world.AddSpawnedInteractable(_interactables[index]);
            _world = world;
        }

        // 넣은 월드가 없으면 아무 일도 없다. 파괴 중에도 불리므로 넣을 때 모은 목록으로 뺀다.
        internal void UnregisterInteractables()
        {
            GameplayWorldContext? world = _world;
            if (world == null) return;
            _world = null;
            for (int index = 0; index < _interactables.Count; index++) world.RemoveSpawnedInteractable(_interactables[index]);
            _interactables.Clear();
        }

        void OnBuildingDestroyed(int instanceId)
        {
            if (instanceId == _instanceId) Destroy(gameObject);
        }

        // 원작 SetupPreview다. 배치 미리보기가 상호작용과 물리 겹침 검사에 걸리지 않도록 기능 묶음과 그래픽 콜라이더를 끈다.
        internal void SetupPreview()
        {
            _functionContainer.SetActive(false);
            Collider[] colliders = _graphicsContainer.GetComponentsInChildren<Collider>();
            for (int index = 0; index < colliders.Length; index++) colliders[index].enabled = false;
        }
    }
}
