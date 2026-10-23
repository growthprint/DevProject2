#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuckCharacter.AvatarParts
{
    // 원본 CustomFaceUI는 피부색과 모든 부위가 154색 목록 하나를 함께 쓴다.
    /// <summary>커스터마이징 화면의 색 목록이다. DuckAvatarPartLibrary 에셋 안에 함께 저장한다.</summary>
    [Serializable]
    public class DuckAvatarColorPalette
    {
        // 인스펙터 안내도 목록이 하나뿐인 원본 구조를 따른다.
        [Header("공통"), Tooltip("피부색과 모든 부위가 함께 쓰는 컬러들.")]
        [SerializeField]
        List<DuckColorObject> _common = new List<DuckColorObject>();

        // _common은 직렬화 목록이라 null이 되지 않으므로 대체 배열 없이 null 아님으로 돌려준다.
        public IReadOnlyList<DuckColorObject> Common
        {
            get { return _common; }
        }
    }
}
