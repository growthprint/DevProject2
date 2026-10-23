#nullable enable
using System;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using DevProject.Serialization;
using Game.Core;
using UnityEngine;
using SharedJsonSerializer = DevProject.Serialization.JsonSerializer;

namespace Game.App
{
    // 원작 SavesSystem의 SaveGlobal과 LoadGlobal처럼 슬롯 밖 공통 파일(Saves/global.json, 사용자 결정 D2)의 값을 소유한다.
    // 재능 해금(원작 Endowment_Unlock_R_<이름>)과 분노 난이도 해금(원작 Difficulty/RageUnlocked)이다. GameManager 프리팹 자식의 컴포넌트이고
    // GameManager.GlobalProgress로 찾는다. 앱 시작 때 옵션처럼 한 번 읽고, 값을 확정할 때마다 바로 쓰며 .bak 하나를 둔다.
    // 슬롯 저장, 삭제, 새 게임, 슬롯과 옵션의 백업 복구는 이 파일을 건드리지 않는다. 메인 스레드에서만 쓴다.
    [DisallowMultipleComponent]
    public sealed class GlobalProgress : MonoBehaviour
    {
        // 값이 바뀌었다는 신호만 보낸다. 열린 재능 선택 화면(EndowmentSelectionScreen)이 듣고 다시 그린다. EndowmentManager는 듣지 않고 해금 여부를 그때그때 읽는다.
        public static event Action? Changed;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => Changed = null;

        // 슬롯과 같은 저장소의 다른 키를 쓴다. 제품 구성과 테스트 Runtime 생성이 InitializeAsync 전에 Configure로 늘 넣는다.
        ISaveStorage? _storage;
        readonly IJsonCodec<GlobalProgressState> _codec = new GlobalSaveJson(new SharedJsonSerializer());
        // 확정한 현재 값이다. 읽기 전과 읽지 못했을 때는 모두 잠긴 기본값이다.
        GlobalProgressState _state = GlobalProgressState.Empty;
        // 파일을 읽었거나 없음을 확인한 뒤에만 참이다. 읽기가 입출력 실패면 읽지 못한 파일을 덮지 않도록 이번 실행 내내 거짓이다.
        bool _writable;
        // 마지막 쓰기 시작 뒤에 값이 바뀌었는지다. 쓰기가 끝나면 이 값을 보고 전체를 한 번 더 쓴다.
        bool _dirty;
        // 공통 파일 쓰기를 한 번에 하나로 줄 세운다. 늦게 확정한 값이 마지막에 쓰인다.
        readonly TrackedOperation _write = new TrackedOperation();

        // GameRuntime이 구성 때 한 번 부른다. 저장소는 슬롯 저장 관리자와 같은 것이다.
        internal void Configure(ISaveStorage storage)
        {
            _storage = storage;
        }

        // 원작 EndowmentManager의 공통 파일 해금 조회다. 기본 해금 재능은 EndowmentManager가 정의로 따로 본다.
        public bool IsEndowmentUnlocked(EndowmentIndex index) => _state.IsEndowmentUnlocked(index);

        // 원작 Difficulty/RageUnlocked다. 범위 안에서 이 값을 쓰는 쪽은 없어 읽기만 둔다. 3-E의 난이도 재선택이 읽는다.
        public bool RageUnlocked => _state.RageUnlocked;

        // GameRuntime이 앱 시작 때 옵션 다음, 세션 전에 한 번 기다린다. 어느 결과도 시작을 막지 않는다.
        // 파일이 없으면 경고 없이 기본값이다. 주 파일이 없거나 손상되고 백업이 맞으면 백업 값을 쓰고 주 파일을 복원한다.
        // 둘 다 쓸 수 없으면 기본값과 경고 한 번이다. 읽기 입출력이 실패하면 기본값과 경고 한 번이고 이번 실행은 쓰지 않는다.
        internal async UniTask InitializeAsync()
        {
            // 호출자는 늘 Configure 뒤에 부르므로 저장소가 없는 경우를 따로 거르지 않는다(내부 상태 결함은 테스트가 막는다).
            ISaveStorage storage = _storage!;
            GlobalProgressState? loaded = null;
            string? warning = null;
            bool writable = false;
            try
            {
                var (_, primary, primaryError) = await ReadFileAsync(storage.ReadAsync);
                loaded = primary;
                if (loaded == null)
                {
                    (string? backupText, GlobalProgressState? backup, string? backupError) = await ReadFileAsync(storage.ReadBackupAsync);
                    if (backup != null)
                    {
                        loaded = backup;
                        // 확인한 백업이 그사이 바뀌었으면 주 파일을 그대로 두고, 다음 변경이 확정값으로 다시 쓴다.
                        bool restored = await storage.RestoreBackupAsync(SaveManager.GlobalKey, backupText!);
                        if (primaryError != null)
                            warning = restored
                                ? "공통 파일이 손상되어 백업으로 복원했습니다. " + primaryError
                                : "공통 파일이 손상되어 백업 값을 적용했습니다. 백업이 확인 뒤 바뀌어 공통 파일은 다음 변경 때 다시 씁니다. " + primaryError;
                    }
                    else if (primaryError != null)
                        warning = "공통 파일이 손상되었고 쓸 수 있는 백업이 없어 기본값을 적용했습니다. " + primaryError
                            + (backupError != null ? " 백업: " + backupError : string.Empty);
                    else if (backupError != null)
                        warning = "공통 파일이 없고 백업이 손상되어 기본값을 적용했습니다. " + backupError;
                }
                writable = true;
            }
            // 예외 경계: 저장소 입출력 실패를 기본값과 경고 한 번으로 바꿔 시작을 막지 않는다. 읽지 못한 파일을 덮지 않도록 쓰기를 끈다.
            catch (Exception error)
            {
                loaded = null;
                warning = "공통 파일을 읽지 못해 기본값을 적용했습니다. 이번 실행은 공통 파일을 쓰지 않습니다. " + error;
            }
            if (this == null) return;
            _state = loaded ?? GlobalProgressState.Empty;
            _writable = writable;
            if (warning != null) Log.Instance.Warning(warning, this);
            ObserverDispatch.InvokeEach(Changed);
        }

