#nullable enable annotations
using DuckCharacter;
using DuckCharacter.AvatarParts;
using UnityEngine;

namespace Game.App
{
    // 적 하나의 원본 얼굴과 무기 표시 수명을 플레이어와 같은 캐릭터 소유 API로 관리한다.
    // 무기 표시와 소켓 계약은 전용 리그 모델과 함께 쓰도록 OriginalEnemyVisual로 옮겼다. 여기는 오리 리그의 얼굴 적용과 소켓만 맡는다.
    public sealed class OriginalEnemyAppearance : OriginalEnemyVisual
    {
        [SerializeField] DuckCharacterRig _rig=null!;
        [SerializeField] DuckAvatarPartLibrary _library=null!;
        [SerializeField] DuckAvatarPreset _preset=null!;
        DuckCharacterInstance? _instance;
        // 조준 Collider는 원본 Helmet 소켓을 따라간다. 리그는 적 프리팹에 저작한 연결이라 늘 있다.
        internal override Transform? HeadSocket => _rig.GetEquipmentSocket(EquipPartType.Helmet);
        // 원본 CharacterMainControl.RightHandSocket처럼 무기를 쥔 손 소켓이다. 근접 공격 예고 효과가 이 소켓을 따라간다.
        internal override Transform? RightHandSocket => _rig.GetEquipmentSocket(EquipPartType.RightHand);

        // 적 컨트롤러 준비가 한 번만 부른다. 리그, 부위 라이브러리, 외형 프리셋은 적 프리팹에 저작한 연결이고 프리셋의 부위 ID는
        // 모두 라이브러리에 있다. 외형 적용이 거부하면 그쪽이 원인을 보고하므로 호출자는 다시 기록하지 않는다.
        // 만든 표시 후보는 예외를 포함해 성공하지 못한 모든 경로에서 finally가 정리한다.
        internal override bool TryPrepare(OriginalItemDefinition weapon)
        {
            var instance = new DuckCharacterInstance(_rig);
            bool prepared = false;
            try
            {
                var result = instance.ApplyAppearance(_library, _preset);
                // 외형 적용이 거부 원인을 직접 보고하므로 여기서는 false만 돌려준다.
                if (!result.IsApplied)
                    return false;
                // 적용 뒤 정리 경고는 오류가 아니므로 다른 외형 호스트와 같은 공용 헬퍼로 Warning 한 번만 보고한다.
                result.ReportCleanupWarnings(this, "적 외형 적용 완료 후 정리 경고");
                // 표시 프리팹이 있는 무기만 오른손 소켓에 표시를 만든다. 생성 규칙은 전용 리그와 같은 공통 계약이 맡는다.
                CreateWeapon(weapon);
                _instance = instance;
                prepared = true;
                return true;
            }
            finally { if (!prepared) instance.Dispose(); }
        }
        // 씬 해제 시 동적으로 생성한 얼굴과 무기의 소유권을 명시적으로 해제한다. 무기 표시는 공통 계약이 먼저 해제한다.
        private protected override void OnDestroy()
        {
            base.OnDestroy();
            _instance?.Dispose();_instance=null;
        }
    }
}
