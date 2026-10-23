#nullable enable
using System.Collections.Generic;

namespace Game.App
{
    // 원본 ItemSetting_Gun의 목표 탄종과 BulletTypeHUD의 탄종 목록을 맡는다. 전투 시뮬레이션이 하나를 가지고, 장전과 탄창 비우기는 시뮬레이션이 한다.
    // 목표 탄종은 원본처럼 저장하지 않는다. 총을 처음 볼 때 탄창의 탄종으로 정하고, 씬이 끝날 때까지 총 인스턴스마다 기억한다.
    internal sealed class BulletTypeSwitch
    {
        // 원본 targetBulletID의 "정하지 않음" 값이다. 목록에서는 원본처럼 "탄약 미장전" 항목이 된다.
        internal const int _none = -1;
        // 원본 "탄약 미장전" 번역 키다. 탄종 목록, 탄약 HUD, 아이템 상세 화면이 같은 키를 쓰도록 여기 한곳에 둔다.
        internal static string NotAssignedKey => "UI_Bullet_NotAssigned";
        readonly GameplayWorldContext _world;
        readonly Dictionary<string, int> _targets = new();
        // 열린 목록의 탄종, 소지 수, 이름이다. 원본 GetBulletTypesInInventory처럼 소지품에서 처음 나온 탄종 순서다.
        readonly List<int> _types = new();
        readonly List<int> _counts = new();
        readonly List<string> _names = new();
        int _selected;

        internal BulletTypeSwitch(GameplayWorldContext world) => _world = world;

        internal bool IsOpen { get; private set; }

        // 목록에서 고른 탄종이다. 목록은 열 때 늘 한 항목 이상이다.
        internal int Selected => _types[_selected];

        // 원본 ItemSetting_Gun.Start의 AutoSetTypeInInventory(null)처럼 처음 본 총은 탄창의 첫 탄 탄종을, 빈 탄창이면 정하지 않음을 목표로 둔다.
        // 탄창을 읽을 수 없는 그래프 결함도 정하지 않음으로 두며, 결함 기록은 탄약 HUD와 발사가 한다.
        internal int Target(ItemOwnershipState items, string weaponId)
        {
            if (_targets.TryGetValue(weaponId, out int target)) return target;
            OriginalInventoryQueries.TryLoadedAmmo(items, weaponId, out ItemInstanceState? loaded);
            target = loaded != null ? loaded.OriginalItemId : _none;
            _targets.Add(weaponId, target);
            return target;
        }

        internal void SetTarget(string weaponId, int type) => _targets[weaponId] = type;

        // 원본 BulletTypeHUD.RefreshContent처럼 소지품에서 총과 구경이 같은 탄종을 모으고, 지금 목표가 없으면 소지 0으로 끝에 붙인다.
        // 하나도 없으면 "탄약 미장전" 항목 하나를 두고, 지금 목표 탄종을 고른 채 HUD에 보인다.
        internal void Open(ItemOwnershipState items, OriginalGameplayCatalog catalog, OriginalItemDefinition weapon, int target)
        {
            _types.Clear(); _counts.Clear(); _names.Clear();
            // 그래프 검증이 플레이어 루트와 소지 인벤토리를 보장하고, 저장 읽기 검사가 모르는 ID를 막아 칸의 아이템과 정의는 늘 있다.
            // 그래서 정의는 조회 결과를 보지 않는 DefinitionOf로 읽는다.
            OriginalInventoryQueries.TryPlayerItem(items, out ItemInstanceState? player);
            IReadOnlyList<ItemPositionState> entries = player!.Inventory!.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                ItemInstanceState item = items.Find(entries[i].InstanceId)!;
                OriginalItemDefinition definition = catalog.DefinitionOf(item.OriginalItemId);
                if (!definition.IsBullet || definition.Caliber != weapon.Caliber) continue;
                int index = _types.IndexOf(item.OriginalItemId);
                if (index >= 0) _counts[index] += item.Quantity;
                else Add(catalog, item.OriginalItemId, item.Quantity);
            }
            if (target > 0 && !_types.Contains(target)) Add(catalog, target, 0);
            if (_types.Count == 0) Add(catalog, _none, 0);
            _selected = _types.IndexOf(target);
            if (_selected < 0) _selected = 0;
            IsOpen = true;
            _world.Hud.Status.ShowBulletTypes(_names, _counts, _selected);
        }

        // 원본 OnSwitchInput처럼 위 방향(+1)은 앞 항목, 아래 방향(-1)은 뒤 항목을 고르고 양 끝에서 반대쪽 끝으로 넘어간다.
        internal void Move(int direction)
        {
            _selected -= direction;
            if (_selected >= _types.Count) _selected = 0;
            else if (_selected < 0) _selected = _types.Count - 1;
            _world.Hud.Status.SelectBulletType(_selected);
        }

        // 원본 CloseList다. 씬 정리 중에는 공용 HUD가 다른 월드에 붙었을 수 있어, 이 월드에 붙어 있을 때만 목록을 숨긴다.
        internal void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (_world.PresentationAttached) _world.Hud.Status.HideBulletTypes();
        }

        // 씬 정리 때 목록을 닫고, 원본처럼 저장하지 않는 목표 탄종을 잊는다.
        internal void Release()
        {
            Close();
            _targets.Clear();
        }

        void Add(OriginalGameplayCatalog catalog, int type, int count)
        {
            _types.Add(type);
            _counts.Add(count);
            // 목록 탄종은 그래프의 아이템이나 그 아이템에서 정한 목표라 카탈로그 정의가 늘 있다. 정하지 않음은 "탄약 미장전" 키를 쓴다.
            _names.Add(GameLocalization.Text(type != _none && catalog.TryGet(type, out OriginalItemDefinition? definition)
                ? definition.NameKey : NotAssignedKey));
        }
    }
}
