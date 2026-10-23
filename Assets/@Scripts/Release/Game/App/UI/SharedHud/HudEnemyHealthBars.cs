#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.App
{
    // 원본 HealthBarManager처럼 HUDCanvas/HealthBars 아래에 교전 대상마다 HealthBar 프리팹을 만들고, 스태미나 HUD와 같은 카메라로 추적한다.
    // 프리팹 참조는 생성자로 다시 받지 않고 SharedHudView의 직렬화 필드를 직접 읽는다.
    internal sealed class HudEnemyHealthBars
    {
        readonly SharedHudView _hud;
        // 적마다 처음 보일 때 만든 체력바다. SharedHudTests가 이 필드 이름으로 목록을 관찰한다.
        readonly Dictionary<OriginalEnemyController, OriginalEnemyHealthView> _bars = new();
        // 원본 HealthBarManager의 풀처럼 죽은 적에게서 거둔 꺼진 체력바다. 다음에 처음 보이는 적이 새로 만들지 않고 다시 쓴다.
        readonly Stack<OriginalEnemyHealthView> _pool = new();
        // 전투 월드는 적 파사드의 나온 적 목록을 넘겨 스폰 후보 전체를 돌지 않는다. 같은 목록 객체라 연결 뒤 준비한 적도 보인다.
        IReadOnlyList<OriginalEnemyController> _enemies = Array.Empty<OriginalEnemyController>();

        internal HudEnemyHealthBars(SharedHudView hud) => _hud = hud;

        // 호출자는 적이 없어도 빈 목록을 넘기므로 null 검사 없이 그대로 쓴다.
        internal void Bind(IReadOnlyList<OriginalEnemyController> enemies) => _enemies = enemies;

        // 소유자가 바뀔 때 앞 씬의 적 목록과 풀에 거둔 막대까지 모든 체력바를 지운다. 저작 검사나 EditMode 테스트처럼
        // Edit Mode에서 불려도 오류 로그 없이 즉시 파괴되도록 공용 파괴 도우미를 쓴다.
        internal void Reset()
        {
            _enemies = Array.Empty<OriginalEnemyController>();
            foreach (OriginalEnemyHealthView bar in _bars.Values) UnityObjects.DestroyOwned(bar.gameObject);
            _bars.Clear();
            foreach (OriginalEnemyHealthView bar in _pool) UnityObjects.DestroyOwned(bar.gameObject);
            _pool.Clear();
        }

        // 적 컨트롤러는 죽어도 상태만 바뀌고 파괴되지 않는다. 소유 씬이 해제되기 전에 Detach가 목록을 비우므로
        // null을 건너뛰지 않는다. 인벤토리나 결과 화면이 열려 있으면(visible이 false) 이미 만든 체력바도 숨긴다.
        // 원본 HealthBar처럼 적이 죽으면 막대를 한 번 끄고 풀로 돌려, 죽은 적은 이후 프레임에 투영도 판정도 하지 않는다.
        internal void Present(Camera camera, bool resultOpen, bool visible)
        {
            // 매 프레임 도는 목록이라 인터페이스 열거자를 할당하지 않도록 색인으로 돈다.
            for (int index = 0; index < _enemies.Count; index++)
            {
                OriginalEnemyController enemy = _enemies[index];
                // 원작 Health.RequestHealthBar처럼 프리셋이 체력바를 끈 적에게는 막대를 만들지도 투영하지도 않는다.
                if (!enemy.ShowHealthBar) continue;
                if (!_bars.TryGetValue(enemy, out OriginalEnemyHealthView bar))
                {
                    if (!enemy.HealthVisible || resultOpen) continue;
                    bar = _pool.Count > 0 ? _pool.Pop() : Object.Instantiate(_hud._healthBarTemplate, _hud._healthBars, false);
                    bar.Bind(enemy);
                    _bars.Add(enemy, bar);
                }
                else if (enemy.Dead)
                {
                    bar.Hide();
                    _bars.Remove(enemy);
                    _pool.Push(bar);
                    continue;
                }
                bar.Present(enemy, camera, visible);
            }
        }
    }
}
