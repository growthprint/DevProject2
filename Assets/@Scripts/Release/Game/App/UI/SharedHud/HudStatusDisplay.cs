#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원본 HUDCanvas의 상태 표시를 묶는다. 체력, 에너지와 수분, 시계, 퀵슬롯, 무기, 탄약, 조준점, 스태미나, 적 체력바는
    // 원본 HUD 컴포넌트처럼 위젯 하나씩이 맡고, 이 클래스는 캐릭터 연결, 상태 요소 배치, 위젯 갱신 순서만 정한다.
    // 위젯은 프리팹 참조를 SharedHudView에서 직접 읽고, 인벤토리와 결과 화면 열림 상태는 HUD를 통해 읽는다.
    internal sealed class HudStatusDisplay
    {
        // 캐릭터를 연결하기 전과 소유자가 바뀐 뒤 위젯이 쓰는 게이지 최대치다. 그동안 상태 요소와 스태미나 게이지는 꺼져 있다.
        internal const float _unboundMaximum = 100f;
        readonly SharedHudView _hud;
        readonly HudHealthDisplay _healthDisplay;
        readonly HudSurvivalGauges _survivalGauges;
        readonly HudClockDisplay _clockDisplay;
        readonly HudQuickSlots _quickSlots;
        readonly HudWeaponSlots _weaponSlots;
        readonly HudAmmoDisplay _ammoDisplay;
        readonly HudAimMarker _aimMarker;
        readonly HudStaminaFollow _staminaFollow;
        // SharedHudTests가 이 필드 이름으로 적 체력바 위젯을 찾아 목록을 관찰한다.
        readonly HudEnemyHealthBars _enemyBars;
        // 스태미나 추적과 적 체력바가 함께 쓰는 추적 대상과 카메라다. 캐릭터 연결 때만 채우고 Reset이 함께 비운다.
        // 추적 대상 참조가 있는 동안이 캐릭터 연결이다. HudTestAccess가 이 필드 이름으로 연결 여부를 읽는다.
        Transform? _hudPlayer;
        Camera? _worldCamera;
        // _statusObjects와 같은 순서로, 인벤토리가 열린 동안 숨길지 표시한다. Awake 전에 BindCharacter가 올 수 있어 첫 갱신 때 만든다.
        bool[]? _statusHiddenDuringInventory;

        // 위젯은 HUD만 받아 참조를 직접 읽으므로 HUD가 처음 이 섹션을 쓸 때 한 번만 만든다.
        internal HudStatusDisplay(SharedHudView hud)
        {
            _hud = hud;
            _healthDisplay = new HudHealthDisplay(hud);
            _survivalGauges = new HudSurvivalGauges(hud);
            _clockDisplay = new HudClockDisplay(hud);
            _quickSlots = new HudQuickSlots(hud);
            _weaponSlots = new HudWeaponSlots(hud);
            _ammoDisplay = new HudAmmoDisplay(hud);
            _aimMarker = new HudAimMarker(hud);
            _staminaFollow = new HudStaminaFollow(hud);
            _enemyBars = new HudEnemyHealthBars(hud);
        }

        // HUD 채움 게이지들이 함께 쓰는 비율 규칙이다. 최대치가 1보다 작으면 1로 나눠 0이나 음수 최대치로 나누지 않는다.
        internal static float GaugeFill(float value, float maximum) => value / Mathf.Max(1f, maximum);

        internal void Initialize()
        {
            // 프롤로그나 맨손처럼 총을 든 상태가 보고되기 전에는 탄약 HUD와 탄종 목록을 숨긴다.
            ShowBulletType(false, "", true);
            HideBulletTypes();
            RefreshLayout();
        }

        // 씬 준비 때와 최대치가 바뀔 때 캐릭터의 최대값 넷과 추적 대상을 연결하고 상태 HUD를 켠다.
        // 위젯은 최대치를 여기서만 받으므로, 최대치를 바꾸는 효과를 더하면 그 경로도 PlayerStatusHud.RefreshHudMaxima를 불러야 한다.
        // 적 목록은 적 월드의 나온 적 목록이라 체력바가 스폰 후보 전체를 돌지 않는다.
        public void BindCharacter(float maxHealth, float maxStamina, float maxEnergy, float maxWater, Transform player, Camera camera,
            IReadOnlyList<OriginalEnemyController> enemies)
        {
            // 최대값 넷은 그 값을 쓰는 위젯이 받아 둔다. 체력 위젯은 문구의 최대 조각을 다시 만들고, 스태미나 위젯은 추적을 새로 시작한다.
            _healthDisplay.SetMaximum(maxHealth);
            _staminaFollow.Bind(maxStamina);
            _survivalGauges.SetMaximum(maxEnergy, maxWater);
            _hudPlayer = player;
            _worldCamera = camera;
            _enemyBars.Bind(enemies);
            RefreshLayout();
        }

        // 소유자가 바뀔 때 캐릭터 연결을 갓 생성한 상태로 되돌린다. 위젯마다 표시 캐시를 시작값으로 돌려 다음 소유자의 첫 갱신이 반드시 적용되게 한다.
        // 인벤토리를 먼저 닫은 뒤 불러야 상태 요소와 퀵슬롯 위치가 닫힌 상태 기준으로 정해진다.
        internal void Reset()
        {
            _hudPlayer = null;
            _worldCamera = null;
            _enemyBars.Reset();
            _healthDisplay.Reset();
            _survivalGauges.Reset();
            _clockDisplay.Reset();
            _quickSlots.Reset();
            _weaponSlots.Reset();
            // 앞 소유자가 탄종 목록을 연 채 떠나도 다음 소유자는 닫힌 목록에서 시작한다.
            ShowBulletType(false, "", true);
            HideBulletTypes();
            _staminaFollow.Reset();
            RefreshLayout();
        }

        // 원본 HP 바, 탄창 수와 소지 수, 스태미나 게이지를 매 프레임 갱신한다. 문자열을 다시 만들지는 각 위젯이 판단한다.
        public void ShowVitals(float health, float stamina, string loaded, string reserve)
        {
            _healthDisplay.Show(health);
            _ammoDisplay.ShowCount(loaded, reserve);
            _staminaFollow.ShowFill(stamina);
        }

        // 원본 에너지와 수분 게이지, 시계를 플레이어와 시계의 저장 값으로 표시한다.
        public void ShowSurvival(float energy, float water, double? secondsOfDay)
        {
            _survivalGauges.Show(energy, water);
            _clockDisplay.Show(secondsOfDay);
        }

        // 원본 AimMarker처럼 조준점을 화면의 조준 위치로 옮긴 뒤, 선택한 무기 프레임만 켠다.
        public void ShowAim(Vector2 point, float scatter, int weaponIndex)
        {
            _aimMarker.Show(point, scatter);
            _weaponSlots.Select(weaponIndex);
        }

        // 소유자는 위젯을 모르고 이 섹션의 표시 메서드만 부르므로, 아래 넷은 해당 위젯에 넘기기만 한다.
        public void ShowQuickSlot(int index, Sprite? icon, string name, int count) => _quickSlots.Show(index, icon, name, count);

        public void ShowQuickSlotUsing(int index, bool used) => _quickSlots.ShowUsing(index, used);

        public void ShowWeaponIcon(int index, Sprite? icon) => _weaponSlots.ShowIcon(index, icon);

        public void ShowBulletType(bool gunHeld, string name, bool assigned) => _ammoDisplay.ShowType(gunHeld, name, assigned);

        public void ShowBulletTypes(IReadOnlyList<string> names, IReadOnlyList<int> counts, int selected) =>
            _ammoDisplay.ShowTypeList(names, counts, selected);

        public void SelectBulletType(int selected) => _ammoDisplay.SelectType(selected);

        public void HideBulletTypes() => _ammoDisplay.HideTypeList();

        // 캐릭터 연결 여부와 인벤토리 열림에 따라 상태 요소의 표시와 퀵슬롯 위치를 한 번에 정한다.
        internal void RefreshLayout()
        {
            // 직렬화 배열은 실행 중 바뀌지 않으므로, 숨김 여부를 첫 갱신 때 한 번 계산해 두고 색인으로 읽는다.
            GameObject[] statusObjects = _hud._statusObjects;
            if (_statusHiddenDuringInventory == null)
            {
                _statusHiddenDuringInventory = new bool[statusObjects.Length];
                for (int i = 0; i < statusObjects.Length; i++)
                    _statusHiddenDuringInventory[i] = Array.IndexOf(_hud._hideDuringInventory, statusObjects[i]) >= 0;
            }
            bool inventoryOpen = _hud.Inventory.IsOpen;
            // 연결 여부는 파괴 검사 없이 참조로 본다. 소유자는 대상이 파괴되기 전에 HUD를 뗀다.
            bool characterBound = !ReferenceEquals(_hudPlayer, null);
            for (int i = 0; i < statusObjects.Length; i++)
                statusObjects[i].SetActive(characterBound && !(inventoryOpen && _statusHiddenDuringInventory[i]));
            _quickSlots.Place(inventoryOpen);
        }

        // 스태미나 HUD를 캐릭터에 따라 붙인 뒤 적 머리 위 체력바를 같은 카메라로 추적한다. 추적 대상이 없으면 스태미나만 숨긴다.
        internal void LateUpdate()
        {
            if (_worldCamera == null || _hudPlayer == null)
            {
                _staminaFollow.Hide();
                return;
            }
            // 인벤토리와 결과 화면 열림은 activeSelf 조회라, 한 번만 읽어 스태미나와 적 체력바 판정에 함께 쓴다.
            bool resultOpen = _hud.Result.IsOpen;
            bool barsVisible = !_hud.Inventory.IsOpen && !resultOpen;
            _staminaFollow.Follow(_hudPlayer, _worldCamera, barsVisible);
            _enemyBars.Present(_worldCamera, resultOpen, barsVisible);
        }
    }
}
