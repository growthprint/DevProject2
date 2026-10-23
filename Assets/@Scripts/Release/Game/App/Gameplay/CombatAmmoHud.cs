#nullable enable

namespace Game.App
{
    // 원본 BulletCountHUD와 BulletTypeHUD처럼 든 총의 탄창 수 문자열, 소지 수 문자열, 탄약 이름 표시를 맡는다. 전투 시뮬레이션이 하나를 가진다.
    // 든 무기는 시뮬레이션이 매번 넘기고, 이 클래스는 문자열 캐시만 가진다.
    internal sealed class CombatAmmoHud
    {
        readonly GameplayWorldContext _world;
        // 원본처럼 탄약 이름과 소지 수는 총의 목표 탄종을 따른다. 목표는 같은 시뮬레이션이 가진 탄종 선택이 기억한다.
        readonly BulletTypeSwitch _bulletTypes;
        // 두 문자열을 마지막으로 만든 탄창 수와 소지 수다. 수가 같으면 문자열을 재사용한다.
        // 빈 문자열을 돌려주는 분기는 이 캐시를 무효로 해, 같은 수로 돌아와도 빈 문자열을 재사용하지 않게 한다.
        bool _ammoCountsValid;
        int _ammoLoaded, _ammoReserve;
        // 원본 BulletCountHUD의 bulletCountText와 capacityText에 넣는 두 자리 문자열이다. 발사는 탄창 수만 바꾸므로
        // 소지 수가 표 밖(100 이상)이어도 발마다 소지 문자열을 다시 만들지 않는다.
        string _loadedText = "", _reserveText = "";
        // 소지 수를 셀 때 넘긴 목표 탄종이다. 수량만 바뀐 그래프에서 소지 수를 같은 규칙으로 다시 센다.
        int _ammoBulletType;
        // 탄약 수 0~99의 두 자리 문자열 표다. 형식 "00"은 문화권과 관계없이 ASCII 두 자리라 표를 한 번만 만들어도 된다.
        static readonly string[] _twoDigits = CreateTwoDigits();
        // 탄약 HUD 문자열을 만들 때 쓴 소유 그래프와 무기다. 둘 중 하나나 로캘이 바뀔 때만 다시 만들고,
        // BulletTypeHUD 표시는 무기, 로캘, 그래프 모양이 바뀔 때만 다시 만든다.
        ItemOwnershipState? _ammoItems;
        string? _ammoWeapon;
        bool _ammoValid;

        internal CombatAmmoHud(GameplayWorldContext world, BulletTypeSwitch bulletTypes)
        {
            _world = world;
            _bulletTypes = bulletTypes;
        }

        // 마지막 Refresh가 정한 원본 capacityText의 소지 수 문자열이다. 총을 들지 않았으면 빈 문자열이다.
        internal string ReserveText => _reserveText;

