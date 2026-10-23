#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 ItemShortcutButton처럼 퀵슬롯 칸의 아이템을 공용 HUD에 보이고 숫자키 사용이 고를 칸 아이템을 찾는다.
    // 원본 WeaponButtons의 장착 무기 아이콘도 함께 보인다. 전투 월드 컨트롤러가 준비 때 만들어 소유하며, 가진 상태는 마지막으로 보낸 표시값뿐이라
    // 따로 정리하지 않는다. 칸 아이템은 확정 세션의 소유 그래프에서 읽는다.
    internal sealed class QuickSlotHud
    {
        readonly GameplayWorldContext _world;
        // 퀵슬롯 6칸과 장착 무기 3칸마다 마지막으로 HUD에 보낸 값이다. 바뀐 칸만 HUD를 다시 부르고, 첫 표시와 로캘 변경 때는 전부 다시 그린다.
        // 퀵슬롯 칸 수는 저장 퀵슬롯이 정한 값 하나를 따른다.
        readonly OriginalItemDefinition?[] _definitions = new OriginalItemDefinition?[ItemShortcutState._slotCount];
        readonly int[] _counts = new int[ItemShortcutState._slotCount];
        readonly Sprite?[] _weaponIcons = new Sprite?[GameplayWeaponSlots.All.Count];
        // 퀵슬롯 6칸마다 마지막으로 보낸 손에 든 표시와, 그 표시를 계산한 손의 아이템, 퀵슬롯, 그래프다. 셋이 그대로면 다시 계산하지 않는다.
        readonly bool[] _using = new bool[ItemShortcutState._slotCount];
        string? _held;
        ItemShortcutState? _heldShortcuts;
        ItemOwnershipState? _heldItems;

        internal QuickSlotHud(GameplayWorldContext world)
        {
            _world = world!;
        }

        // 퀵슬롯 6칸과 장착 무기 3칸을 확정 세션의 소유 그래프에서 읽어 표시한다. 칸마다 마지막으로 보낸 값과 비교해
        // 바뀐 칸만 HUD에 보낸다. all이면 첫 표시나 로캘 변경이라 9칸을 모두 다시 그린다.
        // weapons가 false면 발사처럼 수량만 바뀐 알림이라 장착 슬롯이 그대로이므로 무기 3칸은 읽지 않는다.
        internal void Refresh(GameplayPersistenceState gameplay, bool all, bool weapons)
        {
            // 플레이어 루트는 갱신마다 한 번만 찾아 9칸 질의에 넘긴다. 소유 그래프가 없으면 초기화 전이라 표시할 것이 없다.
            ItemOwnershipState? items = gameplay.Inventory.Items;
            if (items == null) return;
            // 전부 다시 그리는 갱신은 HUD가 새로 붙은 때라, 손에 든 표시도 다음 ShowHeld가 처음부터 다시 보내게 한다.
            if (all) { _held = null; _heldShortcuts = null; _heldItems = null; System.Array.Clear(_using, 0, _using.Length); }
            ItemShortcutState? shortcuts = gameplay.Inventory.Shortcuts;
            ItemInstanceState? player = OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? root) ? root : null;
            // 공용 HUD는 컨텍스트만 가지고 있어, 9칸을 보내는 동안 상태 표시 부분을 한 번만 읽는다.
            HudStatusDisplay status = _world.Hud.Status;
            // 칸 수가 저장 퀵슬롯과 같아야 칸 조회가 저장된 칸 색인 범위 안만 읽는다.
            for (int i = 0; i < ItemShortcutState._slotCount; i++)
            {
                // 칸 조회 결함은 칸마다 한 번 기록하고 빈 칸으로 표시한다. 정의 조회가 실패하면 out 값이 null이라 빈 칸이고 수량도 0이다.
                OriginalItemDefinition? definition = null;
                int count = 0;
                if (TryEntry(items, shortcuts, player, i, out ItemInstanceState? item) && item != null &&
                    _world.Catalog.TryGet(item.OriginalItemId, out definition))
                    count = item.Quantity;
                // 정의와 수량이 같으면 이름 번역도 건너뛰고 이 칸을 다시 그리지 않는다.
                if (!all && ReferenceEquals(_definitions[i], definition) && _counts[i] == count) continue;
                _definitions[i] = definition;
                _counts[i] = count;
                // 공용 HUD 상태 표시의 원본 ItemShortCut 칸에 아이콘, 이름, 수량을 보낸다. 이름은 현재 로캘로 번역한다.
                status.ShowQuickSlot(i, definition?.Icon, definition != null ? GameLocalization.Text(definition.NameKey) : "", count);
            }
            // 장착 무기 아이콘은 선택 중인 무기와 관계없이 공용 HUD의 원본 WeaponButtons 순서로 모두 표시한다.
            // 정의가 없으면 아이콘 없이 표시한다. 장착 슬롯이 그대로인 알림은 건너뛴다.
            if (!weapons) return;
            for (int i = 0; i < GameplayWeaponSlots.All.Count; i++)
            {
                Sprite? icon = null;
                // 저장 읽기 검사가 플레이어 루트를 요구하고 명령은 지우지 않으므로 칸 조회는 실패하지 않는다.
                OriginalInventoryQueries.TryEquippedIn(player, GameplayWeaponSlots.All[i], out string? equipped);
                if (equipped != null)
                {
                    ItemInstanceState? weapon = items.Find(equipped);
                    // 검증된 소유 그래프의 장착 슬롯은 그래프에 있는 아이템만 가리킨다.
                    if (_world.Catalog.TryGet(weapon.OriginalItemId, out OriginalItemDefinition? weaponDefinition)) icon = weaponDefinition!.Icon;
                }
                if (!all && ReferenceEquals(_weaponIcons[i], icon)) continue;
                _weaponIcons[i] = icon;
                status.ShowWeaponIcon(i, icon);
            }
        }

        // 원본 ItemShortcutButton.Update처럼 손에 든 아이템을 가리키는 칸에만 UseIndicator를 켠다. 컨트롤러가 매 프레임 부르며,
        // 손의 아이템, 퀵슬롯, 소유 그래프가 그대로면 아무것도 하지 않는다. 바뀐 칸만 HUD에 보낸다.
        internal void ShowHeld(GameplayPersistenceState gameplay, string? held)
        {
            ItemOwnershipState? items = gameplay.Inventory.Items;
            ItemShortcutState? shortcuts = gameplay.Inventory.Shortcuts;
            if (held == _held && ReferenceEquals(shortcuts, _heldShortcuts) && ReferenceEquals(items, _heldItems)) return;
            _held = held; _heldShortcuts = shortcuts; _heldItems = items;
            if (items == null) return;
            ItemInstanceState? player = OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? root) ? root : null;
            HudStatusDisplay status = _world.Hud.Status;
            for (int i = 0; i < ItemShortcutState._slotCount; i++)
            {
                bool used = held != null && TryEntry(items, shortcuts, player, i, out ItemInstanceState? entry) && entry != null && entry.InstanceId == held;
                if (_using[i] == used) continue;
                _using[i] = used;
                status.ShowQuickSlotUsing(i, used);
            }
        }

        // 숫자키 사용에서 확정 세션의 퀵슬롯 칸 아이템을 던지지 않고 찾는다. 퀵슬롯이 없거나, 빈 칸이거나,
        // 그래프 초기화 전이면 true와 null이다. 구조 결함이면 칸마다 한 번 기록하고 false다.
        internal bool TryEntry(GameplayPersistenceState gameplay, int slot, out ItemInstanceState? entry)
        {
            entry = null;
            ItemOwnershipState? items = gameplay.Inventory.Items;
            if (items == null) return true;
            ItemInstanceState? player = OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? root) ? root : null;
            return TryEntry(items, gameplay.Inventory.Shortcuts, player, slot, out entry);
        }

        // 호출자가 찾아 둔 그래프, 퀵슬롯, 플레이어 루트로 칸 아이템을 찾는다. 퀵슬롯 표시가 갱신마다 루트를 한 번만 찾게 한다.
        // 구조 결함이면 칸마다 한 번 기록하고 false다.
        bool TryEntry(ItemOwnershipState items, ItemShortcutState? shortcuts, ItemInstanceState? player, int slot, out ItemInstanceState? entry)
        {
            if (OriginalInventoryQueries.TryShortcutEntry(items, shortcuts, slot, player, out entry)) return true;
            _world.ReportDefectOnce("shortcut:" + slot, "퀵슬롯 칸이 가리키는 소유 아이템을 읽을 수 없습니다(소유 그래프 구조 결함): " + slot);
            return false;
        }
    }
}
