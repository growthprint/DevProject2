#nullable enable annotations
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원본 BulletCountHUD의 탄약 수와 BulletTypeHUD의 탄약 종류, 탄종 선택 목록이다. 총을 들지 않았으면 탄약 종류를 숨긴다.
    // 프리팹 참조는 생성자로 다시 받지 않고 SharedHudView의 직렬화 필드를 직접 읽는다.
    internal sealed class HudAmmoDisplay
    {
        // 원본 BulletTypeHUD의 normalColor와 emptyColor다. 목표 탄종이 없으면 붉은 배경에 흰 글자를 쓴다.
        static readonly Color _normalColor = new(0.6431373f, 0.6431373f, 0.6431373f, 1f);
        static readonly Color _emptyColor = new(0.8392157f, 0.28235295f, 0.28235295f, 1f);
        readonly SharedHudView _hud;
        // 탄종 목록 항목이다. 첫 항목은 프리팹의 템플릿이고, 더 필요하면 같은 부모에 복제해 다음에 다시 쓴다.
        readonly List<OriginalBulletTypeButton> _typeButtons = new();

        internal HudAmmoDisplay(SharedHudView hud) => _hud = hud;

        // 매 프레임 불린다. 원본 BulletCountHUD의 bulletCountText와 capacityText처럼 탄창 수와 소지 수를 따로 쓴다.
        // 두 문자열은 호출자가 캐시한 같은 인스턴스라 TMP가 비교한 뒤 건너뛴다.
        internal void ShowCount(string loaded, string reserve)
        {
            _hud._ammo.text = loaded;
            _hud._ammoCapacity.text = reserve;
        }

        // 원본 BulletTypeHUD.Update처럼 총이 없으면 alpha 0에 입력을 막고, 총이 있으면 alpha 1로 목표 탄종 이름을 표시한다.
        internal void ShowType(bool gunHeld, string name, bool assigned)
        {
            CanvasGroup group = _hud._bulletGroup;
            group.alpha = gunHeld ? 1f : 0f;
            group.interactable = gunHeld;
            _hud._ammoType.text = name;
            _hud._ammoType.color = assigned ? Color.black : Color.white;
            _hud._bulletTypeBackground.color = assigned ? _normalColor : _emptyColor;
        }

        // 원본 OpenList와 RefreshContent처럼 목록을 켜고 T 안내를 끈 뒤, 항목마다 이름과 소지 수를 채우고 고른 항목만 표시한다.
        internal void ShowTypeList(IReadOnlyList<string> names, IReadOnlyList<int> counts, int selected)
        {
            OriginalBulletTypeButton template = _hud._bulletTypeButton;
            for (int i = 0; i < names.Count; i++)
            {
                if (i >= _typeButtons.Count) _typeButtons.Add(i == 0 ? template : Object.Instantiate(template, template.transform.parent));
                OriginalBulletTypeButton button = _typeButtons[i];
                button.gameObject.SetActive(true);
                button.Bind(names[i], counts[i], i == selected);
            }
            for (int i = names.Count; i < _typeButtons.Count; i++) _typeButtons[i].gameObject.SetActive(false);
            _hud._bulletTypeList.SetActive(true);
            _hud._bulletTypeIndicator.SetActive(false);
        }

        // 꺼진 항목은 다음에 열 때 Bind가 다시 정하므로 함께 바꿔도 된다.
        internal void SelectType(int selected)
        {
            for (int i = 0; i < _typeButtons.Count; i++) _typeButtons[i].SetSelected(i == selected);
        }

        // 원본 CloseList다.
        internal void HideTypeList()
        {
            _hud._bulletTypeList.SetActive(false);
            _hud._bulletTypeIndicator.SetActive(true);
        }
    }
}
