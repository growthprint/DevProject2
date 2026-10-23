#nullable enable annotations
using System;
using UnityEngine.InputSystem;

namespace Game.App
{
    // 전체 지도와 미니맵 표시, 지도가 열린 동안의 입력 차단과 모달을 맡는다. 마커 목록과 편집은 GameManager가 내주는 마커 소유자(MapMarkers)에 맡긴다.
    // 앱 공통 UI(GameUiController)가 바인딩마다 만들고 LateUpdate에서 갱신하며 바인딩을 끊을 때 해제한다.
    internal sealed class GameMapScreen : IDisposable
    {
        readonly GameMapView _mapView, _miniMapView;
        readonly GameRuntime _runtime;
        readonly GameModalStack _modals;
        // 지도 토글 입력의 구독과 켜기를 맡는다. 다른 소비자가 이미 켠 입력은 해제 때 끄지 않는다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        bool _disposed;
        // 마지막으로 본 현재 레벨의 지도 문맥이다. 값이 바뀌면 씬이 바뀐 것이다.
        SceneMapContext? _mapContext;
        // 지도가 열린 동안 보유하는 게임플레이 입력 차단이다. null이 아니면 지도가 열려 있다.
        IDisposable? _mapInputBlockHandle;

        // 지도 문맥은 현재 레벨의 정적 접근자(SceneContext.CurrentMap)로 읽으므로 씬 흐름 참조를 받지 않는다.
        // 지도와 미니맵 View는 GameUguiView의 직렬화 참조이고 EditMode 테스트가 연결을 확인한다.
        internal GameMapScreen(GameUguiView view, GameRuntime runtime, InputActionAsset actions, GameModalStack modals)
        {
            _mapView = view.MapView; _miniMapView = view.MiniMapView;
            _runtime = runtime; _modals = modals;
            _mapView.Bind(); _miniMapView.Bind();
            _mapView.AddRequested += AddMarker;
            _mapView.RemoveRequested += RemoveMarker;
            _mapView.gameObject.SetActive(false); _miniMapView.gameObject.SetActive(false);
            // 지도 토글 액션은 프로젝트 입력 에셋에 늘 있으므로 null 아님으로 받고, 있는지는 ProductUiLocalizationTests가 확인한다.
            // 콜백 해제와 이 화면이 켠 입력의 비활성화는 Dispose의 구독 정리가 맡는다.
            InputAction toggleMap = actions.FindAction("UI/ToggleMap");
            _subscriptions.Bind(toggleMap, OnToggleMap);
        }

        public void Dispose()
        {
            // 두 번 불려도 한 번만 해제한다.
            if (_disposed) return;
            _disposed = true;
            _mapView.AddRequested -= AddMarker;
            _mapView.RemoveRequested -= RemoveMarker;
            CloseMap();
            // 토글 콜백을 끊고 이 화면이 켠 입력만 끈다. 해제 뒤에는 콜백이 오지 않고 이 객체도 버려지므로 필드를 비우지 않는다.
            _subscriptions.Clear();
            _miniMapView.gameObject.SetActive(false);
        }

        // 플레이어 이동 뒤에 지도 문맥과 지도, 미니맵 표시를 갱신한다.
        // 바인딩 해제가 이 화면을 먼저 비우고 해제하므로 해제 뒤에는 불리지 않는다.
        internal void Tick()
        {
            // 현재 레벨의 지도 문맥이 바뀌면 씬이 바뀐 것이므로 지도를 닫는다. 레벨 초기화 전이나 메뉴에서는 null이다.
            SceneMapContext? context = SceneContext.CurrentMap;
            if (_mapContext != context) { CloseMap(); _mapContext = context; }
            // 열기와 같은 지도 문맥 판정을 쓰고, 게임플레이 중일 때만 준비된 것으로 본다.
            bool ready = IsOpenable(_mapContext) && _runtime.IsGameplayActive;
            // 준비가 풀렸거나 지도가 맨 위 모달이 아니면 닫는다. 닫힌 지도는 바꿀 상태가 없으므로 열린 지도만 검사한다.
            if (_mapInputBlockHandle != null && (!ready || _modals.TopId != GameModalId.Map)) CloseMap();
            // 메뉴나 초기화 중에는 세션 마커가 없으므로 준비된 지도에서만 마커 소유자의 목록을 읽는다.
            // 매 프레임 읽으므로 MapMarkers.Changed를 따로 구독하지 않는다. 지도 View는 목록 참조가 바뀌면 마커를 다시 만든다.
            // 이 갱신은 바인딩 동안에만 돌아 앱이 살아 있으므로 GameManager 접근자를 매번 읽는다.
            if (ready)
            {
                var markers = GameManager.Markers.All;
                _mapView.SetMarkers(markers); _miniMapView.SetMarkers(markers);
            }
            // 모달이나 소지품, 창고 화면이 입력을 가진 동안에는 미니맵을 패널 위에 겹쳐 보이지 않는다.
            bool hud = ready && _mapContext!.ShowHud && _modals.Count == 0 && !_runtime.GameplayInput.IsBlocked;
            _miniMapView.gameObject.SetActive(hud);
            if (hud) _miniMapView.SetContext(_mapContext);
            // 열린 지도만 현재 문맥으로 다시 그린다.
            if (_mapInputBlockHandle != null) _mapView.SetContext(_mapContext);
        }

