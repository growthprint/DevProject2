#nullable enable annotations
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 ActionProgressHUD(HUDCanvas/ActionProgress_Slider, AimMarker/NormalPanel/AimMarker/ActionProgress)를 재현한다.
    // 어떤 행동(장전, 사용, 상호작용)을 보일지는 컨트롤러가 정하고, 이 View는 받은 값만 표시한다.
    [DisallowMultipleComponent]
    public sealed class ActionProgressView : MonoBehaviour
    {
        [SerializeField] Image _fill = null!;
        [SerializeField] CanvasGroup _group = null!;
        float _target;
        // 원본 ActionProgressHUD.Update가 투명도를 옮기는 초당 속도다.
        const float _fadeSpeed = 8f;

        // 원본 ActionProgressHUD.Update처럼 진행 중이면 목표 투명도를 1로, 진행률이 1 이상이거나 진행이 끝나면 0으로 둔다.
        public void Present(bool inProgress, float progress)
        {
            if (inProgress) _fill.fillAmount = Mathf.Clamp01(progress);
            _target = inProgress && progress < 1f ? 1f : 0f;
        }

        // 공용 HUD 소유자 전환 때 진행 표시를 페이드 없이 즉시 숨긴다.
        internal void ResetImmediate()
        {
            _target = 0f;
            _group.alpha = 0f;
        }

        // 원본처럼 투명도를 초당 _fadeSpeed씩 목표로 옮긴다. 테스트도 같은 경로를 부르도록 Update와 나눈다.
        internal void Tick(float deltaTime) => _group.alpha = Mathf.MoveTowards(_group.alpha, _target, _fadeSpeed * deltaTime);

        void Update() => Tick(Time.deltaTime);
    }
}
