#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 프롤로그 UI는 복귀 검정 화면 전환만 가진다. 대사는 앱 수명 GameplayUI의 공용 대사 상자가 표시하고,
    // 상호작용 안내는 공용 HUD가 표시한다.
    public sealed class PrologueUguiView : MonoBehaviour
    {
        // 원본 Interact_CustomFace.Hide의 복귀 검정 전환이다. 0.5초 유지한 뒤 0.25초 동안 걷힌다.
        // OriginalBlackScreenAuthoring이 늘 연결하므로 null을 허용하지 않는다.
        [SerializeField] BlackScreenFade _blackScreen = null!;
        // 거울 편집에서 돌아올 때 OriginalPrologueController가 재생한다. 연결 확인은 EditMode 저작 검사가 맡아 검사 없이 돌려준다.
        internal BlackScreenFade BlackScreen => _blackScreen;
    }
}