        void OnToggleMap(InputAction.CallbackContext context)
        {
            // 열린 지도에서 다시 토글하면 닫는다.
            if (_mapInputBlockHandle != null) { CloseMap(); return; }
            // 탭 단추 줄의 지도 단추와 같은 열기 조건을 쓰도록 Open으로 모았다.
            Open();
        }

        // 전투 월드 탭 단추 줄의 MapButton(원작 ViewsProxy.ShowMapView)이 묻는다. 지도가 닫혀 있고 게임플레이 중이며 모달이 없고 현재 레벨 지도를
        // 열 수 있으면 참이다. 인벤토리 같은 게임 화면이 입력을 막고 있어도 참이고, 단추를 받은 쪽이 그 화면을 닫은 뒤 Open을 부른다(원작 View.OnOpen).
        internal bool CanOpen
        {
            get
            {
                SceneMapContext? context = SceneContext.CurrentMap;
                return _mapInputBlockHandle == null && _runtime.IsGameplayActive && _modals.Count == 0 && IsOpenable(context);
            }
        }

        // 현재 레벨에 지도 문맥이 있고 그 문맥이 지도를 열 수 있는지다. 갱신, 열기 판정, 열기가 같은 규칙을 쓴다.
        // 문맥은 MonoBehaviour라 ?. 대신 Unity null 비교를 쓴다.
        static bool IsOpenable(SceneMapContext? context) => context != null && context.CanOpen;

        // 지도 토글 키와 탭 단추가 함께 쓰는 열기다. 토글 키는 열린 지도를 먼저 닫고 탭 단추는 CanOpen으로 거르므로,
        // 두 호출자 모두 닫힌 지도에서만 부른다. 열린 지도는 아래 모달 검사에도 걸린다.
        internal void Open()
        {
            // 인벤토리 같은 다른 화면이나 모달이 입력을 가진 동안에는 지도를 겹쳐 열지 않는다.
            if (!_runtime.AcceptsGameplayInput || _modals.Count != 0) return;
            // 현재 레벨의 지도 문맥만 연다. CurrentMap은 파괴된 문맥을 null로 돌려준다.
            _mapContext = SceneContext.CurrentMap;
            if (!IsOpenable(_mapContext)) return;
            // Runtime이 정리 중이라 입력 차단을 거절하면 지도를 열지 않는다.
            if (!_runtime.GameplayInput.TryAcquire(out IDisposable? block)) return;
            _mapInputBlockHandle = block;
            _mapView.SetContext(_mapContext, true);
            // 닫을 때 돌아갈 선택을 기억해 모달로 열고, 열린 동안 배경 블러를 켠다.
            _modals.Open(GameModalId.Map, _mapView.gameObject, CloseMap, UiFocus.Current(), null);
            _mapContext.SetMapVisible(true);
            // 전체 지도가 열린 동안 미니맵을 숨긴다.
            _miniMapView.gameObject.SetActive(false);
        }

        // 마커 추가와 삭제는 마커 소유자의 메모리에만 반영한다. 파일에는 다음 저장 지점에서 함께 기록된다.
        void AddMarker(MapMarkerState marker) =>
            EditMarker(markers => markers.TryAdd(marker));
        void RemoveMarker(string id) =>
            EditMarker(markers => markers.TryRemove(id));

        // 전환 중이 아니고 세션이 있으면 마커 명령을 바로 실행한다. 명령이 동기로 끝나므로 편집 중 상태를 두지 않는다.
        // 거절은 명령 관문이 닫힌 프레임의 정상 거절이라 결과를 버리고 경고나 안내를 하지 않는다.
        // 마커는 모든 씬에 걸쳐 소유되므로 현재 씬을 넘기지 않는다.
        void EditMarker(Func<MapMarkers, bool> command)
        {
            if (_runtime.IsBusy || !_runtime.HasSession) return;
            // 거절 결과는 위 이유로 버린다.
            _ = command(GameManager.Markers);
        }

        // 지도 모달 취소, 토글, 갱신, Dispose가 함께 쓰는 닫기다. 닫힌 지도에서 불려도 결과가 같다.
        void CloseMap()
        {
            _modals.Remove(GameModalId.Map);
            if (_mapContext != null) _mapContext.SetMapVisible(false);
            // 입력 차단을 반납한다. 마지막 차단이 풀리면 Runtime이 커서와 플레이어 입력을 되돌린다.
            _mapInputBlockHandle?.Dispose(); _mapInputBlockHandle = null;
            _mapView.gameObject.SetActive(false);
        }
    }
}
