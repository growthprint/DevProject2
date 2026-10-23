#nullable enable annotations
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 ItemShortcutPanel의 퀵슬롯이다. 원본 HUD처럼 소비품 아이콘은 확정된 스냅샷으로만 채우고,
    // 인벤토리가 열린 동안에는 인벤토리 화면 옆으로 옮긴다. 프리팹 참조는 SharedHudView의 직렬화 필드를 직접 읽는다.
    internal sealed class HudQuickSlots
    {
        // 인벤토리가 열린 동안 퀵슬롯을 옮겨 두는 인벤토리 화면 옆 위치다. SharedHudTests가 같은 값을 확인한다.
        static readonly Vector2 _inventoryOpenPosition = new(-360, 30);

        readonly SharedHudView _hud;
        // 프롤로그처럼 HUD의 Awake보다 캐릭터 연결이 먼저 와도, 첫 배치 때 퀵슬롯의 원래 위치를 기록해 둔다.
        Vector2 _hudPosition;
        bool _positionKnown;

        internal HudQuickSlots(SharedHudView hud) => _hud = hud;

        // 소유자가 바뀔 때 앞 씬의 퀵슬롯 표시를 비운다. 원래 위치는 프리팹 배치이므로 기록을 유지한다.
        internal void Reset()
        {
            foreach (TMP_Text label in _hud._quickLabels) label.text = "";
            foreach (TMP_Text quickName in _hud._quickNames) quickName.text = "";
            // 빈 아이콘은 Show와 같은 규칙으로 스프라이트를 비우고 끈다.
            foreach (Image icon in _hud._quickIcons) UiVisibility.SetIcon(icon, null);
            foreach (GameObject indicator in _hud._quickUsing) indicator.SetActive(false);
        }

        // 원본 ItemShortcutButton.Update처럼 손에 든 아이템이 이 칸의 아이템인 동안 UseIndicator를 켠다. 원본은 notInteractableIndicator도
        // 같은 객체를 가리켜 매 프레임 이 값으로 덮이므로, 쓸 수 없는 아이템 표시는 따로 두지 않는다.
        internal void ShowUsing(int index, bool used) => _hud._quickUsing[index].SetActive(used);

        // 빈 슬롯은 원본 배경만 남기고 아이템 아이콘과 이름, 수량을 함께 숨긴다.
        // 호출자는 저작된 여섯 칸 범위의 색인만 넘기므로 배열 범위를 검사하지 않는다.
        internal void Show(int index, Sprite? icon, string name, int count)
        {
            _hud._quickLabels[index].text = count > 1 ? count.ToString() : "";
            _hud._quickNames[index].text = name;
            // 아이콘이 없으면 이미지를 꺼 빈 흰 사각형이 보이지 않게 한다. 다른 아이콘 칸과 같은 규칙을 쓴다.
            UiVisibility.SetIcon(_hud._quickIcons[index], icon);
        }

        // 인벤토리가 처음 열리기 전의 원래 위치를 한 번만 기록해, 닫을 때 그 위치로 되돌린다.
        // 열린 동안 이 퀵슬롯이 원본 ItemShortcutEditorPanel 역할이라 셀을 놓을 수 있게 레이캐스트를 받고, 닫으면 원본 HUD처럼 다시 막지 않는다.
        internal void Place(bool inventoryOpen)
        {
            RectTransform root = _hud._quickRoot;
            if (!_positionKnown) { _hudPosition = root.anchoredPosition; _positionKnown = true; }
            // 열린 위치는 프리팹 배치와 상관없는 고정 위치라, 기록한 원래 위치와 따로 둔다.
            root.anchoredPosition = inventoryOpen ? _inventoryOpenPosition : _hudPosition;
            _hud._quickGroup.blocksRaycasts = inventoryOpen;
        }
    }
}
