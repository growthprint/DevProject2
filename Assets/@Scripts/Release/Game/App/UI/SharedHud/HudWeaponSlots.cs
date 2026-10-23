#nullable enable annotations
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // 원본 WeaponButton 칸들이다. 원본 HUD처럼 장비 아이콘은 확정된 스냅샷으로만 채우고, 선택한 무기 칸의 프레임만 켠다.
    // 프리팹 참조는 생성자로 다시 받지 않고 SharedHudView의 직렬화 필드를 직접 읽는다.
    internal sealed class HudWeaponSlots
    {
        readonly SharedHudView _hud;
        // 마지막으로 켠 무기 프레임 색인이다. 시작값이 int.MinValue라 프롤로그의 -1을 포함한 첫 호출은 반드시 적용된다.
        int _shownIndex = int.MinValue;

        internal HudWeaponSlots(SharedHudView hud) => _hud = hud;

        // 소유자가 바뀔 때 무기 아이콘을 비우고, 다음 소유자의 첫 선택이 반드시 프레임에 적용되게 한다.
        internal void Reset()
        {
            _shownIndex = int.MinValue;
            // 빈 아이콘은 ShowIcon과 같은 규칙으로 스프라이트를 비우고 끈다.
            foreach (Image icon in _hud._weaponIcons) UiVisibility.SetIcon(icon, null);
        }

        // 호출자는 저작된 무기 칸 범위의 색인만 넘기므로 범위를 검사하지 않는다. 아이콘이 없으면 다른 아이콘 칸처럼 이미지를 끈다.
        internal void ShowIcon(int index, Sprite? icon) => UiVisibility.SetIcon(_hud._weaponIcons[index], icon);

        // 무기 프레임은 이 메서드만 켜고 끄므로, 선택 무기 색인이 바뀐 프레임에만 SetActive 루프를 돈다.
        // 무기를 들었으면 원본 BulletTypeHUD.OnWeaponButtonSelected처럼 탄약 HUD를 고른 칸 바로 위에 둔다. 무기가 없으면 원본처럼 그 자리에 둔다.
        internal void Select(int weaponIndex)
        {
            if (weaponIndex != _shownIndex)
            {
                _shownIndex = weaponIndex;
                GameObject[] frames = _hud._weaponFrames;
                for (int i = 0; i < frames.Length; i++) frames[i].SetActive(i == weaponIndex);
            }
            if (weaponIndex >= 0) PlaceAmmoAbove(_hud._weaponButtons[weaponIndex]);
        }

        // 원본 식이다. 칸 사각형 중심에서 칸 위쪽으로 반높이 + 8만큼 올라간 점에 탄약 HUD의 피벗(아래 가운데)을 둔다.
        // 칸은 레이아웃 그룹이 나중에 자리를 잡고 화면 크기에 따라 움직이므로 매 프레임 계산하고, 위치가 달라졌을 때만 쓴다.
        void PlaceAmmoAbove(RectTransform button)
        {
            Rect rect = button.rect;
            Vector2 local = rect.center + (Vector2)((rect.height * .5f + 8f) * button.up);
            Vector3 scale = button.lossyScale;
            Vector3 origin = button.position;
            Transform ammo = _hud._bulletGroup.transform;
            Vector3 current = ammo.position;
            Vector3 target = new(origin.x + local.x * scale.x, origin.y + local.y * scale.y, current.z);
            if (target != current) ammo.position = target;
        }
    }
}
