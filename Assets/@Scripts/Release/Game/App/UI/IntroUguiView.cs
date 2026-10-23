#nullable enable annotations
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 인트로의 원본 스킵 안내를 표시만 한다. 스킵 키를 누르고 있는 시간과 완료 판단은 컨트롤러가 맡는다.
    public sealed class IntroUguiView : MonoBehaviour
    {
        [SerializeField] CanvasGroup _hint = null!;
        [SerializeField] Image _fill = null!;
        [SerializeField] TMP_Text _binding = null!;
        // 스킵 안내 CanvasGroup의 interactable과 blocksRaycasts는 프리팹에 false로 저장돼 있어 여기서 쓰지 않는다.
        // EditMode의 ProductUiAssetTests가 그 값을 확인한다.
        public void ShowSkip(float progress, float alpha, string binding)
        {
            _hint.alpha = alpha;
            _fill.fillAmount = progress;
            _binding.text = binding;
        }
    }
}
