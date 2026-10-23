#nullable enable annotations

using System.Collections.Generic;
using Character;
using DuckCharacter.AvatarParts;
using UnityEngine;

namespace DuckCharacter
{
    // 장비 id는 목록 전체에서 유일해야 한다. 커스터마이징 파츠 id는 카테고리 안에서만 유일하면 된다.
    /// <summary>장비 프리팹 루트의 컴포넌트다. 파츠와 공통인 id, 이름, 렌더러 표시는 <see cref="DuckPart"/>에 있고 여기는 장비 전용 값만 둔다.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Duck Character/Duck Equip Part")]
    public class DuckEquipPart : DuckPart
    {
        [Tooltip("이 장비가 들어가는 슬롯. 프리팹에 구워 둔다.")]
        [SerializeField] EquipPartType _equipSlotType;

        // DuckAvatarPreset 필드는 낡은 데이터가 기본값을 물려받아 손상 검사를 지나칠 수 있어 초기값을 금지한다.
        // 이 필드에는 그 위험이 해당하지 않으므로 초기값을 둔다.
        [Tooltip("스킨드 장비를 캐릭터 본에 다시 묶는 값.")]
        [SerializeField] DuckEquipPartBinding _binding = new DuckEquipPartBinding();

        [Tooltip("착용하는 동안 숨길 커스터마이징 파츠.")]
        [SerializeField] DuckAvatarPartType[]? _hiddenAvatarParts;

        public EquipPartType Slot { get { return _equipSlotType; } }

        /// <summary>본에 다시 묶어야 하는 스킨드 장비인지.</summary>
        public bool IsSkinned { get { return _binding.IsSkinned; } }

        /// <summary>착용 중 숨길 파츠 카테고리. 반환된 배열을 고치지 않는다.</summary>
        public DuckAvatarPartType[]? HiddenAvatarParts { get { return _hiddenAvatarParts; } }

        // 바인딩 실패는 DuckEquipPartBinding.TryBind가 이 장비 이름을 붙여 보고하므로 호출자는 다시 기록하지 않는다.
        /// <summary>이 장비의 렌더러를 캐릭터 본에 묶는다. 스킨드 장비가 아니면 아무것도 하지 않고 true를 돌려준다.</summary>
        public bool TryBind(Dictionary<string, Transform> bones) => _binding.TryBind(bones, this);
    }
}
