#nullable enable annotations

using UnityEngine;

namespace Character
{
    // 값은 Editor 빌더가 프리팹에 굽고 런타임에 바뀌지 않으므로 읽기 전용으로만 노출한다. 카테고리 enum은 파츠와 장비가
    // 달라 파생 클래스가 각자 든다. 세 필드의 [SerializeField]가 빠지면 프리팹에 구워지지 않아 id 0과 빈 렌더러로 조용히 동작한다.
    /// <summary>파츠(DuckAvatarPart)와 장비(DuckEquipPart) 프리팹 루트에 붙는 컴포넌트의 공통 기반이다.</summary>
    public abstract class DuckPart : MonoBehaviour
    {
        [SerializeField] protected int _id;

        [SerializeField] protected string? _displayName = "";

        [SerializeField] protected Renderer[] _renderers = new Renderer[0];

        public int Id { get { return _id; } }

        public string DisplayName { get { return string.IsNullOrEmpty(_displayName) ? name : _displayName; } }

        // Release에는 Editor나 테스트 전용 멤버를 두지 않는다. EquipmentBakeValidation과 테스트는
        // 직렬화 필드 _renderers를 SerializedObject나 리플렉션으로 읽는다.
        public void SetVisible(bool visible)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                    _renderers[i].enabled = visible;
            }
        }
    }
}
