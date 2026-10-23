#nullable enable annotations

namespace Game.App
{
    // 공용 HUD와 게임플레이 표시 같은 앱 수명 View를 한 번에 한 씬 소유자만 구동하도록 소유자를 보관한다.
    // 이미 파괴된 소유자도 자기 표시를 뗄 수 있도록, 소유자 비교는 파괴 검사 없는 참조 동일성으로만 한다.
    internal sealed class ViewOwnership
    {
        UnityEngine.Object? _owner;

        internal bool HasOwner => !ReferenceEquals(_owner, null);

        // 레벨 초기화 경로만 자신을 소유자로 넘기고 앞 레벨은 언로드 때 먼저 떼므로 소유자를 바로 바꾼다.
        internal void Attach(UnityEngine.Object owner) => _owner = owner;

        // 현재 소유자만 뗄 수 있다. 이미 뗀 씬의 해제나 파괴 중 호출은 false를 돌려주고 아무것도 바꾸지 않는다.
        internal bool TryDetach(UnityEngine.Object owner)
        {
            if (!HasOwner || !ReferenceEquals(_owner, owner)) return false;
            _owner = null;
            return true;
        }

        // 소유자가 없을 때 null을 넘겨도 붙은 것으로 답하지 않는다.
        internal bool IsAttachedTo(UnityEngine.Object owner) => !ReferenceEquals(owner, null) && ReferenceEquals(_owner, owner);
    }
}
