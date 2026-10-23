#nullable enable
using System;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using DevProject.Serialization;
using Game.Core;

namespace Game.App
{
    // 원작 SavesSystem의 슬롯 파일 정책이다. 여섯 슬롯 파일의 쓰기, 백업 복구, 삭제, 검사와 해석을 맡고 파일 접근은 ISaveStorage에 맡긴다.
    // SaveManager가 Configure에서 한 번 만들고 슬롯은 0부터 세는 인덱스로 넘긴다. 선택, 수집, 상태 게시는 SaveManager가 맡는다.
    // 스레드 풀에서는 JSON 변환만 한다.
    internal sealed class GameSaveSlotFiles
    {
        // 저장소는 내용의 의미를 모르고 키별 문자열만 다룬다. 검증과 백업 정책은 이 클래스가 정한다.
        readonly ISaveStorage _storage;
        // 저장 형식과 파일 정책을 나누려고 변환기를 주입받는다. 여섯 슬롯 검사가 스레드 풀에서 동시에 쓴다.
        readonly IJsonCodec<GameSessionSnapshot> _codec;
        // 같은 슬롯 파일의 저장, 복구, 삭제 순서를 정하는 유일한 장치다. 비동기 대기자는 들어온 순서대로 풀리므로 나중에 요청한 쓰기가
        // 늘 나중에 끝나고, 쓰기 직전 검사와 _knownPrimaries도 이 안에서만 다룬다. 읽기와 검사는 잠그지 않는다.
        readonly SemaphoreSlim[] _slotLocks;
        // 슬롯별로 마지막에 쓰거나 쓰기 직전에 해석한 주 저장 원문과 그 상태다. 주 저장 원문이 이것과 같으면
        // 원본 SaveFile처럼 다시 해석하지 않는다. 슬롯 잠금 안에서만 읽고 쓰며, 복구와 삭제가 지운다.
        readonly (string Payload, GameSaveState State)?[] _knownPrimaries;

        // 슬롯마다 잠금 하나와 빈 원문 기억 자리를 만든다. 만드는 것만으로는 파일을 건드리지 않는다.
        internal GameSaveSlotFiles(ISaveStorage storage, IJsonCodec<GameSessionSnapshot> codec)
        {
            _storage = storage;
            _codec = codec;
            _slotLocks = new SemaphoreSlim[GameSaveSlotId._count];
            _knownPrimaries = new (string Payload, GameSaveState State)?[GameSaveSlotId._count];
            for (int index = 0; index < _slotLocks.Length; index++)
                _slotLocks[index] = new SemaphoreSlim(1, 1);
        }

        // 0부터 세는 슬롯 인덱스의 저장소 키다. 인덱스 0이 Saves/slot-1이다. 순수 함수라 따로 보관하지 않고 쓸 때 계산한다.
        static string KeyAt(int index) => SaveManager.SlotKey(new GameSaveSlotId(index + 1));

        // 예외를 저장 상태로 나누는 유일한 규칙이다. 형식, 인코딩, 정의 손상(InvalidDataException)은 Corrupt이고 나머지는 Error다.
        // 슬롯 검사와, SaveManager.Classify를 거친 이어하기 실패 처리가 함께 쓴다.
        internal static GameSaveState Classify(Exception error) =>
            error is InvalidDataException ? GameSaveState.Corrupt : GameSaveState.Error;

        // 이어하기가 읽을 주 저장 원문이다. 파일이 없으면 null이고, 잘못된 UTF-8은 손상으로 한 번 보고한 뒤 던진다.
        // 읽기만 하므로 슬롯 잠금을 잡지 않는다.
        internal UniTask<string?> ReadPrimaryAsync(int index) => ReadTextAsync(_storage.ReadAsync, KeyAt(index));

        // 슬롯 잠금을 먼저 잡아 호출 순서대로 기록하므로, 오래된 스냅샷이 새 스냅샷을 덮지 않는다.
        // 쓰기 직전 주 저장이 정상이면 백업으로 남기고, 없거나 손상이면 백업은 그대로 둔 채 주 파일만 교체한다.
        // 주 저장을 읽지 못하면 원 예외를 InnerException으로 담은 IOException으로 쓰기를 거부한다.
        internal async UniTask WriteAsync(int index, GameSessionSnapshot snapshot)
        {
            string key = KeyAt(index);
            SemaphoreSlim slotLock = _slotLocks[index];
            await slotLock.WaitAsync();
            try
            {
                string payload = await UniTask.RunOnThreadPool(() => _codec.Write(snapshot));
                // 백업 보존 여부는 주 저장 상태만으로 정하므로 백업은 읽지 않는다.
                GameSaveState previous = await PrimaryStateBeforeWriteAsync(index, key);
                await _storage.WriteAsync(key, payload, previous == GameSaveState.Available);
                // 방금 쓴 원문은 이 변환기가 만든 정상 저장이므로, 다음 저장은 주 저장이 이 원문 그대로면 해석하지 않는다.
                _knownPrimaries[index] = (payload, GameSaveState.Available);
            }
            finally { slotLock.Release(); }
        }

