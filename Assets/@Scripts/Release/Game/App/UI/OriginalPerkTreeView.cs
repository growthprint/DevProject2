#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.Core;
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.UI.PerkTreeView 창(LevelManager GameplayUICanvas/PerkTreeView)이다. 트리 제목, 특성 노드, 레벨 구분선, 연결선 두 종,
    // 고른 특성의 상세를 보인다. 값은 특성 트리 화면 컨트롤러(PerkTreeScreen)가 넣고, 이 View는 노드 고르기와 닫기 의도만 낸다.
    // 노드 배치는 원작 FitChildren 그대로다. 원작처럼 창을 열 때만 배치하고 상태가 바뀌면 모습과 선만 다시 그려 스크롤을 지킨다.
    // 원작 FadeGroup 전환과 열기 후 세 프레임 대기 대신, 활성 여부로 보이고 바로 배치한다(원작 차이).
    public sealed class OriginalPerkTreeView : MonoBehaviour
    {
        [SerializeField] TMP_Text _title = null!;
        // 원작 contentParent(Scroll View/Viewport/Content)다. 노드와 구분선 복제본이 여기에 놓인다.
        [SerializeField] RectTransform _content = null!;
        [SerializeField] OriginalPerkDetailsView _details = null!;
        // 원작 perkEntryPrefab이다. 원작처럼 별도 프리팹 자산(PerkEntry.prefab)이라 계층 밖에 있다.
        [SerializeField] OriginalPerkEntryView _entryTemplate = null!;
        // 원작 perkLinePrefab이다. 내용 안에 꺼진 채 저장된 템플릿이다.
        [SerializeField] OriginalPerkLineEntryView _lineTemplate = null!;
        [SerializeField] OriginalPerkLineGraphic _activeConnections = null!;
        [SerializeField] OriginalPerkLineGraphic _inactiveConnections = null!;
        // 원작 splineTangent 100, layoutFactor 3, padding (256, 256)이다. 제작 도구가 원작 값으로 저장한다.
        [SerializeField] float _splineTangent = 100f;
        [SerializeField] float _layoutFactor = 10f;
        [SerializeField] Vector2 _padding = Vector2.one;

        UiTemplateCollection<OriginalPerkEntryView>? _entries;
        UiTemplateCollection<OriginalPerkLineEntryView>? _lines;
        // 이번 채우기의 특성 번호별 노드다. 상태, 고름, 연결선이 번호로 노드를 찾는다.
        readonly Dictionary<int, OriginalPerkEntryView> _entryByPerk = new Dictionary<int, OriginalPerkEntryView>();
        readonly List<OriginalPerkEntryView> _placedEntries = new List<OriginalPerkEntryView>();
        readonly List<OriginalPerkLineEntryView> _placedLines = new List<OriginalPerkLineEntryView>();
        Action<int>? _entryClicked;
        bool _horizontal;

        // 원작 SetSelection(노드 클릭)이다. 값은 트리 정의의 특성 번호다.
        internal event Action<int>? EntrySelected;
        // 원작 View의 exitButton이다. ESC 닫기는 화면 연결(GameplayScreens)이 TryClose로 처리한다.
        internal event Action? CloseRequested;

        internal bool IsShown => gameObject.activeSelf;
        internal OriginalPerkDetailsView Details => _details;

        UiTemplateCollection<OriginalPerkEntryView> Entries =>
            _entries ??= new UiTemplateCollection<OriginalPerkEntryView>(_entryTemplate, entry => entry.Unbind());

        UiTemplateCollection<OriginalPerkLineEntryView> Lines =>
            _lines ??= new UiTemplateCollection<OriginalPerkLineEntryView>(_lineTemplate);

        internal void Show() => gameObject.SetActive(true);

        // 화면 닫기와 공용 HUD 분리가 부른다. 이미 숨었어도 불러도 된다.
        internal void Hide()
        {
            if (_entries != null) _entries.Clear();
            if (_lines != null) _lines.Clear();
            _entryByPerk.Clear();
            _placedEntries.Clear();
            _placedLines.Clear();
            _activeConnections.ClearConnections();
            _inactiveConnections.ClearConnections();
            gameObject.SetActive(false);
        }

        // 원작 Show_Local과 PopulatePerks를 셋으로 나눈다. BeginPopulate, AddEntry·AddLevelLine, EndPopulate는 같은 호출 안에서 짝을 이룬다.
        internal void BeginPopulate(string title, bool horizontal)
        {
            _title.text = title;
            _horizontal = horizontal;
            Entries.Begin();
            Lines.Begin();
            _entryByPerk.Clear();
            _placedEntries.Clear();
            _placedLines.Clear();
        }

        // 원작처럼 관계 그래프에 노드가 있는 특성만 넣는다(판정은 컨트롤러).
        internal void AddEntry(int perkIndex, Sprite? icon, string displayName, Vector2 layoutPosition)
        {
            OriginalPerkEntryView entry = Entries.Acquire(_content);
            entry.Bind(perkIndex, icon, displayName, layoutPosition, _entryClicked ??= EntryClicked);
            entry.gameObject.SetActive(true);
            _entryByPerk[perkIndex] = entry;
            _placedEntries.Add(entry);
        }

        internal void AddLevelLine(string label, Vector2 layoutPosition)
        {
            OriginalPerkLineEntryView line = Lines.Acquire(_content);
            line.Bind(label, layoutPosition);
            line.gameObject.SetActive(true);
            _placedLines.Add(line);
        }

        internal void EndPopulate()
        {
            Entries.End();
            Lines.End();
            FitChildren();
        }

        // 원작 PerkEntry.Refresh의 모습과 표시다. 노드가 없는 특성 번호는 무시한다.
        internal void SetEntryState(int perkIndex, PerkEntryLook look, bool availableForResearch, bool inProgress, bool timeUp)
        {
            if (_entryByPerk.TryGetValue(perkIndex, out OriginalPerkEntryView entry)) entry.SetState(look, availableForResearch, inProgress, timeUp);
        }

        // 원작 selectionIndicator다. -1은 고른 노드 없음이다.
        internal void SetSelection(int perkIndex)
        {
            foreach (OriginalPerkEntryView entry in _placedEntries) entry.SetSelected(entry.PerkIndex == perkIndex);
        }

        // 원작 RefreshConnections다. 두 선을 비운 뒤 AddConnection으로 채운다. 원작은 채우는 동안 렌더러를 껐다 켜지만,
        // 제품 그래픽은 넣을 때마다 다음 메시 갱신을 표시만 하므로 끄지 않는다.
        internal void BeginConnections()
        {
            _activeConnections.ClearConnections();
            _inactiveConnections.ClearConnections();
        }

        // 원작처럼 부모(from)가 해금됐으면 활성 선, 아니면 비활성 선에 넣는다. 위치는 원작처럼 노드의 내용 기준 localPosition이다.
        // 두 노드 중 하나라도 없으면 원작처럼 넣지 않고 false다.
        internal bool AddConnection(int fromPerk, int toPerk, bool active)
        {
            if (!_entryByPerk.TryGetValue(fromPerk, out OriginalPerkEntryView from) || !_entryByPerk.TryGetValue(toPerk, out OriginalPerkEntryView to)) return false;
            (active ? _activeConnections : _inactiveConnections).AddConnection(from.transform.localPosition, to.transform.localPosition, _horizontal, _splineTangent);
            return true;
        }

        // 고정 닫기 버튼의 저장된 Runtime Only OnClick이 부른다. 버튼은 열린 창 안에만 있다.
        public void OnExitClicked()
        {
            Log.Instance.Assert(IsShown, "특성 트리 창이 열린 동안에만 닫습니다.", this);
            CloseRequested?.Invoke();
        }

        void EntryClicked(int perkIndex) => EntrySelected?.Invoke(perkIndex);

        // 원작 FitChildren이다. 노드는 왼쪽 아래 기준으로 놓고, 가로 범위를 내용 폭(양쪽 여백 제외)에 맞춰 늘린다.
        // 구분선은 원작처럼 세로 이동(최소값 빼기) 없이 y만 정하고 x는 템플릿 값을 지키며 맨 앞 형제로 보낸다. 끝으로 스크롤을 처음으로 되돌린다.
        void FitChildren()
        {
            _content.ForceUpdateRectTransforms();
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (OriginalPerkEntryView entry in _placedEntries)
            {
                RectTransform rect = entry.RectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                Vector2 scaled = Scaled(entry.LayoutPosition, _layoutFactor);
                rect.anchoredPosition = scaled;
                min = Vector2.Min(min, scaled);
                max = Vector2.Max(max, scaled);
            }
            if (_placedEntries.Count > 0)
            {
                Vector2 size = _content.sizeDelta;
                size.y = max.y - min.y + _padding.y * 2f;
                _content.sizeDelta = size;
            }
            float width = _content.rect.width;
            foreach (OriginalPerkEntryView entry in _placedEntries)
                entry.RectTransform.anchoredPosition = Fit(entry.RectTransform.anchoredPosition, min, max, width, _padding);
            foreach (OriginalPerkLineEntryView line in _placedLines)
            {
                RectTransform rect = line.RectTransform;
                Vector2 position = Scaled(line.LayoutPosition, _layoutFactor) + _padding;
                position.x = rect.anchoredPosition.x;
                rect.anchoredPosition = position;
                rect.SetAsFirstSibling();
            }
            _content.anchoredPosition = Vector2.zero;
        }

        // 원작 배치의 첫 단계다. 그래프 y는 아래로 갈수록 커서 뒤집고 layoutFactor를 곱한다. 테스트가 원작 식과 비교한다.
        internal static Vector2 Scaled(Vector2 layoutPosition, float layoutFactor) => new Vector2(layoutPosition.x, -layoutPosition.y) * layoutFactor;

        // 원작 배치의 둘째 단계다. 최소값을 원점으로 옮기고, 가로 폭이 0이면 가운데, 아니면 (내용 폭 - 여백 × 2) / 가로 폭을 곱한 뒤 여백을 더한다.
        internal static Vector2 Fit(Vector2 scaled, Vector2 min, Vector2 max, float contentWidth, Vector2 padding)
        {
            Vector2 position = scaled - min;
            float span = max.x - min.x;
            float usable = contentWidth - padding.x * 2f;
            position.x = span == 0f ? usable / 2f : position.x * (usable / span);
            return position + padding;
        }
    }
}
