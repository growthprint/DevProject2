#nullable enable annotations
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.App
{
    // 원작 GameManager의 SceneLoader 캔버스다. 로딩 표시, 클릭 표시, 화면 클릭 수신(Interaction)과
    // 검은 화면 캔버스 위의 레벨 준비 표시를 보여 준다. 언제 무엇을 보일지는 SceneFlow가 원작 SceneLoader 순서대로 정한다.
    // 원작의 팁(UIContent, TipsDisplay)은 로딩 화면에 보이지 않으므로 이 캔버스에 두지 않는다. 근거: Docs/game-runtime.md#loading-screen
    [DisallowMultipleComponent]
    public sealed class SceneLoadingView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] OriginalFadeGroup _loadingIndicator = null!;
        [SerializeField] OriginalFadeGroup _clickIndicator = null!;
        // 원작 pointerClickEventRecevier가 붙은 전체 화면 Interaction 객체다. 클릭을 기다리는 동안만 켠다.
        // 이 View는 같은 캔버스 루트에서 그 객체가 받은 클릭을 받는다.
        [SerializeField] GameObject _interaction = null!;
        // 원작 BlackScreenCanvas의 LevelInitializing이다. 다음 씬을 켜기 직전에 보이고 레벨 초기화가 끝나면 숨는다.
        [SerializeField] OriginalFadeGroup _levelInitializing = null!;

        // 클릭을 기다리는 동안 받은 클릭이다. WaitForClick이 내리고 OnPointerClick이 세운다.
        public bool Clicked { get; private set; }

        internal void ShowLoadingIndicator() => _loadingIndicator.Show();
        internal void HideLoadingIndicator() => _loadingIndicator.Hide();
        internal bool IsLoadingIndicatorFading => _loadingIndicator.IsFading;
        internal void ShowClickIndicator() => _clickIndicator.Show();
        internal void HideClickIndicator() => _clickIndicator.Hide();
        internal bool IsClickIndicatorFading => _clickIndicator.IsFading;
        internal void ShowLevelInitializing() => _levelInitializing.Show();
        internal void HideLevelInitializing() => _levelInitializing.Hide();

        // 원작처럼 클릭 수신 객체를 켜고 클릭 표시를 내린다. 받은 클릭은 Clicked로 본다.
        internal void BeginClickWait()
        {
            Clicked = false;
            _interaction.SetActive(true);
        }

        internal void EndClickWait() => _interaction.SetActive(false);

        // 원작 SceneLoader.NotifyPointerClick이다. 수신 객체가 켜진 동안만 레이캐스트가 닿는다.
        public void OnPointerClick(PointerEventData eventData) => Clicked = true;

        // 원작 Awake의 SkipHide처럼 모든 표시를 숨긴 채 시작한다. 프리팹도 숨긴 상태로 저장한다.
        void Awake()
        {
            _interaction.SetActive(false);
            _loadingIndicator.SkipHide();
            _clickIndicator.SkipHide();
            _levelInitializing.SkipHide();
        }
    }
}