        // 공통 파일 하나를 읽어 해석한다. 없으면 모두 null이고, 손상됐으면 원문과 오류 문구만 있다. 입출력 실패는 그대로 올린다.
        async UniTask<(string? Text, GlobalProgressState? State, string? Error)> ReadFileAsync(Func<string, UniTask<string?>> read)
        {
            string? text;
            try { text = await read(SaveManager.GlobalKey); }
            // 예외 경계: 저장소의 잘못된 UTF-8 판정을 파일 손상으로 분류해 백업 복구로 넘긴다. 다른 실패는 잡지 않는다.
            catch (DecoderFallbackException decodeError)
            {
                return (null, null, "공통 파일이 올바른 UTF-8이 아닙니다: " + decodeError.Message);
            }
            if (text == null) return (null, null, null);
            if (!_codec.TryRead(text, out GlobalProgressState? state, out string reason)) return (text, null, reason);
            return (text, state, null);
        }

        // 원작 EndowmentManager.UnlockEndowment의 공통 파일 쓰기다. 호출자: 완료 퀘스트의 재능 해금(GameplayCommands.UnlockEndowmentsOfFinishedQuests).
        // 그 명령이 저작된 재능 정의의 번호를 넘기고 아직 잠긴 재능인지 먼저 보므로, 여기서는 범위와 중복을 다시 보지 않는다.
        // 메모리 값을 먼저 확정하고 알린 뒤 파일 쓰기를 건다. 슬롯 저장을 기다리지 않는다.
        internal void UnlockEndowment(EndowmentIndex index)
        {
            _state = new GlobalProgressState(ReadOnlyLists.Appended(_state.EndowmentUnlocks, index), _state.RageUnlocked);
            ObserverDispatch.InvokeEach(Changed);
            RequestWrite();
        }

        // 앱 종료가 남은 공통 파일 쓰기를 기다린다. 쓰기 실패는 쓰기 작업이 이미 경고했으므로 이 완료는 실패하지 않는다.
        // 끝난 쓰기가 다음 쓰기를 이어 걸 수 있어 자리가 빌 때까지 기다린다.
        internal async Task FlushAsync()
        {
            while (_write.Current != null) await _write.Current;
        }

        // 원작 SaveGlobal처럼 값을 확정할 때마다 전체를 쓴다. 쓰는 중이면 끝난 뒤 한 번 더 쓰도록 표시만 한다.
        void RequestWrite()
        {
            // 쓰기 가능은 저장소로 읽기를 마친 뒤에만 참이라 저장소가 있다는 뜻도 담으므로 저장소를 다시 보지 않는다.
            if (!_writable) return;
            _dirty = true;
            if (!_write.IsRunning) StartWrite();
        }

        // 종료 알림은 자리를 비운 뒤 불리므로, 쓰는 동안 바뀐 값이 있으면 거기서 다음 쓰기를 건다.
        void StartWrite() => _ = _write.Start(WriteAsync, finished: _ =>
        {
            if (_dirty) StartWrite();
        });

        // 아무도 기다리지 않는 쓰기 뿌리라 실패를 여기서 경고 한 번으로 남긴다. 실패한 값은 다음 변경 때 전체를 다시 쓴다.
        async Task WriteAsync()
        {
            _dirty = false;
            try
            {
                // 이 쓰기는 _writable이 참일 때만 걸리고, 그 값은 저장소로 읽기를 마친 뒤에만 서므로 저장소가 늘 있다.
                await _storage!.WriteAsync(SaveManager.GlobalKey, _codec.Write(_state), keepBackup: true);
            }
            // 예외 경계: 공통 파일 쓰기 실패를 경고로 남긴다. 원자적 교체라 이전 파일은 그대로다.
            catch (Exception error)
            {
                Log.Instance.Warning("공통 파일을 쓰지 못했습니다. 다음 변경 때 다시 씁니다. " + error, this);
            }
        }
    }
}
