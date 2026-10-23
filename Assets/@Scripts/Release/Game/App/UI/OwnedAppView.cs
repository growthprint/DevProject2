#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 앱 구성이 한 번 로드해 앱이 끝날 때까지 쓰고, 씬 소유자 하나가 한 번에 붙여 구동하는 공용 View의 소유 규칙이다.
    // 붙이고 뗄 때마다 갓 만든 상태로 되돌리고, 소유자가 없으면 숨긴다. 공용 HUD와 게임플레이 표시가 함께 쓴다.
    public abstract class OwnedAppView : MonoBehaviour
    {
        // 지금 이 View를 구동하는 씬 소유자다. 소유자 비교 규칙은 ViewOwnership이 맡는다.
        readonly ViewOwnership _ownership = new();

        // 숨긴 View가 루트를 켜 둔 채 프레임 갱신을 건너뛰도록 소유자가 있는지 알린다.
        private protected bool Attached => _ownership.HasOwner;

        // 레벨 초기화를 마친 씬 소유자가 자신을 붙인다. 소유자를 바꾼 뒤 앞 소유자가 남긴 상태를 지우고 보인다.
        internal void Attach(UnityEngine.Object owner)
        {
            _ownership.Attach(owner);
            ResetState();
            SetShown(true);
        }

        // 앱 구성이 첫 소유자가 붙기 전까지 숨긴다. 숨기는 방법은 SetShown이 View마다 정한다.
        internal void HideUntilAttached() => SetShown(false);

        // 현재 소유자만 뗄 수 있고, 이미 뗀 씬의 OnDisable이나 OnDestroy 같은 다른 호출은 아무것도 바꾸지 않는다.
        // 되돌리기가 그 자리에서 실행하는 앞 소유자의 비동기 연속이 이미 뗀 상태를 보도록 소유자를 먼저 비운다.
        internal void Detach(UnityEngine.Object owner)
        {
            if (!_ownership.TryDetach(owner)) return;
            ResetState();
            SetShown(false);
        }

        // 소유자가 없을 때 null을 넘겨도 붙은 것으로 답하지 않는다.
        internal bool IsAttachedTo(UnityEngine.Object owner) => _ownership.IsAttachedTo(owner);

        // 붙이고 뗄 때 이 View의 씬별 상태를 갓 만든 상태로 되돌린다.
        private protected abstract void ResetState();

        // 작은 View는 루트를 끄고 켠다. 큰 View는 재정의해 루트를 켜 둔 채 그리기와 입력만 끄고 켠다.
        private protected virtual void SetShown(bool shown) => gameObject.SetActive(shown);
    }
}
