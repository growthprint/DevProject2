#nullable enable
using System;

namespace Game.App
{
    // 메뉴가 이어하기나 백업 복구를 고를 때 보는 저장 상태다. Recoverable은 주 파일이 없거나 손상됐지만 검증된 백업이 있다는 뜻이다.
    // Unknown은 Runtime이 아직 조회하지 않았을 때만 쓰며, 슬롯 검사 결과는 Unknown이 되지 않는다.
    public enum GameSaveState { Unknown, Missing, Available, Corrupt, Recoverable, Error }

    // 저장 슬롯 화면에 표시할 정보만 담고 저장 스냅샷은 넘기지 않는다. SaveManager가 슬롯 검사마다 만든다.
    public sealed class GameSaveSlotSummary
    {
        // 표시 값은 정상 주 저장에서, 없으면 백업 후보에서 읽는다. 슬롯 번호는 따로 담지 않고 목록 위치로 정한다.
        // 백업 후보는 주 파일이 없거나 손상됐을 때만 SaveManager가 넘기므로 여기서 다시 검사하지 않는다.
        internal GameSaveSlotSummary(GameSaveState primaryState, GameSessionSnapshot? primarySnapshot, GameSessionSnapshot? backupSnapshot,
            DateTime? savedAtUtc)
        {
            State = backupSnapshot != null ? GameSaveState.Recoverable : primaryState;
            PrimaryMissing = primaryState == GameSaveState.Missing;
            GameSessionSnapshot? shown = primarySnapshot ?? backupSnapshot;
            Difficulty = shown?.Campaign.Difficulty;
            // 정상 주 저장은 안에 기록된 UTC 저장 시각을 먼저 쓰고, 그 밖에는 주 파일 수정 시각을 쓴다. 둘 다 모르면 null이다.
            // GameSessionSnapshot은 UnityEngine.Object가 아니므로 ?.와 ??를 써도 된다.
            SavedAtUtc = primarySnapshot?.Campaign.Gameplay.SavedAtUtc ?? savedAtUtc;
            ElapsedSeconds = shown?.Campaign.Gameplay.Clock.ElapsedSeconds;
        }
        // 백업 후보가 있으면 Recoverable이다. 읽기 실패는 Corrupt나 Error로 빈 슬롯과 구분한다.
        public GameSaveState State { get; }
        // 주 저장 파일이 없는 슬롯이다. 삭제 뒤 백업만 남은 슬롯도 포함한다.
        // UI는 저장 시각 유무가 아니라 이 값으로 빈 슬롯을 판정한다.
        public bool PrimaryMissing { get; }
        // 표시할 저장의 난이도다. 모르면 추정하지 않고 null로 둔다.
        public DifficultyState? Difficulty { get; }
        // 파일에서 확인한 저장 시각만 담고, 추정하지 않는다.
        public DateTime? SavedAtUtc { get; }
        // 전체 플레이 시간(초)이다. 기록이 없는 저장은 0초로 보이지 않도록 null로 둔다.
        public double? ElapsedSeconds { get; }
    }
}
