#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 적 모델의 공통 표시 계약이다. 오리 리그 외형(OriginalEnemyAppearance)과 전용 리그 모델(OriginalDedicatedRigModel)이
    // 적 컨트롤러에 같은 소켓과 무기 표시 수명을 내주므로, 컨트롤러는 리그 종류를 몰라도 무기를 들고 쏘고 머리 조준 구를 붙인다.
    // 무기 표시의 생성, Muzzle 캐시, 반동, 숨김, 해제는 플레이어와 같은 WeaponVisual 규칙이다.
    public abstract class OriginalEnemyVisual : MonoBehaviour
    {
        WeaponVisual? _weapon;

        // 원작 CharacterModel.HelmatSocket이다. 머리 조준 구가 이 소켓을 따라간다. 없으면 머리 조준 구를 붙이지 않는다.
        internal abstract Transform? HeadSocket { get; }
        // 원작 CharacterModel.RightHandSocket이다. 무기 표시와 근접 공격 예고 효과가 이 소켓을 따라간다.
        internal abstract Transform? RightHandSocket { get; }
        // 발사 위치는 TryPrepare가 만든 무기 표시의 Muzzle이다. 준비가 실패해 무기 표시가 없으면 null이다.
        internal Transform? Muzzle => _weapon != null ? _weapon.Muzzle : null;

        // 원본 ItemAgent_Gun은 적이 쏠 때도 총 모델을 뒤로 밀었다 되돌린다.
        internal void KickWeapon() { if (_weapon != null) _weapon.Kick(); }
        internal void TickWeapon(float deltaTime) { if (_weapon != null) _weapon.Tick(deltaTime); }
        // 원본 ItemAgentHolder처럼 처음 무기를 드는 0.15초 동안 무기를 숨긴다.
        internal void SetWeaponVisible(bool visible) { if (_weapon != null) _weapon.SetVisible(visible); }

        // 적 컨트롤러 준비가 한 번만 부른다. 실패하면 구현이 원인을 보고하므로 호출자는 결과만 받는다.
        internal abstract bool TryPrepare(OriginalItemDefinition weapon);

        // 표시 프리팹이 있는 무기만 오른손 소켓에 표시를 만든다. 총 표시 프리팹의 Muzzle 소켓은 EditMode 테스트가 확인한다.
        private protected void CreateWeapon(OriginalItemDefinition weapon)
        {
            if (weapon.Visual != null) _weapon = WeaponVisual.Create(weapon.Visual, RightHandSocket);
        }

        // 씬 해제 시 동적으로 만든 무기 표시의 소유권을 명시적으로 해제한다. 파생은 자기 자원을 정리한 뒤 이 본문도 부른다.
        private protected virtual void OnDestroy()
        {
            if (_weapon != null) { _weapon.Dispose(); _weapon = null; }
        }
    }
}
