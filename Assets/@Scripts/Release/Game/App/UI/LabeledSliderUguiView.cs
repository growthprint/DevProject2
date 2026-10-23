#nullable enable annotations
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원본 CustomFaceCanvas의 슬라이더 한 행이다. 프리팹이 스크립트 GUID와 필드 이름으로 참조하므로 필드 이름을 바꾸지 않는다.
    [DisallowMultipleComponent]
    public sealed class LabeledSliderUguiView : MonoBehaviour
    {
        // 제작 도구와 테스트가 이름으로 채우는 직렬화 필드다. 런타임에서 읽지 않아 생기는 CS0414 경고만 여기서 끈다.
#pragma warning disable CS0414
        [SerializeField] TMP_Text _label = null!;
#pragma warning restore CS0414
        [SerializeField] UnityEngine.UI.Slider _slider = null!;
        // 원본 행에 숫자 입력이 있을 때만 연결하므로 비어 있을 수 있다.
        [SerializeField] TMP_InputField? _input;
        public UnityEngine.UI.Slider Slider => _slider;
        public TMP_InputField? Input => _input;
    }
}
