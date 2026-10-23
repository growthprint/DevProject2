#nullable enable annotations
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원본 ItemVariableEntry, ItemStatEntry, ItemModifierEntry 프리팹 한 줄이다. 세 원본 프리팹이 같은 이름 칸(displayName)과 값 칸(value)을
    // 가지므로 한 View로 보인다. 문구와 값은 상세 화면 소유자가 정하고, 이 줄은 표시만 한다. 직렬화 필드 이름은 SharedHUD 저작 도구가 쓴다.
    public sealed class OriginalItemPropertyEntry : MonoBehaviour
    {
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] TMP_Text _value = null!;
        // 원본 ItemModifierEntry의 color_Neutral, color_Positive, color_Negative다. modifier 줄 템플릿에만 원본 값을 저작한다.
        [SerializeField] Color _neutral = Color.white, _positive = Color.white, _negative = Color.red;

        // 변수, 상수, 수치 줄이다. 값 색은 템플릿에 저작된 색을 그대로 쓴다.
        internal void Show(string name, string value)
        {
            _name.text = name;
            _value.text = value;
        }

        // 원본 ItemModifierEntry.Refresh다. 값이 0이 아니면 그 수치의 좋은 방향(polarity)과 부호로 좋은 색, 나쁜 색을 고르고 그 밖은 중립 색이다.
        internal void ShowModifier(string name, string value, float signedValue, OriginalStatPolarity polarity)
        {
            Show(name, value);
            Color color = _neutral;
            if (signedValue != 0f && polarity == OriginalStatPolarity.Negative) color = signedValue < 0f ? _positive : _negative;
            else if (signedValue != 0f && polarity == OriginalStatPolarity.Positive) color = signedValue > 0f ? _positive : _negative;
            _value.color = color;
        }
    }
}
