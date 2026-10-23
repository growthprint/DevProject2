#nullable enable
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 원작 MasterKeysManager처럼 등록한 열쇠를 소유한다. GameManager 프리팹 자식의 컴포넌트이고 GameManager.MasterKeys로 찾는다.
    // 등록 명령(GameplayCommands.TryRegisterMasterKey)이 확인과 열쇠 제거를 마친 뒤 활성만 맡긴다. 등록된 열쇠는 해제되지 않는다. 메인 스레드에서만 쓴다.
    [DisallowMultipleComponent]
    public sealed class MasterKeysManager : MonoBehaviour
    {
        // 원작 AllPossibleKeys의 Key 태그와 excludeTags(SpecialKey)다. 원작 등록 칸 필터(Key 칸의 requireTags, excludeTags)도 같은 두 태그다.
        const string _keyTag = "Key", _excludedTag = "SpecialKey";

        // 값이 바뀌었다는 신호만 보낸다. 3-M의 등록 창이 듣는다.
        public static event Action? Changed;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics() => Changed = null;

        // 등록한 순서의 원작 아이템 ID 목록과 빠른 조회용 집합이다. 저장은 등록한 순서로 쓴다.
        IReadOnlyList<int> _keys = CampaignRecordsState.Empty.MasterKeys;
        readonly HashSet<int> _active = new HashSet<int>();

        // 원작 MasterKeysManager.IsActive다. 문 열쇠 확인(GameplayCommands.HasRequiredItem, TryUseRequiredItem)과 등록 창이 읽는다.
        internal bool IsActive(int originalItemId) => _active.Contains(originalItemId);

        // 원작 AllPossibleKeys와 등록 칸 CanPlug의 판정이다. Key 태그가 있고 SpecialKey 태그가 없으면 등록할 수 있다. 태그는 원본 Tag 자산 이름이다.
        // 등록 창의 강조·조작 가능 표시와 등록 명령이 같은 판정을 쓴다. 원작의 체험판 LockInDemo 제외는 체험판이 아니라 옮기지 않는다.
        internal static bool IsRegistrable(OriginalItemDefinition definition) =>
            definition.HasTag(_keyTag) && !definition.HasTag(_excludedTag);

        // 원작 Activate_Local이다. 등록 명령이 등록 가능, 미활성을 확인하고 열쇠를 뺀 뒤에만 부른다. 저장은 등록 순서라 끝에 붙인다.
        // 목록은 저장 구역에 복사 없이 넘기므로 고치지 않고 새 배열로 바꾼다. 등록은 드문 명령이라 할당을 줄이지 않는다.
        internal void Activate(int originalItemId)
        {
            _keys = ReadOnlyLists.Appended(_keys, originalItemId);
            _active.Add(originalItemId);
            ObserverDispatch.InvokeEach(Changed);
        }

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

        // 불변 목록이라 복사하지 않고 그대로 넘긴다.
        void WriteSection(GameSaveData data) => data.SetMasterKeys(_keys);

        // 불러온 기록으로 바꾼 뒤 알린다. 기록은 코덱이 검증한 불변 값이다.
        void ReadSection(GameSaveData data)
        {
            _keys = data.MasterKeys;
            _active.Clear();
            foreach (int id in _keys) _active.Add(id);
            ObserverDispatch.InvokeEach(Changed);
        }
    }
}