        // 슬롯 잠금 안에서 쓰기 직전 주 저장 상태를 정한다. 원문을 읽어 기억한 원문과 같으면 그 상태를 쓰고, 다르면 해석해
        // 정상, 없음, 손상으로 나누고 그 원문과 함께 기억한다. 읽기는 저장마다 하지만, 큰 비용인 해석과 검증은 원문이 바뀐 때만 한다.
        // 읽지 못한 주 저장(Error)은 기억하지 않고, 원 예외를 InnerException으로 담은 IOException으로 쓰기를 거부한다.
        async UniTask<GameSaveState> PrimaryStateBeforeWriteAsync(int index, string key)
        {
            (string Payload, GameSaveState State)? known = _knownPrimaries[index];
            string? current = null;
            PrimaryInspection inspection;
            try
            {
                current = await ReadTextAsync(_storage.ReadAsync, key);
                if (current == null) inspection = PrimaryInspection.Missing;
                else if (known != null && string.Equals(known.Value.Payload, current, StringComparison.Ordinal)) return known.Value.State;
                else inspection = await ClassifyAsync(current);
            }
            // 예외 경계: InspectPrimaryAsync와 같이 손상, 입출력, 변환기의 예상 밖 실패를 분류된 검사 결과로 바꾼다.
            catch (Exception error) { inspection = PrimaryInspection.Unreadable(error); }
            if (inspection.State == GameSaveState.Error)
            {
                _knownPrimaries[index] = null;
                throw new IOException(inspection.Failure?.Message, inspection.Failure);
            }
            _knownPrimaries[index] = current == null ? ((string, GameSaveState)?)null : (current, inspection.State);
            return inspection.State;
        }

        // 슬롯의 백업을 잠금 안에서 다시 검증한 뒤, 없거나 손상된 주 저장을 덮어쓴다. 백업은 남는다.
        // 복구할 수 없으면 이유별 예외로 거부한다. 정상 주 저장은 InvalidOperationException이다.
        // 읽지 못한 주 저장과 검증 뒤 바뀐 백업은 IOException, 없거나 손상된 백업은 여기서 한 번 보고한 InvalidDataException이다.
        internal async UniTask RecoverAsync(int index)
        {
            string key = KeyAt(index);
            SemaphoreSlim slotLock = _slotLocks[index];
            await slotLock.WaitAsync();
            // 복구는 주 저장을 바꾸므로, 성공하든 실패하든 기억한 주 저장 원문을 버리고 다음 저장이 다시 해석하게 한다.
            _knownPrimaries[index] = null;
            try
            {
                // 복구 가능 여부는 주 저장 상태로 정하고, 백업은 아래에서 잠금 안에서 다시 검증한다.
                PrimaryInspection primary = await InspectPrimaryAsync(key);
                if (primary.State == GameSaveState.Available)
                    throw new InvalidOperationException("정상 저장이 있으므로 백업으로 덮어쓸 수 없습니다.");
                if (primary.State == GameSaveState.Error) throw new IOException(primary.Failure?.Message, primary.Failure);
                string? backup = await ReadTextAsync(_storage.ReadBackupAsync, key);
                // 메뉴가 복구 가능으로 본 뒤 백업이 사라진 경우다. 손상 분류는 UI 경계가 다시 기록하지 않으므로 여기서 한 번 보고한다.
                if (backup == null)
                {
                    var missing = new InvalidDataException("복구할 백업이 없습니다.");
                    Log.Instance.InvalidData(missing);
                    throw missing;
                }
                // 손상된 백업은 보고한 뒤 던져, 복원하지 않는다.
                InvalidDataException? failure = (await DecodeAndReportAsync(backup)).failure;
                if (failure != null) throw failure;
                if (!await _storage.RestoreBackupAsync(key, backup))
                    throw new IOException("검증 이후 저장 백업이 변경되었습니다.");
            }
            finally { slotLock.Release(); }
        }

