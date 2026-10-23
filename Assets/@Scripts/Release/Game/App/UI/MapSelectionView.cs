#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.Core;
using TMPro;
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.UI.MapSelectionView(GameplayUICanvas/DestinationSelectionView, 순간이동기 지도 선택 창)다. 원작 화면에 고정된 항목 7개
    // (MapSelectionEntryView)와 목적지 확인 표시(원작 confirmIndicatorFadeGroup: 목적지 이름, 비용, 출발·취소 버튼)를 보인다.
    // 값만 받고 의도 이벤트만 낸다. 잠금, 비용 판정, 지불, 출격, 열고 닫기, 입력 차단은 지도 선택 화면 컨트롤러(MapSelectionScreen)가 맡는다.
    // 원작 FadeGroup 전환과 ColorPunch, 효과음 대신 활성 여부로 보이고 숨긴다(원작 차이). 고정 버튼은 저장된 Runtime Only OnClick이 부르고, 항목 클릭만 코드로 받는다.
    public sealed class MapSelectionView : MonoBehaviour
    {
        // 원작 항목들이다. 제작 도구가 원작 형제 순서로 넣는다.
        [SerializeField] MapSelectionEntryView[] _entries = Array.Empty<MapSelectionEntryView>();
        // 원작 confirmIndicatorFadeGroup 루트, destinationDisplayNameText, confirmCostDisplay, btnConfirm, btnCancel이다.
        [SerializeField] GameObject _confirmIndicator = null!;
        [SerializeField] TMP_Text _destination = null!;
        [SerializeField] OriginalCostDisplayView _confirmCost = null!;
        [SerializeField] GameObject _confirmButton = null!;
        [SerializeField] GameObject _cancelButton = null!;

        // 항목 클릭을 이 View의 의도 이벤트로 넘기는 대리자다. 항목마다 할당하지 않도록 한 번만 만든다.
        Action<int>? _clickEntry;

        // 원작 NotifyEntryClicked, btnConfirm, btnCancel, View.exitButton이다. 항목은 컨트롤러가 Bind로 준 데이터 번호를 넘긴다.
        internal event Action<int>? EntryClicked;
        internal event Action? ConfirmRequested;
        internal event Action? CancelRequested;
        internal event Action? CloseRequested;

        internal bool IsShown => gameObject.activeSelf;
        internal IReadOnlyList<MapSelectionEntryView> Entries => _entries;
        internal OriginalCostDisplayView ConfirmCost => _confirmCost;
        internal bool ConfirmShown => _confirmIndicator.activeSelf;

        Action<int> ClickEntry => _clickEntry ??= index => EntryClicked?.Invoke(index);

        // 원작 OnOpen이다. confirmIndicatorFadeGroup.SkipHide처럼 확인 표시를 숨긴 채 연다. 항목은 곧바로 컨트롤러가 채운다.
        internal void Show()
        {
            HideConfirm();
            gameObject.SetActive(true);
        }

        // 원작 OnClose다. 공용 HUD 초기화도 무조건 부르므로 숨어 있어도 된다. 항목 연결을 끊어 닫힌 뒤 눌린 항목이 의도를 보내지 않게 한다.
        internal void Hide()
        {
            foreach (MapSelectionEntryView entry in _entries) entry.ResetBinding();
            HideConfirm();
            gameObject.SetActive(false);
        }

        // 원작 MapSelectionEntry.Refresh다. 비용 줄 내용은 컨트롤러가 이어서 entry.Cost로 채운다.
        internal void BindEntry(MapSelectionEntryView entry, int index, bool locked, bool free) => entry.Bind(ClickEntry, index, locked, free);

        // 원작 LoadTask 앞부분이다. 취소 버튼을 켜고 목적지 이름과 비용을 보인 뒤 출발 버튼을 켠다. 원작은 확인 표시가 다 나타난 뒤 출발 버튼을 켜지만
        // 제품은 전환이 없어 바로 켠다. 비용 줄 내용은 컨트롤러가 이어서 ConfirmCost로 채운다.
        internal void ShowConfirm(MapSelectionEntryView entry, bool free)
        {
            _destination.text = entry.DisplayName;
            _destination.color = Color.white;
            _confirmCost.gameObject.SetActive(!free);
            _cancelButton.SetActive(true);
            _confirmButton.SetActive(true);
            _confirmIndicator.SetActive(true);
        }

        // 원작 WaitForConfirm 뒤 두 버튼을 끄는 자리다. 출발이 정해지면 확인 표시는 남기고 버튼만 끈다.
        internal void HideConfirmButtons()
        {
            _cancelButton.SetActive(false);
            _confirmButton.SetActive(false);
        }

        // 원작 confirmIndicatorFadeGroup.Hide다(취소, 비용 부족, 열기).
        internal void HideConfirm()
        {
            HideConfirmButtons();
            _confirmIndicator.SetActive(false);
        }

        // 원작 btnConfirm.onClick이다. 출발은 컨트롤러가 비용을 다시 확인하고 정한다.
        public void OnConfirmClicked()
        {
            Log.Instance.Assert(IsShown && ConfirmShown, "닫힌 지도 선택 확인 표시의 출발이 눌렸습니다.", this);
            ConfirmRequested?.Invoke();
        }

        // 원작 btnCancel.onClick이다.
        public void OnCancelClicked()
        {
            Log.Instance.Assert(IsShown && ConfirmShown, "닫힌 지도 선택 확인 표시의 취소가 눌렸습니다.", this);
            CancelRequested?.Invoke();
        }

        // 원작 View.exitButton이다. 원작 TryQuit처럼 확인 중이면 닫지 않는 판단은 컨트롤러가 한다.
        public void OnExitClicked()
        {
            Log.Instance.Assert(IsShown, "닫힌 지도 선택 창의 닫기가 눌렸습니다.", this);
            CloseRequested?.Invoke();
        }
    }
}
