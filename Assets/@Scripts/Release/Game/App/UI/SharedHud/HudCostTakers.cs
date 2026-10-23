#nullable enable annotations
using System.Collections.Generic;
using Game.Core;
using Object = UnityEngine.Object;

namespace Game.App
{
    // 원작 CostTakerHUD다. 건설 지점마다 항목 템플릿의 사본을 하나 만들어 소유자에게 돌려주고, 소유자가 바뀌면 사본을 모두 지운다.
    // 위치, 표시 여부, 가진 수는 소유자의 표시기(CostTakerHud)가 매 프레임 넘긴다. 템플릿은 SharedHudView 직렬화 필드를 직접 읽는다.
    internal sealed class HudCostTakers
    {
        readonly SharedHudView _hud;
        readonly List<OriginalCostTakerEntryView> _entries = new();

        internal HudCostTakers(SharedHudView hud) => _hud = hud;

        internal OriginalCostTakerEntryView Add()
        {
            OriginalCostTakerEntryView template = _hud._costTakerEntry;
            OriginalCostTakerEntryView entry = Object.Instantiate(template, template.transform.parent, false);
            entry.gameObject.SetActive(true);
            _entries.Add(entry);
            return entry;
        }

        internal void Clear()
        {
            foreach (OriginalCostTakerEntryView entry in _entries) UnityObjects.DestroyOwned(entry.gameObject);
            _entries.Clear();
        }
    }
}
