#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 QuestGiver처럼 창구 ID를 가진 상호작용 대상이다. 창구 ID로 QuestManager에서 받을 수 있음, 진행 중, 완료 목록을 고른다.
    // 머리 위 표시는 원작처럼 퀘스트 목록 변경, 과제 완료, 건물 건설, 시작 때 다시 계산한다.
    [DisallowMultipleComponent]
    public sealed class QuestGiver : InteractableBase
    {
        // 원작 questGiverID다. 제로존 머드와 헬스장 머드처럼 같은 ID의 창구가 여럿이어도 같은 퀘스트 목록을 본다.
        [SerializeField] QuestGiverId _giverId;
        // 원작 Awake가 만들던 QuestMarker 자리다. 제작 도구가 창구 아래에 저장한 표시 인스턴스를 가리키고, 런타임에 만들지 않는다.
        [SerializeField] GameObject _inspectionIndicator;

        public QuestGiverId GiverId => _giverId;

        protected override void Awake()
        {
            base.Awake();
            QuestManager.Changed += RefreshIndicator;
            QuestManager.TaskFinished += OnTaskFinished;
            // 원작 QuestGiver.cs:42처럼 건물 건설이 건설 과제와 받을 수 있는 퀘스트를 바꿀 수 있어 표시를 다시 계산한다.
            BuildingManager.Built += OnBuildingBuilt;
        }

        void Start() => RefreshIndicator();

        void OnDestroy()
        {
            QuestManager.Changed -= RefreshIndicator;
            QuestManager.TaskFinished -= OnTaskFinished;
            // 정적 이벤트라 파괴될 때 건설 구독도 푼다(원작 QuestGiver.cs:72-75).
            BuildingManager.Built -= OnBuildingBuilt;
        }

        void OnTaskFinished(int questId, int taskId) => RefreshIndicator();

        // 원작처럼 건물 ID와 상관없이 다시 계산한다.
        void OnBuildingBuilt(int buildingId) => RefreshIndicator();

        // 원작 OnInteractStart의 창 열기다. 원작 창구 대기 시간이 0이라 시작 프레임에 끝나는 이 자리와 같은 프레임이다.
        // 창은 시설을 가진 호스트만 연다.
        internal override void OnInteractFinished(IInteractionHost host) => host.Facilities?.OpenQuestGiver(this);

        // 앱이나 세션이 없는 씬 실행에서는 퀘스트 값이 의미가 없어 표시를 끈다.
        void RefreshIndicator()
        {
            bool show = GameManager.HasInstance && GameManager.Live.Runtime.HasSession && GameManager.Quests.ShowsGiverMarker(_giverId);
            _inspectionIndicator.SetActive(show);
        }
    }
}
