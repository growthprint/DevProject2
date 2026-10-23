#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원작 TaskEntry.Refresh가 읽는 과제 값이다. 화면 컨트롤러가 소유자 조회와 번역으로 만든다.
    internal readonly struct QuestTaskValues
    {
        internal QuestTaskValues(string description, IReadOnlyList<string>? extraDescriptions, Sprite? icon, bool finished,
            bool interactable, bool possibleValidInteraction, string interactText)
        {
            Description = description;
            ExtraDescriptions = extraDescriptions;
            Icon = icon;
            Finished = finished;
            Interactable = interactable;
            PossibleValidInteraction = possibleValidInteraction;
            InteractText = interactText;
        }

        internal string Description { get; }
        internal IReadOnlyList<string>? ExtraDescriptions { get; }
        internal Sprite? Icon { get; }
        internal bool Finished { get; }
        // 원작 Task.Interactable이다. 아이템 제출처럼 창구에서 누르는 과제만 참이다.
        internal bool Interactable { get; }
        // 원작 Task.PossibleValidInteraction이다. 거짓이면 버튼 대신 같은 글자의 자리표시를 보인다.
        internal bool PossibleValidInteraction { get; }
        internal string InteractText { get; }
    }

    // 원작 Duckov.Quests.UI.TaskEntry(TaskEntry.prefab)다. 과제 설명과 추가 설명, 아이콘, 완료 아이콘, 상호작용 버튼 또는 자리표시를 보인다.
    // 버튼은 저장된 Runtime Only OnClick이 OnInteractClicked를 부르고, 줄 번호를 상세 창(OriginalQuestDetailsView)에 넘긴다.
    // 원작 치트 강제 완료(Ctrl+Alt+Shift 클릭)는 옮기지 않는다.
    public sealed class OriginalTaskEntryView : MonoBehaviour
    {
        [SerializeField] Image _statusIcon = null!;
        [SerializeField] Image _taskIcon = null!;
        [SerializeField] TMP_Text _description = null!;
        [SerializeField] Button _interaction = null!;
        [SerializeField] GameObject _notInteractable = null!;
        [SerializeField] TMP_Text _interactionText = null!;
        [SerializeField] TMP_Text _placeholderText = null!;
        [SerializeField] Sprite _unsatisfiedIcon = null!;
        [SerializeField] Sprite _satisfiedIcon = null!;
        // 원작 TaskEntry.interactable이다. 상세 창이 진행 중이거나 완료한 퀘스트를 보일 때 켠다.
        [SerializeField] bool _interactable;

        Action<int>? _interact;
        int _index;

        internal bool Interactable
        {
            get => _interactable;
            set => _interactable = value;
        }

        // 원작 Setup과 Refresh다. 추가 설명은 원작처럼 "  \n- " 접두로 잇는다.
        internal void Bind(in QuestTaskValues values, int index, Action<int> interact)
        {
            _index = index;
            _interact = interact;
            string text = values.Description;
            IReadOnlyList<string>? extras = values.ExtraDescriptions;
            if (extras != null)
                for (int extra = 0; extra < extras.Count; extra++) text += "  \n- " + extras[extra];
            _description.text = text;
            bool hasIcon = values.Icon != null;
            if (hasIcon) _taskIcon.sprite = values.Icon;
            _taskIcon.gameObject.SetActive(hasIcon);
            _statusIcon.sprite = values.Finished ? _satisfiedIcon : _unsatisfiedIcon;
            bool open = _interactable && !values.Finished && values.Interactable;
            if (open)
            {
                _interactionText.text = values.InteractText;
                _placeholderText.text = values.InteractText;
            }
            _interaction.gameObject.SetActive(open && values.PossibleValidInteraction);
            _notInteractable.SetActive(open && !values.PossibleValidInteraction);
        }

        // 줄 모음이 다시 채우기 전에 앞 연결을 끊는다.
        internal void Unbind() => _interact = null;

        // 버튼은 Bind가 연결한 줄에서만 켜지고, 쓰지 않은 줄은 줄 모음이 숨기므로 확인 뒤 바로 부른다. 제출 판정과 실행은 화면 컨트롤러와 소유자가 한다.
        public void OnInteractClicked()
        {
            Log.Instance.Assert(_interact != null, "연결되지 않은 과제 줄의 버튼이 눌렸습니다.", this);
            _interact!(_index);
        }
    }
}
