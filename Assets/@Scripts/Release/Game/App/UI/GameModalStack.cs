#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 제품 모달 식별자다. 갱신마다 여러 번 비교하므로 문자열이 아닌 열거형으로 둔다.
    internal enum GameModalId { Pause, Loading, Error, Overwrite, Options, SaveSlots, Map }

    // 열린 모달을 쌓아 두고 맨 위 모달만 입력을 받게 한다. 같은 모달은 두 번 열지 않고, 닫힐 때 선택을 되돌린다.
    internal sealed class GameModalStack
    {
        // 열린 모달 목록이다. 마지막 항목이 맨 위이며 그 창만 입력을 받는다.
        readonly List<Entry> _entries = new List<Entry>();
        // 모든 모달이 닫힌 뒤 되돌릴 선택이다. 뒤 메뉴의 입력 잠금이 풀려 선택에 성공할 때까지 보관한다.
        Selectable? _pendingReturnFocus;

        public int Count => _entries.Count;
        public GameModalId? TopId => _entries.Count == 0 ? (GameModalId?)null : _entries[_entries.Count - 1].Id;
        // 모달이 실제로 열리거나 닫힐 때 식별자를 알린다. 원본 ManagedUIElement.onOpen/onClose에 해당한다.
        // 화면이 끝날 때 Clear로 한꺼번에 닫는 것은 사용자 활동이 아니라서 알리지 않는다.
        public event Action<GameModalId>? Changed;

        // 모달을 열어 스택에 넣는다. underneath면 맨 아래에 넣는다. 갱신마다 같은 열기를 반복하므로 이미 열린 식별자는 false다.
        // View는 직렬화된 참조이고, 모달 루트의 CanvasGroup은 ProductUiAssetTests가 프리팹과 메뉴 씬에서 확인한다.
        public bool Open(GameModalId id, GameObject view, Action? cancel, Selectable? returnFocus,
            Selectable? initialFocus, bool underneath = false)
        {
            if (Contains(id)) return false;

            // 입력 그룹은 프리팹이 소유하므로, 빠져 있어도 실행 중에 만들어 붙이지 않는다.
            CanvasGroup group = view.GetComponent<CanvasGroup>();
            // 새 모달이 선택을 가져가므로, 모든 창이 닫힌 뒤로 미뤄 둔 선택 복원은 취소한다.
            _pendingReturnFocus = null;
            int index = underneath ? 0 : _entries.Count;
            _entries.Insert(index, new Entry(id, view, group, cancel, returnFocus, initialFocus));
            view.SetActive(true);
            if (underneath) view.transform.SetAsFirstSibling();
            else view.transform.SetAsLastSibling();
            RefreshEnabled();
            // 맨 위로 연 모달만 기본 선택에 포커스를 둔다. 아래에 넣은 모달이 맨 위 창의 선택을 빼앗지 않게 한다.
            if (index == _entries.Count - 1) UiFocus.TryFocus(initialFocus);
            NotifyChanged(id);
            return true;
        }

        public bool Contains(GameModalId id) => IndexOf(id) >= 0;

        // 갱신마다 불리므로 람다 클로저를 만드는 FindIndex 대신 for 루프로 찾는다. 없으면 -1이다.
        int IndexOf(GameModalId id)
        {
            for (int index = 0; index < _entries.Count; index++)
                if (_entries[index].Id == id) return index;
            return -1;
        }

        // 맨 위 모달의 취소 동작과 기본 선택만 바꾸고, 맨 위가 아니면 false다. 닫았다 다시 열지 않으므로
        // 사용자가 옮긴 선택과 복귀 선택이 남고 열림 닫힘 알림도 없다. 그래서 오류 창이 갱신마다 선택을 잃지 않는다.
        public bool TryUpdateTop(GameModalId id, Action? cancel, Selectable? initialFocus)
        {
            if (_entries.Count == 0) return false;
            Entry top = _entries[_entries.Count - 1];
            if (top.Id != id) return false;
            top.Cancel = cancel;
            top.InitialFocus = initialFocus;
            return true;
        }

        // 갱신으로 선택된 컨트롤이 숨거나 잠기면 키보드와 패드 탐색이 멈춘다. 그래서 선택이 맨 위 모달 안의 쓸 수 있는
        // 컨트롤이 아니면 기본 선택으로 되돌린다. 기본 선택도 지금 쓸 수 없으면 다음 갱신에서 다시 시도한다.
        public void RecoverTopFocus()
        {
            if (_entries.Count == 0 || EventSystem.current == null) return;
            Entry top = _entries[_entries.Count - 1];
            GameObject? selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(top.View.transform) &&
                UiFocus.CanFocus(selected.GetComponent<Selectable>()))
                return;
            UiFocus.TryFocus(top.InitialFocus);
        }

        // 맨 위 창의 취소 동작을 실행한다. 설정 창이나 선택을 강제하는 오류 창처럼 취소 동작이 없는 창은
        // 취소를 소비만 하고 아래 창으로 넘기지 않는다.
        public bool CancelTop()
        {
            if (_entries.Count == 0) return false;
            Entry entry = _entries[_entries.Count - 1];
            if (entry.Cancel != null)
            {
                Remove(entry.Id);
                entry.Cancel();
            }
            return true;
        }

        // 모달을 닫기만 하고 취소 동작은 실행하지 않는다. 상태 변경으로 닫는 창이 사용자 취소를 다시 부르지 않게 한다.
        public bool Remove(GameModalId id)
        {
            int index = IndexOf(id);
            if (index < 0) return false;
            Entry entry = _entries[index];
            bool wasTop = index == _entries.Count - 1;
            _entries.RemoveAt(index);
            Hide(entry);
            RefreshEnabled();
            if (wasTop) RestoreFocus(entry.ReturnFocus);
            NotifyChanged(id);
            return true;
        }

        // 구독자마다 따로 알린다. 한 구독자가 실패해도 ObserverDispatch가 기록만 하고 모달 상태 변경은 되돌리지 않는다.
        void NotifyChanged(GameModalId id) => ObserverDispatch.InvokeEach(Changed, () => id);

        // 바인딩이 끝날 때 화면만 정리한다. 남은 취소 동작은 낡은 게임 명령이라 실행하지 않는다.
        public void Clear()
        {
            Selectable? returnFocus = _entries.Count == 0 ? null : _entries[0].ReturnFocus;
            foreach (Entry entry in _entries) Hide(entry);
            _entries.Clear();
            RestoreFocus(returnFocus);
            // 선택 복원은 바로 한 번만 시도하고, 화면이 끝난 뒤로 미뤄 두지 않는다.
            _pendingReturnFocus = null;
        }

        // 보이는 하위 모달은 유지하되 최상위 CanvasGroup만 포인터와 키보드 입력을 받는다.
        void RefreshEnabled()
        {
            for (int index = 0; index < _entries.Count; index++)
            {
                Entry entry = _entries[index];
                bool top = index == _entries.Count - 1;
                entry.Group.alpha = 1f;
                entry.Group.interactable = top;
                entry.Group.blocksRaycasts = top;
            }
        }

        // 제거한 모달은 비활성화하여 숨은 Selectable이 탐색 대상으로 남지 않게 한다.
        static void Hide(Entry entry)
        {
            // 앱 종료 때 메뉴 씬이 UI 해제보다 먼저 내려가면 메뉴 창이 이미 파괴돼 있다. 파괴된 창은 숨길 것이 없으므로 건너뛴다.
            if (entry.View == null || entry.Group == null) return;
            entry.Group.interactable = false;
            entry.Group.blocksRaycasts = false;
            entry.Group.alpha = 0f;
            entry.View.SetActive(false);
        }

        // 닫힌 창이 기억한 복귀 선택이 맨 위 창 안에 있으면 그것을, 아니면 맨 위 창의 기본 선택을 고른다.
        void RestoreFocus(Selectable? previous)
        {
            if (_entries.Count > 0)
            {
                Entry top = _entries[_entries.Count - 1];
                if (UiFocus.CanFocus(previous) && previous!.transform.IsChildOf(top.View.transform)) UiFocus.TryFocus(previous);
                else UiFocus.TryFocus(top.InitialFocus);
            }
            // 모든 창이 닫혔으면 복원을 예약하고 바로 시도한다. 부모 CanvasGroup 잠금으로 실패하면 컨트롤러가 잠금을 푼 뒤
            // RestorePendingFocus로 다시 돌려준다.
            else { _pendingReturnFocus = previous; RestorePendingFocus(); }
        }

        // 모든 창이 닫힌 뒤 미뤄 둔 선택을 복원한다. 컨트롤러가 표시와 입력 잠금을 갱신한 뒤 부른다.
        // 선택에 실패하면 예약을 남겨 다음 갱신에서 다시 시도한다.
        public void RestorePendingFocus()
        {
            if (_entries.Count == 0 && UiFocus.TryFocus(_pendingReturnFocus)) _pendingReturnFocus = null;
        }

        // 열린 모달 하나의 등록 정보다. 취소 동작과 기본 선택은 TryUpdateTop이 바꿀 수 있다.
        sealed class Entry
        {
            public GameModalId Id { get; }
            public GameObject View { get; }
            public CanvasGroup Group { get; }
            public Action? Cancel { get; set; }
            public Selectable? ReturnFocus { get; }
            public Selectable? InitialFocus { get; set; }

            public Entry(GameModalId id, GameObject view, CanvasGroup group, Action? cancel,
                Selectable? returnFocus, Selectable? initialFocus)
            {
                Id = id;
                View = view;
                Group = group;
                Cancel = cancel;
                ReturnFocus = returnFocus;
                InitialFocus = initialFocus;
            }
        }
    }
}
