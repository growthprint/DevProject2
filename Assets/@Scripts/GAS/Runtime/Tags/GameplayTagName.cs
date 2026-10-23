#nullable enable
using System;
using UnityEngine;

namespace Game.GAS
{
    // 에셋에 저장하는 태그 이름이다. 직렬화는 문자열로 하고, 실행 중에는 GameplayTag로 바꿔 쓴다.
    // Editor drawer가 등록된 이름 목록에서 고르게 한다.
    [Serializable]
    public struct GameplayTagName
    {
        [SerializeField] string _name;

        public GameplayTagName(string name) => _name = name;

        public string Name => _name ?? "";
        public bool IsEmpty => string.IsNullOrEmpty(_name);

        // 비어 있거나 형식이 틀린 이름은 false다. 보고는 결함인지 아는 호출자가 한다.
        public bool TryResolve(out GameplayTag tag) => GameplayTag.TryParse(_name, out tag);

        public override string ToString() => Name;
    }
}
