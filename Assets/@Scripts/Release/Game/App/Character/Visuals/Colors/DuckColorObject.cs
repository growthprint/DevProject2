#nullable enable

using System;
using UnityEngine;

namespace DuckCharacter.AvatarParts
{

    /// 색 컬러 하나. 커스터마이징 화면의 색 버튼 한 칸에 대응한다.
    [Serializable]
    public class DuckColorObject
    {
        // 제작 도구와 테스트가 이름으로 읽고 쓰는 직렬화 필드다. Release 코드는 읽지 않으므로 CS0414 경고를 이 필드에서만 끈다.
#pragma warning disable CS0414
        [SerializeField]
        private string _displayName = "";
#pragma warning restore CS0414

        [ColorUsage(false)]
        [SerializeField]
        private Color _color = Color.white;

        public DuckColorObject() { }

        // Release에는 Editor나 테스트 전용 멤버를 두지 않는다. CustomizationOriginalDataAuthoring과 테스트는
        // 직렬화 필드 _displayName과 _color를 SerializedProperty나 리플렉션으로 읽고 쓴다.
        public Color Color { get { return _color; } }
    }
}
