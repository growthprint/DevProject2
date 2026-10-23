#nullable enable annotations
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 TagsDisplayEntry 한 개다. 원작처럼 태그 표시 이름과 태그 자산 색의 배경을 보인다. 원작의 태그 설명 툴팁은 이 게임에 없다.
    public sealed class OriginalItemTagEntry : MonoBehaviour
    {
        [SerializeField] Image _background = null!;
        [SerializeField] TMP_Text _text = null!;

        internal void Show(string text, Color color)
        {
            _background.color = color;
            _text.text = text;
        }
    }
}