        // 저장, 복구와 같은 슬롯 잠금 안에서 주 파일만 삭제한다. 백업은 남고, 파일이 이미 없어도 성공이다.
        internal async UniTask DeleteAsync(int index)
        {
            SemaphoreSlim slotLock = _slotLocks[index];
            await slotLock.WaitAsync();
            // 삭제한 주 저장의 원문을 더 기억하지 않는다.
            _knownPrimaries[index] = null;
            try { await _storage.DeleteAsync(KeyAt(index)); }
            finally { slotLock.Release(); }
        }

        // 슬롯 하나를 검사해 목록 요약과, 슬롯 선택 때 소유자를 채울 정상 주 저장을 돌려준다. 주 저장이 정상이 아니면 그 값은 null이다.
        // 주 파일이 없거나 손상됐으면 검증된 백업을 복구 후보로 싣는다. 입출력 같은 Error 실패만 원 예외를 한 번 기록한다.
        // 손상은 예상된 결과라 손상 보고 외에 따로 기록하지 않는다. 저장과 복구는 원 예외를 감싸 던지므로 기록은 여기서만 한다.
        internal async UniTask<(GameSaveSlotSummary Summary, GameSessionSnapshot? Primary)> InspectAsync(int index)
        {
            string key = KeyAt(index);
            PrimaryInspection primary = await InspectPrimaryAsync(key);
            GameSessionSnapshot? backup = null;
            if (primary.State == GameSaveState.Error)
            {
                if (primary.Failure != null) Log.Instance.Exception(primary.Failure);
            }
            else if (primary.State != GameSaveState.Available) backup = await ReadBackupCandidateAsync(key);
            DateTime? written;
            try { written = await _storage.GetLastWriteUtcAsync(key); }
            // 예외 경계: 파일 정보의 입출력과 권한 오류만 받아 수정 시각을 null로 두고 검사 결과는 유지한다.
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { written = null; }
            // 요약은 표시 값만 담으므로 주 저장 기록은 따로 돌려준다.
            return (new GameSaveSlotSummary(primary.State, primary.Snapshot, backup, written), primary.Snapshot);
        }

        // 주 파일이 없거나 손상된 슬롯의 백업을 검증해 복구 후보로 돌려준다. 백업이 없거나 쓸 수 없으면 null이다.
        // 쓸 수 없는 이유는 화면이 쓰지 않으므로 요약에 싣지 않고 보고나 기록만 한다.
        async UniTask<GameSessionSnapshot?> ReadBackupCandidateAsync(string key)
        {
            try
            {
                string? backup = await ReadTextAsync(_storage.ReadBackupAsync, key);
                if (backup == null) return null;
                // 손상된 백업은 보고하고 후보에서 뺀다. 에디터와 DEBUG 빌드에서 보고가 던진 예외는 아래 catch가 같은 실패로 받는다.
                return (await DecodeAndReportAsync(backup)).snapshot;
            }
            // 예외 경계: 백업의 입출력, 잘못된 UTF-8, 변환기 실패를 후보 없음으로 바꿔 목록 조회를 멈추지 않는다. Error 분류만 기록한다.
            catch (Exception error)
            {
                if (Classify(error) == GameSaveState.Error) Log.Instance.Exception(error);
                return null;
            }
        }

        // 주 저장을 정상, 없음, 손상, 오류로 나눈다. 입출력 오류와 손상을 구별하고 원 예외를 남겨, 저장이 읽지 못한 파일을 덮어쓰지 않게 한다.
        // 손상은 메인 스레드에서 보고한다. 에디터와 DEBUG 빌드에서 보고가 던진 예외도 아래 catch가 같은 손상으로 분류한다.
        async UniTask<PrimaryInspection> InspectPrimaryAsync(string key)
        {
            try
            {
                string? payload = await ReadTextAsync(_storage.ReadAsync, key);
                return payload == null ? PrimaryInspection.Missing : await ClassifyAsync(payload);
            }
            // 예외 경계: 손상, 입출력, 변환기의 예상 밖 실패를 분류된 검사 결과로 바꾼다.
            catch (Exception error) { return PrimaryInspection.Unreadable(error); }
        }

        // 읽은 주 저장 원문을 해석해 정상이나 손상으로 나눈다. 손상 보고는 DecodeAndReportAsync가 하고, 보고가 던진 예외는 호출자의 경계가 받는다.
        // 목록 검사와 쓰기 직전 검사가 같은 분류를 쓰도록 둘 다 이 메서드를 부른다.
        async UniTask<PrimaryInspection> ClassifyAsync(string payload)
        {
            (GameSessionSnapshot? snapshot, InvalidDataException? failure) = await DecodeAndReportAsync(payload);
            return failure == null ? PrimaryInspection.Available(snapshot!) : PrimaryInspection.Unreadable(failure);
        }

