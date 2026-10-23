#nullable enable annotations
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 HealthBar의 배치와 체력 게이지, 뒤따라 줄어드는 후행 게이지를 재현한다.
    // 공용 HUD가 적마다 템플릿 프리팹으로 하나씩 만들며, 게이지 참조는 그 프리팹에 직렬화돼 있다.
    public sealed class OriginalEnemyHealthView : MonoBehaviour
    {
        [SerializeField] Image _fill = null!, _follow = null!;
        // 원본 HealthBar.RefreshOffset처럼 적에 연결할 때 한 번 구한 머리 위 표시 높이다. 매 프레임 머리 위치를 다시 재지 않는다.
        Vector3 _offset;
        // 막대 배치 값이다. 머리 위 여유, 머리 콜라이더가 없을 때의 높이, 화면 높이에 대한 위쪽 띄움 비율이다.
        const float _headClearance = .5f, _defaultBarHeight = 1.5f, _screenLiftRatio = .02f;

        // 게이지는 적 위치에서 머리 중심까지 거리보다 _headClearance 위에, 머리 콜라이더가 없으면 _defaultBarHeight 위에 둔다.
        // 원본이 머리 소켓 위치를 재듯 머리 조준 Trigger의 Transform을 읽어, 물리 동기화 시점과 무관한 값을 얻는다.
        internal void Bind(OriginalEnemyController enemy)
        {
            Vector3 position = enemy.transform.position;
            Collider? head = enemy.HeadCollider;
            _offset = Vector3.up * (head != null ? Vector3.Distance(position, head.transform.position) + _headClearance : _defaultBarHeight);
        }

        // 숨길 때는 화면 투영 전에 돌아간다. 처음 보일 때 후행 게이지를 현재 체력으로 맞춰,
        // 풀에서 다시 쓴 막대가 앞 적의 값을 끌고 오지 않게 한다. 체력 비율은 HUD 게이지와 같은 GaugeFill 규칙을 쓴다.
        internal void Present(OriginalEnemyController enemy, Camera camera, bool visible)
        {
            if (!visible || !enemy.HealthVisible) { Hide(); return; }
            Vector3 screen = camera.WorldToScreenPoint(enemy.transform.position + _offset);
            if (IsOffScreen(screen)) { Hide(); return; }
            float health = HudStatusDisplay.GaugeFill(enemy.Health, enemy.MaxHealth);
            if (!gameObject.activeSelf) { _follow.fillAmount = health; gameObject.SetActive(true); }
            transform.position = screen + Vector3.up * (Screen.height * _screenLiftRatio);
            _fill.fillAmount = health;
            _follow.fillAmount = Mathf.MoveTowards(_follow.fillAmount, health, Time.unscaledDeltaTime);
        }

        // 카메라 뒤이거나 화면 밖으로 투영된 막대는 숨긴다.
        static bool IsOffScreen(Vector3 screen) =>
            screen.z <= 0 || screen.x < 0 || screen.x > Screen.width || screen.y < 0 || screen.y > Screen.height;

        // 숨김이 이어지는 프레임마다 SetActive를 다시 부르지 않는다. 죽은 적의 막대를 풀로 거둘 때도 쓴다.
        internal void Hide() => UiVisibility.Set(gameObject, false);
    }
}
