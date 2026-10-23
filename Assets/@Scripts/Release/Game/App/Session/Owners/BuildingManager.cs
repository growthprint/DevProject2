#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 BuildingRotation이다. 값이 원작과 같고, 건물 프리팹은 값 × 90도만큼 돈다.
    public enum BuildingRotation
    {
        Zero = 0,
        Quarter = 1,
        Half = 2,
        ThreeQuarter = 3,
    }

    // 원작 BuildingBuyAndPlaceResults다. 원작은 요구 조건과 배치 위치를 건설 화면에서만 거르지만 배치 명령도 다시 보므로,
    // 그 두 거절을 원작 값 뒤에 둔다.
    public enum BuildingBuyAndPlaceResults
    {
        Succeed = 0,
        NoReferences = 1,
        InvalidBuildingInfo = 2,
        PaymentFailure = 3,
        ReachedAmountLimit = 4,
        RequirementsNotMet = 5,
        InvalidPlacement = 6,
    }

    // 원작 BuildingManager처럼 건설 영역별 설치 건물과 반환 토큰을 모든 씬에 걸쳐 소유한다. GameManager.Buildings로 찾는다.
    // 원작은 LevelManager 자식이지만 레벨마다 같은 저장 키를 다시 읽어 결과가 같다. 정의는 GameplayDataSettings에서 읽고, 메인 스레드에서만 쓴다.
    [DisallowMultipleComponent]
    public sealed class BuildingManager : MonoBehaviour
    {
        // 원작 OnBuildingListChanged, OnBuildingBuilt, OnBuildingDestroyed다. 목록 신호는 바뀐 뒤와 불러온 뒤 한 번,
        // 배치와 회수 신호는 건물마다 한 번 낸다. 실패한 명령은 신호가 없다.
        public static event Action? Changed;
        public static event Action<int>? Built;
        public static event Action<int>? Destroyed;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            Changed = null;
            Built = null;
            Destroyed = null;
        }

        // 파일을 읽기 전의 새 게임 값이다. 새 게임 기록도 빈 설치와 빈 토큰 목록이다.
        static readonly BuildingPersistenceState _newGame =
            new BuildingPersistenceState(Array.Empty<BuildingInstallationState>(), Array.Empty<BuildingTokenState>());

        // 플레이어 명령을 받을지는 GameRuntime이 Configure로 넘긴 조건이 정한다. 구성 전에는 모두 거절한다.
        Func<bool> _acceptsCommands = () => false;
        // 원작 영역 목록을 영역 ID가 붙은 설치 목록 하나로 둔다. 설치한 순서를 지켜 저장한다.
        readonly List<BuildingInstallationState> _installations = new List<BuildingInstallationState>();
        // 원작처럼 0개가 된 토큰 항목도 지우지 않는다. 항목은 불변 값이라 수량이 바뀌면 같은 자리를 새 값으로 바꾼다.
        readonly List<BuildingTokenState> _tokens = new List<BuildingTokenState>();
        // 저장할 값이다. null은 옛 저장의 미포착 값이라 첫 성공 명령 전까지 그대로 되돌려 쓴다.
        BuildingPersistenceState? _persistence = _newGame;

        // GameRuntime이 구성 때 한 번 부른다.
        internal void Configure(Func<bool> acceptsCommands)
        {
            _acceptsCommands = acceptsCommands;
        }

        // 결제와 놓기를 묶는 배치 명령이 결제 전에 본다. 결제 뒤에 놓기가 거절되면 낸 비용이 사라지기 때문이다.
        internal bool AcceptsCommands => _acceptsCommands();

        // 다른 소유자처럼 Awake에서 저장 수집과 불러오기를 구독하고 OnDestroy에서 끊는다. 건물 값은 자기 Buildings 구역으로 쓴다.
        void Awake()
        {
            SaveManager.CollectSaveData += WriteSection;
            SaveManager.FileLoaded += ReadSection;
        }

        void OnDestroy()
        {
            SaveManager.CollectSaveData -= WriteSection;
            SaveManager.FileLoaded -= ReadSection;
        }

        // 원작 Any다. 정의가 있는 설치 건물만 세고, 그 정의가 다른 ID의 대체(alternativeFor)이면 그 ID로도 센다.
        // 퀘스트 건설 과제와 건설 조건은 기본값대로 토큰을 세지 않는다.
        public bool Any(string buildingId, bool includeTokens = false)
        {
            if (includeTokens && GetTokenAmount(buildingId) > 0) return true;
            BuildingDataCollection definitions = GameplayDataSettings.BuildingDataCollection;
            for (int index = 0; index < _installations.Count; index++)
            {
                if (!definitions.TryGetInfo(_installations[index].OriginalBuildingId, out BuildingInfo? info)) continue;
                if (string.Equals(info.Id, buildingId, StringComparison.Ordinal)) return true;
                IReadOnlyList<string> alternatives = info.AlternativeFor;
                for (int alternative = 0; alternative < alternatives.Count; alternative++)
                    if (string.Equals(alternatives[alternative], buildingId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // 원작 GetBuildingAmount다. 정의가 없는 저장 건물도 ID가 같으면 센다.
        public int GetBuildingAmount(string buildingId)
        {
            int amount = 0;
            for (int index = 0; index < _installations.Count; index++)
                if (string.Equals(_installations[index].OriginalBuildingId, buildingId, StringComparison.Ordinal)) amount++;
            return amount;
        }

        public int GetTokenAmount(string buildingId)
        {
            int index = TokenIndex(buildingId);
            return index >= 0 ? _tokens[index].Amount : 0;
        }

        // 한 영역의 설치 건물을 설치한 순서로 새 목록에 담아 준다. 영역 표시가 다시 그릴 때 읽는다.
        public IReadOnlyList<BuildingInstallationState> GetInstallations(string areaId)
        {
            var found = new List<BuildingInstallationState>();
            for (int index = 0; index < _installations.Count; index++)
                if (string.Equals(_installations[index].AreaId, areaId, StringComparison.Ordinal)) found.Add(_installations[index]);
            return found;
        }

        // 원작 GetBuildingData(guid)다. 영역 표시가 배치 신호를 받거나 로드를 기다린 뒤 그 건물이 아직 있는지 볼 때 읽는다.
        public bool TryGetInstallation(int instanceId, [NotNullWhen(true)] out BuildingInstallationState? installation)
        {
            int index = IndexOf(instanceId);
            installation = index >= 0 ? _installations[index] : null;
            return installation != null;
        }

        // 원작 BuildingAreaData.GetBuildingAt이다. 칸을 점유한 첫 설치 건물을 찾는다. 정의가 없는 건물은 원작처럼 칸을 차지하지 않는다.
        public bool TryGetBuildingAt(string areaId, Vector2Int coord, [NotNullWhen(true)] out BuildingInstallationState? building)
        {
            BuildingDataCollection definitions = GameplayDataSettings.BuildingDataCollection;
            for (int index = 0; index < _installations.Count; index++)
            {
                BuildingInstallationState candidate = _installations[index];
                if (!string.Equals(candidate.AreaId, areaId, StringComparison.Ordinal)) continue;
                if (!definitions.TryGetInfo(candidate.OriginalBuildingId, out BuildingInfo? info)) continue;
                if (BuildingRules.Occupies(info.Dimensions, (BuildingRotation)candidate.Rotation, Origin(candidate), coord))
                {
                    building = candidate;
                    return true;
                }
            }
            building = null;
            return false;
        }

        // 원작 BuildingAreaData.Collide다. 놓으려는 칸이 같은 영역의 설치 건물 칸과 하나라도 겹치면 참이고, 맞닿기만 하면 거짓이다.
        public bool Collides(string areaId, Vector2Int dimensions, BuildingRotation rotation, Vector2Int coord)
        {
            BuildingDataCollection definitions = GameplayDataSettings.BuildingDataCollection;
            for (int index = 0; index < _installations.Count; index++)
            {
                BuildingInstallationState placed = _installations[index];
                if (!string.Equals(placed.AreaId, areaId, StringComparison.Ordinal)) continue;
                if (!definitions.TryGetInfo(placed.OriginalBuildingId, out BuildingInfo? info)) continue;
                if (BuildingRules.Overlaps(dimensions, rotation, coord, info.Dimensions, (BuildingRotation)placed.Rotation, Origin(placed)))
                    return true;
            }
            return false;
        }

        // 원작 BuyAndPlace의 상태 변경 부분이다. 정의, 개수, 요구 조건, 위치, 비용 확인은 배치 명령이 끝낸 뒤 부른다.
        // useToken이면 토큰 하나를 빼고 놓는다. 토큰이 남았는지는 배치 명령이 같은 호출에서 보고 그 사이 토큰은 바뀌지 않는다.
        // 관문이 닫혔으면 기록 없이 false다.
        internal bool TryPlace(string areaId, string buildingId, Vector2Int coord, BuildingRotation rotation, bool useToken, out int instanceId)
        {
            instanceId = -1;
            if (!_acceptsCommands()) return false;
            int token = useToken ? TokenIndex(buildingId) : -1;
            // 원작처럼 모든 영역에서 겹치지 않는 번호를 뽑는다. 저장 규칙과 같은 검사로 빈 ID나 범위 밖 회전을 거절해 저장이 깨지지 않게 한다.
            int created = NewInstanceId();
            if (!BuildingInstallationState.TryCreate(created, buildingId, areaId, coord.x, coord.y, (int)rotation,
                    out BuildingInstallationState? installation)) return false;
            if (useToken) _tokens[token] = new BuildingTokenState(buildingId, _tokens[token].Amount - 1);
            _installations.Add(installation);
            Capture();
            instanceId = created;
            ObserverDispatch.InvokeEach(Changed);
            ObserverDispatch.InvokeEachValue(Built, created);
            return true;
        }

        // 원작 ReturnBuilding이다. 건물을 지우고 그 종류의 토큰을 하나 준다. 원작은 영역 ID를 주면 찾아도 실패하므로 영역을 받지 않는다.
        // 없는 번호나 닫힌 관문은 기록 없이 false다.
        internal bool TryReturn(int instanceId)
        {
            if (!_acceptsCommands()) return false;
            int index = IndexOf(instanceId);
            if (index < 0) return false;
            string buildingId = _installations[index].OriginalBuildingId;
            _installations.RemoveAt(index);
            AddTokens(buildingId, 1);
            Capture();
            ObserverDispatch.InvokeEach(Changed);
            ObserverDispatch.InvokeEachValue(Destroyed, instanceId);
            return true;
        }

        // 원작 ReturnBuildingsOfType이다. 그 종류를 모든 영역에서 토큰으로 바꾸고 바꾼 수를 돌려준다.
        // 원작은 하나도 없어도 목록 신호를 내지만, 실패한 명령은 신호가 없다는 규칙을 따라 0이면 아무 신호도 내지 않는다.
        internal int ReturnAllOfType(string buildingId)
        {
            if (!_acceptsCommands()) return 0;
            var returned = new List<int>();
            for (int index = 0; index < _installations.Count; index++)
                if (string.Equals(_installations[index].OriginalBuildingId, buildingId, StringComparison.Ordinal))
                    returned.Add(_installations[index].InstanceId);
            if (returned.Count == 0) return 0;
            _installations.RemoveAll(installation => string.Equals(installation.OriginalBuildingId, buildingId, StringComparison.Ordinal));
            AddTokens(buildingId, returned.Count);
            Capture();
            for (int index = 0; index < returned.Count; index++) ObserverDispatch.InvokeEachValue(Destroyed, returned[index]);
            ObserverDispatch.InvokeEach(Changed);
            return returned.Count;
        }

        static Vector2Int Origin(BuildingInstallationState installation) => new Vector2Int(installation.X, installation.Y);

        int IndexOf(int instanceId)
        {
            for (int index = 0; index < _installations.Count; index++)
                if (_installations[index].InstanceId == instanceId) return index;
            return -1;
        }

        int TokenIndex(string buildingId)
        {
            for (int index = 0; index < _tokens.Count; index++)
                if (string.Equals(_tokens[index].OriginalBuildingId, buildingId, StringComparison.Ordinal)) return index;
            return -1;
        }

        void AddTokens(string buildingId, int amount)
        {
            int index = TokenIndex(buildingId);
            if (index >= 0) _tokens[index] = new BuildingTokenState(buildingId, _tokens[index].Amount + amount);
            else _tokens.Add(new BuildingTokenState(buildingId, amount));
        }

        // 원작 GenerateBuildingGUID처럼 0 이상 int.MaxValue 미만에서 뽑고, 이미 쓰는 번호면 다시 뽑는다.
        int NewInstanceId()
        {
            int id;
            do
            {
                id = UnityEngine.Random.Range(0, int.MaxValue);
            }
            while (IndexOf(id) >= 0);
            return id;
        }

        // 성공한 명령 뒤에는 미포착 값도 전체 목록으로 쓴다. 저장 묶음은 불변 값이라 바뀔 때마다 새로 만든다.
        void Capture() => _persistence = new BuildingPersistenceState(_installations, _tokens);

        // 저장마다 건물 구역을 한 번 쓴다. 미포착 값(null)도 그대로 넘겨, 옛 저장의 건물 기록 없음을 빈 목록으로 바꾸지 않는다.
        void WriteSection(GameSaveData data) => data.SetBuildings(_persistence);

        // 불러온 기록으로 바꾼 뒤 알린다. 기록은 코덱이 검증한 불변 값이고, 정의 없는 저장 건물도 지우지 않고 그대로 다시 쓴다.
        void ReadSection(GameSaveData data)
        {
            BuildingPersistenceState? saved = data.Buildings;
            _installations.Clear();
            _tokens.Clear();
            if (saved != null)
            {
                _installations.AddRange(saved.Installations);
                _tokens.AddRange(saved.Tokens);
            }
            _persistence = saved;
            ObserverDispatch.InvokeEach(Changed);
        }
    }
}
