#nullable enable
using UnityEngine;

namespace Game.App
{
    // 원작이 SavesSystem 키(Breakable_<키>, ConstructionSite_<키>)로 남기는 레벨 물체 상태를 모든 씬에 걸쳐 소유한다.
    // 부서진 벽과 건설한 지점의 원작 키 목록이다. GameManager 프리팹 자식의 컴포넌트이고 GameManager.LevelObjects로 찾는다. 메인 스레드에서만 쓴다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed class SavedLevelObjects : SessionSectionOwner
    {
        // 기록한 순서를 지키는 키 기록이다. 저장은 기록한 순서로 쓴다.
        readonly OrderedKeySet _broken = new OrderedKeySet();
        readonly OrderedKeySet _built = new OrderedKeySet();

        // 원작 Breakable.Awake가 읽는 저장 값이다. 부서진 벽은 다음 레벨에서도 부서진 채로 시작한다.
        internal bool IsBroken(string key) => _broken.Contains(key);
        // 원작 ConstructionSite.Load가 읽는 저장 값이다. 같은 키를 쓰는 지점은 씬이 달라도 함께 건설된 것으로 본다.
        internal bool IsBuilt(string key) => _built.Contains(key);

        // 원작 Breakable.OnDead의 저장이다. 이미 기록한 키는 그대로 둔다.
        internal bool TryMarkBroken(string key)
        {
            if (!AcceptsCommands) return false;
            _broken.Add(key);
            return true;
        }

        // 원작 ConstructionSite.OnBuilt의 저장이다. 이미 기록한 키는 그대로 둔다.
        internal bool TryMarkBuilt(string key)
        {
            if (!AcceptsCommands) return false;
            _built.Add(key);
            return true;
        }

        // 기반 클래스의 저장 수집이 부른다.
        private protected override void WriteSection(GameSaveData data) => data.SetLevelObjects(_broken.ToArray(), _built.ToArray());

        // 불러온 기록의 두 목록으로 바꾼다. 기록의 목록은 검증된 불변 값이다.
        private protected override void ReadSection(GameSaveData data)
        {
            _broken.Load(data.BrokenBreakables);
            _built.Load(data.ConstructionSites);
        }
    }
}
