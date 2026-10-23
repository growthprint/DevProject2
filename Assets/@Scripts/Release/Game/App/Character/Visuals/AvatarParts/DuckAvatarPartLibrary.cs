#nullable enable annotations

using System.Collections.Generic;
using DuckCharacter.AvatarParts;
using UnityEngine;

namespace DuckCharacter
{
    /// <summary>
    /// 카테고리별 id → 파츠 프리팹 매핑 테이블. 원본의 CustomFaceData 와 같은 역할이다.
    /// 커스터마이징 화면은 이 에셋의 카테고리 · 팔레트 · 프리셋으로 목록을 그린다.
    /// </summary>
    [CreateAssetMenu(fileName = "DuckAvatarPartLibrary", menuName = "Duck Character/Avatar Part Library", order = 0)]
    public class DuckAvatarPartLibrary : ScriptableObject
    {
        const int _categoryCount = (int)DuckAvatarPartType.Count;

        [SerializeField]
        DuckAvatarPartCategory[]? _categories;

        // 원작 CustomFaceData.defaultPreset처럼 새 게임의 기본 외형이다. 시작 검증이 값을 확인한다.
        [SerializeField, Header("기본값"), Tooltip("새 게임을 시작할 때의 기본 외형. 원작 CustomFaceData.defaultPreset에 해당한다.")]
        DuckAvatarPreset? _defaultPreset;

        // 새 게임 세션이 읽는 기본 외형이다. 라이브러리 에셋의 값이라 읽는 쪽은 고치지 않고 Clone한 사본을 쓴다.
        public DuckAvatarPreset? DefaultPreset => _defaultPreset;

        // 원본 DATA_3/YellowDuck 버튼이 쓰는 프리셋 목록으로, 원본 CustomFaceUI.presets에 해당한다.
        // 제품 화면에서는 그 버튼의 ApplyRandomPreset만 이 목록을 읽는다.
        [SerializeField, Tooltip("원본 YellowDuck 버튼의 프리셋 목록(원본은 옐로우 덕 하나). 항목을 더하면 버튼이 그중에서 무작위로 고른다.")]
        List<DuckAvatarPreset> _presets = new List<DuckAvatarPreset>();

        // 원본처럼 피부색과 모든 부위가 함께 쓰는 색 목록 하나를 라이브러리 안에 직렬화한다.
        [SerializeField, Tooltip("커스터마이징 화면이 쓰는 색 컬러 표.")]
        DuckAvatarColorPalette? _palette;

        public DuckAvatarColorPalette? Palette { get { return _palette; } }

        // 직렬화 목록은 초기값이 있고 Unity와 제작 도구가 null로 두지 않으므로 그대로 돌려준다.
        public IReadOnlyList<DuckAvatarPreset> Presets
        {
            get { return _presets; }
        }

        // 빈 칸을 채워 돌려주므로 범위 안 부위에는 늘 카테고리가 있다. 호출자는 null을 검사하지 않는다.
        /// <summary>
        /// enum → 카테고리 매핑. 프로젝트에서 이 매핑을 하는 곳은 여기 하나뿐이다. 호출자는 범위 안 부위만 넘긴다.
        /// 카테고리를 추가하려면 DuckAvatarPartType 에 값을 넣고 Count 를 올리면 된다.
        /// </summary>
        public DuckAvatarPartCategory GetCategory(DuckAvatarPartType type)
        {
            EnsureCategories();

            DuckAvatarPartCategory c = _categories![(int)type];
            if (c == null) _categories[(int)type] = c = new DuckAvatarPartCategory();
            return c;
        }

        // 사용자 저장인 DuckAvatarPreset은 배열 길이가 어긋나면 손상으로 거부하지만, 라이브러리는 에디터 도구가 프리팹으로 다시 채우는
        // 제작 산출물이라 빈 칸을 채워도 잃는 것이 없다. 부위마다 프리팹이 등록됐는지는 라이브러리 에셋을 검사하는 EditMode 테스트가 확인한다.
        /// <summary>배열이 없거나 길이가 어긋나면 이미 있는 항목은 두고 빈 칸만 빈 카테고리로 조용히 채운다.</summary>
        void EnsureCategories()
        {
            if (_categories != null && _categories.Length == _categoryCount)
                return;

            DuckAvatarPartCategory[] category = new DuckAvatarPartCategory[_categoryCount];
            for (int i = 0; i < _categoryCount; i++)
            {
                category[i] = (_categories != null && i < _categories.Length && _categories[i] != null)
                    ? _categories[i]
                    : new DuckAvatarPartCategory();
            }

            _categories = category;
        }

        // GetCategory가 범위 안 부위에 늘 카테고리를 돌려주므로 null을 다시 보지 않는다.
        public DuckAvatarPart? Get(DuckAvatarPartType type, int id) => GetCategory(type).Get(id);

        /// <summary>
        /// 외형에 들어 있는 id 가 전부 라이브러리에 있는지 확인하고, 없으면 그 카테고리의 가장 작은 유효 id 로 보정한다.
        /// 후보가 하나도 없는 카테고리는 원래 값을 그대로 둔다.
        /// </summary>
        public void Sanitize(DuckAvatarPreset avatarParts)
        {
            for (DuckAvatarPartType type = 0; type < DuckAvatarPartType.Count; type++)
                avatarParts.SetID(type, GetCategory(type).Sanitize(avatarParts.GetID(type)));
        }

    }
}
