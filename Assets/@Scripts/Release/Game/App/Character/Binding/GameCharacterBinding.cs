#nullable enable annotations

using System;
using System.Collections.Generic;
using DuckCharacter;
using DuckCharacter.AvatarParts;
using UnityEngine;

namespace Game.App
{
    // 넘겨받은 외형을 캐릭터로 보여 주고 표시 객체의 수명을 관리한다. 게임 명령, 세션 상태, 외형 변경 구독은 갖지 않고
    // 언제 적용할지는 SceneContext가 정한다. 몸에 보이는 장비는 전투 월드가 세션 장비 칸을 보고 TrySetEquipment로 바꾼다.
    [DisallowMultipleComponent]
    public sealed class GameCharacterBinding : MonoBehaviour
    {
        // SceneContext가 GameplayDataSettings.CustomFaceData에서 읽어 Prepare로 넘긴 앱 수명 외형 라이브러리다. 재적용에 쓰고 Clear에서 놓는다.
        // 직렬화하지 않으며 PlayMode 테스트는 리플렉션으로 설정한다.
        DuckAvatarPartLibrary? _library;
        // 프리팹에 직렬화된 캐릭터 리그와 장비 프리팹 목록이다. 장비 목록은 DuckCharacterInstance 생성에 필요하다.
        [SerializeField] DuckCharacterRig _rig = null!;
        [SerializeField] DuckEquipParts _equipmentCatalog = null!;
        // 바인딩이 만들고 수명을 소유하는 캐릭터 표시 객체다.
        DuckCharacterInstance? _character;

        // 제작, 미리보기, 검사 도구에 표시 구성 참조만 공개한다.
        public DuckCharacterRig Rig => _rig;
        public DuckEquipParts EquipmentCatalog => _equipmentCatalog;
        // 외형 표시가 실패했는지 나타내며, 재시도로 다시 적용할 수 있다. 원인은 발견한 곳이 기록하므로 여부만 둔다.
        // SceneContext와 UI가 읽으므로 공개로 둔다.
        public bool PresentationFailed { get; private set; }
        // 마지막 외형 적용 뒤 남은 정리 경고다. 거부 결과에서는 비어 있고 표시 실패 UI에는 쓰지 않는다.
        public IReadOnlyList<Exception> AvatarCleanupErrors { get; private set; } = Array.Empty<Exception>();

        // 레벨 초기화가 넘긴 외형으로 캐릭터를 만든다. 외형 적용 실패는 레벨 초기화 실패로 전파한다.
        // 정리 경고만 남은 적용은 성공이며 재적용으로 다시 시도할 수 있다.
        public void Prepare(DuckAvatarPreset appearance, DuckAvatarPartLibrary library)
        {
            // 레벨 초기화는 메인 스레드에서만 부르므로 스레드 검사를 두지 않는다.
            _library = library;
            var character = new DuckCharacterInstance(_rig, _equipmentCatalog);
            try
            {
                // 정리 경고는 준비를 실패로 만들지 않는다. 보관하고 기록만 한다.
                DuckAvatarApplyResult result = character.ApplyAppearance(_library, appearance);
                ObserveCleanup(result);
                // 외형 거부는 던져서 레벨 초기화를 실패로 끝낸다. 원인은 외형 적용이 발견한 곳에서 기록하므로 고정 문구를 쓴다.
                if (!result.IsApplied)
                    throw new InvalidOperationException("저장 외형을 표시하지 못했습니다.");
                // 여기까지 왔으면 외형이 적용된 것이라 표시 실패를 내린다.
                PresentationFailed = false;
                _character = character;
            }
            // 예외 경계: 준비가 실패하면 만든 캐릭터의 Unity 객체를 정리하고 같은 예외를 다시 던진다.
            catch (Exception)
            {
                character.Dispose();
                throw;
            }
        }

        // 외형 변경 알림과 표시 재시도가 함께 쓰며, 부를 때마다 다시 적용한다. 호출자 SceneContext는 Prepare가 성공해 캐릭터와
        // 라이브러리가 채워진 레벨에서만 부르므로 둘을 다시 보지 않는다. 외형 실패 원인은 발견한 곳이 기록하고 여기서는 PresentationFailed만
        // 켠다. 정리 오류는 성공 판정에 넣지 않고 AvatarCleanupErrors에 둔다.
        public bool TryApply(DuckAvatarPreset appearance)
        {
            DuckAvatarApplyResult result = _character!.ApplyAppearance(_library!, appearance);
            // 게임 씬은 같은 객체에 피격 표시를 붙인다. 외형 적용이 파츠를 바꿨을 수 있으니 다음 피격 때 렌더러를 다시 모으게 한다.
            if (TryGetComponent(out OriginalHurtVisual hurt)) hurt.MarkRenderersChanged();
            ObserveCleanup(result);
            bool applied = result.IsApplied;
            PresentationFailed = !applied;
            return applied;
        }

        // 장비 칸이 바뀌면 전투 월드가 그 칸의 장비 외형을 바꾼다. id -1은 비운다. 표시만 바꾸고 아이템 소유는 건드리지 않는다.
        // 캐릭터가 없거나 바꾸는 중이면 기록 없이 false이고, 장비 외형 누락 같은 결함은 장비 표시가 발견한 곳에서 보고한다.
        internal bool TrySetEquipment(EquipPartType slot, int id)
        {
            if (_character == null || !_character.TrySetEquipment(slot, id)) return false;
            // 장비 파츠가 렌더러를 더하거나 뺐을 수 있으니 다음 피격 표시가 렌더러를 다시 모으게 한다.
            if (TryGetComponent(out OriginalHurtVisual hurt)) hurt.MarkRenderersChanged();
            return true;
        }

        // 게임 상태는 건드리지 않고 바인딩이 만든 캐릭터와 표시 상태만 버리며, 던지지 않는다.
        // 정리 중 생긴 실패는 DuckCharacterInstance.Dispose가 실패마다 한 번씩 기록한다.
        public void Clear()
        {
            _character?.Dispose();
            // Dispose가 던지지 않으므로 표시 상태와 소유자 참조를 항상 비운다. 정리에 실패한 파츠는 호출자가 이어서 파괴하는
            // 캐릭터 계층 아래에 남는다.
            PresentationFailed = false;
            AvatarCleanupErrors = Array.Empty<Exception>();
            _character = null;
            // 라이브러리는 앱 수명 게임플레이 데이터라 이 바인딩이 소유하지 않으므로 참조만 놓는다.
            _library = null;
        }

        // 정리 오류를 보관하고 이 컴포넌트를 로그 문맥으로 한 번 기록한다. 제품 실패 UI에는 띄우지 않는다.
        void ObserveCleanup(DuckAvatarApplyResult result)
        {
            AvatarCleanupErrors = result.CleanupErrors;
            result.ReportCleanupWarnings(this, "외형 적용 완료 후 정리 경고");
        }

        // 씬이나 프리팹이 파괴될 때도 소유한 파츠를 정리한다.
        void OnDestroy() => Clear();
    }
}
