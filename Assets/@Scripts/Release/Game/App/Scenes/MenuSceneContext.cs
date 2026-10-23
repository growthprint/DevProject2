#nullable enable annotations
using System;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 메뉴 씬의 표시 수명과 필수 UI 계약을 소유한다. 씬이 로드되면 현재 메뉴가 되고, 다음 씬 로드가 시작되면 물러난다.
    [DisallowMultipleComponent]
    public sealed class MenuSceneContext : MonoBehaviour
    {
        // 메뉴 카메라, 입력, 연출을 담은 루트다. Awake가 이 루트에서 메뉴 연출을 한 번 찾는다.
        [SerializeField] GameObject _contentRoot = null!;
        // 화면 제어기는 직렬화된 uGUI View에만 의존한다.
        [SerializeField] MainMenuUguiView _view = null!;
        // UI 컨트롤러에 현재 화면의 uGUI 계약만 제공한다.
        public MainMenuUguiView View => _view;
        // Awake가 콘텐츠 루트에서 한 번 찾아 둔 메뉴 연출이며, 없으면 null이다. Release 코드는 이 컴포넌트를
        // 추가하거나 파괴하지 않아 캐시가 낡지 않는다. GameUiController와 GameAudioManager는 현재 메뉴에서만 읽는다.
        OriginalMenuPresentation? _presentation;
        // 이 메뉴 루트의 연출만 공개해 다른 씬을 검색하지 않는다. 읽을 때마다 GetComponent를 부르지 않고 캐시를 돌려준다.
        internal OriginalMenuPresentation? Presentation => _presentation;

        // UI와 오디오가 연결할 현재 메뉴다. 다음 씬 로드가 시작되거나 메뉴가 파괴되면 null이다.
        public static MenuSceneContext? Current { get; private set; }
        // 현재 메뉴가 생기거나 물러날 때 알린다. null은 연결을 끊으라는 뜻이다.
        public static event Action<MenuSceneContext?>? CurrentChanged;

        // 도메인 재로드 없이 Play Mode에 들어오면 정적 값과 구독자가 남으므로 세션 시작마다 비운다. 테스트도 정리 때 부른다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            Current = null;
            CurrentChanged = null;
        }

        // 콘텐츠 루트와 View 연결, 루트 소유 규칙은 EditMode 저작 검사가 확인하므로 여기서 다시 검사하지 않는다.
        // 다음 씬 로드가 시작되면 물러나도록 로드 시작을 구독한다.
        void Awake()
        {
            _contentRoot.TryGetComponent(out _presentation);
            Current = this;
            SceneFlow.LoadingStarted += OnLoadingStarted;
        }

        // UI와 오디오가 이 알림 안에서 메뉴 View를 연결하므로, 추가 로드로 같은 씬의 다른 메뉴 컴포넌트가 Awake를 마친 Start에서 알린다.
        // 그 전에 로드 시작이나 파괴로 물러났으면 알리지 않는다.
        void Start()
        {
            if (ReferenceEquals(Current, this)) ObserverDispatch.InvokeEachValue(CurrentChanged, this);
        }

        // 메뉴 객체가 아직 살아 있을 때 UI와 오디오가 연결을 끊도록, 현재 씬을 내리기 전에 물러나며 알린다.
        void OnLoadingStarted(GameScene scene)
        {
            if (!ReferenceEquals(Current, this)) return;
            Current = null;
            ObserverDispatch.InvokeEachValue<MenuSceneContext?>(CurrentChanged, null);
        }

        // 앱 종료나 SceneFlow 종료처럼 로드 시작 없이 파괴될 때를 대비해 현재 메뉴만 알림 없이 비운다.
        void OnDestroy()
        {
            SceneFlow.LoadingStarted -= OnLoadingStarted;
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
