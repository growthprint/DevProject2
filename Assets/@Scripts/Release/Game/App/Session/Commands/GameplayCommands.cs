#nullable enable
using DuckCharacter.AvatarParts;

namespace Game.App
{
    // 여러 상태 소유자에 걸친 명령이다. 값을 바꾸는 명령은 모든 소유자의 조건을 먼저 확인한 뒤 소유자마다 차례로 적용하므로,
    // 확인을 통과한 뒤의 적용은 실패하지 않는다. 월드 초기화와 튜토리얼 재시작은 소유자마다 차례로 맞추고, 소유 그래프 생성은 실패하지 않는다.
    // 메인 스레드에서만 부른다.
    // 묶음마다 명령을 partial 새 파일에 나눠 쓰도록 partial로 둔다. 지불과 환급은 GameplayCommands.Economy.cs에 있다.
    internal static partial class GameplayCommands
    {
        // 처치나 대사 완료를 진행에 기록하고, 처음 완료한 사건에만 경험치를 준다. 경험치 넘침은 기록 전에 거절하므로
        // 이미 완료한 사건도 넘치면 거절한다. 누적 경험치가 long 끝에 닿아야 생기는 경우라 실제로는 일어나지 않는다.
        internal static bool TryCompleteEvent(GameSessionManager session, ExperienceManager experience, string eventId,
            long experienceReward)
        {
            if (!experience.CanAdd(experienceReward)) return false;
            if (!session.World.TryCompleteEvent(eventId, out bool newlyCompleted)) return false;
            // 같은 호출 안에서 CanAdd를 통과했으므로 처음 완료한 사건의 경험치는 늘 더해진다. 보상 0은 경험치 소유자가 알림 없이 넘긴다.
            if (newlyCompleted) experience.Add(experienceReward);
            return true;
        }

        // 외형 편집 확인을 메모리에만 확정하고, 파일 저장은 다음 저장 지점이 맡는다. 기지에서는 외형만 바꾸고,
        // 프롤로그 거울 세션에서는 거울 닫힘 체크포인트도 함께 기록한다. 편집은 이 두 곳에서만 열리고, 잘못된 외형은 기록 없이 거절한다.
        internal static bool TryCommitCustomization(GameSessionManager session, PlayerAppearance appearance, DuckAvatarPreset? value)
        {
            if (!appearance.CanApply(value)) return false;
            GameSessionSnapshot snapshot = session.Snapshot();
            // 편집 확정 동안 명령 게이트가 열려 있고 같은 스냅샷으로 거울 세션을 확인했으므로 거울 닫힘은 늘 기록된다.
            if (CampaignRules.IsPrologueMirrorSession(snapshot)) session.Campaign.CloseMirrorCheckpoint();
            // 같은 호출 안에서 CanApply를 통과했으므로 외형 적용은 늘 성공하고 value는 null이 아니다.
            appearance.Apply(value);
            return true;
        }

        // 전투 월드의 첫 초기화다. 플레이어를 먼저 만들어, 저장할 소유 그래프가 있으면 플레이어도 늘 있게 한다.
        // 두 단계 모두 없는 값만 채운다. 플레이어 단계는 명령 관문이 닫혔을 때만 거절하고, 그래프 단계는 실패하지 않는다.
        // 원작 LevelManager.InitLevel처럼 기지 월드는 들어올 때마다 DestroyInBase 태그 아이템을 지운다. 지운 장비의 효과는 이 뒤에 맞춘다.
        internal static bool TryInitializeWorld(GameSessionManager session, PlayerVitals vitals, OriginalGameplayCatalog catalog)
        {
            if (!vitals.TryEnsurePlayer(catalog)) return false;
            session.Inventory.EnsureGraph(catalog);
            if (session.Snapshot().SceneId == GameScene.BaseCamp) session.Inventory.DestroyInBase(catalog);
            return true;
        }

        // 소비품 하나를 써서 생존 수치에 효과를 준다. 효과를 받을 수 있는지 먼저 확인한 뒤 소지품을 줄이므로,
        // 받을 수 없는 상태에서는 아이템이 사라지지 않는다. difficulty와 scene은 음수 회복 피해의 배율과 사망 규칙을 정한다.
        // 원작 Item.onUseStatic처럼 사용이 확정된 뒤 퀘스트 사용 과제에 알린다.
        internal static bool TryUseItem(GameSessionManager session, PlayerVitals vitals, QuestManager quests, OriginalGameplayCatalog catalog,
            string id, DifficultyState? difficulty, GameScene scene)
        {
            if (!vitals.CanApplyItemEffect(catalog)) return false;
            if (!session.Inventory.TryConsumeOriginalItem(catalog, id, out OriginalItemDefinition? effect)) return false;
            // 같은 호출 안에서 CanApplyItemEffect를 통과했고, 소비 확정 알림의 구독자는 Runtime의 명령 관문
            // (GameRuntime.AcceptsSessionCommands)을 닫지 않으므로 효과를 받을 플레이어가 늘 있다.
            vitals.ApplyItemEffect(effect, catalog, difficulty, scene);
            // 사용 과제의 수는 퀘스트 소유자가 센다. 요구 수에 닿았거나 맞는 과제가 없으면 그쪽이 아무것도 바꾸지 않는다.
            quests.NotifyItemUsed(effect.Id);
            return true;
        }

        // 원본 튜토리얼(Level_Guide)처럼 사망 뒤 새 출격으로 다시 시작한다. 명령 관문을 보지 않는 Runtime 전환이라 세션이
        // 월드 첫 초기화와 같은 계산으로 빠진 소유 그래프를 먼저 채우고, 생존 수치는 새 게임 플레이어와 최대 체력으로 맞춘다.
        // 두 단계 모두 거절이 없어 결과를 돌려주지 않는다.
        internal static void RestartTutorial(GameSessionManager session, PlayerVitals vitals, OriginalGameplayCatalog catalog)
        {
            session.Inventory.EnsureGraph(catalog);
            vitals.ApplyTutorialRestart(catalog);
        }
    }
}
