#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuckCharacter
{
    // 목록이 파츠 프리팹을 직접 담아 id의 출처를 프리팹 하나로 두므로 따로 적은 id와 어긋나지 않는다.
    // 파츠를 앞뒤로 넘기는 순환은 CustomizationScreen이, 무작위 선택은 부위가 아니라 프리셋 단위로 편집 세션의 ApplyRandomPreset이 맡는다.
    /// <summary>한 카테고리(머리깃, 눈, 부리 등)의 파츠 프리팹 목록과 id 유효성 판정이다.</summary>
    [Serializable]
    public class DuckAvatarPartCategory
    {
        [SerializeField]
        List<DuckAvatarPart> _avatarParts = new List<DuckAvatarPart>();

        // 직렬화 목록은 초기값이 있고 Unity와 제작 도구가 null로 두지 않으므로 목록 자체의 null은 검사하지 않는다.
        public DuckAvatarPart? Get(int id)
        {
            for (int i = 0; i < _avatarParts.Count; i++)
            {
                DuckAvatarPart avatarPart = _avatarParts[i];
                if (avatarPart != null && avatarPart.Id == id)
                    return avatarPart;
            }

            return null;
        }

        // 판정마다 정렬 목록을 만들지 않도록 할당 없는 Get과 같은 조건으로 찾는다. 프리팹 참조가 끊어진 줄은 Get이 건너뛴다.
        public bool Contains(int id) => Get(id) != null;

        // 화면과 Sanitize는 Count와 인덱스로만 읽으므로, 호출마다 새로 만든 정렬 목록을 다시 복사하지 않고 돌려준다.
        /// <summary>
        /// null 이 아닌 프리팹의 id 를 오름차순으로 돌려준다(UI 목록용). 호출마다 새 목록을 만들고, 비면 공유 빈 배열이다.
        /// 무효화 경로가 없는 캐시는 도메인 리로드를 끈 Play Mode 에서 낡은 값이 남으므로 두지 않는다.
        /// </summary>
        public IReadOnlyList<int> AllIDs()
        {
            if (_avatarParts.Count == 0)
                return Array.Empty<int>();

            List<int> ids = new List<int>(_avatarParts.Count);
            for (int i = 0; i < _avatarParts.Count; i++)
            {
                DuckAvatarPart avatarPart = _avatarParts[i];

                if (avatarPart != null)
                    ids.Add(avatarPart.Id);
            }

            ids.Sort();
            return ids;
        }


        // TODO : 수정할 것 : Sanitize

        /// <summary>목록에 없는 id 가 들어오면 첫 번째 유효 id 로 보정한다. 후보가 없으면 그대로 둔다.</summary>
        public int Sanitize(int id)
        {
            // 목록에 있는 id는 할당 없는 Contains로 바로 돌려주고, 없을 때만 정렬 목록을 한 번 만든다.
            if (Contains(id))
                return id;

            IReadOnlyList<int> ids = AllIDs();
            return ids.Count > 0 ? ids[0] : id;
        }
    }
}