        // 상태 HUD의 탄창 수 문자열을 돌려주고 소지 수 문자열(ReserveText)도 맞춘다. 소유 그래프, 든 무기, 로캘, 목표 탄종이 바뀐 뒤에만 다시 만든다.
        // 그때 원본 BulletTypeHUD에 목표 탄종 이름을, 목표가 없으면 "탄약 미장전"을 보이고, 총이 아니면 숨긴다.
        // weapon은 weaponId 인스턴스의 정의다. 무기를 들지 않았으면 둘 다 null이다.
        internal string Text(string? weaponId, OriginalItemDefinition? weapon)
        {
            // 씬 정리 중이라 그래프가 없으면 캐시를 그대로 두고 기록 없이 마지막 표시를 돌려준다.
            if (!_world.TryItems(out ItemOwnershipState? items)) return _loadedText;
            if (_ammoValid && ReferenceEquals(items, _ammoItems) && _ammoWeapon == weaponId) return _loadedText;
            // 출격 사망처럼 세션이 다른 씬으로 확정되면 컨트롤러가 장비를 다시 맞추지 않아, 유실물로 옮겨진 총이 새 그래프에 없을 수 있다.
            // 탄창 결함이 아니므로 기록하지 않고, 이 월드가 내려갈 때까지 마지막 탄약 표시를 그대로 둔다.
            if (weaponId != null && items.Find(weaponId) == null) return _loadedText;
            // 발사처럼 수량만 바뀐 그래프는 탄 묶음과 칸이 그대로라, 탄약 이름과 BulletTypeHUD는 두고 두 수만 같은 탄종으로 다시 센다.
            // 묶음이 다 떨어지거나 새로 들어오면 모양이 바뀌어 아래에서 전부 다시 만든다. 수를 세어 둔 상태면 든 무기는 정의가 있는 총이다.
            if (_ammoValid && _ammoCountsValid && _ammoWeapon == weaponId && _ammoItems != null && items.SharesPositions(_ammoItems) &&
                OriginalInventoryQueries.TryCompatibleAmmo(items, _world.Catalog, weapon!, out _, out int kept, _ammoBulletType))
            {
                _ammoItems = items;
                return CountText(OriginalInventoryQueries.MagazineCount(items, weaponId!), kept);
            }
            _ammoValid = true; _ammoItems = items; _ammoWeapon = weaponId;
            // 무기를 들었으면 정의도 정해져 있다. 총이 아니거나 조회가 구조 결함이면 HUD를 숨기고 빈 문자열을 이 그래프의 캐시로 두어
            // 매 프레임 다시 조회하지 않는다. 소지 수는 원본 GetBulletCountInInventory처럼 목표 탄종만 세고, 목표가 없으면 0이다.
            int target = weaponId != null && weapon!.IsGun ? _bulletTypes.Target(items, weaponId) : BulletTypeSwitch._none;
            if (weaponId == null || !weapon!.IsGun ||
                !OriginalInventoryQueries.TryCompatibleAmmo(items, _world.Catalog, weapon!, out _, out int reserve, target))
            {
                _world.Hud.Status.ShowBulletType(false, "", true);
                _ammoCountsValid = false;
                _reserveText = "";
                return _loadedText = "";
            }
            // 탄창 수량은 실패하지 않는 MagazineCount로 읽는다.
            int loaded = OriginalInventoryQueries.MagazineCount(items, weaponId);
            _ammoBulletType = target;
            // 목표 탄종은 그래프의 탄이나 그 탄에서 정한 값이라 카탈로그 정의가 늘 있다.
            // 목표가 없으면 탄종 목록과 같은 "탄약 미장전" 키를 쓴다.
            OriginalItemDefinition? bulletDefinition = null;
            bool assigned = target != BulletTypeSwitch._none && _world.Catalog.TryGet(target, out bulletDefinition);
            string bulletName = GameLocalization.Text(assigned ? bulletDefinition!.NameKey : BulletTypeSwitch.NotAssignedKey);
            _world.Hud.Status.ShowBulletType(true, bulletName, assigned);
            return CountText(loaded, reserve);
        }

        // 로캘이나 목표 탄종이 바뀌면 다음 탄약 HUD 갱신에서 탄약 이름과 소지 수를 다시 읽는다.
        internal void Invalidate() => _ammoValid = false;

        // 원본처럼 두 수를 따로 "D2" 문자열로 만든다. 바뀐 수의 문자열만 다시 고르고, 0~99는 표의 같은 문자열을 재사용한다.
        // 캐시가 무효면 두 수가 낡았을 수 있어 둘 다 다시 고른다.
        string CountText(int loaded, int reserve)
        {
            if (!_ammoCountsValid || loaded != _ammoLoaded) _loadedText = TwoDigits(loaded);
            if (!_ammoCountsValid || reserve != _ammoReserve) _reserveText = TwoDigits(reserve);
            _ammoCountsValid = true; _ammoLoaded = loaded; _ammoReserve = reserve;
            return _loadedText;
        }

        // uint 비교로 음수도 범위 밖이 된다. 0~99만 표에서 꺼내고, 나머지는 ToString("00")으로 만든다.
        static string TwoDigits(int value) => (uint)value < (uint)_twoDigits.Length ? _twoDigits[value] : value.ToString("00");

        static string[] CreateTwoDigits()
        {
            var digits = new string[100];
            for (int i = 0; i < digits.Length; i++) digits[i] = i.ToString("00");
            return digits;
        }
    }
}
