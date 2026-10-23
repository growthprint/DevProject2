#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 AimMarker/NormalPanel/AimMarker처럼 조준점과 장전 원형을 함께 화면의 조준 위치로 옮기고, 팔 간격을 20 + 산포 × 5로 둔다.
    // 프리팹 참조는 생성자로 다시 받지 않고 SharedHudView의 직렬화 필드를 직접 읽는다.
    internal sealed class HudAimMarker
    {
        // 조준점 팔의 방향이다. 매 프레임 배열을 만들지 않도록 정적으로 한 번만 둔다.
        static readonly Vector2[] _crosshairDirections = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
        // 원본 AimMarker의 팔 간격 식 20 + 산포 × 5에 쓰는 두 값이다.
        const float _armBaseDistance = 20f, _armScatterScale = 5f;

        readonly SharedHudView _hud;
        // 마지막으로 쓴 조준 위치와 팔 간격이다. Transform 대입은 같은 값이어도 변경으로 처리되어 HUD 캔버스 전체를 다시 묶으므로,
        // 마우스와 산포가 그대로인 프레임에는 쓰지 않는다. NaN으로 시작해 첫 표시는 늘 쓴다.
        Vector2 _shownPoint = new(float.NaN, float.NaN);
        float _shownArmDistance = float.NaN;

        internal HudAimMarker(SharedHudView hud) => _hud = hud;

        internal void Show(Vector2 point, float scatter)
        {
            if (point != _shownPoint)
            {
                _hud._aimRoot.position = point;
                _shownPoint = point;
            }
            // 조준점 팔은 저작 때 방향 배열과 같은 네 개로 고정되므로 길이 상한을 두지 않는다. 간격은 산포에 비례해 벌어진다.
            float armDistance = _armBaseDistance + scatter * _armScatterScale;
            if (armDistance == _shownArmDistance) return;
            _shownArmDistance = armDistance;
            RectTransform[] arms = _hud._crosshairArms;
            for (int i = 0; i < arms.Length; i++) arms[i].anchoredPosition = _crosshairDirections[i] * armDistance;
        }
    }
}
