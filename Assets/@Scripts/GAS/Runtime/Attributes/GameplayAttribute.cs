#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.GAS
{
    // 어트리뷰트 하나를 가리키는 핸들이다. 키 문자열(원작 스탯 이름 같은)을 정수로 바꿔 두어 조회에 할당이 없다.
    // 게임 쪽이 static readonly 필드로 Register해 두고, 에셋의 이름은 TryFind로 이 표에서 찾는다.
    public readonly struct GameplayAttribute : IEquatable<GameplayAttribute>
    {
        static readonly List<string> _keys = new List<string> { "" };
        static readonly Dictionary<string, int> _ids = new Dictionary<string, int>(StringComparer.Ordinal);

        // 0은 등록되지 않은 값이라 default가 곧 무효 핸들이다.
        readonly int _id;

        GameplayAttribute(int id) => _id = id;

        public bool IsValid => _id != 0;
        public string Key => _keys[_id];
        internal int Id => _id;
        internal static int Count => _keys.Count;

        // 같은 키를 다시 등록하면 같은 핸들을 돌려준다. 게임 코드는 비어 있지 않고 공백이 없는 키만 등록한다.
        public static GameplayAttribute Register(string key)
        {
            if (_ids.TryGetValue(key, out int id)) return new GameplayAttribute(id);
            id = _keys.Count;
            _keys.Add(key);
            _ids.Add(key, id);
            return new GameplayAttribute(id);
        }

        public static bool TryFind(string? key, out GameplayAttribute attribute)
        {
            attribute = default;
            if (key == null || !_ids.TryGetValue(key, out int id)) return false;
            attribute = new GameplayAttribute(id);
            return true;
        }

        // Editor drawer가 등록된 키 목록을 보여 줄 때 쓴다.
        internal static string KeyAt(int id) => _keys[id];

        public bool Equals(GameplayAttribute other) => _id == other._id;
        public override bool Equals(object? obj) => obj is GameplayAttribute other && Equals(other);
        public override int GetHashCode() => _id;
        public static bool operator ==(GameplayAttribute left, GameplayAttribute right) => left._id == right._id;
        public static bool operator !=(GameplayAttribute left, GameplayAttribute right) => left._id != right._id;
        public override string ToString() => Key;
    }

    // 에셋에 저장하는 어트리뷰트 키다. 실행 중 처음 읽을 때 핸들로 바꾼다.
    [Serializable]
    public struct GameplayAttributeName
    {
        [SerializeField] string _key;

        public GameplayAttributeName(string key) => _key = key;

        public string Key => _key ?? "";
        public bool IsEmpty => string.IsNullOrEmpty(_key);

        public bool TryResolve(out GameplayAttribute attribute) => GameplayAttribute.TryFind(_key, out attribute);

        public override string ToString() => Key;
    }

    // 어트리뷰트 현재값이 바뀌었다는 알림 값이다. 한 작업 안의 여러 변화는 처음 값과 마지막 값 하나로 합친다.
    public readonly struct AttributeChangeData
    {
        public AttributeChangeData(GameplayAttribute attribute, float oldValue, float newValue)
        {
            Attribute = attribute;
            OldValue = oldValue;
            NewValue = newValue;
        }

        public GameplayAttribute Attribute { get; }
        public float OldValue { get; }
        public float NewValue { get; }
    }
}
