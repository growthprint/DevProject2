#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원본 EnergyHUD와 WaterHUD처럼 에너지와 수분 게이지를 플레이어의 저장 값으로 채운다. 두 게이지는 규칙이 같아 한 위젯이 맡는다.
    // 프리팹 참조는 생성자로 다시 받지 않고 SharedHudView의 직렬화 필드를 직접 읽는다.
    internal sealed class HudSurvivalGauges
    {
        readonly SharedHudView _hud;
        // 최대값 둘은 캐릭터 연결 때 한 번 받아 두고, 매 프레임 갱신은 현재 값만 받는다. 연결 전에는 상태 표시의 시작 최대치다.
        float _maxEnergy = HudStatusDisplay._unboundMaximum, _maxWater = HudStatusDisplay._unboundMaximum;

        internal HudSurvivalGauges(SharedHudView hud) => _hud = hud;

        internal void SetMaximum(float maxEnergy, float maxWater)
        {
            _maxEnergy = maxEnergy;
            _maxWater = maxWater;
        }

        // 소유자가 바뀔 때 최대값을 시작값으로 돌린다.
        internal void Reset() => SetMaximum(HudStatusDisplay._unboundMaximum, HudStatusDisplay._unboundMaximum);

        // 다른 HUD 게이지와 같은 채움 비율 규칙을 쓴다.
        internal void Show(float energy, float water)
        {
            _hud._energyFill.fillAmount = HudStatusDisplay.GaugeFill(energy, _maxEnergy);
            _hud._waterFill.fillAmount = HudStatusDisplay.GaugeFill(water, _maxWater);
        }
    }
}
