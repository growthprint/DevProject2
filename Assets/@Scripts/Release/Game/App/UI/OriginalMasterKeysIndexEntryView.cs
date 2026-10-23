#nullable enable annotations
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.App
{
    // 열쇠 도감 줄 하나의 값이다. 등록하지 않은 열쇠는 이름과 설명을 찾지 않고 빈 글자로 넘기며, 줄과 상세가 원작처럼 ???를 보인다.
    internal readonly struct MasterKeyIndexEntryValues
    {
        internal MasterKeyIndexEntryValues(int itemId, bool active, string name, string description, Sprite? icon)
        {
            ItemId = itemId;
            Active = active;
            Name = name;
            Description = description;
            Icon = icon;
        }

        internal int ItemId { get; }
        // 원작 Status.active다. 제품 등록기는 등록한 열쇠만 기록하므로 기록이 있으면 늘 참이다.
        internal bool Active { get; }
        internal string Name { get; }
        internal string Description { get; }
        // 원작 metaData.icon이다. 등록하지 않은 줄도 원작처럼 미발견 그림이 없을 때 이 아이콘으로 물러나므로 늘 넘긴다.
        internal Sprite? Icon { get; }
    }

    // 원작 Duckov.MasterKeys.UI.MasterKeysIndexEntry(KeyEntry)다. 열쇠 아이콘과 이름을 보이고, 등록하지 않은 열쇠는 미발견 그림,
    // 흐린 색, ???로 보인다. 클릭은 도감 창(OriginalMasterKeysIndexView)에 넘긴다. 원작 Look의 재질은 두 값 모두 비어 있어 색만 옮긴다.
    // 원작 클릭 흔들림(PunchReceiver)과 호버 표시 컴포넌트는 옮기지 않았다.
    public sealed class OriginalMasterKeysIndexEntryView : MonoBehaviour, IPointerClickHandler
    {
        // 원작 Entry.DisplayName과 Description이 등록하지 않은 열쇠에 보이는 글자다. 줄과 도감 상세가 같은 글자를 쓴다.
        internal const string _undiscoveredText = "???";

        [SerializeField] Image _icon = null!;
        [SerializeField] TMP_Text _name = null!;
        // 원작 notDiscoveredLook.color, activeLook.color, undiscoveredIcon이다. 제작 도구가 원본 값을 옮긴다.
        [SerializeField] Color _notDiscoveredColor = Color.white;
        [SerializeField] Color _activeColor = Color.white;
        [SerializeField] Sprite _undiscoveredIcon = null!;

        Action<int>? _clicked;
        int _itemId;

        // 원작 MasterKeysIndexEntry.Icon처럼 상세 창도 등록하지 않은 열쇠에 이 그림을 보인다. 그림은 템플릿 한 곳에만 둔다.
        internal Sprite UndiscoveredIcon => _undiscoveredIcon;

        // 원작 SetupActive와 SetupNotDiscovered다. 원작 Look.ApplyTo처럼 아이콘 색을 바꾼다.
        internal void Bind(in MasterKeyIndexEntryValues values, Action<int> clicked)
        {
            _itemId = values.ItemId;
            _clicked = clicked;
            if (values.Active)
            {
                _icon.sprite = values.Icon;
                _icon.color = _activeColor;
                _name.text = values.Name;
                return;
            }
            _icon.sprite = _undiscoveredIcon != null ? _undiscoveredIcon : values.Icon;
            _icon.color = _notDiscoveredColor;
            // 도감 상세와 같은 미발견 글자다.
            _name.text = _undiscoveredText;
        }

        // 목록을 다시 채우거나 창을 닫을 때 앞 클릭 대상을 끊는다.
        internal void Unbind() => _clicked = null;

        public void OnPointerClick(PointerEventData eventData) => _clicked?.Invoke(_itemId);
    }
}
