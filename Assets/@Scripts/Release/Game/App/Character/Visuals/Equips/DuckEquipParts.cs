#nullable enable annotations

using System.Collections.Generic;
using UnityEngine;

namespace DuckCharacter
{
    // 외형 파츠는 세이브에 남고 장비는 인벤토리 상태에 따라 갈아 끼우므로 라이브러리를 따로 둔다. 아이템 소유권과 내구도는 게임 계층이 맡는다.
    /// <summary>슬롯별 장비 프리팹 목록. Editor 제작 도구가 채우고 DuckEquipmentVisuals가 조회 결과를 장착한다.</summary>
    public class DuckEquipParts : MonoBehaviour
    {
        [Tooltip("등록할 장비 프리팹의 루트 DuckEquipPart. id 는 목록 전체에서 유일해야 한다.")]
        [SerializeField] List<DuckEquipPart> _equipParts = new List<DuckEquipPart>();

        // 장비를 갈아 끼울 때마다 쓰는 id 색인이다. [NonSerialized]라 도메인 리로드 뒤 null이 되면 Find가 다시 만든다.
        [System.NonSerialized]
        Dictionary<int, DuckEquipPart>? _byId = null;

        /// <summary>id로 장비를 찾는다. 등록되지 않았거나 음수면 null이다.</summary>
        public DuckEquipPart? Find(int id)
        {
            if (_byId == null)
                BuildIndex();

            DuckEquipPart item;
            return _byId!.TryGetValue(id, out item) ? item : null;
        }

        // 이 클래스를 상속하는 형식이 없어 색인 만들기는 Find만 부른다.
        void BuildIndex()
        {
            // 직렬화 목록은 필드 초기값이 있고 아무 코드도 null을 넣지 않으므로 크기를 바로 읽는다.
            _byId = new Dictionary<int, DuckEquipPart>(_equipParts.Count);
            for (int i = 0; i < _equipParts.Count; i++)
            {
                DuckEquipPart item = _equipParts[i];
                if (item == null)
                    continue;

                // 음수 ID 제작 오류는 런타임 색인에서 안전하게 제외하고 Editor 검증에서 보고한다.
                if (item.Id < 0)
                    continue;

                // 중복 ID는 첫 항목을 유지하고 상세 원인은 Editor 검증에서 에셋 경로와 함께 보고한다.
                _byId.TryAdd(item.Id, item);
            }
        }
    }
}
