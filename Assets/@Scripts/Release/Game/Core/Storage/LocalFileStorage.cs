#nullable enable annotations
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Game.Core
{
    // ISaveStorage의 로컬 파일 구현이다. 저장 루트 아래 "키.json"에 원자적으로 쓰고, 백업은 "키.json.bak" 하나뿐이다.
    // 임시 파일은 주 파일 이름 뒤에 "."과 32자리 소문자 16진 GUID와 ".tmp"를 붙인다. 파일 I/O 경계라 파일 작업 실패는 예외로 전달한다.
    // 저장 폴더에 남아 있을 수 있는 .bak.h-*, .bak.1~10, .corrupt.* 파일은 만들지도 지우지도 않는다.
    public sealed class LocalFileStorage : ISaveStorage
    {
        // 저장 파일 이름 조각이다. 사용자 폴더에 이미 저장된 파일을 계속 읽으려면 이름이 같아야 한다.
        const string _extension = ".json";
        const string _backupSuffix = ".bak";
        const string _temporarySuffix = ".tmp";
        // 임시 파일 이름의 GUID 조각이다. 쓰기의 "N" 형식(소문자 16진 32자)과 남은 임시 파일을 찾는 패턴이 늘 맞아야 한다.
        const string _temporaryIdFormat = "N";
        const string _temporaryIdPattern = "[0-9a-f]{32}";

        // 커밋을 시도한 뒤 실패한 예외의 Data에 붙이는 진단 키다. 값은 주 저장 전체 경로다.
        const string _primaryPathData = "SavePrimaryPath";

        const int _bufferSize = 4096;

        // BOM 없는 UTF-8이다. 잘못된 바이트나 문자를 조용히 바꾸지 않고 예외로 실패한다.
        static readonly UTF8Encoding _encoding = new UTF8Encoding(false, true);

        // 주 저장 전체 경로마다 하나인 비동기 잠금이다. 인스턴스나 키 대소문자가 달라도 같은 파일이면 같은 잠금을 쓴다.
        // 한 키의 주 저장, 백업, 임시 파일 작업은 모두 이 잠금을 한 번만 잡는다. 호출 하나 안의 파일 일관성만 지키며,
        // 읽고 판단한 뒤 쓰는 여러 호출의 순서는 그 판단을 소유한 호출자가 정한다.
        static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        // 호출마다 람다를 새로 만들지 않도록 잠금 생성 함수를 한 번만 만든다.
        static readonly Func<string, SemaphoreSlim> _createLock = _ => new SemaphoreSlim(1, 1);

        // 정규화한 저장 루트 절대 경로다. 생성 인수가 잘못되면 null로 남고, 파일 작업은 모두 예외로 실패한다.
        readonly string _root;

        // 주 저장 교체 동작이다. 제품은 File.Replace를 쓰고, 테스트는 교체 중 실패나 취소를 넣는다.
        readonly Action<string, string, string?> _replaceFile;

        // 실패한 쓰기의 임시 파일을 지우는 동작이다. 제품은 File.Delete를 쓰고, 테스트는 정리 실패를 넣는다.
        readonly Action<string> _deleteTemporary;

        // 저장 루트는 절대 경로여야 한다. 보통 Application.persistentDataPath를 넘긴다.
        public LocalFileStorage(string rootDirectory) : this(rootDirectory, File.Replace, File.Delete) { }

        // 테스트가 교체와 임시 파일 정리 동작을 넣는 생성자다. internal이라 Game.Tests.EditMode만 쓸 수 있다.
        internal LocalFileStorage(string rootDirectory, Action<string, string, string?> replaceFile, Action<string> deleteTemporary)
        {
            _replaceFile = replaceFile;
            _deleteTemporary = deleteTemporary;
            // 드라이브 상대 경로도 거부해 저장 위치가 실행 폴더에 따라 바뀌지 않게 한다. 에디터와 DEBUG 빌드는 예외로 멈추고,
            // 릴리즈는 결함을 기록한 뒤 루트를 비워 둔 채 생성을 끝내 잘못된 위치에 쓰지 않는다.
            if (string.IsNullOrWhiteSpace(rootDirectory) || !Path.IsPathFullyQualified(rootDirectory))
            {
                Log.Instance.Fail(new ArgumentException("저장 루트 폴더는 절대 경로여야 합니다.", nameof(rootDirectory)));
                return;
            }
            _root = Path.GetFullPath(rootDirectory);
        }

        // 주 저장을 읽는다. 없으면 null이다.
        public UniTask<string?> ReadAsync(string key) =>
            RunLockedAsync<string?>(key, primary => ReadFileAsync(primary));

        // 주 저장을 원자적으로 교체한다. null 내용은 결함으로 보고하고 릴리즈는 아무것도 쓰지 않는다.
        public async UniTask WriteAsync(string key, string data, bool keepBackup)
        {
            if (data == null)
            {
                Log.Instance.Fail(new ArgumentNullException(nameof(data)));
                return;
            }

            await RunLockedAsync<bool>(key, async primary =>
            {
                await WriteFileAsync(primary, data, keepBackup);
                return true;
            });
        }

        // 백업(.bak)을 읽는다. 없으면 null이다.
        public UniTask<string?> ReadBackupAsync(string key) =>
            RunLockedAsync<string?>(key, primary => ReadFileAsync(primary + _backupSuffix));

        // 백업이 expectedBackup과 같을 때만 주 저장을 그 내용으로 덮어쓴다. 백업이 없거나 바뀌었으면 false다.
        // null 기대값은 결함으로 보고하고 릴리즈는 false를 돌려준다.
        public async UniTask<bool> RestoreBackupAsync(string key, string expectedBackup)
        {
            if (expectedBackup == null)
                return Log.Instance.Fail(new ArgumentNullException(nameof(expectedBackup)));

            return await RunLockedAsync<bool>(key, primary => RestoreFileAsync(primary, expectedBackup));
        }

        // 주 저장만 지운다. 파일·폴더가 없으면 성공이고 백업은 남는다.
        public async UniTask DeleteAsync(string key)
        {
            await RunLockedAsync<bool>(key, primary =>
            {
                DeleteFile(primary);
                return UniTask.FromResult(true);
            });
        }

        // 주 저장의 마지막 수정 시각(UTC)이다. 없으면 null이다.
        public UniTask<DateTime?> GetLastWriteUtcAsync(string key) =>
            RunLockedAsync<DateTime?>(key, primary => UniTask.FromResult(LastWriteUtc(primary)));

        // 키의 파일 잠금을 한 번 잡고 파일 작업을 스레드 풀에서 돌린 뒤, 결과와 관계없이 메인 스레드로 돌아와 끝낸다.
        async UniTask<T> RunLockedAsync<T>(string key, Func<string, UniTask<T>> work)
        {
            try
            {
                string primary = ResolvePrimary(key);
                SemaphoreSlim gate = _locks.GetOrAdd(primary, _createLock);
                await gate.WaitAsync();
                try
                {
                    return await UniTask.RunOnThreadPool<T>(() => work(primary));
                }
                finally
                {
                    gate.Release();
                }
            }
            finally
            {
                await UniTask.SwitchToMainThread();
            }
        }

        // 키를 주 저장 전체 경로로 바꾼다. 키는 슬롯과 옵션의 내부 상수뿐이라 상대 경로 규칙은 에디터와 DEBUG 빌드에서만 검사한다.
        string ResolvePrimary(string key)
        {
            Log.Check(IsRelativeKey(key), "저장 키는 저장 루트 아래의 상대 경로여야 합니다: " + key);
            if (_root == null)
                throw new InvalidOperationException("저장 루트 폴더가 정해지지 않았습니다.");
            return Path.GetFullPath(Path.Combine(_root, key + _extension));
        }

        // 키가 저장 루트 밖을 가리키지 못하게 한다. 루트나 드라이브로 시작하는 키와 빈 조각, ".", ".." 조각을 거부한다.
        static bool IsRelativeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || Path.IsPathRooted(key))
                return false;
            foreach (string segment in key.Split('/', '\\'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                    return false;
            }
            return true;
        }

        // 파일이나 폴더가 없으면 저장 없음으로 null을 돌려주고, 그 밖의 읽기 실패는 예외로 전달한다.
        static async UniTask<string?> ReadFileAsync(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, _bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var reader = new StreamReader(stream, _encoding, false, _bufferSize))
                {
                    return await reader.ReadToEndAsync();
                }
            }
            // 예외 경계: 파일 읽기에서 파일이 없으면 저장이 없는 상태라 null을 돌려준다.
            catch (FileNotFoundException)
            {
                return null;
            }
            // 예외 경계: 파일 읽기에서 저장 폴더가 없으면 저장이 없는 상태라 null을 돌려준다.
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        // 이 키의 남은 임시 파일을 지우고 새 임시 파일에 써서 디스크까지 비운다. 주 저장이 있으면 교체하고 없으면 이동해 커밋한다.
        // 실패하면 임시 파일을 지우고 원래 예외를 원래 스택 그대로 다시 던진다.
        async UniTask WriteFileAsync(string primary, string content, bool keepBackup)
        {
            string directory = Path.GetDirectoryName(primary)!;
            Directory.CreateDirectory(directory);
            DeleteStaleTemporaries(directory, Path.GetFileName(primary));
            string temporary = primary + "." + Guid.NewGuid().ToString(_temporaryIdFormat) + _temporarySuffix;

            bool commitAttempted = false;
            try
            {
                byte[] bytes = _encoding.GetBytes(content);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, _bufferSize, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length);
                    await stream.FlushAsync();
                    stream.Flush(true);
                }

                // 교체나 이동을 시작한 뒤 실패하면 예외에 주 저장 경로를 붙이도록 표시한다.
                commitAttempted = true;
                if (File.Exists(primary))
                    _replaceFile(temporary, primary, keepBackup ? primary + _backupSuffix : null);
                else
                    File.Move(temporary, primary);
            }
            // 예외 경계: 쓰기나 커밋이 실패하면 임시 파일을 지우고 원래 예외를 다시 던진다. 정리 실패는 Cleanup이 기록만 해 원래 실패를 가리지 않는다.
            catch (Exception exception)
            {
                if (commitAttempted)
                    exception.Data[_primaryPathData] = primary;
                Cleanup.Run(() => DeleteTemporary(temporary));
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }

        // 중단된 쓰기가 남긴 이 키의 임시 파일만 지운다. 다른 키의 파일과 .bak.history.*.tmp, .bak.<n>.*.tmp는 이름이 맞지 않아 남는다.
        // 잠금과 같게 대소문자를 구분하지 않고, 삭제에 실패해도 기록만 하고 저장을 계속한다.
        static void DeleteStaleTemporaries(string directory, string primaryName)
        {
            string pattern = "^" + Regex.Escape(primaryName) + @"\." + _temporaryIdPattern + Regex.Escape(_temporarySuffix) + "$";
            foreach (string path in Directory.GetFiles(directory, "*" + _temporarySuffix))
            {
                if (Regex.IsMatch(Path.GetFileName(path), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    Cleanup.Run(() => File.Delete(path));
            }
        }

        // 주입한 삭제 동작으로 실패한 쓰기의 임시 파일을 지운다.
        void DeleteTemporary(string temporary)
        {
            try { _deleteTemporary(temporary); }
            // 예외 경계: 파일 삭제 I/O에서 부모 폴더까지 없어졌다면 정리는 이미 끝난 상태다.
            catch (DirectoryNotFoundException) { return; }
        }

        // 같은 잠금 안에서 백업을 다시 읽어 서수 비교한다. 손상된 주 저장을 백업으로 남기지 않도록 keepBackup 없이 쓴다.
        async UniTask<bool> RestoreFileAsync(string primary, string expectedBackup)
        {
            string? backup = await ReadFileAsync(primary + _backupSuffix);
            if (!string.Equals(backup, expectedBackup, StringComparison.Ordinal))
                return false;

            await WriteFileAsync(primary, expectedBackup, false);
            return true;
        }

        // 주 저장만 지운다.
        static void DeleteFile(string primary)
        {
            try { File.Delete(primary); }
            // 예외 경계: 파일 삭제 I/O에서 저장 폴더가 없으면 지울 주 저장도 없으므로 성공이다.
            catch (DirectoryNotFoundException) { return; }
            // 예외 경계: 파일 삭제 I/O에서 주 저장이 없으면 이미 지운 상태이므로 성공이다.
            catch (FileNotFoundException) { return; }
        }

        static DateTime? LastWriteUtc(string primary)
        {
            var info = new FileInfo(primary);
            return info.Exists ? info.LastWriteTimeUtc : (DateTime?)null;
        }
    }
}
