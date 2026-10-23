#nullable enable annotations

using System.Collections.Generic;
using DuckCharacter.AvatarParts;
using Game.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DuckCharacter
{
    // 캐릭터 인스턴스가 소유하는 장비 표시 객체다. 슬롯별 장비를 리그 소켓이나 본에 붙이고, 장비가 가리는 외형 부위를 숨긴다.
    // 인벤토리 규칙은 다루지 않는다.
    internal sealed class DuckEquipmentVisuals
    {
        // 장비 목록과 외형은 생성자에서 받아 빌려 쓰는 참조다. 이 객체가 파괴하지 않는다.
        readonly DuckEquipParts? _catalog;
        readonly DuckAvatarInstance _avatar;

        // 테스트 도우미 CharacterTestAccess.Equipment가 이 필드를 이름으로 읽는다. 이름을 바꾸면 그쪽도 함께 맞춘다.
        // 복제본은 프리팹과 같은 Id를 가지므로 어느 장비인지는 인스턴스의 Id로 안다.
        readonly DuckEquipPart?[] _instances = new DuckEquipPart?[(int)EquipPartType.EquipCount];
        // 같은 캐릭터 수명 동안 리그의 본 이름 사전을 재사용한다.
        Dictionary<string, Transform>? _bones;
        // 장비를 만들 비활성 준비 부모다. 캐릭터 수명 동안 재사용한다.
        Transform? _staging;

        // 장비 목록이 없으면 외형 전용 인스턴스다.
        internal DuckEquipmentVisuals(DuckEquipParts? partCatalog, DuckAvatarInstance targetAvatar)
        {
            _catalog = partCatalog;
            _avatar = targetAvatar!;
        }

        // 새 장비를 다 준비한 뒤에만 현재 장비와 교체해, 실패해도 착용 중인 장비가 그대로 보이게 한다. 바인딩 실패는 TryBind가
        // Log.Instance.Fail로 보고하고 false가 되므로 호출자는 다시 기록하지 않는다. 목록에 없는 id는 저작 결함이라 실패 결과에 넣지 않는다.
        public bool TrySetEquipment(EquipPartType slot, int id)
        {
            int index = (int)slot;

            if (id == -1)
            {
                // 비운 슬롯은 실제 null이라 가림 계산 없이 성공한다. 밖에서 파괴된 장비는 참조가 남아 있어
                // 아래에서 슬롯을 비우고 가림을 다시 계산한다.
                if (ReferenceEquals(_instances[index], null))
                    return true;
                RemoveAt(index);
                RefreshHiddenParts();
                return true;
            }

            // 제품 호출자는 장비 목록이 있는 캐릭터에 저작 카탈로그의 EquipmentId만 넘긴다. 그 id가 목록에 있는지는 EditMode 테스트가 확인한다.
            DuckEquipPart prefab = _catalog!.Find(id)!;

            // 같은 id의 장비가 살아 있으면 지금 인스턴스와 가림 상태를 그대로 둔다. 색인은 id마다 프리팹 하나만 두므로
            // 프리팹을 비교한 것과 같다.
            DuckEquipPart? current = _instances[index];
            if (current != null && current.Id == id)
                return true;

            DuckCharacterRig rig = _avatar.Rig;
            Transform? parent = prefab.IsSkinned
                // 스킨 장비는 몸통 렌더러 트랜스폼을, 강체 장비는 슬롯 소켓을 부모로 쓴다. 장비를 받는 캐릭터는 첫 외형 적용의
                // 준비 검사를 통과했고 몸통 렌더러 연결은 리그 프리팹 EditMode 테스트가 확인하므로 다시 보지 않는다.
                ? rig.Rendering.BodyRenderer!.transform
                : rig.GetEquipmentSocket(slot);

            Dictionary<string, Transform>? bones = null;
            if (prefab.IsSkinned)
            {
                // 본 이름 사전만 캐시하고 바인딩 검사는 장비마다 한다. 리그의 본 구성은 리그 프리팹 EditMode 테스트가 확인한다.
                if (_bones == null)
                    _bones = rig.Rendering.BuildBoneMap();
                bones = _bones;
            }

            DuckEquipPart? candidate = null;
            bool prepared = false;
            try
            {
                // 비활성 계층에서 생성하여 본 연결보다 OnEnable이 먼저 실행되지 않게 한다.
                if (_staging == null)
                {
                    var staging = new GameObject("장비 교체 준비");
                    staging.SetActive(false);
                    staging.transform.SetParent(_avatar.Rig.transform, false);
                    _staging = staging.transform;
                }
                candidate = Object.Instantiate(prefab, _staging, false);
                candidate.gameObject.SetActive(false);
                candidate.transform.localPosition = Vector3.zero;
                candidate.transform.localRotation = Quaternion.identity;
                candidate.transform.localScale = Vector3.one;
                // 바인딩까지 끝난 후보만 실제 부모로 옮기고, 활성화는 교체를 확정한 뒤에 한다.
                // 바인딩 실패는 TryBind가 보고하고, 준비하지 못한 후보는 아래 finally에서 정리한다.
                if (!candidate.IsSkinned || candidate.TryBind(bones!))
                {
                    candidate.transform.SetParent(parent, false);
                    prepared = true;
                }
            }
            // 바인딩 실패와 예상 밖 예외 모두 확정하지 않은 후보를 남기지 않는다. 예외는 잡지 않고 정리 뒤 그대로 전파한다.
            finally
            {
                if (!prepared) DestroyOwned(candidate);
            }
            // 예외 없이 끝난 미확정 후보는 바인딩 실패뿐이고 TryBind가 이미 보고했으므로 false만 돌려준다.
            if (!prepared)
                return false;

            RemoveAt(index);
            _instances[index] = candidate;
            candidate.gameObject.SetActive(true);
            candidate.SetVisible(true);
            RefreshHiddenParts();
            return true;
        }

        // 모든 착용 장비가 숨기는 부위의 합집합을 계산해, 한 장비를 벗어도 다른 장비가 가린 부위는 계속 숨긴다.
        // 장비 변경과 정리 안에서만 호출한다.
        void RefreshHiddenParts()
        {
            // 고정된 외형 부위 수라 합집합을 할당 없는 비트 마스크로 계산한다.
            uint hiddenMask = 0;
            for (int i = 0; i < _instances.Length; i++)
            {
                DuckEquipPart? part = _instances[i];
                if (part == null || part.HiddenAvatarParts == null) continue;
                foreach (DuckAvatarPartType type in part.HiddenAvatarParts)
                {
                    if ((uint)type < (uint)DuckAvatarPartType.Count)
                        hiddenMask |= 1u << (int)type;
                }
            }
            _avatar.SetHiddenMask(hiddenMask);
        }

        // 캐릭터 소유자만 장비와 준비 오브젝트를 명시적으로 정리한다.
        internal void Clear()
        {
            for (int i = 0; i < _instances.Length; i++) RemoveAt(i);
            RefreshHiddenParts();
            // 종료한 캐릭터의 본 캐시와 준비 부모 참조를 해제한다.
            _bones = null;
            if (_staging != null)
            {
                // 이 객체가 만든 준비 오브젝트만 파괴한다.
                UnityObjects.DestroyOwned(_staging.gameObject);
                _staging = null;
            }
        }

        void RemoveAt(int index)
        {
            DestroyOwned(_instances[index]);
            _instances[index] = null;
        }

        // 이 객체가 만든 장비만 실행 상태에 맞게 파괴한다. Play Mode의 Destroy는 프레임 끝까지 미뤄지므로
        // 먼저 비활성화해 교체하는 프레임에 두 장비가 함께 보이지 않게 한다.
        static void DestroyOwned(DuckEquipPart? part)
        {
            if (part == null) return;
            part.gameObject.SetActive(false);
            UnityObjects.DestroyOwned(part.gameObject);
        }
    }
}
