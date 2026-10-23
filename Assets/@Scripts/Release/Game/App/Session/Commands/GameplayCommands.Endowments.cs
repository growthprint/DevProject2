#nullable enable
using System.Collections.Generic;

namespace Game.App
{
    // 원작 UnlockEndowmentWhenQuestComplete처럼 해금 퀘스트가 끝난 재능을 슬롯 밖 공통 파일(GlobalProgress)에 여는 명령이다.
    // 퀘스트 소유자는 읽기만 하고 공통 파일 소유자만 바꾼다. 원작은 완료 알림과 시작 때 이미 완료된 경우의 두 길로 열지만,
    // 제품은 퀘스트 변경 알림(불러오기 포함)마다 이 명령 하나로 둘을 함께 맞춘다. EndowmentManager가 그 알림을 받아 구성된 콜백으로 부른다.
    internal static partial class GameplayCommands
    {
        // 해금 퀘스트가 없거나 이미 열린 재능은 건너뛰어, 같은 퀘스트로 공통 파일을 두 번 쓰지 않는다. 끝난 퀘스트는 완료와
        // 보상을 다 받은 상태다(원작 Quest.Complete). 공통 파일 소유자는 관문이 없어 불러오기 알림에서도 연다(원작도 불러온 뒤 시작 때 연다).
        internal static void UnlockEndowmentsOfFinishedQuests(QuestManager quests, GlobalProgress global)
        {
            IReadOnlyList<EndowmentDefinition> definitions = GameplayDataSettings.Progression.Endowments;
            for (int position = 0; position < definitions.Count; position++)
            {
                EndowmentDefinition definition = definitions[position];
                int questId = definition.UnlockQuestId;
                if (questId == 0 || definition.UnlockedByDefault || global.IsEndowmentUnlocked(definition.Index)) continue;
                // 위에서 잠긴 재능만 남겼으므로 공통 파일 소유자는 확인 없이 연다. 쓰기 실패는 그 소유자가 경고한다.
                if (quests.IsFinished(questId)) global.UnlockEndowment(definition.Index);
            }
        }
    }
}
