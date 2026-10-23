#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace Game.App
{
    // 원작 QuestGiverID다. 저장과 원작 데이터가 숫자로 창구를 가리키므로 값과 이름을 원작 그대로 둔다.
    public enum QuestGiverId
    {
        None = 0,
        Jeff = 1,
        Xavier = 2,
        Orange = 3,
        Bob = 4,
        Mud = 5,
        DemoTrophy = 6,
        Ming = 7,
        Fo = 8,
        SnowPMC = 9,
        Alex = 10,
        GunFire = 11,
    }

    // 원작 보상 컴포넌트 네 종류다. 원작 AutoClaim은 아이템만 거짓이므로 자동 수령 여부를 종류로 정한다.
    public enum QuestRewardKind
    {
        Experience = 0,
        Money = 1,
        Item = 2,
        UnlockStockItem = 3,
    }

    // 원작 Reward 하나다. 받음 여부는 저장 진행 값이 소유하므로 여기에는 정의만 둔다.
    [Serializable]
    public sealed class OriginalQuestRewardDefinition
    {
        [SerializeField] int _id;
        [SerializeField] QuestRewardKind _kind;
        // 경험치와 돈은 지급량, 아이템은 개수다. 상점 품목 해금은 원작에 수량이 없어 0이다.
        [SerializeField] int _amount;
        // 아이템 보상의 itemTypeID이거나 상점 품목 해금의 unlockItem이다. 경험치와 돈은 0이다.
        [SerializeField] int _itemId;

        public int Id => _id;
        public QuestRewardKind Kind => _kind;
        public int Amount => _amount;
        public int ItemId => _itemId;
        // 원작 RewardItem만 AutoClaim이 거짓이라 완료 패널을 닫아도 아이템 보상은 남는다.
        public bool AutoClaim => _kind != QuestRewardKind.Item;
    }

    // 원작 prerequisit의 조건 하나다. 적 처치 조건만 저장된 처치 수로 판정하고, 나머지 형식은 범위 안에 판정할 소유자가 없어 거짓으로 본다.
    [Serializable]
    public sealed class OriginalQuestConditionDefinition
    {
        [SerializeField] string _originalTypeName = "";
        // 원작 조건 값의 기록이다. 적 처치 조건은 적 nameKey와 처치 수, 저장 값 조건은 키와 요구 값(1 또는 0)이다.
        // 적 처치 조건 판정(QuestRules)이 두 값을 읽으므로 사용하지 않는 필드 경고를 끄지 않는다.
        [SerializeField] string _key = "";
        [SerializeField] int _value = 0;

        public string OriginalTypeName => _originalTypeName;
        // 원작 RequireEnemyKilled의 enemyPreset.nameKey와 threshold다. 적 프리셋이 비어 있던 조건은 빈 키다.
        public string Key => _key;
        public int Value => _value;
    }

    // 원작 Quest 프리팹 하나를 프리팹 참조 없이 값으로 옮긴 정의다. 진행, 완료, 받음 여부는 QuestManager 저장 값이 소유한다.
    [Serializable]
    public sealed class OriginalQuestDefinition
    {
        [SerializeField] int _id;
        [SerializeField] QuestGiverId _giver;
        [SerializeField] int _requireLevel;
        [SerializeField] bool _lockInDemo;
        // 원작 requireSceneID 문자열이다. 비어 있으면 씬 조건이 없고, 범위 밖 지도 ID도 그대로 남긴다.
        [SerializeField] string _requireSceneId = "";
        [SerializeField] string _nameKey = "";
        [SerializeField] string _descriptionKey = "";
        // 원작 상세 화면이 보이는 필요 아이템 표시다. 수락이나 완료 판정에는 쓰지 않는다.
        [SerializeField] int _requiredItemId;
        [SerializeField] int _requiredItemCount = 1;
        // 원작 MeetsPrerequisit은 관계 그래프에 일반 노드가 없는 퀘스트를 받을 수 없게 한다. 대리 노드만 있으면 거짓이다.
        [SerializeField] bool _inRelationGraph;
        // 원작 GetRequiredIDs 결과다. 일반 노드로 들어오는 연결의 출발 퀘스트 ID를 연결 순서대로 둔다.
        [SerializeField] int[] _prerequisiteIds = Array.Empty<int>();
        [SerializeField] OriginalQuestTaskDefinition[] _tasks = Array.Empty<OriginalQuestTaskDefinition>();
        [SerializeField] OriginalQuestRewardDefinition[] _rewards = Array.Empty<OriginalQuestRewardDefinition>();
        [SerializeField] OriginalQuestConditionDefinition[] _conditions = Array.Empty<OriginalQuestConditionDefinition>();

        public int Id => _id;
        public QuestGiverId Giver => _giver;
        public int RequireLevel => _requireLevel;
        public bool LockInDemo => _lockInDemo;
        public string RequireSceneId => _requireSceneId;
        public string NameKey => _nameKey;
        public string DescriptionKey => _descriptionKey;
        public int RequiredItemId => _requiredItemId;
        public int RequiredItemCount => _requiredItemCount;
        public bool InRelationGraph => _inRelationGraph;
        public IReadOnlyList<int> PrerequisiteIds => _prerequisiteIds;
        public IReadOnlyList<OriginalQuestTaskDefinition> Tasks => _tasks;
        public IReadOnlyList<OriginalQuestRewardDefinition> Rewards => _rewards;
        public IReadOnlyList<OriginalQuestConditionDefinition> Conditions => _conditions;

        // 시험용 퀘스트 -1은 과제 ID가 겹친다. 첫 항목을 돌려주고, 겹침은 가져오기 도구가 보고한다.
        public bool TryGetTask(int taskId, [NotNullWhen(true)] out OriginalQuestTaskDefinition? task)
        {
            foreach (OriginalQuestTaskDefinition candidate in _tasks)
            {
                if (candidate.Id != taskId) continue;
                task = candidate;
                return true;
            }
            task = null;
            return false;
        }

        // 찾지 못하면 거짓만 돌려준다. 정의 없는 보상을 막고 보고하는 일은 호출자가 맡는다.
        public bool TryGetReward(int rewardId, [NotNullWhen(true)] out OriginalQuestRewardDefinition? reward)
        {
            foreach (OriginalQuestRewardDefinition candidate in _rewards)
            {
                if (candidate.Id != rewardId) continue;
                reward = candidate;
                return true;
            }
            reward = null;
            return false;
        }
    }
}
