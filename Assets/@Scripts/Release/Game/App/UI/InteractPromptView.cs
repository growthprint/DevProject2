#nullable enable annotations
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 InteractHUD(LevelManager/HUDCanvas/MouseKeyboardOnly/Interact_MouseKeyboard)의 상호작용 안내다.
    // 원본 SyncPos처럼 매 LateUpdate에 대상의 marker 지점을 화면 좌표로 옮겨 안내 위치를 맞춘다.
    // 원본 InteractSelectionHUD 행을 묶음 항목 수만큼 보이고 고른 행을 표시한다. 입력 해석과 항목 고르기는 컨트롤러가 맡는다.
    public sealed class InteractPromptView : MonoBehaviour
    {
        [SerializeField] RectTransform _anchor = null!;
        [SerializeField] TMP_Text _label = null!;
        [SerializeField] TMP_Text _key = null!;
        // 원본 InteractSelectionHUD 참조다. _label과 _key가 있는 _row가 첫 행이자 복제 템플릿이다. 복제 행은 같은 형제 순서 경로로
        // 참조를 찾으므로 모든 참조는 _row 아래에 있다(제작 도구가 확인한다). _selectIndicator는 키캡이 든 InputIndicator,
        // _selectionPoint는 고르지 않은 묶음 행의 점, _upDownIndicator는 고른 묶음 행의 위아래 표시다. 배경 색은 원본 선택 색 두 개다.
        [SerializeField] RectTransform _row = null!;
        [SerializeField] Graphic _background = null!;
        [SerializeField] GameObject _selectIndicator = null!;
        [SerializeField] GameObject _selectionPoint = null!;
        [SerializeField] GameObject _upDownIndicator = null!;
        [SerializeField] Color _selectedColor = new Color(.29411766f, .63529414f, .83137256f, .6745098f);
        [SerializeField] Color _unselectedColor = new Color(0f, .1882353f, .29411766f, .6745098f);

        Camera? _camera;
        Vector3 _worldPoint;
        // 첫 행과 원본 PrefabPool처럼 늘려 둔 복제 행이다. 줄어든 목록의 남는 행은 끄고 다시 쓴다.
        readonly List<Row> _rows = new List<Row>();
        // 지금 행에 넣은 이름과 키캡, 고른 번호다. 같은 내용이면 행을 다시 채우지 않고, 번호만 바뀌면 선택 표시만 바꾼다.
        readonly List<string> _shown = new List<string>();
        string? _shownKey;
        int _selected = -1;

        // 원본 RefreshContent와 RefreshSelection이다. labels는 대표가 첫 항목이고 selected는 그 안의 번호다. 호출자는 Update에서 부른다.
        // 꺼져 있다가 켤 때만 위치를 바로 맞추고, 이미 켜져 있으면 같은 프레임의 LateUpdate가 맞춘다.
        public void Show(IReadOnlyList<string> labels, int selected, string key, Vector3 worldPoint, Camera camera)
        {
            _worldPoint = worldPoint;
            _camera = camera;
            if (!SameContent(labels, key))
            {
                Fill(labels, key);
                _selected = -1;
            }
            if (_selected != selected) Select(selected);
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
                Sync();
            }
        }

        bool SameContent(IReadOnlyList<string> labels, string key)
        {
            if (labels.Count != _shown.Count || key != _shownKey) return false;
            for (int i = 0; i < labels.Count; i++) if (labels[i] != _shown[i]) return false;
            return true;
        }

        // 원본 SetInteractable이다. 항목 수만큼 행을 켜고 이름과 키캡을 넣는다. 호출자의 목록은 다음 프레임에 다시 채워지므로 문구만 복사해 둔다.
        void Fill(IReadOnlyList<string> labels, string key)
        {
            if (_rows.Count == 0)
                _rows.Add(new Row(_row.gameObject, _label, _key, _background, _selectIndicator, _selectionPoint, _upDownIndicator));
            while (_rows.Count < labels.Count) _rows.Add(Clone(_rows.Count));
            _shown.Clear();
            for (int i = 0; i < labels.Count; i++) _shown.Add(labels[i]);
            _shownKey = key;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                UiVisibility.Set(row.Root, i < labels.Count);
                if (i >= labels.Count) continue;
                row.Label.text = labels[i];
                row.Key.text = key;
            }
        }

        // 원본 SetSelection이다. 고른 행만 키캡과 선택 배경을 보이고, 묶음이면 고른 행에 위아래 표시를, 나머지 행에 점을 보인다.
        void Select(int selected)
        {
            _selected = selected;
            bool group = _shown.Count > 1;
            for (int i = 0; i < _shown.Count; i++)
            {
                Row row = _rows[i];
                bool on = i == selected;
                UiVisibility.Set(row.Select, on);
                UiVisibility.Set(row.UpDown, on && group);
                UiVisibility.Set(row.Point, !on && group);
                row.Background.color = on ? _selectedColor : _unselectedColor;
            }
        }

        // 원본 PrefabPool.Get처럼 첫 행을 복제해 앞 행 바로 뒤에 둔다. 묶음 항목이 처음 늘어날 때만 만든다.
        Row Clone(int index)
        {
            Transform copy = Instantiate(_row.gameObject, _row.parent, false).transform;
            copy.SetSiblingIndex(_row.GetSiblingIndex() + index);
            return new Row(copy.gameObject, Twin(_label.transform, copy).GetComponent<TMP_Text>(),
                Twin(_key.transform, copy).GetComponent<TMP_Text>(), Twin(_background.transform, copy).GetComponent<Graphic>(),
                Twin(_selectIndicator.transform, copy).gameObject, Twin(_selectionPoint.transform, copy).gameObject,
                Twin(_upDownIndicator.transform, copy).gameObject);
        }

        // 복제 행은 템플릿과 계층이 같으므로 템플릿 참조와 같은 형제 순서 경로의 객체가 그 행의 참조다.
        Transform Twin(Transform part, Transform copy) => part == _row ? copy : Twin(part.parent, copy).GetChild(part.GetSiblingIndex());

        // 상호작용 대상이 없거나 입력이 막힌 동안 매 프레임 불리므로, 이미 꺼져 있으면 SetActive를 다시 부르지 않는다.
        public void Hide() => UiVisibility.Set(gameObject, false);

        void LateUpdate() => Sync();

        // 켜져 있을 때만 돌고 Show가 카메라를 먼저 넣으므로 카메라는 검사하지 않는다.
        // HUDCanvas는 원본처럼 Overlay라 화면 좌표가 곧 월드 위치다. 다른 HUD 위젯처럼 화면 좌표를 바로 쓰고 깊이는 캔버스 평면에 둔다.
        void Sync() => _anchor.position = (Vector2)_camera.WorldToScreenPoint(_worldPoint);

        // 원본 InteractSelectionHUD 행 하나의 참조다. 첫 행은 직렬화 참조로, 복제 행은 Twin으로 채운다.
        readonly struct Row
        {
            internal Row(GameObject root, TMP_Text label, TMP_Text key, Graphic background, GameObject select, GameObject point, GameObject upDown)
            {
                Root = root; Label = label; Key = key; Background = background; Select = select; Point = point; UpDown = upDown;
            }

            internal GameObject Root { get; }
            internal TMP_Text Label { get; }
            internal TMP_Text Key { get; }
            internal Graphic Background { get; }
            internal GameObject Select { get; }
            internal GameObject Point { get; }
            internal GameObject UpDown { get; }
        }
    }
}
