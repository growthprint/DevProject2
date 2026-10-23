#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 PerkBehaviour 하위 형식 하나에 대응한다. 에셋에 정수로 저장되므로 값을 바꾸지 않는다.
    // 뒤의 정원과 블랙마켓 4종은 범위 밖이라 데이터만 두고 동작은 만들지 않는다.
    public enum PerkEffectKind
    {
        // 원작 ModifyCharacterStatsBase다.
        Stats = 0,
        // 원작 AddPlayerStorage다.
        PlayerStorage = 1,
        // 원작 UnlockStockShopItem이다.
        ShopItem = 2,
        // 원작 UnlockFormula다. 대상 제조식은 requirePerk가 "<트리 ID>/<특성 이름>"인 것이라 따로 값을 두지 않는다.
        Formula = 3,
        // 원작 UnlockAchievement다. 업적 시스템이 없어 키만 둔다.
        Achievement = 4,
        GardenSize = 5,
        GardenAutoWater = 6,
        BlackMarketRefreshChance = 7,
        BlackMarketRefreshTimeFactor = 8,
    }

    // 원작 ModifyCharacterStatsBase.Entry다. 원작은 percentage와 상관없이 스탯 Base에 value를 더하고, percentage는 설명 표시에만 쓴다.
    [Serializable]
    public sealed class PerkStatEntry
    {
        [SerializeField] string _key = string.Empty;
        [SerializeField] float _value;
        [SerializeField] bool _percentage;

        internal string Key => _key;
        internal float Value => _value;
        internal bool Percentage => _percentage;
    }

    // 특성 GameObject에 붙은 원작 동작 컴포넌트 하나다. 종류에 맞는 값만 읽는다.
    [Serializable]
    public sealed class PerkEffect
    {
        [SerializeField] PerkEffectKind _kind;
        [SerializeField] PerkStatEntry[] _stats = Array.Empty<PerkStatEntry>();
        // PlayerStorage는 늘릴 칸 수, ShopItem은 상품 아이템 ID, BlackMarketRefreshChance는 더할 횟수다.
        [SerializeField] int _intValue;
        // Achievement는 업적 키, 정원 동작은 정원 ID다.
        [SerializeField] string _text = string.Empty;

        internal PerkEffectKind Kind => _kind;
        internal IReadOnlyList<PerkStatEntry> Stats => _stats;
        internal int IntValue => _intValue;
        internal string Text => _text;
    }
}
