#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 CharacterMainControl.Update는 모든 레벨에서 생존 틱과 갈증, 기아 처리를 돌리므로 프롤로그와 전투 월드가 이 규칙을 함께 쓴다.
    // 생존 HUD 갱신은 PlayerStatusHud가 맡는다. 씬 컨트롤러가 레벨 초기화 때 만들어 소유한다.
    // 구독이나 빌린 객체가 없어 따로 정리하지 않는다. 생존 수치 값과 갈증, 기아 효과는 생존 수치 소유자(PlayerVitals)가 가진다.
    internal sealed class PlayerSurvival
    {
        readonly GameRuntime _runtime;
        readonly OriginalGameplayCatalog _catalog;
        // 원본 TickVariables의 1초 간격 생존 수치 틱에 쓰는 게임 시간 누적값이다.
        float _survivalTick;
        // 생존 시간을 마지막으로 잰 Unity 게임 시각(Time.timeAsDouble)이다. 프레임 경과 시간은 이 시각과의 차로 구한다.
        double _lastGameTime;

        // 생존 틱은 Runtime과 카탈로그만 읽는다. 모터와 공용 HUD는 PlayerStatusHud가 받는다.
        internal PlayerSurvival(GameRuntime runtime, OriginalGameplayCatalog catalog)
        {
            _runtime = runtime!;
            _catalog = catalog!;
            _lastGameTime = Time.timeAsDouble;
        }

        // 생존 수치가 진행되는 Runtime 조건이다. 일시정지, 결과 화면, 전환, 세션 없음에서는 멈춘다. 씬마다 다른 조건은 Tick의 halted로 받는다.
        // 프롤로그는 이 조건을 통과한 프레임에만 새 게임 플레이어를 만든다.
        internal bool Active => _runtime.IsGameplayActive && _runtime.AcceptsSessionCommands;

        // 원본 TickVariables처럼 게임 시간 1초마다 생존 수치 소유자의 틱을 한 번 부르고 남은 시간은 버린다. 회복과 소모는 맵 종류로 갈린다.
        // 이어서 매 프레임 소유자의 갈증, 기아 효과와 기아 피해 시계를 진행한다. 시계와 달리 대사 중에도 진행한다.
        internal void Tick(bool halted)
        {
            // 원본은 Time.deltaTime을 그대로 더하지만, 여기서는 게임 시계의 차로 실제 경과 시간을 구한다. 개발 플레이어에 Frame Debugger를
            // 붙이거나 뗄 때 Unity는 정지 프레임의 deltaTime을 -7e-9 같은 반올림 오차 음수로 주고, GAS는 음수를 호출자 결함으로 보아 개발 빌드에서
            // 예외를 던진다. 게임 시계는 거꾸로 가지 않고 그 프레임에 실제로 흐른 시간을 가진다(2026-10-17 측정: 붙일 때 0.0111초, 뗄 때 0).
            // 멈춘 프레임의 시간도 여기서 소비해, 다시 진행할 때 한꺼번에 넘기지 않는다.
            double now = Time.timeAsDouble;
            float deltaTime = (float)(now - _lastGameTime);
            _lastGameTime = now;
            if (halted || !Active) return;
            // 호출자는 앱이 살아 있는 프레임에만 부르고, 위 Active가 이 Runtime에 세션이 있음을 확인했다.
            // 맵 종류는 그 세션의 현재 장소로 정하고, 생존 틱과 기아 처리가 같은 장소를 쓴다.
            PlayerVitals vitals = _runtime.Vitals;
            GameScene scene = _runtime.Snapshot().SceneId;
            if (OriginalCharacter.AdvanceSurvivalTick(ref _survivalTick, deltaTime)) vitals.TryTickSurvival(_catalog, scene);
            // 틱이 바꾼 값을 같은 프레임의 기아 판정에 쓰도록 틱 뒤에 부른다. 기아 피해 배율은 난이도 소유자의 현재 값이다.
            // 원본처럼 모든 장소가 같은 기아 규칙을 쓴다. 프롤로그는 에너지가 기아까지 내려가지 않아 피해가 생기지 않는다.
            vitals.TickSurvivalStatus(scene, GameManager.Difficulty.Current, deltaTime);
        }
    }
}
