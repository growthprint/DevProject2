#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.App
{
    // 원작 QuestEntry.Refresh가 읽는 값이다. 화면 컨트롤러가 소유자 조회로 만들고, View는 소유자를 읽지 않는다.
    internal readonly struct QuestEntryValues
    {
        internal QuestEntryValues(int questId, string name, string? location, bool needsInspection, bool claimable, bool selected)
        {
            QuestId = questId;
            Name = name;
            Location = location;
            NeedsInspection = needsInspection;
            Claimable = claimable;
            Selected = selected;
        }

        internal int QuestId { get; }
        internal string Name { get; }
        // 원작 RequireSceneInfo의 표시 이름이다. 비어 있으면 장소 제한이 없어 원작처럼 anyLocationKey 문구를 보인다.
        internal string? Location { get; }
        internal bool NeedsInspection { get; }
        // 원작 claimableIndicator 조건(Complete || AreTasksFinished)이다.
        internal bool Claimable { get; }
        internal bool Selected { get; }
    }

    // 원작 Duckov.Quests.UI.QuestEntry(QuestEntry.prefab)다. 퀘스트 하나의 이름, 네 자리 번호, 장소, 빨간 점, 받을 보상 표시,
    // 선택 표시를 보인다. 클릭은 퀘스트 번호로 창(OriginalQuestGiverView)에 넘기고, 선택 판정은 화면 컨트롤러가 한다.
    public sealed class OriginalQuestEntryView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] GameObject _selection = null!;
        [SerializeField] TMP_Text _name = null!;
        [SerializeField] TMP_Text _location = null!;
        // 원작 QuestEntry.anyLocationKey(UI_Location_Any)다. 제작 도구가 원작 값을 옮긴다.
        [SerializeField] string _anyLocationKey = "UI_Location_Any";
        [SerializeField] GameObject _redDot = null!;
        [SerializeField] GameObject _claimable = null!;
        [SerializeField] TMP_Text _questId = null!;

        Action<int>? _clicked;

        internal int QuestId { get; private set; }

        // 원작 Setup과 Refresh다. 번호는 원작 형식 0000이다.
        internal void Bind(in QuestEntryValues values, Action<int> clicked)
        {
            QuestId = values.QuestId;
            _clicked = clicked;
            _selection.SetActive(values.Selected);
            _name.text = values.Name;
            _questId.text = values.QuestId.ToString("0000", CultureInfo.InvariantCulture);
            _location.text = string.IsNullOrEmpty(values.Location) ? GameLocalization.Text(_anyLocationKey) : values.Location;
            _redDot.SetActive(values.NeedsInspection);
            _claimable.SetActive(values.Claimable);
        }

        // 줄 모음(UiTemplateCollection)이 다시 채우기 전에 앞 연결을 끊는다. 숨은 줄은 클릭을 받지 않는다.
        internal void Unbind() => _clicked = null;

        // 원작 QuestGiverView.RefreshEntries와 QuestView.RefreshEntryList다. 퀘스트 창구와 일지가 같은 줄 채우기를 쓴다.
        // 순서와 선택은 화면 컨트롤러가 정하고, 비면 자리표시를 켠다.
        internal static void Fill(UiTemplateCollection<OriginalQuestEntryView> entries, Transform parent, GameObject placeholder,
            IReadOnlyList<QuestEntryValues> quests, Action<int> clicked)
        {
            entries.Begin();
            for (int index = 0; index < quests.Count; index++)
            {
                OriginalQuestEntryView entry = entries.Acquire(parent);
                entry.gameObject.SetActive(true);
                entry.Bind(quests[index], clicked);
            }
            entries.End();
            placeholder.SetActive(quests.Count == 0);
        }

        public void OnPointerClick(PointerEventData eventData) => _clicked?.Invoke(QuestId);
    }
}
