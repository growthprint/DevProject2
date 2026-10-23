#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 SmoothFollowCharacterHUD와 그 아래 StaminaHUD다. FollowCharacterHUD.UpdatePos처럼 스태미나 게이지를 캐릭터에 따라 붙인다.
    // 추적 값 셋(offset, smoothTime, maxDistance)은 플레이 중 Inspector에서 고친 값도 바로 반영되도록 SharedHudView에서 매번 읽는다.
    internal sealed class HudStaminaFollow
    {
        // 채움 비율이 이 값 이상이면 스태미나가 가득 찬 것으로 보고 게이지를 숨긴다.
        const float _fullFillAmount = .999f;

        readonly SharedHudView _hud;
        // 최대 스태미나는 캐릭터 연결 때 한 번 받아 두고, 매 프레임 갱신은 현재 값만 받는다. 연결 전에는 상태 표시의 시작 최대치다.
        float _maxStamina = HudStatusDisplay._unboundMaximum;
        Vector3 _followPoint, _followVelocity;
        // 캐릭터를 새로 연결하면 첫 추적에서 따라가지 않고 목표 위치에 바로 놓는다.
        bool _followPlaced;
        // 마지막으로 쓴 게이지 화면 위치다. NaN으로 시작해 첫 추적은 늘 쓴다.
        Vector3 _shownScreen = new(float.NaN, float.NaN, float.NaN);

        internal HudStaminaFollow(SharedHudView hud) => _hud = hud;

        internal void Bind(float maxStamina)
        {
            _maxStamina = maxStamina;
            _followPlaced = false;
        }

        // 소유자가 바뀔 때 추적 상태와 최대값을 시작값으로 돌리고 게이지를 숨긴다.
        internal void Reset()
        {
            _followPlaced = false;
            _followPoint = Vector3.zero;
            _followVelocity = Vector3.zero;
            _maxStamina = HudStatusDisplay._unboundMaximum;
            _hud._staminaRoot.gameObject.SetActive(false);
        }

        // 다른 HUD 게이지와 같은 채움 비율 규칙을 쓴다.
        internal void ShowFill(float stamina) => _hud._staminaFill.fillAmount = HudStatusDisplay.GaugeFill(stamina, _maxStamina);

        // 추적 대상이나 카메라가 없는 동안 매 프레임 불리므로, 이미 꺼져 있으면 SetActive를 부르지 않는다.
        internal void Hide() => UiVisibility.Set(_hud._staminaRoot.gameObject, false);

        // 카메라의 수평 방향으로 오프셋을 둔 목표를 부드럽게 따라가고, 최대 거리 밖으로 벌어지면 끌어당긴다.
        // 게이지는 visible이고 스태미나가 가득 차지 않았을 때만 보인다.
        internal void Follow(Transform player, Camera camera, bool visible)
        {
            Transform cameraTransform = camera.transform;
            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = cameraTransform.right;
            right.y = 0f;
            right.Normalize();
            Vector2 followOffset = _hud._followOffset;
            float followMaxDistance = _hud._followMaxDistance;
            Vector3 target = player.position + forward * followOffset.y + right * followOffset.x;
            if (!_followPlaced) { _followPoint = target; _followVelocity = Vector3.zero; _followPlaced = true; }
            _followPoint = Vector3.SmoothDamp(_followPoint, target, ref _followVelocity, _hud._followSmoothTime);
            // 제곱근을 피하려고 제곱 거리로 비교한다. 경계에서 생기는 아주 작은 float 오차는 허용한다.
            // 최대 거리가 음수면 제곱할 때 부호가 사라지므로, 거리 비교와 같게 NaN이 아닌 모든 거리에서 끌어당긴다.
            Vector3 offset = _followPoint - target;
            float squaredDistance = offset.sqrMagnitude;
            bool beyond = followMaxDistance < 0f ? !float.IsNaN(squaredDistance) : squaredDistance > followMaxDistance * followMaxDistance;
            if (beyond)
                _followPoint = offset.normalized * followMaxDistance + target;
            // 같은 값의 Transform 대입도 HUD 캔버스 전체를 다시 묶으므로, 캐릭터와 카메라가 멈춰 화면 위치가 그대로면 쓰지 않는다.
            Vector3 screen = camera.WorldToScreenPoint(_followPoint);
            if (screen != _shownScreen)
            {
                _hud._followRoot.position = screen;
                _shownScreen = screen;
            }
            // 회복이 끝나 가득 찬 게이지는 보이지 않게 한다.
            _hud._staminaRoot.gameObject.SetActive(visible && _hud._staminaFill.fillAmount < _fullFillAmount);
        }
    }
}
