#nullable enable
using System;
using UnityEngine;

namespace Game.App
{
    // 원작 DecomposeFormula 하나다. 가져오기 도구가 유효한 항목만 넣으므로 원작 valid 플래그는 두지 않는다.
    [Serializable]
    internal sealed class DecomposeFormula
    {
        [SerializeField] int _item;
        // 분해 한 번의 결과다. 원작처럼 겹친 수를 곱해 지급하는 일은 분해 명령이 맡는다.
        [SerializeField] Cost _result;

        internal int Item => _item;
        internal Cost Result => _result;
    }
}
