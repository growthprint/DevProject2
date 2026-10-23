#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // 원작 CraftingFormula 하나다. 범위 안 화면이 읽지 않는 lockInDemo는 가져오지 않는다. hideInIndex는 레시피 도감이 읽는다.
    // 해금 여부는 제조 소유자가 가지므로 여기에는 정의 값만 둔다.
    [Serializable]
    public sealed class CraftingFormula
    {
        [SerializeField] string _id = "";
        // 원작 result(ItemEntry)의 아이템 ID와 수량이다.
        [SerializeField] int _resultId;
        [SerializeField] int _resultAmount;
        // 작업대, 인쇄기, 의료소처럼 이 제조식을 보이는 장소 태그다. 한 제조식이 여러 장소에 뜰 수 있다.
        [SerializeField] string[] _tags = Array.Empty<string>();
        [SerializeField] Cost _cost;
        [SerializeField] bool _unlockByDefault;
        // "트리/특성" 형식의 특성 키다. 비어 있으면 특성 조건이 없다.
        [SerializeField] string _requirePerk = "";
        // 원작 hideInIndex다. 참이면 레시피 도감(FormulasIndexView)에 보이지 않는다.
        [SerializeField] bool _hideInIndex;

        public string Id => _id;
        public int ResultId => _resultId;
        public int ResultAmount => _resultAmount;
        public IReadOnlyList<string> Tags => _tags;
        public Cost Cost => _cost;
        public bool UnlockByDefault => _unlockByDefault;
        public string RequirePerk => _requirePerk;
        public bool HideInIndex => _hideInIndex;
    }
}
