#nullable enable
using UnityEngine;

namespace Game.App
{
    // 원작이 SavesSystem 키 하나로 남기는 한 번짜리 진행 기록을 모든 씬에 걸쳐 소유한다. 낚시 경험(원작 HasFished)과
    // playOnce 컷신의 재생 기록(원작 CutScene_<id>, 범위 안에서는 머드 첫 만남 CoachFirstMeet)이다. 한 번 세운 기록은 지우지 않는다.
    // GameManager 프리팹 자식의 컴포넌트이고 GameManager.Flags로 찾는다. 저장은 Flags 구역이다. 메인 스레드에서만 쓴다.
    // 명령 관문과 저장 구독은 SessionSectionOwner가 맡는다.
    [DisallowMultipleComponent]
    public sealed class CampaignFlags : SessionSectionOwner
    {
        bool _hasFished;
        // 재생한 순서의 원작 CutScene id다. 저장은 재생한 순서로 쓴다.
        readonly OrderedKeySet _playedCutScenes = new OrderedKeySet();

        // 원작 RequireHasFished 조건이 읽는 값이다. 5-F1의 낚시와 그 조건을 쓰는 쪽이 읽는다.
        internal bool HasFished => _hasFished;

        // 원작 낚시가 물고기를 처음 낚을 때 쓰는 HasFished 기록이다. 낚시가 부른다. 다시 불러도 참으로 남는다.
        internal bool TrySetHasFished()
        {
            if (!AcceptsCommands) return false;
            _hasFished = true;
            return true;
        }

        // 원작 CutScene.playOnce가 읽는 기록이다. 머드 첫 만남 컷신이 읽는다.
        internal bool IsCutScenePlayed(string cutSceneId) => _playedCutScenes.Contains(cutSceneId);

        // 원작 CutScene이 끝날 때 쓰는 기록이다. 이미 기록한 ID는 그대로 둔다. 빈 ID는 저장 규칙에 맞지 않아 기록 없이 거절한다.
        internal bool TryMarkCutScenePlayed(string cutSceneId)
        {
            if (!AcceptsCommands || string.IsNullOrWhiteSpace(cutSceneId)) return false;
            _playedCutScenes.Add(cutSceneId);
            return true;
        }

        // 기반 클래스의 저장 수집이 부른다.
        private protected override void WriteSection(GameSaveData data) => data.SetFlags(_hasFished, _playedCutScenes.ToArray());

        // 불러온 기록으로 바꾼다. 기록의 목록은 검증된 불변 값이다.
        private protected override void ReadSection(GameSaveData data)
        {
            _hasFished = data.HasFished;
            _playedCutScenes.Load(data.PlayedCutScenes);
        }
    }
}
