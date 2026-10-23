#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 CutScene의 재생 1회 기록 부분(id, playOnce, setActiveFalseWhenFinished)이다. 같은 오브젝트의 대사 지점(OriginalGameplayPoint)을
    // 캠페인 전체에서 한 번만 재생하게 한다. 기록은 CampaignFlags가 소유하고, 대사 재생은 전투 월드(WorldInteractions)가 맡는다.
    // 레벨 준비 뒤 처음 한 번 기록을 보고, 이미 재생했으면 원작 PlayIfNessisary처럼 지점을 끈다. 끝 기록은 월드가 대사를 마칠 때 MarkFinished로 남긴다.
    // 범위 안 사용처는 제로존 머드 첫 만남(CoachFirstMeet) 하나이고, 씬 제작 도구(5-T)가 붙인다. 메인 스레드에서만 쓴다.
    [DisallowMultipleComponent]
    public sealed class OriginalCutScene : MonoBehaviour
    {
        // 원작 id다. 저장 기록 키(원작 CutScene_<id>)에 쓰므로 원작 문자열 그대로 둔다.
        [SerializeField] string _cutSceneId = "";
        [SerializeField] bool _playOnce = true;
        [SerializeField] bool _setActiveFalseWhenFinished = true;

        internal string CutSceneId => _cutSceneId;

        // 레벨 준비가 끝난 첫 프레임에 저장 기록을 본 뒤 이 컴포넌트를 끈다. 꺼진 하위 씬에 있으면 그 하위 씬이 켜진 뒤 본다.
        // 앱이 없으면 읽을 기록이 없어 보지 않는다.
        void Update()
        {
            if (!SceneContext.LevelInited || !GameManager.HasInstance) return;
            enabled = false;
            ShowSavedState(GameManager.Flags);
        }

        // 원작 PlayIfNessisary의 재생 여부다. playOnce이고 기록이 있으면 다시 재생하지 않는다.
        // 전투 월드가 대사 지점에 들어설 때 읽는다(5-T 요청으로 WorldInteractions가 부른다).
        internal bool CanPlay(CampaignFlags flags) => !_playOnce || !flags.IsCutScenePlayed(_cutSceneId);

        // 이미 재생한 컷신이면 대사 지점을 꺼 탐색에 걸리지 않게 한다. 꺼진 지점은 전투 월드가 건너뛴다.
        internal void ShowSavedState(CampaignFlags flags)
        {
            if (!CanPlay(flags)) gameObject.SetActive(false);
        }

        // 원작 OnPlayFinished다. 원작 MarkPlayed처럼 playOnce와 관계없이 기록하고, setActiveFalseWhenFinished면 지점을 끈다.
        // 기록 명령이 거절되면 false다. 그래도 원작처럼 지점은 끄므로 이번 레벨에서는 다시 재생하지 않고, 다음 레벨에서 다시 재생한다.
        // 전투 월드가 대사 끝 콜백에서 부른다(5-T 요청으로 WorldInteractions가 부른다).
        internal bool MarkFinished(CampaignFlags flags)
        {
            bool recorded = flags.TryMarkCutScenePlayed(_cutSceneId);
            if (_setActiveFalseWhenFinished) gameObject.SetActive(false);
            return recorded;
        }
    }
}
