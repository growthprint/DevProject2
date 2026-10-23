#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Game.App
{
    // 진행과 출격 명령(TryPrepare*)은 세션을 바꾸지 않고 후보만 만든다. Runtime이 씬 준비와 저장 지점을 마친 뒤
    // GameSessionManager.Commit으로 확정한다. 프롤로그 거울 닫힘(CloseMirrorCheckpoint)만 메모리에 바로 확정한다.
    internal sealed class SessionCampaignCommands
    {
        readonly GameSessionManager _session;

        internal SessionCampaignCommands(GameSessionManager session) => _session = session;

        // 난이도 선택과 인트로 진입을 한 후보로 확정해, 선택 없이 진행된 중간 저장이 생기지 않게 한다. 후보는 인트로로의 일반 진행과 같다.
        // 준비 씬 화면만 부르고, 이어하기가 준비 단계를 준비 씬에 맞춘다. 원본 GameRulesManager처럼 난이도는 소유자 한 곳에만 두므로,
        // 고른 난이도는 SceneFlow가 후보 채택 직후 DifficultyRules에 반영하고 여기서는 받지 않는다.
        internal bool TryPrepareDifficultySelection([NotNullWhen(true)] out GameSessionSnapshot? candidate)
        {
            // 준비 씬은 기지 뒤 재설정에도 열린다. 그때 첫 선택 명령이 오면 진행이 인트로로 되돌아가므로 준비 단계에서만 받는다.
            candidate = null;
            if (_session.Snapshot().Campaign.Progress.Stage != CampaignStage.Prepare) return false;
            return TryPrepareAdvance(CampaignStage.Intro, out candidate);
        }

        // 원작 기지 SelectDifficulty의 SceneLoaderProxy.LoadScene("Prepare")다. 진행 단계와 플레이 상태는 그대로 두고 씬만 준비 씬으로 바꾼다.
        // 기지 해금 뒤 출격 없는 기지에서만 연다. 떠나는 기지 저장은 SceneFlow가 후보 채택 전에 한다(원작 SceneLoader가 로드 전에 저장).
        internal bool TryPrepareDifficultyReselect([NotNullWhen(true)] out GameSessionSnapshot? candidate)
        {
            candidate = null;
            if (!_session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = _session.Snapshot();
            // 원작 난이도 재설정 상호작용은 해금된 기지에만 있어 출격 없는 해금 기지 세션에서만 받는다.
            if (!CampaignRules.IsUnlockedBaseSession(snapshot)) return false;
            candidate = snapshot.WithScene(GameScene.Prepare);
            return true;
        }

        // 원작 GamePrepareProcess의 goToBaseSceneIfVisted다. 재설정 세션에서 확정하면 진행 단계를 그대로 두고 기지로 돌아간다.
        // 고른 난이도의 선택 제한은 두 소유자(난이도, 공통 파일)를 보는 GameplayCommands가 먼저 확인하고, 반영은 SceneFlow가 채택 직후 한다.
        internal bool TryPrepareDifficultyReselectReturn([NotNullWhen(true)] out GameSessionSnapshot? candidate)
        {
            candidate = null;
            if (!_session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = _session.Snapshot();
            // 준비 씬 컨트롤러의 화면 모드와 같은 세션 규칙으로 재설정 세션인지 본다.
            if (!CampaignRules.IsDifficultyReselectSession(snapshot)) return false;
            candidate = snapshot.WithScene(GameScene.BaseCamp);
            return true;
        }

        // 프롤로그 거울 편집의 확인과 취소가 거울 닫힘 체크포인트를 메모리에만 확정하고, 파일 저장은 다음 저장 지점이 맡는다.
        // 외형은 GameplayCommands가 외형 소유자에 따로 확정한다. 유일한 호출자 GameplayCommands.TryCommitCustomization이
        // 명령 게이트가 열린 편집 확정 안에서 같은 스냅샷으로 거울 세션을 확인한 뒤 부르므로 거절 없이 늘 확정한다.
        internal void CloseMirrorCheckpoint()
        {
            GameSessionSnapshot snapshot = _session.Snapshot();
            // 상수 단계와 체크포인트라 검증 없는 생성자로 만든다. 출격 없는 세션이라 WithProgress는 출격 규칙에 걸리지 않는다.
            var closed = new CampaignProgress(CampaignStage.Prologue, CampaignProgress._prologueMirrorClosed);
            // 진행만 바뀐 후보라 장비 검증 없이 확정 경로로 알린다.
            _session.Commit(snapshot.WithCampaign(snapshot.Campaign.WithProgress(closed)));
        }

        // 도입부는 준비, 인트로, 프롤로그, 튜토리얼 순서다. 체크포인트 없는 다음 한 단계 후보를 만든다. 호출자는 난이도 선택과
        // 인트로, 프롤로그 씬 컨트롤러뿐이고, 프롤로그 출구는 거울 닫힘 뒤에만 켜진다. 기지 해금은 월드 종료의 탈출(TryPrepareWorldEnding)로만 만든다.
        internal bool TryPrepareAdvance(CampaignStage next, [NotNullWhen(true)] out GameSessionSnapshot? candidate)
        {
            candidate = null;
            if (!_session.AcceptsCommands) return false;
            candidate = BuildStageAdvance(next, null);
            return true;
        }

        // 다음 한 단계 후보의 공통 생성이며 실패하지 않는다. 명령 수락은 호출자가 이미 확인했다. 호출자는 자기 단계 씬에서 바로 다음
        // 단계 상수만 넘기고, 이어하기가 씬을 단계에 맞춘다. checkpointId는 null(일반 진행)이나 'tutorial:completed'(튜토리얼 완료)만 온다.
        GameSessionSnapshot BuildStageAdvance(CampaignStage next, string? checkpointId)
        {
            GameSessionSnapshot snapshot = _session.Snapshot();
            CampaignState campaign = snapshot.Campaign;
            GameScene? nextScene = CampaignRules.SceneForStage(next);
            // 다음 단계는 호출자가 넘긴 정의된 단계 상수이고 체크포인트는 null이나 상수라 검증 없는 생성자로 만들며, 다음 씬도 늘 있다.
            // 현재 단계는 기지 해금 전이라 출격이 없으므로 WithProgress는 출격 규칙에 걸리지 않는다.
            var progress = new CampaignProgress(next, checkpointId);
            return snapshot.WithCampaign(campaign.WithProgress(progress)).WithScene(nextScene!.Value);
        }

        // 해금된 기지에서 마지막 정산 다음 순번의 출격만 시작한다. 순번 범위 위반은 로그 없이 거절한다.
        // 출격 지점은 기지 씬에만 있고, 이어하기가 기지에 들어오기 전에 출격을 닫으며 중복 출발은 전환 가드가 막는다.
        internal bool TryPrepareRaidStart(string raidId, [NotNullWhen(true)] out GameSessionSnapshot? candidate)
        {
            candidate = null;
            if (!_session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = _session.Snapshot();
            CampaignState campaign = snapshot.Campaign;
            if (campaign.SettledRaidSequence == long.MaxValue) return false;
            // 위 범위 검사로 +1은 넘치지 않고, 유일한 호출자 SceneFlow.BeginRaidAsync가 새 GUID로 출격 ID를 만들므로
            // 검증 없는 생성자로 만든다. 출격 없는 기지 해금 단계에서만 불리므로 WithRaid는 출격 규칙에 걸리지 않는다.
            var raid = new RaidState(campaign.SettledRaidSequence + 1, raidId, null);
            candidate = snapshot.WithCampaign(campaign.WithRaid(raid)).WithScene(GameScene.ZeroZone);
            return true;
        }

        // 사망, 탈출, 튜토리얼 완료를 결과 화면보다 먼저 저장할 한 후보로 만든다. 규칙 위반은 로그 없이 거절하고, 불변식 위반은
        // 감지한 곳이 보고한다. 카탈로그 누락, 체력이 남은 사망, 출격이나 튜토리얼이 아닌 종료는 전투 월드가 막는다. difficulty와 player는 각 소유자의 현재 값이다.
        // - 튜토리얼 사망: 유실물 없이 restartTutorial만 세운다. Runtime이 슬롯 파일로 되돌린 뒤 GameplayCommands.RestartTutorial을 부른다.
        // - 튜토리얼 완료(Extracted): 기지 해금 단계와 'tutorial:completed' 체크포인트로 기지에 간다.
        // - 출격 사망과 탈출: 출격을 그 순번으로 닫고 기지로 간다. 사망만 소지품을 정리하고 복귀 플레이어를 endingPlayer로 주며, 그 밖에는 null이다.
        internal bool TryPrepareWorldEnding(OriginalGameplayCatalog catalog, RaidOutcome outcome, string subScene,
            float x, float y, float z, DifficultyState? difficulty, PlayerState? player,
            [NotNullWhen(true)] out GameSessionSnapshot? candidate, out bool restartTutorial, out PlayerState? endingPlayer)
        {
            candidate = null;
            // 튜토리얼 사망 말고는 Runtime이 후보를 그대로 채택하므로 거짓으로 시작한다.
            restartTutorial = false;
            // 출격 사망 말고는 플레이어를 그대로 두므로 생존 값 소유자에 줄 값이 없다.
            endingPlayer = null;
            if (!_session.AcceptsCommands) return false;
            GameSessionSnapshot snapshot = _session.Snapshot();
            CampaignState campaign = snapshot.Campaign;
            bool died = outcome == RaidOutcome.Died;
            if (campaign.Progress.Stage == CampaignStage.Tutorial)
            {
                // 튜토리얼 완료는 공통 단계 진행 도우미로 만든다. 체크포인트 문자열은 저장 파일에 남아 상수 한곳에서 읽는다.
                if (!died)
                {
                    candidate = BuildStageAdvance(CampaignStage.BaseUnlocked, CampaignProgress._tutorialCompleted);
                    return true;
                }
                // 되돌아갈 값은 슬롯 파일에 있어 세션 규칙은 여기서 바꿀 것이 없다. Runtime은 이 후보를 채택하지 않고
                // 사망 직전 슬롯 파일을 다시 불러 GameplayCommands.RestartTutorial로 다시 시작한다. 후보는 계약상 비울 수 없어 지금 확정 값을 둔다.
                candidate = snapshot;
                restartTutorial = true;
                return true;
            }
            CampaignState ended = campaign;
            if (died)
            {
                // 사망 계산의 실패는 사망 토큰 넘침 같은 구조 결함뿐이고 감지한 곳이 이미 보고했으므로 다시 기록하지 않고 거절한다.
                // 성공한 계산은 늘 후보와 복귀 플레이어를 채운다.
                if (!PrepareRaidDeath(campaign, catalog, subScene, x, y, z, difficulty, player, out CampaignState? dead,
                        out PlayerState? returned).IsOk) return false;
                ended = dead!;
                endingPlayer = returned;
            }
            // 사망 계산은 출격을 남겨 두므로 WithRaidClosed가 현재 출격을 그 순번으로 닫는다.
            candidate = snapshot.WithCampaign(ended.WithRaidClosed()).WithScene(GameScene.BaseCamp);
            return true;
        }

        // 출격 사망 뒤의 캠페인 후보를 만든다. 보호되지 않은 소지품은 하위 아이템째 유실물로 옮기고, 사망 기록은 늘 남긴다.
        // 실패는 사망 토큰 넘침 같은 구조 결함뿐이며 감지한 줄에서 보고한 Invalid를 돌려준다.
        // 복귀 플레이어는 생존 값 소유자만 받는다. 저장의 Player 구역도 그 소유자가 쓴다.
        // 성공하면 result와 returnedPlayer는 null이 아니다.
        // 출격 사망에만 쓰고, 튜토리얼 사망은 사망 직전 슬롯 파일로 되돌린다. player는 생존 값 소유자의 사망 직전 값이다.
        internal static InventoryResult PrepareRaidDeath(CampaignState before, OriginalGameplayCatalog catalog, string subScene,
            float x, float y, float z, DifficultyState? difficulty, PlayerState? player, out CampaignState? result,
            out PlayerState? returnedPlayer)
        {
            result = null;
            returnedPlayer = null;
            GameplayPersistenceState gameplay = before.Gameplay;
            // 출격 사망은 월드 첫 초기화가 소유 그래프를 만든 뒤에만 일어나므로 그래프가 있다. 포착과 정리는 실패하지 않는다.
            var inventory = new GameplayInventory(gameplay.Inventory.Items!);
            ItemOwnershipState history = inventory.CaptureCharacter();
            // 원본 Item.Sticky는 정의의 'Sticky' 태그라 장착 여부와 상관없이 남기고, DontDropOnDeadInSlot 태그는 장착 슬롯에서만 지킨다.
            // 이어하기와 저장 지점이 소유 그래프의 아이템 ID를 카탈로그와 대조하므로 정의 조회는 늘 성공한다.
            bool Retain(ItemInstanceState item, bool inSlot)
            {
                OriginalItemDefinition definition = catalog.DefinitionOf(item.OriginalItemId);
                return definition.Sticky || (inSlot && definition.RetainedOnDeath);
            }
            // 보호하지 않은 소지품을 하위 트리째 떼어 유실물 그래프로 받는다.
            ItemOwnershipState dropped = inventory.DropCarried(Retain);
            // 작업 사본 계산은 그래프 규칙을 지키므로 남은 소유 그래프는 늘 만들어진다.
            ItemOwnershipState retained = inventory.Build();
            // 사망 토큰은 사망마다 1씩 늘며 유실물 식별자가 된다. uint 범위를 넘으면 구조 결함으로 보고한다.
            uint previous = gameplay.Deaths.DeadCharacterToken ?? 0;
            if (previous == uint.MaxValue)
                return InventoryResult.Defect(new InvalidOperationException("세션 불변식 위반: 사망 토큰이 범위를 넘습니다."));
            uint token = previous + 1;
            var bodies = new List<LostBodyState>(gameplay.Deaths.LostBodies ?? Array.Empty<LostBodyState>());
            // 원작 DeadBodyManager.RecordDeath처럼 SpawnDeadBody나 떨군 아이템 유무와 상관없이 사망마다 기록을 더한다. 묘비는
            // 원작 ShouldSpawnDeadBody처럼 레벨을 열 때 DeadBodySpawner가 그때 난이도로 정한다. 토큰, 하위 씬, 좌표, 그래프는 코드가 만든 값이라
            // 검증 없는 생성자로 만들고, 세 bool 인수는 순서를 헷갈리기 쉬워 이름을 붙인다.
            bodies.Add(new LostBodyState(token, subScene, x, y, z, valid: true, spawned: false, touched: false, dropped));
            // 원작 AppendDeathInfo처럼 가장 오래된 기록부터 지워 SaveDeadbodyCount개만 남긴다. 난이도가 없으면 원작 Ruleset 기본값 1이다.
            // DifficultyState가 이 값을 0 이상으로 보장해 빈 목록에서 지우지 않는다. Unity 객체가 아니라 ?.와 ??를 써도 된다.
            int maximum = difficulty?.SaveDeadbodyCount ?? 1;
            while (bodies.Count > maximum) bodies.RemoveAt(0);
            // 소지품과 사망 기록만 바꾸고 시계, 경험치, 마커 같은 나머지 플레이 상태는 그대로 둔다. 퀵슬롯은 소지품 묶음이 맞춘다.
            // 그래프와 사망 기록은 위에서 작업 사본이 규칙을 지켜 만들었으므로 다시 검증하지 않는 WithDeathRecord를 쓴다.
            GameplayPersistenceState next = gameplay.WithDeathRecord(gameplay.Inventory.WithItems(retained),
                new DeathPersistenceState(bodies, history!, token));
            // 복귀한 플레이어는 원본 최대 체력과 스태미나로 시작하고, 에너지와 수분은 생존 값 소유자가 넘긴 사망 직전 값을 잇는다.
            // 값이 없으면 새 게임 기본값이다. 진행 사건은 바꾸지 않는다. 플레이어 정의는 씬 카탈로그에 늘 저작돼 있고 씬 데이터 테스트가 확인하는
            // 빌드 데이터라, 정의 수치와 소유자의 생존 값으로 복귀 플레이어를 검증 없이 만든다. PlayerState는 Unity 객체가 아니라 ?.와 ??를 써도 된다.
            PlayerState returned = OriginalCharacter.NewOriginalPlayer(catalog.PlayerCharacter,
                player?.Energy ?? OriginalCharacter._newGameEnergy, player?.Hydration ?? OriginalCharacter._newGameHydration);
            // 복귀 플레이어는 SceneFlow가 후보 채택 직후 PlayerVitals에 반영하므로 허브 사본의 플레이어는 그대로 둔다.
            // 그래프가 있던 캠페인이라 허브 사본에 플레이어가 이미 있어, 그래프가 있으면 플레이어도 있다는 불변식을 지킨다.
            result = before.WithGameplay(next);
            returnedPlayer = returned;
            return InventoryResult.Ok;
        }
    }
}
