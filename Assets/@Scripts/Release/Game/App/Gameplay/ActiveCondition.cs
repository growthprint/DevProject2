#nullable enable
using System;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.Quests.Conditions 가운데 표시 조건(SetActiveByCondition)이 쓰는 종류다. 정수로 직렬화되므로 값을 바꾸지 않고 새 종류는 끝에 붙인다.
    // 제로존 과제 현장 물체(불꽃놀이 633, 차 문 41, 무기 상자 42)가 원작 RequireQuestsActive를 쓰므로 QuestsActive를 끝에 붙인다.
    public enum ActiveConditionKind { PerkUnlocked = 0, FormulaUnlocked = 1, QuestsFinished = 2, QuestsActive = 3 }

    // 원작 RequirePerkUnlocked, RequireFormulaUnlocked, RequireQuestsFinished, RequireQuestsActive 하나다. 종류에 맞는 필드만 쓰고 나머지는 비워 둔다.
    // 건물 프리팹 제작 도구가 원작 값을 옮겨 적는다. 제조식 해금은 5-G 설계도 자료함이 쓴다.
    [Serializable]
    public sealed class ActiveCondition
    {
        [SerializeField] ActiveConditionKind _kind;
        // 원작 perkTreeID와 perkObjectName이다. 특성은 트리 ID와 특성 GameObject 이름으로 찾는다.
        [SerializeField] string _perkTreeId = "";
        [SerializeField] string _perkName = "";
        // 원작 formulaID다. 원작 itemID와 setFromItem은 판정에 쓰지 않아 옮기지 않는다.
        [SerializeField] string _formulaId = "";
        // 원작 requiredQuestIDs다. QuestsFinished는 모두 끝나야, QuestsActive는 모두 진행 중이어야 참이다.
        // 비어 있으면 원작 AreQuestFinished, AreQuestsActive처럼 참이다.
        [SerializeField] int[] _questIds = Array.Empty<int>();

        // 원작 Evaluate다. 소유자 조회만 읽고 상태를 바꾸지 않는다. 기록이 없는 특성은 원작처럼 거짓이다.
        // 모르는 종류는 원작 Condition.Evaluate처럼 거짓이다.
        internal bool Evaluate(PerkTreeManager perks, CraftingManager crafting, QuestManager quests) => _kind switch
        {
            ActiveConditionKind.PerkUnlocked => perks.IsUnlocked(_perkTreeId, _perkName),
            ActiveConditionKind.FormulaUnlocked => crafting.IsFormulaUnlocked(_formulaId),
            ActiveConditionKind.QuestsFinished => quests.AreFinished(_questIds),
            // 원작 RequireQuestsActive.Evaluate(QuestManager.AreQuestsActive)다. 끝난 퀘스트는 진행 중이 아니라 거짓이다.
            ActiveConditionKind.QuestsActive => quests.AreActive(_questIds),
            _ => false,
        };
    }
}
