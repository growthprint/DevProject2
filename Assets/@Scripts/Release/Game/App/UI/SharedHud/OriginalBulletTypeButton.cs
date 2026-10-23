#nullable enable annotations
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원본 BulletTypeSelectButton이다. 탄종 이름과 소지 수를 보이고, 고른 항목에만 선택 그림자와 키 안내 표시를 켠다.
    public sealed class OriginalBulletTypeButton : MonoBehaviour
    {
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] TMP_Text _count = null!;
        [SerializeField] GameObject _indicator = null!;
        // 원본 selectShadow(TrueShadow 하늘색 발광)다. 프리팹에는 꺼진 채 저장되고 고른 항목에서만 켠다.
        [SerializeField] OriginalTrueShadow _selectShadow = null!;

        // 원본 Init처럼 소지 수는 자리 맞춤 없이 그대로 쓴다. 목록을 열 때만 불려 문자열 할당이 프레임마다 생기지 않는다.
        internal void Bind(string name, int count, bool selected)
        {
            _name.text = name;
            _count.text = count.ToString();
            SetSelected(selected);
        }

        // 원본 SetSelection이다. 그림자를 켜고 끄면 메시 효과가 그래픽 정점을 다시 만든다.
        internal void SetSelected(bool selected)
        {
            _selectShadow.enabled = selected;
            _indicator.SetActive(selected);
        }
    }
}
