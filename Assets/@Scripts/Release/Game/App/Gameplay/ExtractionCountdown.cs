#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Game.App
{
    // 원작 CountDownArea의 철수 카운트다운이다. 상호작용 객체가 만들고, 컨트롤러가 탄환 진행 직후 매 프레임 Tick을 부른다.
    internal sealed class ExtractionCountdown
    {
        readonly GameplayWorldContext _world;
        readonly Func<RaidOutcome, UniTask> _finish;
        // 철수는 원본 CountDownArea.timeWhenCountDownBegan처럼 지점에 들어선 시각(_extractBegan)을 기억한다.
        OriginalGameplayPoint? _extractPoint;
        float _extractBegan;

        internal ExtractionCountdown(GameplayWorldContext world, Func<RaidOutcome, UniTask> finish)
        {
            _world = world;
            _finish = finish;
        }

        // 원본 CountDownArea처럼 철수 지점에 들어선 시각부터 Time.time으로 경과를 잰다. 일시정지는 timeScale이 0이라 멈춘다.
        // 인벤토리나 지도가 열려 있으면(원본 View.ActiveView) 시간은 흐르고 완료 판정만 미룬다. 그래서 창을 닫을 때 시간이 다 찼으면 바로 철수한다.
        // 범위를 벗어나면 원본 EvacuationCountdownUI.Release처럼 표시를 숨긴다. 철수를 시작했으면 true를 돌려준다.
        internal bool Tick()
        {
            // 원작 CountDownArea가 캐릭터 루트 캡슐로 감지하므로 그 중심 높이에서 판정한다.
            Vector3 position = _world.PlayerPosition + Vector3.up * OriginalInteractionSearch._areaProbeHeight;
            OriginalGameplayPoint? area = null;
            foreach (OriginalGameplayPoint point in _world.Points)
                if (IsOpenExtraction(point, position))
                {
                    area = point;
                    break;
                }
            SharedHudView hud = _world.Hud;
            if (area == null)
            {
                _extractPoint = null;
                hud.SetEvacuation(false, 0, 0);
                return false;
            }
            if (_extractPoint != area) { _extractPoint = area; _extractBegan = Time.time; }
            float elapsed = Time.time - _extractBegan, required = area.Duration;
            // 원본 EvacuationCountdownUI처럼 남은 시간과 진행률을 표시한다. 값 자르기는 EvacuationCountdownView가 하고,
            // required > 0 검사는 0으로 나누기를 막는다.
            hud.SetEvacuation(true, required - elapsed, required > 0 ? elapsed / required : 1);
            if (elapsed < required || _world.Runtime.GameplayInput.IsBlocked) return false;
            hud.SetEvacuation(false, 0, 1);
            _finish(RaidOutcome.Extracted).Forget();
            return true;
        }

        // 켜진 철수 지점의 영역 안이고 조건 사건을 채웠는지다. 꺼진 지점은 영역을 보기 전에 건너뛴다.
        bool IsOpenExtraction(OriginalGameplayPoint point, Vector3 position) =>
            point.Kind == OriginalPointKind.Extraction && point.gameObject.activeInHierarchy && point.Contains(position) && RequiredEventMet(point);

        // 조건 사건이 없으면 늘 열려 있다. DoorSync의 사건 조건은 빈 사건을 닫힌 것으로 보는 다른 규칙이라 함께 쓰지 않는다.
        bool RequiredEventMet(OriginalGameplayPoint point) =>
            point.RequiredEvent.Length == 0 || _world.TutorialEventComplete(point.RequiredEvent);
    }
}
