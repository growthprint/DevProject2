#nullable enable annotations
using System;
using Game.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game.App
{
    // 원작 Duckov.Buildings.UI.BuilderView 창(LevelManager GameplayUICanvas/BuilderView)이다. 건물 목록, 건물 메뉴, 배치 안내,
    // 마우스 올림 안내, 격자 표시, 건설 카메라를 가진다. 값과 카메라 위치는 화면 컨트롤러(BuilderScreen)가 넣고, 이 View는 의도만 이벤트로 낸다.
    // 보이기와 숨기기는 원작 FadeGroup 전환 대신 활성 여부로 한다(보관함 창과 같은 원작 차이).
    public sealed class OriginalBuilderView : MonoBehaviour, IPointerClickHandler
    {
        // 원작 selectionPanel의 buildingBtnTemplate이다. 꺼진 채 저장되고, 복제본은 템플릿과 같은 부모(목록 배치) 아래에 놓인다.
        [SerializeField] OriginalBuildingEntryView _entryTemplate = null!;
        // 원작 contextMenu, placingModeInputIndicator, followCursorUI, hoveringBuildingFadeGroup(활성 여부로 보인다), gridDisplay다.
        [SerializeField] OriginalBuildingContextMenuView _contextMenu = null!;
        [SerializeField] GameObject _placingModeInputIndicator = null!;
        [SerializeField] RectTransform _followCursor = null!;
        [SerializeField] GameObject _hoveringBuildingIndicator = null!;
        [SerializeField] OriginalBuildingGridDisplay _grid = null!;
        // 원작 vcam(CinemachineVirtualCamera, 시야각 24)이다. 게임 카메라가 열린 동안 이 자세로 섞어 들어간다.
        [SerializeField] OriginalVirtualCamera _camera = null!;
        // 원작 cameraSpeed, pitch, cameraDistance, yaw다. 기본값은 원작 직렬화 기본값이고, 원작 프리팹 값은 제작 도구가 넣는다.
        [SerializeField] float _cameraSpeed = 10f;
        [SerializeField] float _pitch = 45f;
        [SerializeField] float _cameraDistance = 10f;
        [SerializeField] float _yaw = -45f;

        UiTemplateCollection<OriginalBuildingEntryView>? _entries;
        Action<string>? _entryClicked, _recycleFulfilled;

        // 원작 selectionPanel.onButtonSelected와 onRecycleRequested다. 인자는 건물 정의 ID다.
        internal event Action<string>? EntrySelected;
        internal event Action<string>? RecycleRequested;
        // 원작 OnPointerClick의 왼쪽 클릭이다. 배치 확정은 컨트롤러가 지금 모드를 보고 정한다.
        internal event Action? ConfirmRequested;
        // 원작 View의 exitButton이다. ESC 닫기는 화면 연결(GameplayScreens)이 TryClose로 처리한다.
        internal event Action? CloseRequested;

        internal bool IsShown => gameObject.activeSelf;
        internal OriginalBuildingContextMenuView ContextMenu => _contextMenu;
        internal OriginalBuildingGridDisplay Grid => _grid;
        internal OriginalVirtualCamera VirtualCamera => _camera;

        UiTemplateCollection<OriginalBuildingEntryView> Entries =>
            _entries ??= new UiTemplateCollection<OriginalBuildingEntryView>(_entryTemplate, entry => entry.Unbind());

        internal void Show() => gameObject.SetActive(true);

        // 화면 닫기와 공용 HUD 분리가 부른다. 이미 숨었어도 불러도 된다. 격자는 꺼지면서 스스로 숨는다.
        internal void Hide()
        {
            if (_entries != null) _entries.Clear();
            _contextMenu.Hide();
            _placingModeInputIndicator.SetActive(false);
            _hoveringBuildingIndicator.SetActive(false);
            gameObject.SetActive(false);
        }

        // 원작 BuildingSelectionPanel.Refresh의 Pool.ReleaseAll, Get().Setup을 셋으로 나눈다. 셋은 같은 호출 안에서 짝을 이룬다.
        // 줄 순서는 컨트롤러가 원작처럼 비용이 모자란 줄을 뒤로 보낸 순서다. 돌려준 비용 표시는 컨트롤러가 이어서 채운다.
        internal void BeginEntries() => Entries.Begin();

        internal OriginalCostDisplayView AddEntry(string buildingId, Sprite? icon, string displayName, string description, string tokenText,
            bool costShown, string amountText, bool reachedLimit, bool available, bool recyclable)
        {
            OriginalBuildingEntryView entry = Entries.Acquire(_entryTemplate.transform.parent);
            entry.Bind(buildingId, icon, displayName, description, tokenText, costShown, amountText, reachedLimit, available, recyclable,
                _entryClicked ??= EntryClicked, _recycleFulfilled ??= RecycleFulfilled);
            entry.gameObject.SetActive(true);
            return entry.Cost;
        }

        internal void EndEntries() => Entries.End();

        // 원작 SetMode의 placingModeInputIndicator다.
        internal void SetPlacing(bool placing) => _placingModeInputIndicator.SetActive(placing);

        // 원작 UpdateContextMenuIndicator의 hoveringBuildingFadeGroup이다. 바뀔 때만 켜고 끈다.
        internal void SetHovering(bool hovering)
        {
            if (_hoveringBuildingIndicator.activeSelf != hovering) _hoveringBuildingIndicator.SetActive(hovering);
        }

        // 원작 UpdateCamera의 이동이다. 카메라가 거의 수직이면 화면 위쪽, 아니면 앞쪽을 바닥에 투영한 방향으로 움직인다.
        // 범위 제한은 영역을 아는 컨트롤러가 한다.
        internal Vector3 PanCamera(Vector3 cursor, Vector2 input, float unscaledDeltaTime)
        {
            Transform pose = _camera.transform;
            float forwardUp = Mathf.Abs(Vector3.Dot(pose.forward, Vector3.up));
            float upUp = Mathf.Abs(Vector3.Dot(pose.up, Vector3.up));
            Vector3 forward = Vector3.ProjectOnPlane(forwardUp > upUp ? pose.up : pose.forward, Vector3.up);
            Vector3 right = Vector3.ProjectOnPlane(pose.right, Vector3.up);
            return cursor + (right * input.x + forward * input.y) * _cameraSpeed * unscaledDeltaTime;
        }

        // 원작 UpdateCamera의 자세다. 커서에서 요와 피치만큼 돈 방향으로 거리만큼 떨어져 커서를 본다.
        internal void PlaceCamera(Vector3 cursor)
        {
            Transform pose = _camera.transform;
            pose.position = cursor + Quaternion.Euler(0f, _yaw, 0f) * Quaternion.Euler(_pitch, 0f, 0f) * Vector3.forward * _cameraDistance;
            pose.LookAt(cursor, Vector3.up);
        }

        // 원작 UpdateContextMenuIndicator의 followCursorUI다. 공용 HUD 루트 캔버스가 Overlay라 카메라 없이 부모 지역 좌표로 바꾼다.
        void Update()
        {
            Pointer? pointer = Pointer.current;
            if (pointer == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_followCursor.parent, pointer.position.ReadValue(), null,
                out Vector2 localPoint);
            _followCursor.localPosition = localPoint;
        }

        // 원작처럼 목록이나 메뉴가 받지 않은 왼쪽 클릭만 창 바탕까지 올라온다.
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) ConfirmRequested?.Invoke();
        }

        // 고정 닫기 버튼의 저장된 Runtime Only OnClick이 부른다. 버튼은 열린 창 안에만 있다.
        public void OnCloseClicked()
        {
            Log.Instance.Assert(IsShown, "건설 창이 열린 동안에만 닫습니다.", this);
            CloseRequested?.Invoke();
        }

        void EntryClicked(string buildingId) => EntrySelected?.Invoke(buildingId);
        void RecycleFulfilled(string buildingId) => RecycleRequested?.Invoke(buildingId);
    }
}
