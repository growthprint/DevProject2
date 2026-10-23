#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;

namespace Game.Core
{
    // 게임 슬롯과 옵션이 함께 쓰는 키 저장소다. 내용은 해석하지 않고, 키는 저장 루트 아래 상대 경로다. 예: "Saves/slot-1"
    // 모든 메서드는 메인 스레드에서 끝난다. 취소는 없다. 앱이 끝나며 버려진 쓰기는 원자적 교체라 이전 파일을 남긴다.
    public interface ISaveStorage
    {
        // 주 저장을 읽는다. 없으면 null이고, 읽거나 해석하지 못하면 예외로 전달한다.
        UniTask<string?> ReadAsync(string key);

        // 주 저장을 원자적으로 교체한다. keepBackup이면 덮어쓰는 주 저장을 백업(.bak)으로 남기고,
        // 아니면 그 주 저장은 버리고 백업 파일은 건드리지 않는다.
        UniTask WriteAsync(string key, string data, bool keepBackup);

        // 백업(.bak)을 읽는다. 없으면 null이다.
        UniTask<string?> ReadBackupAsync(string key);

        // 백업이 expectedBackup과 같을 때만 그 내용으로 주 저장을 덮어쓰고 true다. 백업이 없거나 바뀌었으면 false다. 백업은 남는다.
        UniTask<bool> RestoreBackupAsync(string key, string expectedBackup);

        // 주 저장만 지운다. 이미 없으면 성공이며 백업은 남는다.
        UniTask DeleteAsync(string key);

        // 주 저장의 마지막 수정 시각(UTC)이다. 없으면 null이다.
        UniTask<DateTime?> GetLastWriteUtcAsync(string key);
    }
}
