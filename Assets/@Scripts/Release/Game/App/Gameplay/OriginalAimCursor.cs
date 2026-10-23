#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 InputManager처럼 OS 포인터와 따로 관리하는 가상 조준 화면 위치와 그 반동, 마우스 보상 상태다.
    // 카메라, HUD 커서, 사격이 모두 이 한 좌표를 읽는다. 플레이어 모터가 하나를 가지고 조준 프레임마다 Advance로 진행한다.
    internal sealed class OriginalAimCursor
    {
        // 원본 반동과 마우스 보상 수치의 기준 화면 높이다. 실제 화면 높이에 비례해 수치를 늘리거나 줄인다.
        const float _referenceScreenHeight = 1440f;
        // 반동을 나눌 때 쓰는 조작 수치 하한이다. 0으로 나누는 것을 막는다.
        const float _minimumRecoilControl = .01f;
        // 반동과 다른 쪽으로 마우스를 이만큼(기준 화면 높이 1440의 픽셀) 움직이면 남은 반동을 버린다. 원본처럼 정수 곱으로 잰다.
        const int _recoilCancelDistance = 15;

        // 원본처럼 OS 포인터와 따로 관리하는 가상 조준 화면 위치다.
        Vector2 _position;
        // false면 다음 조준 프레임에 가상 조준 위치를 실제 포인터 위치로 다시 맞춘다.
        // 일시정지, 포커스 상실, 입력 억제 중에 false가 된다.
        bool _synchronized;
        // 원본 InputManager의 반동과 마우스 보상 상태다.
        Vector2 _recoilShot, _recoilRemaining;
        float _recoilElapsed, _recoilDuration, _recoilDelay, _recoilRecovery, _oppositeDelta;

        internal Vector2 Position => _position;

        // 설정 창, 전환, 입력 억제 중에 움직인 커서를 조준에 누적하지 않도록 다음 조준 프레임에 실제 포인터로 다시 맞춘다.
        internal void Desynchronize() => _synchronized = false;

        internal void ClearRecoil() { _recoilShot = _recoilRemaining = Vector2.zero; _recoilElapsed = _recoilDuration = _oppositeDelta = 0; }

        // 실제 발사 결과의 반동을 건다. 방향은 캐릭터 화면 위치에서 조준 위치 쪽이다.
        internal void AddRecoil(Vector2 characterScreen, OriginalItemDefinition weapon, float control, float difficulty)
        {
            Vector2 direction = (_position - characterScreen).normalized;
            float vertical = UnityEngine.Random.Range(weapon.Stat("RecoilVMin"), weapon.Stat("RecoilVMax")) * weapon.Stat("RecoilScaleV");
            float horizontal = UnityEngine.Random.Range(weapon.Stat("RecoilHMin"), weapon.Stat("RecoilHMax")) * weapon.Stat("RecoilScaleH");
            _recoilShot = (direction * vertical - Vector2.Perpendicular(direction) * horizontal) * difficulty / Mathf.Max(_minimumRecoilControl, control);
            // 반동 시간은 발사 간격을 넘지 않는다. 공격 속도 하한은 0으로 나누는 것을 막는다.
            _recoilDuration = Mathf.Min(weapon.Stat("RecoilTime"),
                1 / Mathf.Max(OriginalItemDefinition._minimumAttackSpeed, weapon.Stat(OriginalItemDefinition._shootSpeedStat)));
            _recoilDelay = weapon.Stat("RecoilRecoverTime"); _recoilRecovery = weapon.Stat("RecoilRecover"); _recoilElapsed = 0;
        }

        // 원본 InputManager처럼 맞춰진 동안은 Look 델타를 누적하고, 아니면 실제 포인터 위치로 다시 맞춘다. 같은 델타를 반동 보상에도 쓴다.
        // 실제 발사 결과가 누적한 반동을 다음 조준 프레임부터 적용하고, 커서 이동과 조준 광선이 화면 밖 좌표를 받지 않게 자른다.
        internal void Advance(Vector2 pointer, Vector2 look, float deltaTime)
        {
            if (!_synchronized)
            {
                _position = pointer;
                _synchronized = true;
            }
            else _position += look;
            ApplyRecoil(look, deltaTime);
            _position = ClampToScreen(_position);
        }

        // 원본 규칙대로 반동만큼 조준 위치를 밀고, 지연 뒤 되돌린다. 수치는 화면 높이 1440 기준이다.
        // 마우스를 반동 반대쪽으로 움직이면 남은 반동을 줄이고, 다른 쪽으로 일정 거리 이상 움직이면 남은 반동을 버린다.
        void ApplyRecoil(Vector2 mouseDelta, float deltaTime)
        {
            float step = Mathf.Min(deltaTime, _recoilDuration - _recoilElapsed);
            if (step > 0 && _recoilDuration > 0)
            {
                Vector2 change = _recoilShot * (step / _recoilDuration) * (Screen.height / _referenceScreenHeight);
                Vector2 before = _position;
                // 화면 끝에서 잘린 만큼은 되돌릴 반동에 넣지 않도록, 자른 뒤의 실제 이동량만 남은 반동에 더한다.
                _position = ClampToScreen(_position + change);
                _recoilRemaining += _position - before;
            }
            else if (_recoilElapsed > _recoilDelay)
            {
                float recoveryStep = Mathf.Min(deltaTime, _recoilElapsed - _recoilDelay);
                Vector2 next = Vector2.MoveTowards(_recoilRemaining,Vector2.zero,recoveryStep * _recoilRecovery * Screen.height / _referenceScreenHeight);
                _position += next - _recoilRemaining; _recoilRemaining = next;
            }
            float compensation = Vector2.Dot(-_recoilRemaining.normalized, mouseDelta);
            if (compensation > 0) { _oppositeDelta = 0; _recoilRemaining = Vector2.MoveTowards(_recoilRemaining,Vector2.zero,compensation); }
            else if ((_oppositeDelta += mouseDelta.magnitude) > _recoilCancelDistance * Screen.height / _referenceScreenHeight)
            { _oppositeDelta = 0; _recoilRemaining = Vector2.zero; }
            _recoilElapsed += deltaTime;
        }

        // 가상 조준 위치는 화면 밖으로 나가지 않는다. 반동 적용과 조준 누적이 같은 범위를 쓴다.
        static Vector2 ClampToScreen(Vector2 position) =>
            new Vector2(Mathf.Clamp(position.x, 0f, Screen.width), Mathf.Clamp(position.y, 0f, Screen.height));
    }
}
