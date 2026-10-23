#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using DevProject.Serialization;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 SavesSystem처럼 여섯 게임 슬롯(Saves/slot-N)의 선택, 저장 지점, 메뉴 명령과 저장 상태를 맡고, 제품 저장 위치와 슬롯 선택 저장도 정한다.
    // 저장할 때 구역 소유자들에게서 값을 모으고, 파일을 불러오면 소유자들에게 알린다. 슬롯 파일의 쓰기, 복구, 삭제, 검사는 GameSaveSlotFiles가 맡는다.
    // GameManager 프리팹 자식의 컴포넌트다. 저장소는 GameBootstrap이 Configure로 넣고, 독점 예약과 상태 알림을 맡는 Runtime은 Bind로 받는다.
    // 슬롯 작업은 시작할 때의 슬롯을 붙잡으므로 도중에 선택이 바뀌어도 대상 파일은 그대로다.
    [DisallowMultipleComponent]
    public sealed class SaveManager : MonoBehaviour
    {
        // 옵션 파일의 저장소 키다. 필드 이름 규칙(_camelCase)을 피하려고 public const 대신 정적 속성으로 둔다.
        public static string OptionsKey => "Options/options";
        // 슬롯 밖 공통 파일(사용자 결정 D2)의 키다. 원작 Saves/Global.json 자리이고, GlobalProgress만 읽고 쓴다.
        // 슬롯 명령은 슬롯 키만 다루므로 슬롯 삭제, 새 게임, 백업 복구가 이 파일을 건드리지 않는다.
        public static string GlobalKey => "Saves/global";

        // 슬롯 키 접두사다. 로컬 저장소에서 키 Saves/slot-N은 파일 Saves/slot-N.json이 된다.
        const string _slotKeyPrefix = "Saves/slot-";

        // 원작 SavesSystem.OnCollectSaveData처럼 저장마다 각 소유자가 자기 구역을 써 넣는다. 소유자는 Awake에서 구독하고 OnDestroy에서 끊는다.
        public static event Action<GameSaveData>? CollectSaveData;
        // 원작 SavesSystem.OnSetFile처럼 새 게임, 이어하기, 슬롯 선택으로 기록을 불러오면 각 소유자가 자기 구역을 읽어 간다.
        public static event Action<GameSaveData>? FileLoaded;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            CollectSaveData = null;
            FileLoaded = null;
        }

        // 슬롯, 슬롯 밖 공통 파일, 옵션이 함께 쓰는 키 저장소다. 키별 문자열만 다루고, 슬롯 검증과 백업 정책은 슬롯 파일 정책이 정한다.
        // Unity가 인수 없이 만드는 컴포넌트라 아래 구성 필드는 Configure가 채우고, 그 전에는 비어 있다.
        ISaveStorage _storage = null!;
        // 옵션도 이 저장소에서 읽고 쓸지다. 제품은 참이고, 거짓이면 OptionsStorage가 null이라
        // 옵션 서비스, 그래픽 옵션 어댑터, 화면 모드 감시를 만들지 않는다.
        bool _storesOptions;
        // 슬롯 파일의 쓰기 순서, 백업 복구, 삭제, 검사 정책이다. Configure가 저장소와 변환기로 만든다.
        GameSaveSlotFiles _slotFiles = null!;
        // Configure로 받은 선택 저장소는 슬롯 번호만 저장한다. 없으면 1번 슬롯에서 시작하고 선택을 저장하지 않는다.
        IGameSaveSlotSelectionStore? _selection;
        // 마지막 목록 조회의 여섯 슬롯 요약이다. 저장이 성공하면 그 슬롯만 쓴 기록의 요약으로 바꾸고, 아직 읽지 않았으면 빈 목록이다.
        IReadOnlyList<GameSaveSlotSummary> _summaries = Array.Empty<GameSaveSlotSummary>();
        // 선택 슬롯의 0 기반 인덱스다. 저장된 선택이 없거나 범위 밖이면 원본의 첫 번째 게임 저장 슬롯(0)이다.
        int _selectedIndex;
        // 마지막 목록 조회의 슬롯별 정상 주 저장이며, 저장이 성공한 슬롯은 쓴 기록이다. 슬롯을 고를 때 이 값으로 소유자를 채우고,
        // 없거나 손상된 슬롯은 null이다.
        GameSessionSnapshot?[] _primaries = Array.Empty<GameSessionSnapshot?>();
        // 슬롯 줄에 서서 아직 끝나지 않은 저장 수다. 저장 중 표시와 대기만을 위한 값이라 순서는 정하지 않는다. 메인 스레드에서만 바꾼다.
        int _pendingSaves;
        // 줄에 선 저장이 모두 끝나면 완료되는 공유 완료다. 첫 저장이 줄에 설 때 만들고 마지막 저장이 끝날 때 비운다.
        TaskCompletionSource<bool>? _savesDone;
        // 메뉴의 저장 상태 조회 작업이다. 씬 전환을 막지 않고, 종료는 그 끝을 기다린다. 늦게 끝난 조회는 IsCurrentQuery가 버린다.
        readonly TrackedOperation _query = new TrackedOperation();
        // 슬롯 명령의 독점 예약, 상태 알림, 종료 판정을 맡는 Runtime이다. Bind 전에는 비어 있고, 파일 작업만 쓰는 테스트는 묶지 않는다.
        GameRuntime _runtime = null!;
        // 저장 상태를 기록하거나 무효화할 때마다 올라, 그보다 먼저 시작한 조회의 결과를 버리게 한다.
        // 무효화는 작업 예약, 저장 시작, 종료 요청, 새 조회가 한다.
        long _statusGeneration;
        // 이어하기 읽기와 저장 수집이 아이템을 대조할 앱 수명 카탈로그다. 씬 흐름이 세션을 열기 전에 넣는다.
        // 테스트 씬 흐름이 대역을 주지 않으면 null이고, 그때는 대조하지 않는다. 메인 스레드에서만 읽는다.
        OriginalGameplayCatalog? _itemCatalog;

        // 앱을 만든 GameBootstrap이 Runtime 시작 전에 메인 스레드에서 다른 API보다 먼저 한 번 부른다. 테스트는 대역 저장소를 바로 넘긴다.
        // 선택 저장소만 생략할 수 있고, 저장소와 코덱은 늘 넘긴다. 옵션은 storesOptions가 참일 때만 같은 저장소에 둔다.
        internal void Configure(ISaveStorage storage, IJsonCodec<GameSessionSnapshot> codec, IGameSaveSlotSelectionStore? selection = null,
            bool storesOptions = false)
        {
            _storage = storage;
            _storesOptions = storesOptions;
            // 재시작 때 저장한 선택을 복원한다. 범위 밖의 값은 첫 슬롯으로 대체한다.
            _selection = selection;
            int selected = selection?.Read() ?? 1;
            _selectedIndex = selected >= 1 && selected <= GameSaveSlotId._count ? selected - 1 : 0;
            _slotFiles = new GameSaveSlotFiles(storage, codec);
        }

        // 원작 SavesSystem처럼 사용자 영구 저장 폴더 아래 Saves/slot-N.json과 Options/options.json을 쓰고,
        // 마지막으로 고른 슬롯 번호는 PlayerPrefs에 둔다. 구성만으로는 파일이나 폴더를 만들지 않는다.
        internal void ConfigureProduct(string persistentDataPath) =>
            Configure(new LocalFileStorage(persistentDataPath), GameSaveJson.CreateDefault(new DevProject.Serialization.JsonSerializer()),
                new PlayerPrefsSlotSelection(), true);

        // 슬롯과 같은 저장소다. 슬롯 밖 공통 파일(GlobalProgress)도 같은 저장소의 다른 키를 쓴다.
        internal ISaveStorage Storage => _storage;
        // 옵션 관리자가 쓸 저장소다. 옵션을 저장하지 않는 구성이면 null이다.
        internal ISaveStorage? OptionsStorage => _storesOptions ? _storage : null;

        // 슬롯 명령과 저장 지점이 쓰는 Runtime을 묶는다. 제품 구성과 테스트 하위 Runtime이 Configure 뒤에 한 번 부른다.
        internal void Bind(GameRuntime runtime)
        {
            _runtime = runtime;
        }

        // 씬 흐름이 세션 진입마다 부른다. 카탈로그는 앱 수명이라 같은 값을 다시 넣어도 된다.
        internal void UseItemCatalog(OriginalGameplayCatalog? catalog) => _itemCatalog = catalog;

        // 슬롯의 저장소 키다. 어느 문화권에서도 같은 파일 이름이 되도록 고정 숫자 형식을 쓴다.
        public static string SlotKey(GameSaveSlotId slot) => _slotKeyPrefix + slot.Number.ToString(CultureInfo.InvariantCulture);

        // 화면에 보이는 번호와 같게 1부터 센다.
        public int SelectedSlot => _selectedIndex + 1;
        // 요약에는 슬롯 번호가 없다. 목록의 index 위치가 슬롯 번호 index + 1이다.
        public IReadOnlyList<GameSaveSlotSummary> Summaries => _summaries;
        // 마지막 목록 조회나 그 뒤에 성공한 저장이 남긴 선택 슬롯의 요약이다. 목록을 아직 읽지 않았으면 null이다.
        public GameSaveSlotSummary? SelectedSummary =>
            _selectedIndex < _summaries.Count ? _summaries[_selectedIndex] : null;

        // 화면의 저장 중 표시가 본다. 수집은 동기로 끝나므로 줄에 선 쓰기가 하나라도 남은 동안만 참이다.
        public bool IsSaving => _pendingSaves > 0;
        // 메뉴 복귀, 편집 진입, 종료가 남은 쓰기를 모두 기다릴 공유 완료다. 쓰기가 실패해도 정상 완료하며, 유휴 상태면 null이다.
        internal Task? CurrentSave => _savesDone?.Task;
        // 지금 세션이 슬롯 파일에서 왔거나 이미 저장됐는지다. 새 게임은 첫 저장이 성공하기 전까지 거짓이다.
        internal bool HasSessionFile { get; private set; }
        // 메뉴 버튼이 보는 선택 슬롯의 마지막 확정 저장 상태다. 목록 조회, 저장 성공, 이어하기 읽기가 기록하며 조회 전에는 Unknown이다.
        // 이어하기 읽기는 목록 조회에 없는 아이템 대조까지 하므로, 그 결과는 다음 목록 조회 전까지 같은 슬롯의 요약 상태와 다를 수 있다.
        public GameSaveState State { get; private set; }
        // 진행 중인 저장 상태 조회의 완료다. 종료가 조회의 끝을 기다리는 데 쓴다.
        internal Task? QueryTask => _query.Current;

        // 메뉴 UI만 부르는 독점 작업이다. 진행 중인 조회와 저장이 끝나면 목록을 다시 읽고 선택을 바꾼 뒤 그 슬롯의 상태를
        // 게시한다. 슬롯 번호는 제작된 여섯 버튼에서 오므로 따로 검사하지 않는다.
        public UniTask SelectAsync(int slotNumber)
        {
            var slot = new GameSaveSlotId(slotNumber);
            return _runtime.Operations.Run(null, () => ChangeSelectedSlotAsync(ReadSlotsAsync, slot));
        }

        // 메뉴의 저장 상태 조회다. 따로 예약해 새 게임 명령을 막지 않으며, 슬롯 목록 새로고침도 이 조회 수명 안에서 한다.
        // 새 조회 세대는 자리를 잡은 뒤 started에서 받는다. 중복 조회는 TrackedOperation이 결함으로 보고하고 진행 중인 완료를 돌려준다.
        public UniTask RefreshStatusAsync()
        {
            // 충돌하는 독점 명령 중에는 UI가 IsBusy로 이 조회를 먼저 막는다.
            long generation = 0;
            return _query.Start(() => QuerySaveStatusAsync(generation),
                started: () => generation = InvalidateStatus()).AsUniTaskOnCurrentContext();
        }

        // 메뉴의 삭제다. 선택 슬롯의 주 파일만 지우고 백업은 남겨 복구할 수 있다.
        public UniTask DeleteAsync() => _runtime.Operations.Run(null, () => ChangeSelectedSlotAsync(DeleteSelectedAsync));

        // 메뉴의 백업 복구다. 선택 슬롯의 백업을 주 저장으로 되돌리고, 이어하기는 복구된 주 저장을 읽는다.
        public UniTask RecoverBackupAsync() => _runtime.Operations.Run(null, () => ChangeSelectedSlotAsync(RecoverSelectedAsync));

        // 상태 값은 그대로 두고 이미 시작한 조회의 결과만 버리게 한 뒤 새 세대 번호를 돌려준다.
        // 새 조회만 이 번호로 끝날 때 최신인지 확인하고, 예약과 저장과 종료는 반환값을 쓰지 않는다.
        internal long InvalidateStatus() => ++_statusGeneration;

        // 확정한 저장 상태를 기록하고, 먼저 시작한 조회가 이 값을 덮지 못하게 세대를 올린다. 알림은 보내지 않는다.
        internal void SetStatus(GameSaveState state)
        {
            ++_statusGeneration;
            State = state;
        }

        // 확정한 저장 상태를 기록하고 알린다. 이어하기의 읽기 결과가 쓴다.
        internal void PublishState(GameSaveState state)
        {
            SetStatus(state);
            _runtime.Publish();
        }

        // 슬롯 목록을 다시 읽고 선택 슬롯의 상태를 게시한다. 앱 시작, 삭제, 복구가 함께 쓴다.
        internal async Task RefreshAllAsync()
        {
            await ReadSlotsAsync();
            if (_runtime.IsAbandoned) return;
            PublishSelectedState();
        }

        // 원작 저장 지점처럼 상태를 먼저 바꾼 뒤 불러, 지금 상태를 바로 모아 선택 슬롯에 쓴다. Runtime 코드의 유일한 저장 입구다.
        // 앞선 쓰기를 따로 기다리지 않는다. 쓰기 순서는 SaveAsync가 서는 슬롯 줄이 지키고, 실패는 SaveAsync가 한 번 기록하고 false다.
        // 시작과 끝에 알려 UI가 IsSaving 표시를 맞춘다. 이미 버려진 Runtime이면 저장하지 않고 false로 끝난다.
        internal async UniTask<bool> SaveAtPointAsync()
        {
            if (_runtime.IsAbandoned) return false;
            // 이 저장보다 먼저 시작한 조회의 결과가 저장 뒤 상태를 덮지 않게 한다.
            InvalidateStatus();
            UniTask<bool> save = SaveAsync();
            _runtime.Publish();
            try
            {
                bool saved = await save;
                if (saved && !_runtime.IsAbandoned) SetStatus(GameSaveState.Available);
                return saved;
            }
            finally { if (!_runtime.IsAbandoned) _runtime.Publish(); }
        }

        // 마지막으로 읽은 목록에서 선택 슬롯의 상태를 기록하고 알린다. 목록을 다시 읽은 직후에만 부른다.
        void PublishSelectedState()
        {
            GameSaveSlotSummary? selected = SelectedSummary;
            SetStatus(selected != null ? selected.State : GameSaveState.Unknown);
            _runtime.Publish();
        }

        // 조회 작업의 본문이다. 목록을 다시 읽고, 조회 세대가 최신이며 아직 메뉴에 있을 때만 선택 슬롯의 상태를 게시한다.
        // 목록 읽기 실패는 Error 상태로 게시하고 호출자에게 전하지 않는다.
        async Task QuerySaveStatusAsync(long generation)
        {
            try
            {
                await ReadSlotsAsync();
            }
            // 예외 경계: 저장 상태엔 원인이 남지 않아 예상 밖 읽기 예외를 게시 여부와 무관하게 여기서 한 번 기록한다.
            catch (Exception error)
            {
                // 파일 입출력 실패는 슬롯 검사 결과로 바뀌므로 여기까지 오지 않는다. 게시는 유효한 조회일 때만 한다.
                Log.Instance.Exception(error, this);
                if (IsCurrentQuery(generation)) PublishState(GameSaveState.Error);
                return;
            }
            if (IsCurrentQuery(generation)) PublishSelectedState();
        }

        // 조회 시작 뒤 상태 기록, 작업 예약, 종료가 없었고 아직 메뉴에 있을 때만 조회 결과를 게시한다.
        bool IsCurrentQuery(long generation) => !_runtime.IsAbandoned && generation == _statusGeneration &&
            _runtime.State == GameRuntimeState.Running && _runtime.SessionState == GameSessionState.Menu;

        // 선택, 삭제, 복구의 공통 본문이다. 진행 중인 조회가 끝나길 기다려 늦은 결과가 변경 뒤 상태를 덮지 못하게 한다.
        // 선택은 change가 막 읽은 목록에서 슬롯을 고르고, 삭제와 복구는 목록을 다시 읽는다. Runtime이 버려지면 결과를 게시하지 않는다.
        async Task ChangeSelectedSlotAsync(Func<UniTask> change, GameSaveSlotId? select = null)
        {
            // 메뉴 복귀가 남은 저장을 모두 기다린 뒤 메뉴를 열므로 메뉴 명령 중에는 끝나지 않은 저장이 없다.
            // 파일 순서는 슬롯 잠금이 지키지만, 저장이 남아 있으면 다시 읽은 목록과 게시 상태가 그 저장보다 낡을 수 있다.
            await TrackedTask.ObservePreviousAsync(_query.Current);
            if (_runtime.IsAbandoned) return;
            await change();
            if (_runtime.IsAbandoned) return;
            if (select == null)
            {
                await RefreshAllAsync();
                return;
            }
            Select(select);
            PublishSelectedState();
        }

        // 메뉴 선택(SelectAsync)이 독점 예약 안에서 부른다. 선택 저장이 예외로 실패하면 현재 슬롯은 그대로다.
        // 이미 진행 중인 작업은 시작할 때 붙잡은 슬롯을 계속 쓴다. 원작 SavesSystem.SetFile처럼 같은 슬롯을 다시 골라도 알린다.
        internal void Select(GameSaveSlotId slot)
        {
            if (slot.Number != SelectedSlot)
            {
                _selection?.Write(slot.Number);
                _selectedIndex = slot.Number - 1;
            }
            // 고른 슬롯의 정상 주 저장으로 소유자를 채운다. 없거나 손상된 슬롯은 채울 값이 없어 알리지 않는다.
            // 세션을 시작한 것이 아니므로 HasSessionFile은 바꾸지 않는다.
            GameSessionSnapshot? primary = _selectedIndex < _primaries.Length ? _primaries[_selectedIndex] : null;
            if (primary != null) ObserverDispatch.InvokeEachValue(FileLoaded, GameSaveData.FromRecord(primary));
        }

        // 여섯 슬롯을 동시에 검사하고, 모두 끝난 뒤에만 목록과 주 저장 캐시를 한 번에 바꾼다. 상태 게시는 메뉴 명령이 맡는다.
        // 검사 중에 이 컴포넌트가 파괴됐으면 결과를 버린다.
        internal async UniTask ReadSlotsAsync()
        {
            var reads = new UniTask<(GameSaveSlotSummary Summary, GameSessionSnapshot? Primary)>[GameSaveSlotId._count];
            // 슬롯 검사가 곧 요약과 주 저장을 만들므로 슬롯 인덱스로 검사만 부른다.
            for (int index = 0; index < reads.Length; index++)
                reads[index] = _slotFiles.InspectAsync(index);
            (GameSaveSlotSummary Summary, GameSessionSnapshot? Primary)[] results = await UniTask.WhenAll(reads);
            if (this == null) return;
            var summaries = new GameSaveSlotSummary[results.Length];
            var primaries = new GameSessionSnapshot?[results.Length];
            for (int index = 0; index < results.Length; index++)
                (summaries[index], primaries[index]) = results[index];
            _summaries = Array.AsReadOnly(summaries);
            _primaries = primaries;
        }

        // 성공한 저장이 쓴 기록으로 그 슬롯의 요약과 정상 주 저장을 바꾼다. 목록을 아직 읽지 않았으면 바꿀 목록이 없다.
        // 쓴 기록에는 저장 시각이 늘 있어 파일 시각이 필요 없고, 정상 주 저장이라 백업 후보도 싣지 않는다.
        void RememberWrittenPrimary(int index, GameSessionSnapshot record)
        {
            if (index >= _summaries.Count) return;
            var summaries = new GameSaveSlotSummary[_summaries.Count];
            for (int slot = 0; slot < summaries.Length; slot++) summaries[slot] = _summaries[slot];
            summaries[index] = new GameSaveSlotSummary(GameSaveState.Available, record, null, null);
            _summaries = Array.AsReadOnly(summaries);
            _primaries[index] = record;
        }

        // 씬 흐름이 이어하기 읽기 실패를 목록 조회와 같은 저장 상태로 나눌 때 부른다. 분류 규칙은 슬롯 파일 정책 한 곳에 둔다.
        internal static GameSaveState Classify(Exception error) => GameSaveSlotFiles.Classify(error);

        // 이어하기가 선택 슬롯의 주 저장을 읽는다. 파일이 없을 때만 null이고, 손상은 InvalidDataException으로 던져 새 게임으로 넘어가지 않게 한다.
        // 카탈로그와 맞지 않는 아이템도 손상으로 본다. 손상은 메인 스레드에서 한 번 보고한 뒤 던지고, 에디터와 DEBUG 빌드에서는 보고가 먼저 던진다.
        // 읽기만 하므로 슬롯 잠금을 잡지 않는다. 정산과 Load는 호출자가 이 기록으로 한다.
        internal async UniTask<GameSessionSnapshot?> ReadSelectedAsync()
        {
            // 원문 읽기와 해석은 목록 검사와 같은 슬롯 파일 정책으로 해, 잘못된 UTF-8도 같은 손상으로 나눈다.
            // 잘못된 UTF-8은 읽기가 이미 한 번 보고하고 던지므로, 여기서는 해석과 아이템 대조의 손상만 보고한다.
            string? payload = await _slotFiles.ReadPrimaryAsync(_selectedIndex);
            if (payload == null) return null;
            (GameSessionSnapshot? snapshot, InvalidDataException? failure) = await _slotFiles.DecodeAsync(payload);
            // 카탈로그 색인은 스레드 안전하지 않으므로, 스레드 풀 해석이 끝나 메인 스레드로 돌아온 뒤 여기서 대조한다.
            if (failure == null) failure = CheckItems(snapshot!);
            if (failure != null)
            {
                Log.Instance.InvalidData(failure);
                throw failure;
            }
            return snapshot;
        }

        // 원작 저장 지점의 CollectSaveData 뒤 SaveFile처럼, 소유자 구역을 지금 모아 하나의 기록으로 합친 뒤 선택 슬롯의 줄에 쓴다.
        // 앞선 쓰기가 남아 있어도 기다리지 않고 바로 모은다. 슬롯 잠금이 요청 순서대로 쓰므로 나중에 모은 기록이 늘 나중에 쓰인다.
        // 구독자, 아이템 대조, 쓰기 실패는 한 번 보고하고 false를 돌려준다. 구역 합치기 실패는 EditMode 테스트가 막는 소유자 결함이라 기록 없이 false다.
        // 수집과 합치기는 메인 스레드에서 동기로 끝나므로 IsSaving은 쓰기가 남은 동안만 참이다.
        internal async UniTask<bool> SaveAsync()
        {
            // 구독자 실패는 TryCollect가 기록하므로 여기서는 쓰지 않고 false만 돌려준다.
            if (!TryCollect(out GameSessionSnapshot? record)) return false;
            // 쓰는 도중 선택이 바뀌어도 수집할 때 고른 슬롯에 쓰도록 슬롯을 먼저 붙잡는다.
            int index = _selectedIndex;
            // 첫 쓰기가 줄에 설 때 공유 완료를 만들어, 그 뒤에 선 쓰기까지 모두 끝나야 대기자가 깨어나게 한다.
            if (_pendingSaves++ == 0) _savesDone = SharedCompletion.Create<bool>();
            bool saved = false;
            try
            {
                // Task로 기다려 저장소가 어느 스레드에서 끝나도 메인 스레드에서 이어 간다. 쓰기 순서는 슬롯 파일 정책의 슬롯 잠금이 지킨다.
                await _slotFiles.WriteAsync(index, record).AsTask();
                saved = true;
                HasSessionFile = true;
                // 메뉴가 다음 목록 조회 전에도 저장 전 요약을 보이지 않도록, 대기자를 깨우기 전에 방금 쓴 기록을 그 슬롯 요약에 반영한다.
                RememberWrittenPrimary(index, record);
            }
            // 예외 경계: 쓰기 실패의 소유자로서 한 번 기록하고 false를 돌려준다. 원자적 쓰기라 슬롯 파일은 그대로 남는다.
            catch (Exception error)
            {
                Log.Instance.Exception(error, this);
            }
            finally
            {
                // 대기자가 깨어날 때 IsSaving이 거짓이도록 마지막 쓰기는 자리를 먼저 비운 뒤 완료를 게시한다.
                if (--_pendingSaves == 0)
                {
                    TaskCompletionSource<bool> done = _savesDone!;
                    _savesDone = null;
                    done.TrySetResult(true);
                }
            }
            return saved;
        }

        // 저장 지점이 쓸 기록을 파일 없이 만든다. 소유자 구역을 모으고 저장 시각을 찍은 뒤 하나로 합친다.
        // 저장과, 지금 저장하면 쓸 내용을 확인하는 테스트가 같은 수집 규칙을 쓰도록 나눈다. 구독자 실패는 여기서 한 번 기록한다.
        internal bool TryCollect([NotNullWhen(true)] out GameSessionSnapshot? record)
        {
            record = null;
            var data = new GameSaveData();
            // 구독자 하나가 실패하면 그 구역 값을 믿을 수 없으므로, 실패를 구독자마다 기록하고 이번 저장은 쓰지 않는다.
            bool collected = true;
            ObserverDispatch.InvokeEachValue(CollectSaveData, data, failure =>
            {
                collected = false;
                Log.Instance.Exception(failure, this);
            });
            if (!collected) return false;
            data.SetSavedAt(DateTime.UtcNow);
            // 구역을 따로 모으므로 구역을 넘는 규칙 위반은 소유자 결함이고 EditMode 테스트가 확인한다.
            // 합치지 못하면 슬롯 파일은 건드리지 않는다.
            if (!data.TryToRecord(out record, out _)) return false;
            // 세션은 대조한 그래프에 카탈로그 정의로 만든 아이템만 더하므로, 여기서 어긋나면 코드 결함이다.
            // 보고하고 이번 저장은 쓰지 않아 이전 파일을 지킨다.
            if (_itemCatalog != null && !OriginalInventoryQueries.TryCheckSavedItems(record.Campaign, _itemCatalog, out string itemReason))
            {
                record = null;
                Log.Instance.Fail(new InvalidOperationException("SaveManager: 저장할 아이템이 카탈로그와 맞지 않습니다. " + itemReason), this);
                return false;
            }
            return true;
        }

        // 읽은 기록의 아이템이 카탈로그와 어긋나면 손상 예외를, 맞거나 카탈로그가 없으면 null을 돌려준다. 보고는 호출자가 한다.
        InvalidDataException? CheckItems(GameSessionSnapshot snapshot)
        {
            if (_itemCatalog == null || OriginalInventoryQueries.TryCheckSavedItems(snapshot.Campaign, _itemCatalog, out string reason))
                return null;
            return new InvalidDataException("게임 저장의 아이템이 카탈로그와 맞지 않습니다. 기존 파일을 보존합니다. " + reason);
        }

        // 원작 SavesSystem.OnSetFile처럼 새 게임 기록이나 이어하기로 읽은 기록을 소유자들에게 알린다.
        // 구독자가 HasSessionFile을 읽을 수 있도록 알리기 전에 기록한다. 구독자 실패는 구독자마다 기록하고 나머지에게 계속 알린다.
        internal void Load(GameSaveData data, bool fromFile)
        {
            HasSessionFile = fromFile;
            ObserverDispatch.InvokeEachValue(FileLoaded, data);
        }

        // 선택 슬롯의 백업을 잠금 안에서 다시 검증한 뒤, 없거나 손상된 주 저장을 덮어쓴다. 백업은 남는다.
        // 메뉴 복구(RecoverBackupAsync)가 독점 예약 안에서 부르고, 복구한 내용은 이어하기가 다시 읽는다.
        internal UniTask RecoverSelectedAsync() => _slotFiles.RecoverAsync(_selectedIndex);

        // 선택 슬롯의 주 파일만 삭제한다. 백업은 남는다. 메뉴 삭제(DeleteAsync)가 독점 예약 안에서 부른다.
        internal UniTask DeleteSelectedAsync() => _slotFiles.DeleteAsync(_selectedIndex);

        // 마지막으로 고른 슬롯 번호를 원본처럼 PlayerPrefs에 둔다. 저장이 실패하면 메모리 값을 되돌린다.
        sealed class PlayerPrefsSlotSelection : IGameSaveSlotSelectionStore
        {
            const string _key = "CurrentSlot";
            public int Read() => PlayerPrefs.GetInt(_key, 1);
            public void Write(int slotNumber)
            {
                int previous = Read();
                PlayerPrefs.SetInt(_key, slotNumber);
                try { PlayerPrefs.Save(); }
                // 예외 경계: PlayerPrefs 저장이 실패하면 메모리 값을 원래 선택으로 되돌리고 예외를 그대로 전파한다.
                catch (Exception) { PlayerPrefs.SetInt(_key, previous); throw; }
            }
        }
    }
}