        // 잘못된 UTF-8을 손상으로 바꿔 모든 슬롯 읽기가 같은 분류를 쓰게 한다. 다른 예외는 그대로 전달한다.
        // 손상 슬롯은 한 번만 기록하므로 해석 손상처럼 읽기 소유자인 여기서 읽기마다 보고하고, 호출자는 다시 기록하지 않는다.
        // 저장소는 메인 스레드에서 끝나므로 여기서 기록해도 된다. 에디터와 DEBUG 빌드에서는 보고가 같은 예외를 먼저 던진다.
        static async UniTask<string?> ReadTextAsync(Func<string, UniTask<string?>> read, string key)
        {
            try { return await read(key); }
            // 예외 경계: 저장소의 잘못된 UTF-8 판정을 저장 손상(InvalidDataException)으로 번역해 한 번 보고한다.
            catch (DecoderFallbackException error)
            {
                var failure = new InvalidDataException("게임 저장 파일이 올바른 UTF-8이 아닙니다. 기존 파일을 보존합니다.", error);
                Log.Instance.InvalidData(failure);
                throw failure;
            }
        }

        // 해석을 스레드 풀에서 하고 메인 스레드로 돌아온다. 보고와 분류는 호출자가 메인 스레드에서 한다.
        internal UniTask<(GameSessionSnapshot? snapshot, InvalidDataException? failure)> DecodeAsync(string payload) =>
            UniTask.RunOnThreadPool(() => Decode(payload));

        // 해석이 실패하면 메인 스레드에서 손상을 한 번 보고한다. 에디터와 DEBUG 빌드에서는 보고가 그 예외를 던지고 호출자의 경계가 받는다.
        // 이어하기 읽기는 아이템 대조까지 마친 뒤 보고해야 하므로 이 함수 대신 DecodeAsync를 쓴다.
        async UniTask<(GameSessionSnapshot? snapshot, InvalidDataException? failure)> DecodeAndReportAsync(string payload)
        {
            (GameSessionSnapshot? snapshot, InvalidDataException? failure) result = await DecodeAsync(payload);
            if (result.failure != null) Log.Instance.InvalidData(result.failure);
            return result;
        }

        // 스레드 풀에서 불리므로 Unity API와 로그를 쓰지 않는다. 변환기가 만든 값은 이미 검증된 불변 값이라 다시 검사하거나 복사하지 않는다.
        (GameSessionSnapshot? snapshot, InvalidDataException? failure) Decode(string payload)
        {
            if (!_codec.TryRead(payload, out GameSessionSnapshot? snapshot, out string error))
                return (null, new InvalidDataException("게임 저장 데이터가 손상되었습니다. 기존 파일을 보존합니다.", new FormatException(error)));
            return (snapshot, null);
        }

        // 주 저장 파일 하나의 검사 결과다. 저장, 복구, 목록 검사가 같은 분류를 쓰도록 아래 팩터리로만 만든다.
        // 백업 후보는 목록 검사만 보므로 여기 담지 않는다.
        readonly struct PrimaryInspection
        {
            // 값 조합 규칙은 아래 팩터리가 정하므로 생성자는 private로 두어 다른 조합을 막는다.
            PrimaryInspection(GameSaveState state, GameSessionSnapshot? snapshot, Exception? failure)
            {
                State = state;
                Snapshot = snapshot;
                Failure = failure;
            }

            // 주 파일이 없으면 스냅샷도 원 예외도 없다.
            public static PrimaryInspection Missing => new PrimaryInspection(GameSaveState.Missing, null, null);

            // 해석에 성공한 스냅샷만 받는다.
            public static PrimaryInspection Available(GameSessionSnapshot snapshot) =>
                new PrimaryInspection(GameSaveState.Available, snapshot, null);

            // 상태는 원 예외를 Classify로 나눈 Corrupt나 Error이고, 원 예외를 그대로 보존한다.
            public static PrimaryInspection Unreadable(Exception failure) => new PrimaryInspection(Classify(failure), null, failure);

            // Missing, Available, Corrupt, Error 중 하나다.
            public GameSaveState State { get; }
            // 정상 주 저장의 불변 값이다. Available일 때만 있다.
            public GameSessionSnapshot? Snapshot { get; }
            // 주 저장을 읽지 못한 원 예외다. Corrupt나 Error일 때만 있다.
            public Exception? Failure { get; }
        }
    }
}
