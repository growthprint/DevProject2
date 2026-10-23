#nullable enable annotations

using System;
using System.Collections.Generic;
using DuckCharacter.AvatarParts;
using Game.Core;

namespace DuckCharacter
{
    // 한 캐릭터의 외형 표시와 장비 표시를 소유하고, 만들 때부터 정리할 때까지 그 변경을 조정한다.
    public sealed class DuckCharacterInstance : IDisposable
    {
        // 하위 객체를 공개하지 않아 생성과 정리 책임이 호스트로 흩어지지 않게 한다.
        readonly DuckAvatarInstance _avatar;
        readonly DuckEquipmentVisuals _equipment;
        // 정리를 시작한 뒤에는 다시 쓰지 못하게 막는다.
        bool _disposed;
        // 정리 완료를 시작과 구분해 실패한 정리를 반복 Dispose로 재시도하게 하고, _mutating은 콜백 안의 변경 재진입을 막는다.
        bool _disposeCompleted;
        bool _mutating;

        // 비활성 씬의 사전 준비를 허용하면서 파괴되거나 정리된 인스턴스는 거부한다. 리그는 외형이 들고 있는 참조를 본다.
        public bool IsAlive => !_disposed && _avatar.Rig != null;

        // 외형 라이브러리는 적용 시에만 받으며 생성자는 수명에 고정된 리그와 장비 카탈로그만 받는다.
        public DuckCharacterInstance(DuckCharacterRig rig,
            DuckEquipParts? equipmentCatalog = null)
            : this(equipmentCatalog, new DuckAvatarInstance(rig)) { }

        // 공개 생성자가 위임하는 초기화다. 리그는 외형이 수명 동안 고정해 들고 있어 따로 받지 않는다. 장비 표시 생성은 이 소유자가
        // 만든 외형만 받아 실패하지 않으므로 예외 경계가 필요 없다. 종료와 재시도 테스트는 이 생성자를 리플렉션으로 불러 테스트용 외형을 넣는다.
        DuckCharacterInstance(DuckEquipParts? equipmentCatalog, DuckAvatarInstance avatar)
        {
            _avatar = avatar;
            _equipment = new DuckEquipmentVisuals(equipmentCatalog, _avatar);
        }

        // 결과는 적용 성공과 정리 경고를 구분한다. 재진입했거나, 정리됐거나, 리그가 파괴됐으면 던지지 않고
        // 아무것도 바꾸기 전에 기록 없이 거부 결과를 돌려준다.
        public DuckAvatarApplyResult ApplyAppearance(DuckAvatarPartLibrary avatarPartLibrary, DuckAvatarPreset appearance)
        {
            if (!TryBeginMutation()) return DuckAvatarApplyResult.Reject();
            try
            {
                if (!IsAlive) return DuckAvatarApplyResult.Reject();
                return _avatar.Apply(avatarPartLibrary, appearance);
            }
            finally { _mutating = false; }
        }

        // 장비 표시만 바꾸고 아이템 소유권이나 게임 세션은 건드리지 않는다.
        public bool TrySetEquipment(EquipPartType slot, int id)
        {
            // 외형 활성화나 정리 콜백 안에서, 또는 정리된 뒤에는 장비와 가림 상태를 바꾸지 못하게 기록 없이 거부한다.
            if (!TryBeginMutation()) return false;
            try
            {
                if (!IsAlive) return false;
                return _equipment.TrySetEquipment(slot, id);
            }
            finally { _mutating = false; }
        }

        // 리그가 사라져 새 장착은 못 해도 소유한 슬롯 표시는 정리하므로 IsAlive 대신 정리 여부만 본다.
        // 재진입은 기록 없이 거부하고, 정리 뒤에는 정리할 표시가 없으므로 둘 다 던지지 않고 false를 돌려준다.
        public bool TryClearEquipment(EquipPartType slot)
        {
            if (!TryBeginMutation()) return false;
            try
            {
                if (_disposed) return false;
                return _equipment.TrySetEquipment(slot, -1);
            }
            finally { _mutating = false; }
        }

        // 실제 적용된 외형의 복사본만 반환하여 저장값과 표시값의 일치를 확인하게 한다.
        public DuckAvatarPreset? SnapshotAppearance() => IsAlive ? _avatar.Snapshot() : null;

        // 정리 단계를 하나씩 따로 시도해 한 단계의 실패가 나머지를 막지 않는다. 실패가 남으면 다음 Dispose가 남은 자원을 다시
        // 정리한다. 콜백 안에서 다시 부른 Dispose는 무시하고 바깥 Dispose가 정리를 이어 간다. 외형 정리가 모은 해제 실패는 여기서 기록한다.
        public void Dispose()
        {
            if (!TryBeginMutation()) return;
            try
            {
                if (_disposeCompleted) return;
                _disposed = true;
                // 외형이 들고 있는 리그를 정리 문맥으로 쓴다. 이미 파괴된 리그도 문맥으로는 넘길 수 있다.
                DuckCharacterRig rig = _avatar.Rig;
                bool completed = Cleanup.Run(_equipment.Clear, rig);
                var avatarFailures = new List<Exception>();
                completed &= Cleanup.Run(() => _avatar.Clear(avatarFailures), rig);
                foreach (Exception failure in avatarFailures) Log.Instance.Exception(failure, rig);
                completed &= avatarFailures.Count == 0;
                completed &= Cleanup.Run(() => { if (rig != null) rig.Animation.Restore(); }, rig);
                _disposeCompleted = completed;
            }
            finally { _mutating = false; }
        }

        // 모든 변경 진입점이 상태를 바꾸기 전에 부른다. 외형 적용과 정리는 이 잠금 안에서만 불리므로 소유자 잠금만 보고,
        // 변경 중이면 던지지 않고 기록 없이 false를 돌려준다. 정리 여부는 호출부가 IsAlive나 _disposed로 따로 본다.
        bool TryBeginMutation()
        {
            if (_mutating) return false;
            _mutating = true;
            return true;
        }
    }
}
