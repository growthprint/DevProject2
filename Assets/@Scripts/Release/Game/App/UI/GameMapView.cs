#nullable enable annotations
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace Game.App
{
    // 전체 지도와 HUD 미니맵이 같은 좌표 투영으로 쓰는 지도 화면이다. 마커 편집은 요청 이벤트로만 내보낸다.
    public sealed class GameMapView : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler, IPointerClickHandler
    {
        // 이 화면이 구독한 UI 이벤트를 모아 두었다가 파괴될 때 한 번에 해제한다.
        readonly UiEventSubscriptions _subscriptions = new UiEventSubscriptions();
        void OnDestroy() => _subscriptions.Dispose();

        [SerializeField] bool _hud;
        [SerializeField] RectTransform _viewport = null!;
        [SerializeField] RawImage _image = null!;
        [SerializeField] RectTransform _player = null!;
        [SerializeField] RectTransform _compass = null!;
        [SerializeField] Image _markerTemplate = null!;
        [SerializeField] Sprite[] _icons = Array.Empty<Sprite>();
        [SerializeField] Slider _zoom = null!;
        // 제목은 전체 지도만 갱신하고, 지도 없음 안내는 구역에 지도가 없을 때 켠다.
        [SerializeField] TMP_Text _title = null!, _noMap = null!;
        // 북쪽 글자는 회전하는 화살표와 분리해 항상 똑바로 읽히게 한다.
        [SerializeField] TMP_Text _northLabel = null!;
        [SerializeField] Button[] _iconButtons = Array.Empty<Button>(), _colorButtons = Array.Empty<Button>();
        // 선택할 때마다 GetComponent 하지 않도록 Bind에서 한 번 찾아 둔 선택 외곽선이다. 순서는 버튼 배열과 같다.
        Outline[] _iconOutlines = Array.Empty<Outline>(), _colorOutlines = Array.Empty<Outline>();
        // 마커 색상표다. 인덱스가 저장되는 MapMarkerState.ColorId이자 색상 버튼 순서라 순서를 유지해야 한다.
        // 크기를 저장 마커의 색 수 상수로 정해, 색 개수가 그 상수와 다르면 컴파일 오류가 나게 한다.
        static readonly Color[] _colors = new Color[MapMarkerState._colorCount] { Color.white, new Color(.9725491f,.33333334f,.40000004f),
            new Color(1,.61960787f,.227451f), new Color(.8f,.9294118f,.59607846f), new Color(.3803922f,.79215693f,.97647065f) };
        // 지도에 표시 중인 마커다. 마커 이미지는 재사용하고, 마커 목록이 바뀌면 새 목록에 다시 연결한다.
        readonly List<MarkerIcon> _markers = new List<MarkerIcon>();
        UiTemplateCollection<Image>? _markerInstances;
        SceneMapContext? _context;
        // 이 화면은 세션과 저장소를 모른다. 표시할 마커만 받고, 추가와 삭제는 요청 이벤트로 내보낸다.
        IReadOnlyList<MapMarkerState> _data = Array.Empty<MapMarkerState>();
        public event Action<MapMarkerState>? AddRequested;
        public event Action<string>? RemoveRequested;
        // 편집은 클릭 안에서 동기로 끝나므로 편집 중 상태 없이 마커 목록만 받는다.
        // 다른 목록 참조가 오면 다음 갱신에서 마커를 다시 만든다.
        public void SetMarkers(IReadOnlyList<MapMarkerState> markers) => _data = markers;
        SceneMapRegion? _region;
        IReadOnlyList<MapMarkerState>? _shownMarkers;
        Vector2 _center;
        // 투영 배율이다. HUD는 Bind가 고정값 _hudScale로 두고, 전체 지도는 확대 슬라이더 콜백이 Lerp 값으로, 열 때 TryFitRegion이 맞춤 값으로 바꾼다.
        // 매 프레임 마커마다 부르는 투영에서 Lerp를 다시 하지 않도록 담아 둔다. 초기값 _initialScale은 Bind가 슬라이더 위치로도 맞춘다.
        float _scale = _initialScale;
        int _icon, _color;
        bool _bound, _dragging;
        // 제목을 마지막으로 쓴 지도 문맥과 선택 로캘이다. 둘 다 같으면 번역도 같으므로 제목을 다시 쓰지 않는다.
        SceneMapContext? _titleContext;
        Locale? _titleLocale;
        float Rotation => _hud ? 0f : _context!.Rotation;
        // 확대 배율 범위와 우클릭 마커 삭제 반경이다. 반경은 지도 UI 좌표로 잰다.
        const float _minScale = 1f, _maxScale = 13f;
        const float _removeRadius = 20f;
        // HUD 미니맵 배율, 전체 지도 첫 배율, 나침반 중심에서 북쪽 글자까지의 UI 거리, 휠 한 칸의 슬라이더 이동량이다.
        const float _hudScale = 6f, _initialScale = 3f, _northLabelRadius = 67f, _scrollZoomStep = .05f;
        // 회전각이 바뀔 때만 다시 계산하는 회전 값이다. 초깃값 NaN은 어떤 각과도 같지 않아 첫 갱신에서 반드시 계산한다.
        float _cachedRotation = float.NaN;
        Quaternion _rotationQuaternion;
        Vector2 _northOffset;
        float _rotationCos, _rotationSin;

        public void Bind()
        {
            if (_bound) return;
            _bound = true;
            // HUD 미니맵은 확대 슬라이더 없이 고정 배율을 쓴다.
            if (_hud) { _scale = _hudScale; return; }
            _iconOutlines = Outlines(_iconButtons);
            _colorOutlines = Outlines(_colorButtons);
            for (int index = 0; index < _iconButtons.Length; index++)
            { int selected = index; _subscriptions.Bind(_iconButtons[index].onClick, () => { _icon = selected; RefreshSelection(); }); }
            for (int index = 0; index < _colorButtons.Length; index++)
            { int selected = index; _subscriptions.Bind(_colorButtons[index].onClick, () => { _color = selected; RefreshSelection(); }); }
            _subscriptions.Bind(_zoom.onValueChanged, value => _scale = Mathf.Lerp(_minScale, _maxScale, value));
            _zoom.SetValueWithoutNotify(Mathf.InverseLerp(_minScale, _maxScale, _scale));
            RefreshSelection();
        }

        public void SetContext(SceneMapContext? context, bool recenter = false)
        {
            // 문맥이나 구역이 바뀌면 표시 마커를 다시 만들고 중심을 플레이어로 옮긴다. HUD는 늘 플레이어가 중심이다.
            // 구역은 씬 컨트롤러가 하위 씬을 켤 때 지도 문맥에 정해 두므로 여기서는 읽기만 한다.
            if (_context != context) { _context = context; _region = null; _shownMarkers = null; recenter = true; }
            if (_region != _context.Region) { _region = _context.Region; _shownMarkers = null; recenter = true; }
            // 사용자 결정(2026-10-23): 전체 지도를 열면 구역 지도 전체가 보이게 한다. 첫 배율로 뷰포트에 넘치는 구역(제로존)만
            // 뷰포트에 맞게 줄이고 지도 이미지 중심에 둔다. 다 들어가는 구역은 원작 MiniMapView.OnOpen처럼 배율을 두고 플레이어를 중심에 둔다.
            if (recenter && !_hud && TryFitRegion()) _center = _region.ImageCenter;
            else if (recenter || _hud) _center = _region.WorldToMap(_context.Player.position);
            RefreshMap();
        }

        // 지도 이미지 한 변(구역 월드 크기 × 첫 배율)이 뷰포트의 짧은 변보다 길 때만 배율을 맞추고 확대 슬라이더도 옮긴다.
        // 지도가 없는 구역은 맞출 이미지가 없어 그대로 둔다.
        bool TryFitRegion()
        {
            Rect viewport = _viewport.rect;
            float fit = Mathf.Min(viewport.width, viewport.height) / _region!.WorldSize;
            if (!_region.HasMap || fit >= _initialScale) return false;
            _scale = Mathf.Max(_minScale, fit);
            _zoom.SetValueWithoutNotify(Mathf.InverseLerp(_minScale, _maxScale, _scale));
            return true;
        }

        // 제목을 쓴 뒤에 문맥과 로캘을 기록한다. 그래서 번역 조회가 도중에 실패하면 다음 갱신에서 다시 시도한다.
        void RefreshTitle()
        {
            Locale? locale = LocalizationSettings.SelectedLocale;
            if (_titleContext == _context && _titleLocale == locale) return;
            // 번역 키는 map.<씬 ID> 형식이다.
            _title.text = GameLocalization.Text("map." + GameScenes.Id(_context!.SceneId));
            _titleContext = _context;
            _titleLocale = locale;
        }

        // 지도 이미지만 회전하고, 마커와 글자는 방향과 화면 크기를 유지한다.
        // GameMapScreen이 CanOpen으로 문맥과 플레이어를 확인한 뒤에만 SetContext를 거쳐 부르므로 null을 검사하지 않는다.
        void RefreshMap()
        {
            float rotation = Rotation;
            if (rotation != _cachedRotation) CacheRotation(rotation);
            _image.texture = _region!.Texture;
            _image.enabled = _region.HasMap;
            _player.gameObject.SetActive(_region.HasMap);
            _noMap.gameObject.SetActive(!_region.HasMap);
            if (!_hud)
            {
                // 제목, 나침반, 북쪽 글자는 전체 지도만 갱신한다.
                RefreshTitle();
                _compass.localRotation = _rotationQuaternion;
                ((RectTransform)_northLabel.rectTransform.parent).anchoredPosition = _northOffset;
            }
            _image.rectTransform.sizeDelta = Vector2.one * (_region.WorldSize * _scale);
            _image.rectTransform.anchoredPosition = Project(_region.ImageCenter);
            _image.rectTransform.localRotation = _rotationQuaternion;
            // HUD는 SetContext가 중심을 플레이어 위치로 두었으므로 투영이 늘 (0, 0)이라 계산을 건너뛴다.
            _player.anchoredPosition = _hud ? Vector2.zero : Project(_region.WorldToMap(_context.Player.position));
            if (_hud)
            {
                Vector3 aim = _context.AimDirection;
                _player.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg);
            }
            IReadOnlyList<MapMarkerState> markers = _data;
            if (!ReferenceEquals(markers, _shownMarkers))
            {
                // 마커 이미지를 모두 회수한 뒤, 이 씬과 구역의 마커 수만큼 다시 꺼내 연결한다.
                _markerInstances ??= new UiTemplateCollection<Image>(_markerTemplate);
                _markerInstances.Begin();
                _markers.Clear(); _shownMarkers = markers;
                // IReadOnlyList 열거자의 박싱을 피하려고 색인으로 돈다.
                Transform parent = _markerTemplate.transform.parent;
                for (int index = 0; index < markers.Count; index++)
                {
                    MapMarkerState marker = markers[index];
                    if (marker.SceneId != _context.SceneId || marker.RegionId != _region.Id) continue;
                    Image icon = _markerInstances.Acquire(parent);
                    icon.name = "MapMarker_" + marker.Id;
                    icon.sprite = _icons[marker.IconId]; icon.color = _colors[marker.ColorId];
                    icon.gameObject.SetActive(_region.HasMap);
                    _markers.Add(new MarkerIcon(marker, icon));
                }
                // 이번 구역에서 쓰지 않은 마커 이미지만 숨긴다. 계속 쓰는 이미지는 끄고 켜지 않는다.
                _markerInstances.End();
                _player.SetAsLastSibling();
            }
            // 마커 표시 여부는 다시 만들 때 구역의 HasMap으로 정했고 구역이 바뀌면 다시 만들므로, 여기서는 위치만 갱신한다.
            foreach (MarkerIcon marker in _markers)
                marker.Image.rectTransform.anchoredPosition = Project(_region.WorldToMap(new Vector3(marker.State.X, marker.State.Y, marker.State.Z)));
        }

        // RefreshMap이 맞춰 둔 cos와 sin을 쓰므로 RefreshMap 안에서만 부른다. 식은 SceneMapContext.Rotate와 같다.
        Vector2 Project(Vector2 map)
        {
            Vector2 offset = map - _center;
            return new Vector2(offset.x * _rotationCos - offset.y * _rotationSin, offset.x * _rotationSin + offset.y * _rotationCos) * _scale;
        }

        // 지도와 나침반 회전, 북쪽 글자 위치, 투영용 cos와 sin을 한 번에 계산해 둔다.
        void CacheRotation(float rotation)
        {
            _cachedRotation = rotation;
            _rotationQuaternion = Quaternion.Euler(0, 0, rotation);
            float angle = rotation * Mathf.Deg2Rad;
            _rotationCos = Mathf.Cos(angle);
            _rotationSin = Mathf.Sin(angle);
            _northOffset = SceneMapContext.Rotate(Vector2.up * _northLabelRadius, rotation);
        }
        Vector2 Unproject(Vector2 local) => _center + SceneMapContext.Rotate(local / _scale, -Rotation);
        bool Pointer(PointerEventData data, out Vector2 local)
        {
            local = default;
            return !_hud && _region != null && _region.HasMap &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, data.position, data.pressEventCamera, out local) && _viewport.rect.Contains(local);
        }
        public void OnBeginDrag(PointerEventData data) { _dragging = data.button == PointerEventData.InputButton.Left && Pointer(data, out _); }
        public void OnDrag(PointerEventData data)
        {
            if (!_dragging || !Pointer(data, out Vector2 current)) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, data.position - data.delta, data.pressEventCamera, out Vector2 previous);
            _center -= SceneMapContext.Rotate((current - previous) / _scale, -Rotation);
        }
        public void OnScroll(PointerEventData data)
        {
            // InputSystem UI의 장치별 휠 배율이 한 번에 최대 확대를 만들지 않게 한 단계(_scrollZoomStep)로 제한한다.
            if (Pointer(data, out _)) _zoom.value = Mathf.Clamp01(_zoom.value + Mathf.Clamp(data.scrollDelta.y,-1f,1f) * _scrollZoomStep);
        }
        // 우클릭하면 삭제 반경 안의 마커 삭제를 요청하고, 없으면 그 지점에 현재 아이콘과 색상으로 마커 추가를 요청한다.
        // 편집은 클릭 안에서 동기로 끝나므로 편집 중인지 검사하지 않는다.
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Right || !Pointer(data, out Vector2 local)) return;
            foreach (MarkerIcon marker in _markers)
                // 제곱근을 피하려고 제곱 거리로 비교한다. 반경 경계에서 생기는 아주 작은 오차는 허용한다.
                if ((marker.Image.rectTransform.anchoredPosition - local).sqrMagnitude <= _removeRadius * _removeRadius)
                { RemoveRequested?.Invoke(marker.State.Id); return; }
            Vector2 point = Unproject(local);
            if (!_region!.ContainsMapPoint(point)) return;
            Vector3 world = _region.MapToWorld(point, _context!.Player!.position.y);
            // 저장 ID 공용 규칙의 새 ID, 준비된 씬과 구역 ID, 지도 안 좌표, 저작된 버튼 번호뿐이라 믿을 수 있는 값이다.
            // 그래서 TryCreate 없이 믿을 수 있는 입력용 생성자로 만든다.
            AddRequested?.Invoke(new MapMarkerState(PersistentIds.New(), _context.SceneId, _region.Id, world.x, world.y, world.z,
                _icon, _color));
        }

        // 선택된 아이콘 버튼과 색상 버튼에만 외곽선을 켠다.
        void RefreshSelection()
        {
            for (int index = 0; index < _iconOutlines.Length; index++) _iconOutlines[index].enabled = index == _icon;
            for (int index = 0; index < _colorOutlines.Length; index++) _colorOutlines[index].enabled = index == _color;
        }

        // 외곽선이 없는 버튼은 저작 결함이라 따로 검사하지 않는다. 그 결함은 RefreshSelection의 null 참조로 드러난다.
        static Outline[] Outlines(Button[] buttons)
        {
            var outlines = new Outline[buttons.Length];
            for (int index = 0; index < buttons.Length; index++) outlines[index] = buttons[index].GetComponent<Outline>();
            return outlines;
        }
        // 표시 중인 마커 상태와 그 이미지의 쌍이다. 마커마다 힙 할당하지 않도록 readonly struct로 둔다.
        readonly struct MarkerIcon
        {
            public MapMarkerState State { get; }
            public Image Image { get; }
            public MarkerIcon(MapMarkerState state, Image image) { State = state; Image = image; }
        }
    }
}
