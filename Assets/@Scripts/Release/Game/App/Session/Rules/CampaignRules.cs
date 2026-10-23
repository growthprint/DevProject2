#nullable enable

namespace Game.App
{
    // 세션 스냅샷만으로 판정하는 도입부와 출격 규칙이다. 상태를 갖지 않으며, 세션 명령과 Runtime, 씬 컨트롤러가 함께 쓴다.
    internal static class CampaignRules
    {
        // 이어하기 후보를 Manager 없이 저장 스냅샷만으로 계산한다. 도입부 단계면 그 단계의 씬으로 보낸다.
        // 기지 해금 뒤에는 결과 없는 출격을 닫거나, 결과만 기록하고 정산하지 않은 레거시 저장을 한 번 정산해 기지로 보낸다.
        // 바뀔 것이 없으면 같은 객체를 돌려준다. 호출자가 저장 없음을 먼저 거절하므로 saved는 늘 있다.
        internal static GameSessionSnapshot PrepareContinueSnapshot(GameSessionSnapshot saved)
        {
            CampaignState campaign = saved.Campaign;
            if (campaign.Progress.Stage != CampaignStage.BaseUnlocked)
            {
                // CampaignProgress가 단계 범위를 보장하므로 씬 조회는 null이 될 수 없어 '!'로 null 경고를 닫는다.
                GameScene stageScene = SceneForStage(campaign.Progress.Stage)!.Value;
                return saved.SceneId == stageScene ? saved : saved.WithScene(stageScene);
            }
            RaidState? raid = campaign.Raid;
            // 정산과 종료는 현재 출격 값만 쓰는 신뢰 경로다. 결과의 플레이 상태 규칙은 저장을 읽을 때 CampaignState.TryCreate가 검사했다.
            if (raid?.Result != null)
                campaign = campaign.WithSettlement();
            // 결과 없이 출격을 닫아도 소유물과 시설, 분실물, 시간을 모두 그대로 보존한다.
            else if (raid != null)
                campaign = campaign.WithRaidClosed();
            if (ReferenceEquals(campaign, saved.Campaign) && saved.SceneId == GameScene.BaseCamp)
                return saved;
            return saved.WithCampaign(campaign).WithScene(GameScene.BaseCamp);
        }

        // 프롤로그 거울 편집 세션인지 판정한다. Runtime 외형 편집과 프롤로그 출구가 이 규칙을 함께 쓴다.
        internal static bool IsPrologueMirrorSession(GameSessionSnapshot snapshot) =>
            IsStageSession(snapshot, GameScene.Prologue, CampaignStage.Prologue);

        // 프롤로그 거울 편집을 닫은 세션이다. 프롤로그 출구는 이때만 열린다.
        internal static bool IsPrologueMirrorClosed(GameSessionSnapshot snapshot) =>
            IsPrologueMirrorSession(snapshot) && snapshot.Campaign.Progress.CheckpointId == CampaignProgress._prologueMirrorClosed;

        // 튜토리얼 세션인지 판정한다. 튜토리얼 사망 처리와, Runtime이 되돌아갈 슬롯 파일을 확인할 때 이 규칙을 함께 쓴다.
        internal static bool IsTutorialSession(GameSessionSnapshot snapshot) =>
            IsStageSession(snapshot, GameScene.Tutorial, CampaignStage.Tutorial);

        // 준비 씬이 첫 선택인지 기지 뒤 재설정인지 가른다. 준비 씬 컨트롤러의 화면 모드와 재설정 확정이 같은 판정을 쓴다.
        // 이어하기는 기지 해금 단계를 늘 기지로 보내므로 재설정 세션은 파일에서 다시 열리지 않는다.
        internal static bool IsDifficultyReselectSession(GameSessionSnapshot snapshot) =>
            IsStageSession(snapshot, GameScene.Prepare, CampaignStage.BaseUnlocked);

        // 기지 해금 뒤 출격 없는 기지 세션이다. 원작 기지의 난이도 재설정(SelectDifficulty)은 이때만 연다.
        internal static bool IsUnlockedBaseSession(GameSessionSnapshot snapshot) =>
            IsStageSession(snapshot, GameScene.BaseCamp, CampaignStage.BaseUnlocked);

        // 씬만 보면 다른 진행에도 그 씬의 규칙이 걸리므로 단계와 출격 없음까지 함께 확인한다. 도입부와 기지 판정이 함께 쓴다.
        static bool IsStageSession(GameSessionSnapshot snapshot, GameScene scene, CampaignStage stage) =>
            snapshot.SceneId == scene && snapshot.Campaign.Progress.Stage == stage && snapshot.Campaign.Raid == null;

        // 원본 파일명이나 배열 순서 대신 승인된 진행 단계를 앱 씬 ID에 대응한다. 정의 밖 단계는 던지지 않고 null을 돌려준다.
        // Try 경로는 이를 규칙 거절로 처리한다.
        internal static GameScene? SceneForStage(CampaignStage stage)
        {
            switch (stage)
            {
                case CampaignStage.Prepare: return GameScene.Prepare;
                case CampaignStage.Intro: return GameScene.Intro;
                case CampaignStage.Prologue: return GameScene.Prologue;
                case CampaignStage.Tutorial: return GameScene.Tutorial;
                case CampaignStage.BaseUnlocked: return GameScene.BaseCamp;
                default: return null;
            }
        }
    }
}
